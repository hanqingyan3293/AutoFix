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
    /// <summary>一个已安装的 Autodesk 产品。</summary>
    internal sealed class ProductItem
    {
        public string Name = "";
        public string Version = "";
        public string Publisher = "";
        public string InstallLocation = "";
        public string UninstallString = "";
        public string QuietUninstallString = "";
        public string RegistryPath = "";
        public string ProductCode = "";
    }

    // 产品卸载清理：按参考项目（MIT）的数据清单与阶段设计实现。
    internal static partial class RepairService
    {
        // ---- 数据清单（来自 autodesk-complete-uninstaller，MIT）----

        /// <summary>需要结束的进程。</summary>
        internal static readonly string[] UninstallKillProcs =
        {
            "AdSSO", "AdskLicensingService", "AdskLicensingAgent", "AdskIdentityManager",
            "GenuineService", "AdAppMgrSvc", "AutodeskDesktopApp", "RevitAccelerator",
            "acad", "lmgrd", "adskflex", "AdskAccessServiceHost", "AdskAccessService",
            "AdskAccessCore", "ADPClientService", "AcEventSync", "FNPLicensingService64",
            "Installer", "setup", "AdODIS", "DesktopConnector.Applications.Tray",
            "DesktopConnector.Core.Service", "AdskAccessUIHost"
        };

        /// <summary>需要停止的服务。</summary>
        internal static readonly string[] UninstallServices =
        {
            "AdskLicensingService", "AdskAccessServiceHost", "AdAppMgrSvc",
            "AdskNLM", "DesktopConnectorService"
        };

        /// <summary>需要清理的 HKCU 命名类键。</summary>
        private static readonly string[] UninstallClassKeys =
        {
            "AutodeskDGN", "AutoLISPFile", "3dsFile", "cdc_auto_file",
            "CompleteR16PlotConfigurationFile", "adsk.idmgr", "adskidmgr"
        };

        /// <summary>需检查 IFEO 调试器劫持的可执行文件。</summary>
        private static readonly string[] UninstallIfeoExes =
        {
            "ProcessManager.exe", "DownloadManager.exe", "InstallManager.exe",
            "install_manager.exe", "install_helper_tool.exe", "AdODIS-installer.exe",
            "GenuineService.exe", "AdskIdentityManager.exe", "Installer.exe",
            "AdskAccessServiceHost.exe", "AdskAccessService.exe", "AdskAccessCore.exe",
            "AdSSO.exe", "AdskLicensingService.exe", "LogAnalyzer.exe"
        };

        /// <summary>需要删除的目录（深度清理阶段）。</summary>
        private static readonly string[] UninstallFolders =
        {
            // 与参考项目 Phase E 的清单逐项对齐
            @"C:\Program Files\Autodesk",
            @"C:\Program Files\Common Files\Autodesk Shared",
            @"C:\Program Files\Common Files\Autodesk",
            @"C:\Program Files (x86)\Autodesk",
            @"C:\Program Files (x86)\Common Files\Autodesk Shared",
            @"C:\Program Files (x86)\Common Files\Autodesk",
            @"C:\ProgramData\Autodesk",
            @"C:\Users\Public\Documents\Autodesk",
            @"C:\Users\Public\Autodesk",
            @"C:\Autodesk",
            // 注意：Macrovision Shared\\FlexNet Publisher 被多个厂商共用（Adobe / PTC / Siemens 等）。
            // 参考项目在此无条件删除；本工具保留该行为，但在确认框中单独警示。
            @"C:\Program Files\Common Files\Macrovision Shared"
        };

        /// <summary>用户级 Autodesk 目录（随当前用户清理）。</summary>
        private static IEnumerable<string> UserLevelFolders()
        {
            string roaming = null, local = null;
            try { roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } catch { }
            try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } catch { }
            if (!string.IsNullOrEmpty(roaming))
            {
                yield return Path.Combine(roaming, "Autodesk");
            }
            if (!string.IsNullOrEmpty(local))
            {
                yield return Path.Combine(local, "Autodesk");
                yield return Path.Combine(local, "Programs", "Autodesk");
            }
        }

        /// <summary>需要清理的注册表分支（深度清理阶段）。</summary>
        private static readonly string[] UninstallRegistryBranches =
        {
            @"SOFTWARE\Autodesk",
            @"SOFTWARE\WOW6432Node\Autodesk",
            @"SOFTWARE\Classes\Installer\Products",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Autodesk",
            @"SOFTWARE\Autodesk\AdODIS",
            @"SOFTWARE\Autodesk\UPI2"
        };

        /// <summary>扫描已安装的 Autodesk 产品（只读）。</summary>
        internal static List<ProductItem> ScanInstalledProducts()
        {
            var found = new List<ProductItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var locations = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var loc in locations)
            {
                string hiveName = loc.Item1 == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(loc.Item1, loc.Item2))
                    using (RegistryKey root = baseKey.OpenSubKey(loc.Item3, false))
                    {
                        if (root == null)
                        {
                            continue;
                        }
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
                                    string name = Str(item, "DisplayName");
                                    string pub = Str(item, "Publisher");
                                    if (name.Length == 0)
                                    {
                                        continue;
                                    }
                                    if (name.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) < 0
                                     && pub.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) < 0)
                                    {
                                        continue;
                                    }

                                    string uninstall = Str(item, "UninstallString");
                                    string quiet = Str(item, "QuietUninstallString");
                                    if (uninstall.Length == 0 && quiet.Length == 0)
                                    {
                                        continue;
                                    }

                                    string ver = Str(item, "DisplayVersion");
                                    string key = name + "|" + ver + "|" + sub;
                                    if (!seen.Add(key))
                                    {
                                        continue;
                                    }

                                    found.Add(new ProductItem
                                    {
                                        Name = name,
                                        Version = ver,
                                        Publisher = pub,
                                        InstallLocation = Str(item, "InstallLocation"),
                                        UninstallString = uninstall,
                                        QuietUninstallString = quiet,
                                        RegistryPath = (loc.Item2 == RegistryView.Registry64 ? "64" : "32") + "|" + hiveName + "\\" + loc.Item3 + "\\" + sub,
                                        ProductCode = ExtractGuid(sub + " " + uninstall)
                                    });
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        private static string Str(RegistryKey key, string name)
        {
            try
            {
                object v = key.GetValue(name);
                return v == null ? "" : v.ToString();
            }
            catch { return ""; }
        }

        /// <summary>从字符串中提取第一个 {GUID}。</summary>
        private static string ExtractGuid(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            Match m = Regex.Match(text, @"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}");
            return m.Success ? m.Value : "";
        }

        /// <summary>
        /// 按阶段执行产品卸载。
        /// 阶段：还原点 → 停进程/服务 → 多轮卸载 → 目录 → 快捷方式 → 缓存 → 注册表 → 复查。
        /// </summary>
        internal static string UninstallProducts(List<ProductItem> selected, bool restorePoint,
            bool deepClean, bool multiUser, bool cleanInstallers, Action<string> log)
        {
            var report = new StringBuilder();
            int ok = 0, failed = 0;

            // --- Phase A：还原点 ---
            if (restorePoint)
            {
                Log(log, "[阶段 A] 创建系统还原点 ...");
                string rp = CreateRestorePoint(log);
                report.AppendLine("还原点：" + rp);
                if (!DryRun && rp.Contains("失败"))
                {
                    report.AppendLine("（未能创建还原点，继续执行。若需回退请自行确认系统保护状态。）");
                }
            }

            // --- Phase B：停进程 / 服务 ---
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
            Thread.Sleep(1000);

            // --- Phase C：多轮卸载 ---
            Log(log, "[阶段 C] 卸载选中产品 ...");
            var remaining = new List<ProductItem>(selected);
            for (int pass = 1; pass <= 3 && remaining.Count > 0; pass++)
            {
                Log(log, "  第 " + pass + " 轮，待处理 " + remaining.Count + " 项");
                var next = new List<ProductItem>();
                foreach (ProductItem p in remaining)
                {
                    Log(log, "  → " + p.Name);
                    bool success = UninstallOne(p, log);
                    if (success)
                    {
                        ok++;
                    }
                    else
                    {
                        failed++;
                        next.Add(p);
                    }
                    Thread.Sleep(500);
                }
                remaining = next;
                if (remaining.Count > 0 && pass < 3)
                {
                    Log(log, "  重新扫描后再试 ...");
                }
            }

            if (remaining.Count > 0)
            {
                report.AppendLine("以下 " + remaining.Count + " 项未能正常卸载，可能需要手动处理：");
                foreach (ProductItem p in remaining)
                {
                    report.AppendLine("  · " + p.Name);
                }
            }

            // --- 深度清理 ---
            if (deepClean)
            {
                List<string> notes = RunDeepClean(false, multiUser, cleanInstallers, log);
                foreach (string n in notes)
                {
                    Log(log, "  · " + n);
                    report.AppendLine("  · " + n);
                }
            }

            // --- 复查 ---
            Log(log, "[复查] 重新扫描已安装产品 ...");
            int left = 0;
            if (!DryRun)
            {
                try { left = ScanInstalledProducts().Count; } catch { }
            }

            var sb = new StringBuilder();
            sb.AppendLine("产品卸载流程结束。");
            sb.AppendLine();
            sb.AppendLine("成功卸载：" + ok + " 项");
            if (failed > 0)
            {
                sb.AppendLine("卸载失败：" + failed + " 项（已重试 3 轮）");
            }
            if (deepClean)
            {
                sb.AppendLine("已执行深度清理：目录 / 快捷方式 / 缓存 / 注册表 / 服务注册");
            }
            if (!DryRun)
            {
                sb.AppendLine("复查后本机仍检测到 Autodesk 产品：" + left + " 项");
            }
            sb.AppendLine();
            sb.Append(report.ToString());
            sb.AppendLine();
            sb.AppendLine("建议重启电脑后再检查一次。");
            return sb.ToString();
        }

        /// <summary>卸载单个产品：优先 msiexec /x GUID，其次 UninstallString。</summary>
        private static bool UninstallOne(ProductItem p, Action<string> log)
        {
            if (DryRun)
            {
                if (!string.IsNullOrEmpty(p.ProductCode))
                {
                    DryNote("卸载：" + p.Name + "  →  msiexec /x " + p.ProductCode + " /qn", log);
                }
                else
                {
                    DryNote("卸载：" + p.Name + "  →  " + (p.QuietUninstallString.Length > 0 ? p.QuietUninstallString : p.UninstallString), log);
                }
                KillProcess("Installer.exe");
                return true;
            }

            // 1) 有 ProductCode 走 msiexec
            if (!string.IsNullOrEmpty(p.ProductCode))
            {
                if (TryRunMsi(p.ProductCode, "/qn", log))
                {
                    return true;
                }
                if (TryRunMsi(p.ProductCode, "/qb", log))
                {
                    return true;
                }
            }

            // 2) 走 QuietUninstallString / UninstallString
            string cmd = p.QuietUninstallString.Length > 0 ? p.QuietUninstallString : p.UninstallString;
            if (!string.IsNullOrEmpty(cmd))
            {
                // 已含 GUID 的 msiexec 命令，补静默参数
                string guid = ExtractGuid(cmd);
                if (guid.Length > 0 && cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0
                    && cmd.IndexOf("/q", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    if (TryRunMsi(guid, "/qn", log))
                    {
                        return true;
                    }
                }
                return RunUninstallString(cmd, log);
            }

            return false;
        }

        private static bool TryRunMsi(string guid, string quiet, Action<string> log)
        {
            string args = "/x " + guid + " " + quiet + " /norestart REMOVE=ALL REBOOT=ReallySuppress";
            Log(log, "    msiexec " + args);
            return RunAndWait("msiexec.exe", args, 600000, log);
        }

        private static bool RunUninstallString(string cmd, Action<string> log)
        {
            Log(log, "    " + cmd);
            string file;
            string args;
            SplitCommand(cmd, out file, out args);
            if (string.IsNullOrEmpty(file))
            {
                return false;
            }
            return RunAndWait(file, args, 600000, log);
        }

        /// <summary>把 "C:\path\app.exe" /arg 拆成文件名与参数。</summary>
        private static void SplitCommand(string cmd, out string file, out string args)
        {
            file = "";
            args = "";
            cmd = (cmd ?? "").Trim();
            if (cmd.Length == 0)
            {
                return;
            }
            if (cmd[0] == '"')
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0)
                {
                    file = cmd.Substring(1, end - 1);
                    args = cmd.Substring(end + 1).Trim();
                    return;
                }
            }
            int sp = cmd.IndexOf(' ');
            if (sp < 0)
            {
                file = cmd;
            }
            else
            {
                file = cmd.Substring(0, sp);
                args = cmd.Substring(sp + 1).Trim();
            }
        }

        private static bool RunAndWait(string file, string args, int timeoutMs, Action<string> log)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (p == null)
                    {
                        return false;
                    }
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        Log(log, "    超时，已终止");
                        return false;
                    }
                    int code = p.ExitCode;
                    Log(log, "    退出代码：" + code);
                    // 0 成功；3010 成功但需重启；1605/1614 表示已不存在 → 视为完成
                    return code == 0 || code == 3010 || code == 1605 || code == 1614;
                }
            }
            catch (Exception ex)
            {
                Log(log, "    执行失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>清理桌面与开始菜单中的 Autodesk 快捷方式。</summary>
        private static int CleanAutodeskShortcuts(Action<string> log)
        {
            int n = 0;
            var dirs = new List<string>();
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs)); } catch { }
            try { dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)); } catch { }

            foreach (string dir in dirs)
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    {
                        continue;
                    }
                    foreach (string f in Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories))
                    {
                        string name = Path.GetFileName(f);
                        if (AutodeskKeywordInName(name))
                        {
                            FsDeleteFile(f, log);
                            n++;
                        }
                    }
                }
                catch { }
            }
            return n;
        }

        /// <summary>清理 Autodesk 缓存目录。</summary>
        private static void CleanAutodeskCaches(Action<string> log)
        {
            var caches = new List<string>();
            try
            {
                caches.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk"));
                caches.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk"));
                caches.Add(Path.Combine(Path.GetTempPath(), "Autodesk"));
            }
            catch { }

            foreach (string c in caches)
            {
                DeleteDirectory(c, log);
            }
        }
    }
}
