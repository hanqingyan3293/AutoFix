using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AutoFix
{
    // 深度清理的补充阶段：顽固注册表项、Installer 幽灵项、系统 PATH、共享组件、安装包。
    // 阶段设计参考 autodesk-complete-uninstaller（MIT），实现为原生 C#。
    internal static partial class RepairService
    {
        // ==================== Phase C2：强制清除顽固注册表项 ====================

        /// <summary>
        /// 卸载多轮后仍在注册表中的 Autodesk 产品项，直接强制删除。
        /// 对应参考项目的 Phase C2（仅在确认全量清除后执行）。
        /// </summary>
        internal static int ForceRemoveStuckEntries(Action<string> log)
        {
            int removed = 0;
            var locations = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var loc in locations)
            {
                string hiveName = loc.Item1 == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(loc.Item1, loc.Item2))
                    using (RegistryKey root = baseKey.OpenSubKey(loc.Item3, false))
                    {
                        if (root == null)
                        {
                            continue;
                        }

                        var targets = new List<string>();
                        foreach (string sub in root.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey item = root.OpenSubKey(sub, false))
                                {
                                    if (item == null)
                                    {
                                        continue;
                                    }
                                    string name = item.GetValue("DisplayName") == null ? "" : item.GetValue("DisplayName").ToString();
                                    string pub = item.GetValue("Publisher") == null ? "" : item.GetValue("Publisher").ToString();
                                    if (name.Length == 0)
                                    {
                                        continue;
                                    }
                                    if (pub.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                                     || name.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        targets.Add(sub);
                                    }
                                }
                            }
                            catch { }
                        }

                        foreach (string sub in targets)
                        {
                            string full = loc.Item3 + "\\" + sub;
                            if (DryRun)
                            {
                                DryNote("强制删除顽固注册表项 " + hiveName + "\\" + full, log);
                                removed++;
                                continue;
                            }
                            try
                            {
                                root.DeleteSubKeyTree(sub, false);
                                Log(log, "  [强制] " + hiveName + "\\" + full);
                                removed++;
                            }
                            catch (Exception ex)
                            {
                                Log(log, "  [强制] 失败：" + full + " -> " + ex.Message);
                            }
                        }
                    }
                }
                catch { }
            }
            return removed;
        }

        // ==================== Installer\Products 幽灵项清理 ====================

        /// <summary>
        /// 清除 Installer\Products 下 ProductName 含 Autodesk 的记录（对应参考项目的 ghost cleanup）。
        /// 删除前把该分支导出为 .reg 备份。
        /// </summary>
        internal static int CleanInstallerProductGhosts(Action<string> log)
        {
            const string branch = @"SOFTWARE\Classes\Installer\Products";
            int removed = 0;

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = baseKey.OpenSubKey(branch, false))
                {
                    if (root == null)
                    {
                        return 0;
                    }

                    var targets = new List<string>();
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey p = root.OpenSubKey(sub, false))
                            {
                                if (p == null)
                                {
                                    continue;
                                }
                                object pn = p.GetValue("ProductName");
                                if (pn != null && pn.ToString().IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    targets.Add(sub);
                                }
                            }
                        }
                        catch { }
                    }

                    if (targets.Count == 0)
                    {
                        return 0;
                    }

                    if (DryRun)
                    {
                        foreach (string t in targets)
                        {
                            DryNote("删除 Installer\\Products 幽灵项 " + t, log);
                            removed++;
                        }
                        return removed;
                    }

                    // 备份整个分支
                    BackupRegistryBranch("HKLM\\" + branch, "installer_products_backup.reg", log);

                    using (RegistryKey writable = baseKey.OpenSubKey(branch, true))
                    {
                        if (writable == null)
                        {
                            return 0;
                        }
                        foreach (string t in targets)
                        {
                            try
                            {
                                writable.DeleteSubKeyTree(t, false);
                                removed++;
                            }
                            catch (Exception ex)
                            {
                                Log(log, "  幽灵项删除失败：" + t + " -> " + ex.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, "清理 Installer 幽灵项失败：" + ex.Message);
            }

            if (removed > 0)
            {
                Log(log, "  已删除 Installer\\Products 幽灵项 " + removed + " 个");
            }
            return removed;
        }

        // ==================== 系统 PATH 清理 ====================

        /// <summary>
        /// 从系统 PATH 中移除指向 Autodesk / AdODIS 的条目。
        /// 修改前导出 Environment 分支备份。类型保持 REG_EXPAND_SZ。
        /// </summary>
        internal static int CleanSystemPath(Action<string> log)
        {
            const string subKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey env = baseKey.OpenSubKey(subKey, true))
                {
                    if (env == null)
                    {
                        return 0;
                    }

                    object raw = env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (raw == null)
                    {
                        return 0;
                    }

                    string path = raw.ToString();
                    if (path.Length == 0)
                    {
                        return 0;
                    }

                    var kept = new List<string>();
                    int removed = 0;
                    foreach (string part in path.Split(';'))
                    {
                        if (part.Length == 0)
                        {
                            continue;
                        }
                        if (part.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                         || part.IndexOf("AdODIS", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Log(log, "  移除 PATH 条目：" + part);
                            removed++;
                            continue;
                        }
                        kept.Add(part);
                    }

                    if (removed == 0)
                    {
                        return 0;
                    }

                    if (DryRun)
                    {
                        DryNote("从系统 PATH 移除 " + removed + " 个 Autodesk/AdODIS 条目", log);
                        return removed;
                    }

                    BackupRegistryBranch("HKLM\\" + subKey, "system_path_backup.reg", log);

                    env.SetValue("Path", string.Join(";", kept.ToArray()), RegistryValueKind.ExpandString);
                    Log(log, "  已从系统 PATH 移除 " + removed + " 个条目");
                    return removed;
                }
            }
            catch (Exception ex)
            {
                Log(log, "清理系统 PATH 失败：" + ex.Message);
                return 0;
            }
        }

        // ==================== 安装包与下载缓存清理 ====================

        /// <summary>清理 Autodesk 安装包与下载缓存。</summary>
        internal static int CleanInstallerDownloads(Action<string> log)
        {
            var dirs = new List<string>
            {
                @"C:\Autodesk",
                @"C:\ProgramData\Autodesk\Uninstallers",
                @"C:\ProgramData\Autodesk\ODIS\metadata"
            };

            // 用户下载目录下的 Autodesk 安装包
            try
            {
                string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(dl))
                {
                    foreach (string f in Directory.GetFiles(dl, "*Autodesk*", SearchOption.TopDirectoryOnly))
                    {
                        deleteInstallerFile(f, log);
                    }
                }
            }
            catch { }

            int n = 0;
            foreach (string d in dirs)
            {
                try
                {
                    if (Directory.Exists(d))
                    {
                        Log(log, "  删除安装包目录：" + d);
                        DeleteDirectory(d, log);
                        n++;
                    }
                }
                catch { }
            }
            return n;
        }

        private static void deleteInstallerFile(string path, Action<string> log)
        {
            try
            {
                string name = Path.GetFileName(path);
                Log(log, "  删除安装包：" + name);
                FsDeleteFile(path, log);
            }
            catch { }
        }

        // ==================== 共享组件（Phase D） ====================

        /// <summary>清理 Autodesk 共享组件目录（对应参考项目的 Phase D）。</summary>
        internal static int CleanSharedComponents(Action<string> log)
        {
            var dirs = new List<string>
            {
                @"C:\Program Files\Common Files\Autodesk Shared",
                @"C:\Program Files (x86)\Common Files\Autodesk Shared",
                @"C:\Program Files\Common Files\Autodesk",
                @"C:\Program Files (x86)\Common Files\Autodesk",
                @"C:\Program Files\Common Files\Macrovision Shared\FlexNet Publisher",
                @"C:\Program Files (x86)\Common Files\Macrovision Shared\FlexNet Publisher"
            };

            int n = 0;
            foreach (string d in dirs)
            {
                try
                {
                    if (Directory.Exists(d))
                    {
                        Log(log, "  删除共享组件目录：" + d);
                        bool existed = true;
                        DeleteDirectory(d, log);
                        if (existed && !Directory.Exists(d))
                        {
                            n++;
                        }
                    }
                }
                catch { }
            }
            return n;
        }

        // ==================== 公用：注册表分支备份 ====================

        /// <summary>把注册表分支导出为 .reg 文件，放在 %LOCALAPPDATA%\AutoFix\Backup 下。</summary>
        private static void BackupRegistryBranch(string regPath, string fileName, Action<string> log)
        {
            if (DryRun)
            {
                return;
            }
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutoFix", "Backup");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string file = Path.Combine(dir, fileName);

                // 用 reg.exe 导出（备份不属于系统改动，不受预演影响）
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = "export \"" + regPath + "\" \"" + file + "\" /y",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }))
                {
                    if (p != null)
                    {
                        p.WaitForExit(30000);
                        if (p.ExitCode == 0)
                        {
                            Log(log, "  已备份注册表分支：" + file);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, "  备份注册表分支失败：" + ex.Message);
            }
        }

        // ==================== 统一深度清理流程 ====================

        /// <summary>
        /// 深度清理主流程。产品卸载与「仅深度清理」共用，避免两条路径逻辑分叉。
        /// 阶段顺序对齐参考项目：B → C2 → 幽灵项 → D → E → E3 → E2 → F → G → H
        ///   → PATH → 多用户 → 刷新服务 → I（Genuine Service，放最后）。
        /// </summary>
        internal static List<string> RunDeepClean(bool stopProcessesFirst, bool multiUser, Action<string> log)
        {
            var notes = new List<string>();

            if (stopProcessesFirst)
            {
                Log(log, "[阶段 B] 结束进程并停止服务 ...");
                foreach (string p in UninstallKillProcs)
                {
                    KillProcess(p + ".exe");
                }
                foreach (string s in UninstallServices)
                {
                    StopService(s);
                    RunCommand("sc", "stop \"" + s + "\"", false);
                }
                System.Threading.Thread.Sleep(1000);
            }

            // --- Phase C2 ---
            Log(log, "[阶段 C2] 强制清除顽固注册表项 ...");
            int stuck = ForceRemoveStuckEntries(log);
            notes.Add("强制清除顽固注册表项 " + stuck + " 个");

            // --- 幽灵项 ---
            Log(log, "[幽灵项] 清理 Installer\\Products ...");
            int ghosts = CleanInstallerProductGhosts(log);
            notes.Add("清理 Installer 幽灵项 " + ghosts + " 个");

            // --- Phase D ---
            Log(log, "[阶段 D] 清理共享组件目录 ...");
            int shared = CleanSharedComponents(log);
            notes.Add("清理共享组件目录 " + shared + " 个");

            // --- Phase E ---
            Log(log, "[阶段 E] 删除残留目录 ...");
            var locked = new List<string>();
            foreach (string d in UninstallFolders)
            {
                bool existed = false;
                try { existed = Directory.Exists(d); } catch { }
                DeleteDirectory(d, log);
                if (existed)
                {
                    try { if (Directory.Exists(d)) { locked.Add(d); } } catch { }
                }
            }

            // --- Phase E3 ---
            if (locked.Count > 0)
            {
                List<string> still = RetryLockedFolders(locked, log);
                if (still.Count > 0)
                {
                    notes.Add(still.Count + " 个目录仍被占用，已安排重启后清理");
                }
            }

            // --- Phase E2 ---
            Log(log, "[阶段 E2] 清理快捷方式 ...");
            int sc = CleanAutodeskShortcuts(log);
            notes.Add("清理快捷方式 " + sc + " 个");

            // --- Phase F ---
            Log(log, "[阶段 F] 清理缓存 ...");
            CleanAutodeskCaches(log);
            Log(log, "[阶段 F2] 清理安装包与下载缓存 ...");
            int dl = CleanInstallerDownloads(log);
            notes.Add("清理安装包目录 " + dl + " 个");

            // --- Phase G ---
            Log(log, "[阶段 G] 清理服务注册、计划任务、防火墙规则 ...");
            foreach (string s in UninstallServices)
            {
                RunCommand("sc", "delete \"" + s + "\"", false);
            }
            int tasks = RemoveAutodeskScheduledTasks(log);
            notes.Add("删除计划任务 " + tasks + " 个");
            int fw = RemoveAutodeskFirewallRules(log);
            notes.Add("删除防火墙规则 " + fw + " 条");

            // --- Phase H ---
            Log(log, "[阶段 H] 清理注册表 ...");
            foreach (string exe in UninstallIfeoExes)
            {
                DeleteRegistryValue(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\" + exe,
                    "Debugger", log);
            }
            foreach (string cls in UninstallClassKeys)
            {
                DeleteRegistryKey(RegistryHive.CurrentUser, "Software\\Classes\\" + cls, log);
            }
            foreach (string branch in UninstallRegistryBranches)
            {
                DeleteRegistryKey(RegistryHive.LocalMachine, branch, log);
                DeleteRegistryKey(RegistryHive.CurrentUser, branch, log);
            }
            CleanAutodeskEnvVars(log);

            // --- 系统 PATH ---
            Log(log, "[PATH] 清理系统 PATH 中的 Autodesk 条目 ...");
            int pe = CleanSystemPath(log);
            notes.Add("清理系统 PATH 条目 " + pe + " 个");

            // --- 多用户 ---
            if (multiUser)
            {
                Log(log, "[多用户清理] 处理其他用户配置文件 ...");
                notes.Add(CleanOtherUserProfiles(log));
            }

            // --- 刷新服务 ---
            FlushInstallerServices(log);

            // --- Phase I：Genuine Service（必须最后） ---
            Log(log, "[阶段 I] 移除 Genuine Service（最后执行）...");
            try
            {
                string g = RemoveGenuineService(log);
                notes.Add("Genuine Service：" + g);
            }
            catch (Exception ex)
            {
                notes.Add("Genuine Service 处理异常：" + ex.Message);
            }

            return notes;
        }
    }
}
