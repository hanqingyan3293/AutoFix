using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AutoFix
{
    // 12 项工作台的其余功能：仅深度清理、17 项验证、系统审计、Desktop Connector、
    // 重启挂起修复、模板备份。设计与清单参考 autodesk-complete-uninstaller（MIT）。
    internal static partial class RepairService
    {
        // ==================== #4 仅深度清理残留 ====================

        /// <summary>不卸载产品，只清理残留（对应参考项目的 Deep Clean Only）。</summary>
        internal static string DeepCleanOnly(bool multiUser, bool cleanInstallers, Action<string> log)
        {
            List<string> notes = RunDeepClean(true, multiUser, cleanInstallers, log);

            var sb = new StringBuilder();
            sb.AppendLine("深度清理完成（未卸载任何产品）。");
            sb.AppendLine();
            foreach (string n in notes)
            {
                sb.AppendLine("  · " + n);
            }
            sb.AppendLine();
            sb.AppendLine("说明：本操作只清理残留，不卸载已安装的 Autodesk 产品。");
            return sb.ToString();
        }

        // ==================== #8 完整系统审计（只读预览） ====================

        /// <summary>只读审计：列出完整清理会删除的全部内容。</summary>
        internal static string RunSystemAudit(Action<string> log)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Autodesk 系统足迹审计");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("计算机：" + Environment.MachineName + "   用户：" + Environment.UserName);
            sb.AppendLine("本报告为只读扫描，未修改任何内容。");
            sb.AppendLine(new string('=', 72));
            int total = 0;

            // 1 产品
            sb.AppendLine();
            sb.AppendLine("1. 已安装的 Autodesk 产品");
            sb.AppendLine(new string('-', 72));
            List<ProductItem> products = new List<ProductItem>();
            try { products = ScanInstalledProducts(); } catch { }
            if (products.Count == 0)
            {
                sb.AppendLine("   无");
            }
            else
            {
                foreach (ProductItem p in products)
                {
                    sb.AppendLine("   · " + p.Name + (string.IsNullOrEmpty(p.Version) ? "" : "  " + p.Version));
                    if (!string.IsNullOrEmpty(p.UninstallString))
                    {
                        sb.AppendLine("       卸载命令：" + p.UninstallString);
                    }
                }
            }
            total += products.Count;
            Log(log, "审计 1/17 产品：" + products.Count);

            // 2 进程
            sb.AppendLine();
            sb.AppendLine("2. 运行中的相关进程");
            sb.AppendLine(new string('-', 72));
            int procs = 0;
            foreach (string name in UninstallKillProcs)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(name);
                    foreach (Process p in ps)
                    {
                        sb.AppendLine("   · " + p.ProcessName + " (PID " + p.Id + ")");
                        procs++;
                        p.Dispose();
                    }
                }
                catch { }
            }
            if (procs == 0) { sb.AppendLine("   无"); }
            total += procs;
            Log(log, "审计 2/17 进程：" + procs);

            // 3 服务
            sb.AppendLine();
            sb.AppendLine("3. 相关服务");
            sb.AppendLine(new string('-', 72));
            int svcs = 0;
            foreach (string s in UninstallServices)
            {
                string st = GetServiceState(s);
                if (st != "未安装")
                {
                    sb.AppendLine("   · " + s + "  [" + st + "]");
                    svcs++;
                }
            }
            if (svcs == 0) { sb.AppendLine("   无"); }
            total += svcs;
            Log(log, "审计 3/17 服务：" + svcs);

            // 4/5 目录
            sb.AppendLine();
            sb.AppendLine("4. 程序目录（将被删除）");
            sb.AppendLine(new string('-', 72));
            int folders = AppendFolderAudit(sb, UninstallFolders);
            total += folders;
            Log(log, "审计 4/17 程序目录：" + folders);

            sb.AppendLine();
            sb.AppendLine("5. 用户数据目录（将被删除）");
            sb.AppendLine(new string('-', 72));
            var userDirs = new List<string>();
            try
            {
                userDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk"));
                userDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk"));
                userDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Autodesk"));
                userDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DC"));
                userDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ACCDocs"));
            }
            catch { }
            int ud = AppendFolderAudit(sb, userDirs);
            total += ud;
            Log(log, "审计 5/17 用户目录：" + ud);

            // 6 注册表分支
            sb.AppendLine();
            sb.AppendLine("6. Autodesk 注册表分支");
            sb.AppendLine(new string('-', 72));
            int regs = 0;
            foreach (string branch in UninstallRegistryBranches)
            {
                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                        using (RegistryKey k = baseKey.OpenSubKey(branch, false))
                        {
                            if (k != null)
                            {
                                string hiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                                int sub = 0;
                                try { sub = k.GetSubKeyNames().Length; } catch { }
                                sb.AppendLine("   · " + hiveName + "\\" + branch + "  （" + sub + " 个子项）");
                                regs++;
                            }
                        }
                    }
                    catch { }
                }
            }
            if (regs == 0) { sb.AppendLine("   无"); }
            total += regs;
            Log(log, "审计 6/17 注册表分支：" + regs);

            // 7 注册表深度扫描
            sb.AppendLine();
            sb.AppendLine("7. 注册表残留（Autodesk 相关项）");
            sb.AppendLine(new string('-', 72));
            int deep = 0;
            try
            {
                foreach (ResidueFinding f in ScanResidues(null))
                {
                    if (f.Type == "注册表")
                    {
                        if (deep < 60)
                        {
                            sb.AppendLine("   · " + f.Path);
                        }
                        deep++;
                    }
                }
            }
            catch { }
            if (deep == 0) { sb.AppendLine("   无"); }
            else if (deep > 60) { sb.AppendLine("   … 共 " + deep + " 项，此处仅列前 60 项"); }
            total += deep;
            Log(log, "审计 7/17 注册表残留：" + deep);

            // 8 旧版许可
            sb.AppendLine();
            sb.AppendLine("8. 旧版许可数据");
            sb.AppendLine(new string('-', 72));
            int lic = 0;
            foreach (string f in new[]
            {
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data",
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup",
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup.001",
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_event.log"
            })
            {
                try { if (File.Exists(f)) { sb.AppendLine("   · " + f); lic++; } } catch { }
            }
            try
            {
                string sl = @"C:\ProgramData\Autodesk\Software Licenses";
                if (Directory.Exists(sl)) { sb.AppendLine("   · " + sl); lic++; }
            }
            catch { }
            if (lic == 0) { sb.AppendLine("   无"); }
            total += lic;
            Log(log, "审计 8/17 旧版许可：" + lic);

            // 9 环境变量
            sb.AppendLine();
            sb.AppendLine("9. 相关环境变量");
            sb.AppendLine(new string('-', 72));
            int env = 0;
            foreach (string v in AutodeskEnvVarNames())
            {
                try
                {
                    string m = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.Machine);
                    string u = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.User);
                    if (!string.IsNullOrEmpty(m)) { sb.AppendLine("   · [系统] " + v + " = " + m); env++; }
                    if (!string.IsNullOrEmpty(u)) { sb.AppendLine("   · [用户] " + v + " = " + u); env++; }
                }
                catch { }
            }
            if (env == 0) { sb.AppendLine("   无"); }

            // 系统 PATH 中的 Autodesk 条目
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey e = k.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", false))
                {
                    if (e != null)
                    {
                        object raw = e.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (raw != null)
                        {
                            foreach (string part in raw.ToString().Split(';'))
                            {
                                if (part.Length > 0
                                    && (part.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                                     || part.IndexOf("AdODIS", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    sb.AppendLine("   · [系统 PATH] " + part);
                                    env++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            total += env;
            Log(log, "审计 9/17 环境变量：" + env);

            // 10 InstallerProducts 幽灵项
            sb.AppendLine();
            sb.AppendLine("10. Installer\\Products 幽灵项");
            sb.AppendLine(new string('-', 72));
            int ghosts = CountInstallerProductGhosts(log);
            if (ghosts == 0) { sb.AppendLine("   无"); }
            total += ghosts;
            Log(log, "审计 10/17 幽灵项：" + ghosts);

            // 11 快捷方式
            sb.AppendLine();
            sb.AppendLine("11. 快捷方式");
            sb.AppendLine(new string('-', 72));
            int scn = 0;
            foreach (string f in AutodeskShortcutPaths())
            {
                if (scn < 40) { sb.AppendLine("   · " + f); }
                scn++;
            }
            if (scn == 0) { sb.AppendLine("   无"); }
            else if (scn > 40) { sb.AppendLine("   … 共 " + scn + " 个，此处仅列前 40 个"); }
            total += scn;
            Log(log, "审计 11/17 快捷方式：" + scn);

            // 12 计划任务
            sb.AppendLine();
            sb.AppendLine("12. 计划任务");
            sb.AppendLine(new string('-', 72));
            List<string> tasks = AutodeskScheduledTasks();
            if (tasks.Count == 0) { sb.AppendLine("   无"); }
            else { foreach (string t in tasks) { sb.AppendLine("   · " + t); } }
            total += tasks.Count;
            Log(log, "审计 12/17 计划任务：" + tasks.Count);

            // 13 防火墙规则
            sb.AppendLine();
            sb.AppendLine("13. 防火墙规则");
            sb.AppendLine(new string('-', 72));
            List<string> fw = AutodeskFirewallRules();
            if (fw.Count == 0) { sb.AppendLine("   无"); }
            else { foreach (string r in fw) { sb.AppendLine("   · " + r); } }
            total += fw.Count;
            Log(log, "审计 13/17 防火墙规则：" + fw.Count);

            // 14 IFEO
            sb.AppendLine();
            sb.AppendLine("14. IFEO 调试器劫持");
            sb.AppendLine(new string('-', 72));
            int ifeo = 0;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = k.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", false))
                {
                    if (root != null)
                    {
                        foreach (string exe in UninstallIfeoExes)
                        {
                            using (RegistryKey sub = root.OpenSubKey(exe, false))
                            {
                                if (sub != null && sub.GetValue("Debugger") != null)
                                {
                                    sb.AppendLine("   · " + exe + "  →  " + sub.GetValue("Debugger"));
                                    ifeo++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            if (ifeo == 0) { sb.AppendLine("   无"); }
            total += ifeo;
            Log(log, "审计 14/17 IFEO：" + ifeo);

            // 15 PendingFileRenameOperations
            sb.AppendLine();
            sb.AppendLine("15. 待处理文件重命名操作");
            sb.AppendLine(new string('-', 72));
            bool pfro = false;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey sm = k.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", false))
                {
                    pfro = sm != null && sm.GetValue("PendingFileRenameOperations") != null;
                }
            }
            catch { }
            sb.AppendLine(pfro ? "   · 存在 PendingFileRenameOperations（会阻止安装）" : "   无");
            if (pfro) { total++; }
            Log(log, "审计 15/17 PFRO：" + (pfro ? "有" : "无"));

            // 16 hosts
            sb.AppendLine();
            sb.AppendLine("16. hosts 中的 Autodesk 条目");
            sb.AppendLine(new string('-', 72));
            List<string> hosts = AutodeskHostsEntries();
            if (hosts.Count == 0) { sb.AppendLine("   无"); }
            else { foreach (string h in hosts) { sb.AppendLine("   · " + h); } }
            total += hosts.Count;
            Log(log, "审计 16/17 hosts：" + hosts.Count);

            // 17 Desktop Connector
            sb.AppendLine();
            sb.AppendLine("17. Desktop Connector 工作区");
            sb.AppendLine(new string('-', 72));
            int dtc = AppendFolderAudit(sb, DesktopConnectorFolders());
            total += dtc;
            Log(log, "审计 17/17 Desktop Connector：" + dtc);

            sb.AppendLine();
            sb.AppendLine(new string('=', 72));
            sb.AppendLine("合计：审计发现 " + total + " 项将被处理的内容。");
            sb.AppendLine("本报告为只读预览，未做任何修改。");
            return sb.ToString();
        }

        private static int AppendFolderAudit(StringBuilder sb, IEnumerable<string> dirs)
        {
            int n = 0;
            foreach (string d in dirs)
            {
                try
                {
                    if (Directory.Exists(d))
                    {
                        long size = 0;
                        int files = 0;
                        try
                        {
                            foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                            {
                                try { size += new FileInfo(f).Length; files++; } catch { }
                            }
                        }
                        catch { }
                        sb.AppendLine("   · " + d + "  （" + files + " 个文件，" + FormatSize(size) + "）");
                        n++;
                    }
                }
                catch { }
            }
            if (n == 0)
            {
                sb.AppendLine("   无");
            }
            return n;
        }

        // ==================== #9 Desktop Connector 工作区 ====================

        private static IEnumerable<string> DesktopConnectorFolders()
        {
            string p = null;
            try { p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            if (!string.IsNullOrEmpty(p))
            {
                yield return Path.Combine(p, "DC");
                yield return Path.Combine(p, "ACCDocs");
            }
        }

        /// <summary>统计 Desktop Connector 工作区（文件数与大小），供确认前展示。</summary>
        internal static string DescribeDesktopConnectorWorkspace()
        {
            int files = 0;
            long size = 0;
            var found = new List<string>();
            foreach (string d in DesktopConnectorFolders())
            {
                try
                {
                    if (!Directory.Exists(d))
                    {
                        continue;
                    }
                    found.Add(d);
                    foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                    {
                        try { size += new FileInfo(f).Length; files++; } catch { }
                    }
                }
                catch { }
            }
            if (found.Count == 0)
            {
                return null;
            }
            var sb = new StringBuilder();
            sb.AppendLine("检测到 Desktop Connector 本地工作区：");
            sb.AppendLine();
            foreach (string d in found)
            {
                sb.AppendLine("  · " + d);
            }
            sb.AppendLine();
            sb.AppendLine("合计：" + files + " 个文件，" + FormatSize(size));
            sb.AppendLine();
            sb.AppendLine("⚠ 这些目录存放 ACC / BIM 360 的本地同步项目文件。");
            sb.AppendLine("   尚未完整上传到云端的文件将被永久删除，无法恢复。");
            return sb.ToString();
        }

        /// <summary>删除 Desktop Connector 本地工作区（调用方必须已完成强确认）。</summary>
        internal static string CleanDesktopConnectorWorkspace(Action<string> log)
        {
            Log(log, "结束 Desktop Connector 进程并停服务 ...");
            KillProcess("DesktopConnector.Applications.Tray.exe");
            KillProcess("DesktopConnector.Core.Service.exe");
            StopService("DesktopConnectorService");

            int deleted = 0;
            foreach (string d in DesktopConnectorFolders())
            {
                bool existed = false;
                try { existed = Directory.Exists(d); } catch { }
                if (!existed)
                {
                    continue;
                }
                Log(log, "删除：" + d);
                DeleteDirectory(d, log);
                bool gone = true;
                try { gone = !Directory.Exists(d); } catch { }
                if (gone)
                {
                    deleted++;
                }
                else
                {
                    RetryLockedFolders(new List<string> { d }, log);
                    try { if (!Directory.Exists(d)) { deleted++; } } catch { }
                }
            }

            return deleted == 0
                ? "未删除任何目录（可能不存在，或全部删除失败）。"
                : "已删除 " + deleted + " 个 Desktop Connector 工作区目录。";
        }

        // ==================== #11 修复重启挂起 ====================

        /// <summary>检查并清除全部 5 类挂起的重启标记，然后刷新 msiserver。</summary>
        internal static string FixRestartPending(Action<string> log)
        {
            var found = new List<string>();
            var done = new List<string>();

            // 1 PendingFileRenameOperations
            Log(log, "[1/5] PendingFileRenameOperations ...");
            bool pfro = false;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey sm = k.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", true))
                {
                    if (sm != null && sm.GetValue("PendingFileRenameOperations") != null)
                    {
                        pfro = true;
                        if (DryRun)
                        {
                            DryNote("删除注册表值 PendingFileRenameOperations", log);
                        }
                        else
                        {
                            sm.DeleteValue("PendingFileRenameOperations", false);
                        }
                    }
                }
            }
            catch (Exception ex) { Log(log, "  处理失败：" + ex.Message); }
            if (pfro) { found.Add("PendingFileRenameOperations"); done.Add("已清除 PendingFileRenameOperations"); }

            // 2 WindowsUpdate Auto Update
            Log(log, "[2/5] WindowsUpdate Auto Update RebootRequired ...");
            bool wu = DeleteRegistryKeyReport(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", log);
            if (wu) { found.Add("WindowsUpdate RebootRequired"); done.Add("已清除 WindowsUpdate 挂起标记"); }

            // 3 Orchestrator
            Log(log, "[3/5] WindowsUpdate Orchestrator RebootRequired ...");
            bool orch = DeleteRegistryKeyReport(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\WindowsUpdate\Orchestrator\RebootRequired", log);
            if (orch) { found.Add("Orchestrator RebootRequired"); done.Add("已清除 Orchestrator 挂起标记"); }

            // 4 UpdateExeVolatile
            Log(log, "[4/5] Updates UpdateExeVolatile ...");
            bool uev = false;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey up = k.OpenSubKey(@"SOFTWARE\Microsoft\Updates", true))
                {
                    if (up != null)
                    {
                        object v = up.GetValue("UpdateExeVolatile");
                        if (v != null)
                        {
                            int iv;
                            if (int.TryParse(v.ToString(), out iv) && iv != 0)
                            {
                                uev = true;
                                RegWriteValue(up, "UpdateExeVolatile", 0, RegistryValueKind.DWord, log);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log(log, "  处理失败：" + ex.Message); }
            if (uev) { found.Add("UpdateExeVolatile 非零"); done.Add("已将 UpdateExeVolatile 置 0"); }

            // 5 CBS RebootPending
            Log(log, "[5/5] Component Based Servicing RebootPending ...");
            bool cbs = DeleteRegistryKeyReport(RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", log);
            if (cbs) { found.Add("CBS RebootPending"); done.Add("已清除 CBS 挂起标记"); }

            FlushInstallerServices(log);

            var sb = new StringBuilder();
            if (found.Count == 0)
            {
                sb.AppendLine("未发现挂起的重启标记，无需处理。");
            }
            else
            {
                sb.AppendLine("发现并清除了 " + found.Count + " 项挂起的重启标记：");
                sb.AppendLine();
                foreach (string d in done)
                {
                    sb.AppendLine("  · " + d);
                }
            }
            sb.AppendLine();
            sb.AppendLine("已刷新 Windows Installer 服务。");
            sb.AppendLine("建议重启系统，但通常可直接继续安装。");
            return sb.ToString();
        }

        private static bool DeleteRegistryKeyReport(RegistryHive hive, string subKey, Action<string> log)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(subKey, false))
                {
                    if (k == null)
                    {
                        return false;
                    }
                }
            }
            catch { return false; }

            DeleteRegistryKey(hive, subKey, log);
            return true;
        }

        // ==================== #12 备份模板与设置 ====================

        /// <summary>把 Autodesk 用户数据备份到指定目录。返回结果说明。</summary>
        internal static string BackupAutodeskData(string destRoot, Action<string> log)
        {
            var sources = new List<string>();
            try
            {
                sources.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk"));
                sources.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk"));
                sources.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Autodesk"));
            }
            catch { }

            var existing = new List<string>();
            int files = 0;
            long size = 0;
            foreach (string s in sources)
            {
                try
                {
                    if (!Directory.Exists(s))
                    {
                        continue;
                    }
                    existing.Add(s);
                    foreach (string f in Directory.GetFiles(s, "*", SearchOption.AllDirectories))
                    {
                        try { size += new FileInfo(f).Length; files++; } catch { }
                    }
                }
                catch { }
            }

            if (existing.Count == 0)
            {
                return "未找到可备份的 Autodesk 用户数据。";
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dest = Path.Combine(destRoot, "Autodesk_Backup_" + stamp);

            if (DryRun)
            {
                DryNote("备份 " + existing.Count + " 个目录（" + files + " 个文件，" + FormatSize(size) + "）到 " + dest, log);
                return "[预演] 将备份 " + existing.Count + " 个目录、" + files + " 个文件（" + FormatSize(size) + "）到："
                     + Environment.NewLine + dest;
            }

            try
            {
                if (!Directory.Exists(dest))
                {
                    Directory.CreateDirectory(dest);
                }
            }
            catch (Exception ex)
            {
                return "创建备份目录失败：" + dest + " -> " + ex.Message;
            }

            int copied = 0, failedCopy = 0;
            foreach (string s in existing)
            {
                string name = Path.GetFileName(s.TrimEnd('\\', '/'));
                string parent = Path.GetFileName(Path.GetDirectoryName(s));
                string target = Path.Combine(dest, parent + "_" + name);
                Log(log, "复制 " + s + "  →  " + target);
                CopyDirectory(s, target, ref copied, ref failedCopy);
            }

            var sb = new StringBuilder();
            sb.AppendLine("备份完成。");
            sb.AppendLine();
            sb.AppendLine("来源目录：" + existing.Count + " 个");
            sb.AppendLine("已复制文件：" + copied + " 个");
            if (failedCopy > 0)
            {
                sb.AppendLine("复制失败：" + failedCopy + " 个（可能被占用）");
            }
            sb.AppendLine("备份位置：" + dest);
            sb.AppendLine();
            sb.AppendLine("说明：备份的是用户配置与模板，不包含程序本体。");
            return sb.ToString();
        }

        private static void CopyDirectory(string source, string target, ref int copied, ref int failed)
        {
            try
            {
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                }
                foreach (string f in Directory.GetFiles(source))
                {
                    try
                    {
                        File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
                        copied++;
                    }
                    catch { failed++; }
                }
                foreach (string d in Directory.GetDirectories(source))
                {
                    CopyDirectory(d, Path.Combine(target, Path.GetFileName(d)), ref copied, ref failed);
                }
            }
            catch { }
        }

        // ==================== #7 / #12 共用清单 ====================

        private static IEnumerable<string> AutodeskShortcutPaths()
        {
            var dirs = new List<string>();
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)); } catch { }

            foreach (string dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }
                string[] files = null;
                try { files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); } catch { }
                if (files == null) { continue; }
                foreach (string f in files)
                {
                    string n;
                    try { n = Path.GetFileName(f); } catch { continue; }
                    if (AutodeskKeywordInName(n))
                    {
                        yield return f;
                    }
                }
            }
        }

        private static List<string> AutodeskScheduledTasks()
        {
            var list = new List<string>();
            string output = CaptureOutput("schtasks.exe", "/query /fo csv /nh");
            if (string.IsNullOrEmpty(output))
            {
                return list;
            }
            foreach (string line in output.Split('\n'))
            {
                string t = line.Trim();
                // 只处理 CSV 数据行；错误信息等非引号开头的行跳过
                if (t.Length == 0 || !t.StartsWith("\""))
                {
                    continue;
                }
                if (t.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                 || t.IndexOf("Adsk", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add(t);
                }
            }
            return list;
        }

        /// <summary>取 CSV 行的第一个字段（正确处理 \"\" 转义）。</summary>
        private static string FirstCsvField(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return "";
            }
            string s = line.TrimStart();
            if (s.Length == 0 || s[0] != '"')
            {
                int comma = s.IndexOf(',');
                return comma < 0 ? s.Trim() : s.Substring(0, comma).Trim();
            }

            var sb = new StringBuilder();
            for (int i = 1; i < s.Length; i++)
            {
                if (s[i] == '"')
                {
                    // 连续两个引号表示转义引号
                    if (i + 1 < s.Length && s[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                        continue;
                    }
                    break;   // 字段结束
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static int RemoveAutodeskScheduledTasks(Action<string> log)
        {
            int n = 0;
            foreach (string raw in AutodeskScheduledTasks())
            {
                string name = FirstCsvField(raw);
                if (name.Length == 0)
                {
                    continue;
                }
                Log(log, "  删除计划任务：" + name);
                RunCommand("schtasks.exe", "/delete /tn \"" + name + "\" /f", true);
                n++;
            }
            return n;
        }

        private static List<string> AutodeskFirewallRules()
        {
            var list = new List<string>();
            string output = CaptureOutput("netsh.exe", "advfirewall firewall show rule name=all");
            if (string.IsNullOrEmpty(output))
            {
                return list;
            }
            string current = null;
            foreach (string line in output.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("Rule Name", StringComparison.OrdinalIgnoreCase))
                {
                    int i = t.IndexOf(':');
                    current = i >= 0 ? t.Substring(i + 1).Trim() : null;

                    // 规则名本身可能就含 Autodesk（常见情况）
                    if (current != null
                        && (current.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                         || current.IndexOf("Adsk", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (!list.Contains(current))
                        {
                            list.Add(current);
                        }
                    }
                }
                else if (current != null && t.Length > 0
                      && (t.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                       || t.IndexOf("Adsk", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    if (!list.Contains(current))
                    {
                        list.Add(current);
                    }
                }
            }
            return list;
        }

        private static int RemoveAutodeskFirewallRules(Action<string> log)
        {
            int n = 0;
            foreach (string rule in AutodeskFirewallRules())
            {
                Log(log, "  删除防火墙规则：" + rule);
                RunCommand("netsh.exe", "advfirewall firewall delete rule name=\"" + rule + "\"", true);
                n++;
            }
            return n;
        }

        private static string[] AutodeskEnvVarNames()
        {
            return new[]
            {
                "ADSK_3DSMAX_LOAD_LANGUAGE", "ADSK_3DSMAX_STARTUP_LANGUAGE",
                "MAYA_UI_LANGUAGE", "MAYA_LOCATION",
                "ADSKFLEX_LICENSE_FILE", "LM_LICENSE_FILE",
                "ACAD", "ACADVER", "AUTOCAD_OD"
            };
        }

        private static void CleanAutodeskEnvVars(Action<string> log)
        {
            foreach (string v in AutodeskEnvVarNames())
            {
                foreach (EnvironmentVariableTarget t in new[]
                {
                    EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User
                })
                {
                    try
                    {
                        string cur = Environment.GetEnvironmentVariable(v, t);
                        if (string.IsNullOrEmpty(cur))
                        {
                            continue;
                        }
                        if (DryRun)
                        {
                            DryNote("清除环境变量 " + (t == EnvironmentVariableTarget.Machine ? "[系统]" : "[用户]") + " " + v, log);
                            continue;
                        }
                        Environment.SetEnvironmentVariable(v, null, t);
                        Log(log, "  已清除环境变量：" + v + "（" + t + "）");
                    }
                    catch { }
                }
            }
        }

        private static List<string> AutodeskHostsEntries()
        {
            var list = new List<string>();
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string hosts = Path.Combine(win, @"System32\drivers\etc\hosts");
                if (!File.Exists(hosts))
                {
                    return list;
                }
                foreach (string raw in File.ReadAllLines(hosts))
                {
                    string t = raw.Trim();
                    if (t.Length == 0 || t.StartsWith("#"))
                    {
                        continue;
                    }
                    if (t.IndexOf("autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        list.Add(t);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>统计 InstallerProducts 中注册但已无对应安装的幽灵项。</summary>
        private static int CountInstallerProductGhosts(Action<string> log)
        {
            int n = 0;
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products", false))
                {
                    if (root == null)
                    {
                        return 0;
                    }
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
                                // 有 InstallProperties 但无 DisplayName 的通常为幽灵项
                                using (RegistryKey ip = p.OpenSubKey("InstallProperties", false))
                                {
                                    if (ip != null)
                                    {
                                        object dn = ip.GetValue("DisplayName");
                                        if (dn == null || string.IsNullOrWhiteSpace(dn.ToString()))
                                        {
                                            n++;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return n;
        }

        /// <summary>捕获外部命令的标准输出。</summary>
        private static string CaptureOutput(string file, string args)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (p == null)
                    {
                        return null;
                    }
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(30000);
                    return outp;
                }
            }
            catch
            {
                return null;
            }
        }

        // ==================== #5 17 项验证 ====================

        /// <summary>17 项只读验证，覆盖残留的各个维度。</summary>
        internal static List<VerifyItem> RunFullVerification(Action<string> log)
        {
            var list = new List<VerifyItem>();
            int idx = 0;

            void Add(string name, int count, string note)
            {
                list.Add(new VerifyItem
                {
                    Index = ++idx,
                    Name = name,
                    Status = count == 0 ? "正常" : "发现残留",
                    Detail = count == 0 ? "未发现" : (count + " 项" + (string.IsNullOrEmpty(note) ? "" : "  —— " + note)),
                    IsIssue = count > 0
                });
            }

            Log(log, "[1/17] 已注册产品 ...");
            int products = 0;
            try { products = ScanInstalledProducts().Count; } catch { }
            Add("已注册产品", products, "仍可卸载");

            Log(log, "[2/17] 运行中的进程 ...");
            int procs = 0;
            foreach (string name in UninstallKillProcs)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(name);
                    foreach (Process p in ps) { procs++; p.Dispose(); }
                }
                catch { }
            }
            Add("运行中的进程", procs, "正在运行");

            Log(log, "[3/17] 服务 ...");
            int svcs = 0;
            foreach (string s in UninstallServices)
            {
                if (GetServiceState(s) != "未安装") { svcs++; }
            }
            Add("服务", svcs, "服务仍注册");

            Log(log, "[4/17] 程序目录 ...");
            int folders = 0;
            foreach (string d in UninstallFolders)
            {
                try { if (Directory.Exists(d)) { folders++; } } catch { }
            }
            Add("程序目录", folders, "目录存在");

            Log(log, "[5/17] 用户数据目录 ...");
            int userDirs = 0;
            try
            {
                string[] ds =
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Autodesk")
                };
                foreach (string d in ds) { if (Directory.Exists(d)) { userDirs++; } }
            }
            catch { }
            Add("用户数据目录", userDirs, "含用户配置");

            Log(log, "[6/17] 注册表分支 ...");
            int regs = 0;
            foreach (string branch in UninstallRegistryBranches)
            {
                foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                        using (RegistryKey k = baseKey.OpenSubKey(branch, false))
                        {
                            if (k != null) { regs++; }
                        }
                    }
                    catch { }
                }
            }
            Add("注册表分支", regs, "分支存在");

            Log(log, "[7/17] 注册表深度扫描 ...");
            int deep = 0;
            try
            {
                foreach (ResidueFinding f in ScanResidues(null))
                {
                    if (f.Type == "注册表") { deep++; }
                }
            }
            catch { }
            Add("注册表残留", deep, "与 Autodesk 相关");

            Log(log, "[8/17] 旧版许可 ...");
            int lic = 0;
            foreach (string f in new[]
            {
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data",
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup",
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup.001",
                @"C:\ProgramData\FLEXnet\adskflex_00691b00_event.log"
            })
            {
                try { if (File.Exists(f)) { lic++; } } catch { }
            }
            Add("旧版许可数据", lic, "FLEXnet 许可文件");

            Log(log, "[9/17] 环境变量 ...");
            int env = 0;
            foreach (string v in AutodeskEnvVarNames())
            {
                try
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.Machine))) { env++; }
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.User))) { env++; }
                }
                catch { }
            }
            Add("环境变量", env, "Autodesk 相关变量");

            Log(log, "[10/17] Installer\\Products 幽灵项 ...");
            int ghosts = CountInstallerProductGhosts(log);
            Add("Installer 幽灵项", ghosts, "已无对应安装");

            Log(log, "[11/17] 快捷方式 ...");
            int scn = 0;
            foreach (string f in AutodeskShortcutPaths()) { scn++; }
            Add("快捷方式", scn, "桌面或开始菜单");

            Log(log, "[12/17] 计划任务 ...");
            int tasks = AutodeskScheduledTasks().Count;
            Add("计划任务", tasks, "Autodesk 相关任务");

            Log(log, "[13/17] 防火墙规则 ...");
            int fw = AutodeskFirewallRules().Count;
            Add("防火墙规则", fw, "Autodesk 相关规则");

            Log(log, "[14/17] IFEO 调试器劫持 ...");
            int ifeo = 0;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = k.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", false))
                {
                    if (root != null)
                    {
                        foreach (string exe in UninstallIfeoExes)
                        {
                            using (RegistryKey sub = root.OpenSubKey(exe, false))
                            {
                                if (sub != null && sub.GetValue("Debugger") != null) { ifeo++; }
                            }
                        }
                    }
                }
            }
            catch { }
            Add("IFEO 调试器劫持", ifeo, "被劫持的程序");

            Log(log, "[15/17] PendingFileRenameOperations ...");
            bool pfro = false;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey sm = k.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", false))
                {
                    pfro = sm != null && sm.GetValue("PendingFileRenameOperations") != null;
                }
            }
            catch { }
            Add("待处理重命名操作", pfro ? 1 : 0, "会阻止安装");

            Log(log, "[16/17] hosts 条目 ...");
            int hosts = AutodeskHostsEntries().Count;
            Add("hosts 条目", hosts, "会屏蔽许可服务器");

            Log(log, "[17/17] Desktop Connector ...");
            int dtc = 0;
            try
            {
                foreach (string d in DesktopConnectorFolders())
                {
                    if (Directory.Exists(d)) { dtc++; }
                }
            }
            catch { }
            Add("Desktop Connector 工作区", dtc, "本地同步目录");

            int issues = 0;
            foreach (VerifyItem v in list)
            {
                if (v.IsIssue) { issues++; }
            }
            Log(log, "17 项验证完成：" + issues + " 项存在残留");
            return list;
        }
    }
}
