using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace AutoFix
{
    /// <summary>一条残留记录。</summary>
    internal sealed class ResidueFinding
    {
        public string Type = "";      // 注册表 / 文件目录 / 服务 / 进程 / 卸载项 / 快捷方式
        public string Path = "";
        public string Detail = "";
    }

    // 残留检测：七个维度的只读扫描，不修改任何系统状态。
    internal static partial class RepairService
    {
        private static readonly string[] AutodeskKeywords =
        {
            "revit", "acad", "autocad", "aca &", "aca&", "maya", "3dsmax", "3ds max",
            "inventor", "civil 3d", "civil3d", "plant 3d", "plant3d", "navisworks",
            "motionbuilder", "mudbox", "fusion 360", "fusion360", "alias", "sketchbook",
            "softimage", "recap", "bim 360", "bim360", "results", "rex", "rsa"
        };

        private static readonly string[] ExcludedProducts =
        {
            "microsoft", "windows", "visual c++", "visual studio", "visual c#", "visual basic",
            "visual", "vba", "crt", ".net", "runtime", "sdk", "framework", "office",
            "sql server", "internet explorer", "edge", "azure", "xbox", "directx",
            "adobe", "adobe reader", "adobe acrobat", "photoshop", "illustrator",
            "premiere", "after effects", "intel", "nvidia", "amd", "java", "oracle",
            "google", "chrome", "firefox", "mozilla"
        };

        /// <summary>名称是否像 Autodesk 相关（含排除名单过滤）。</summary>
        internal static bool SoundsAutodesk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string s = text.ToLowerInvariant();

            foreach (string ex in ExcludedProducts)
            {
                if (s.Contains(ex))
                {
                    return false;
                }
            }
            if (s.Contains("autodesk"))
            {
                return true;
            }
            foreach (string kw in AutodeskKeywords)
            {
                if (s.Contains(kw))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 只读扫描 Autodesk 残留，15 个维度（对齐参考项目的残留扫描）：
        /// 产品 / 进程 / 服务 / 程序目录 / 数据目录 / 全盘搜索 / 注册表分支 /
        /// COM 注册 / HKCU 类注册 / 外壳扩展 / Installer 幽灵项 / 快捷方式任务环境变量 /
        /// IFEO / 待处理重命名 / hosts。
        /// </summary>
        public static List<ResidueFinding> ScanResidues(Action<string> log)
        {
            var found = new List<ResidueFinding>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string type, string path, string detail)
            {
                string key = type + "|" + path;
                if (seen.Add(key))
                {
                    found.Add(new ResidueFinding { Type = type, Path = path, Detail = detail });
                }
            }

            Log(log, "[ 1/15] 已注册产品 ...");
            ScanUninstallResidues(Add, log);

            Log(log, "[ 2/15] 运行中的进程 ...");
            ScanProcessResidues(Add, log);

            Log(log, "[ 3/15] 服务 ...");
            ScanServiceResidues(Add, log);

            Log(log, "[ 4/15] 程序目录 ...");
            ScanFileResidues(Add, log);

            Log(log, "[ 5/15] 数据目录 ...");
            ScanDataFolders(Add, log);

            Log(log, "[ 6/15] 全盘搜索 ...");
            ScanWholeSystem(Add, log);

            Log(log, "[ 7/15] 注册表分支 ...");
            ScanRegistryResidues(Add, log);

            Log(log, "[ 8/15] COM 注册（CLSID / TypeLib）...");
            ScanComRegistry(Add, log);

            Log(log, "[ 9/15] HKCU 类注册 ...");
            ScanUserClasses(Add, log);

            Log(log, "[10/15] 外壳扩展 ...");
            ScanShellExtensions(Add, log);

            Log(log, "[11/15] Installer 幽灵项 ...");
            ScanInstallerGhosts(Add, log);

            Log(log, "[12/15] 快捷方式 / 计划任务 / 环境变量 ...");
            ScanShortcutResidues(Add, log);
            ScanTasksAndEnv(Add, log);

            Log(log, "[13/15] IFEO 调试器劫持 ...");
            ScanIfeo(Add, log);

            Log(log, "[14/15] 待处理文件重命名 ...");
            ScanPendingRename(Add, log);

            Log(log, "[15/15] hosts 条目 ...");
            ScanHosts(Add, log);

            Log(log, "扫描完成，共 " + found.Count + " 项");
            return found;
        }

        // ---------- 注册表 ----------

        private static void ScanRegistryResidues(Action<string, string, string> add, Action<string> log)
        {
            var bases = new List<Tuple<RegistryHive, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, @"SOFTWARE\Autodesk"),
                Tuple.Create(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Autodesk"),
                Tuple.Create(RegistryHive.CurrentUser, @"SOFTWARE\Autodesk"),
                Tuple.Create(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData"),
                Tuple.Create(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Installer\UserData"),
                Tuple.Create(RegistryHive.LocalMachine, @"SOFTWARE\Classes\Installer\Features"),
                Tuple.Create(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Classes\Installer\Features"),
                Tuple.Create(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Installer\Features"),
                Tuple.Create(RegistryHive.CurrentUser, @"SOFTWARE\Classes\Installer\Features")
            };

            foreach (var b in bases)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(b.Item1, RegistryView.Registry64))
                    using (RegistryKey root = baseKey.OpenSubKey(b.Item2, false))
                    {
                        if (root == null)
                        {
                            continue;
                        }

                        string hiveName = b.Item1 == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                        string full = hiveName + "\\" + b.Item2;

                        // 分支本身即 Autodesk，或分支下存在 Autodesk 相关子项
                        bool selfIsAutodesk = b.Item2.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0;
                        int listed = 0;
                        foreach (string sub in root.GetSubKeyNames())
                        {
                            if (SoundsAutodesk(sub) || selfIsAutodesk)
                            {
                                add("注册表", full + "\\" + sub, "注册表项");
                                listed++;
                                if (listed >= 300)
                                {
                                    add("注册表", full, "… 子项过多，仅列出前 300 项");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(log, "  读取失败：" + b.Item2 + " -> " + ex.Message);
                }
            }
        }

        // ---------- 文件与目录 ----------

        private static void ScanFileResidues(Action<string, string, string> add, Action<string> log)
        {
            var known = new List<string>
            {
                @"C:\Program Files\Autodesk",
                @"C:\Program Files (x86)\Autodesk",
                @"C:\ProgramData\Autodesk",
                @"C:\Program Files\Common Files\Autodesk Shared",
                @"C:\Program Files\Common Files\Autodesk",
                @"C:\Program Files\Common Files\Macrovision Shared\FlexNet Publisher",
                @"C:\Program Files (x86)\Common Files\Macrovision Shared\FlexNet Publisher",
                @"C:\Program Files (x86)\Common Files\Autodesk Shared",
                @"C:\Program Files\Autodesk\AdODIS"
            };

            // 用户目录下的 Autodesk 相关位置
            try
            {
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                {
                    known.Add(Path.Combine(profile, "Autodesk"));
                    known.Add(Path.Combine(profile, "AppData", "Local", "Autodesk"));
                    known.Add(Path.Combine(profile, "AppData", "Roaming", "Autodesk"));
                }
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
                if (!string.IsNullOrEmpty(docs))
                {
                    known.Add(Path.Combine(docs, "Autodesk"));
                }
            }
            catch { }

            foreach (string dir in known)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        long size = 0;
                        try
                        {
                            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                            {
                                try { size += new FileInfo(f).Length; } catch { }
                            }
                        }
                        catch { }
                        add("文件目录", dir, "目录存在" + (size > 0 ? "，约 " + FormatSize(size) : ""));
                    }
                }
                catch { }
            }

            // C 盘根目录下名称含 Autodesk 关键字的目录
            try
            {
                foreach (string dir in Directory.GetDirectories(@"C:\"))
                {
                    string name;
                    try { name = Path.GetFileName(dir); } catch { continue; }
                    if (AutodeskKeywordInName(name))
                    {
                        add("文件目录", dir.TrimEnd('\\', '/'), "根目录下的 Autodesk 相关目录");
                    }
                }
            }
            catch { }

            // 公共文档与用户目录下的 Autodesk 相关子目录
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
                if (!string.IsNullOrEmpty(docs) && Directory.Exists(docs))
                {
                    foreach (string dir in Directory.GetDirectories(docs))
                    {
                        string name;
                        try { name = Path.GetFileName(dir); } catch { continue; }
                        if (AutodeskKeywordInName(name))
                        {
                            add("文件目录", dir, "公共文档下的 Autodesk 相关目录");
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>文件名关键字判断（不含排除名单，用于目录名）。</summary>
        private static bool AutodeskKeywordInName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            string s = name.ToLowerInvariant();
            if (s.Contains("autodesk"))
            {
                return true;
            }
            foreach (string kw in AutodeskKeywords)
            {
                if (s.Contains(kw))
                {
                    return true;
                }
            }
            return false;
        }

        // ---------- 服务 ----------

        private static void ScanServiceResidues(Action<string, string, string> add, Action<string> log)
        {
            try
            {
                foreach (ServiceController sc in ServiceController.GetServices())
                {
                    try
                    {
                        string display = sc.DisplayName ?? "";
                        string name = sc.ServiceName ?? "";
                        bool hit = display.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                                || display.IndexOf("FlexNet Licensing Service", StringComparison.OrdinalIgnoreCase) >= 0
                                || name.IndexOf("FlexNet Licensing Service", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (hit)
                        {
                            string state;
                            try { state = sc.Status.ToString(); } catch { state = "未知"; }
                            add("服务", name, display + "  状态：" + state);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { sc.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, "  服务枚举失败（可能需要管理员权限）：" + ex.Message);
            }
        }

        // ---------- 进程 ----------

        private static void ScanProcessResidues(Action<string, string, string> add, Action<string> log)
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string name = (p.ProcessName ?? "").ToLowerInvariant();
                        string file = "";
                        try { file = p.MainModule == null ? "" : (p.MainModule.FileName ?? ""); } catch { }

                        bool hit = name.Contains("autodesk") || name.Contains("adsk") || name.Contains("acad")
                                || name.Contains("3dsmax") || name.Contains("maya") || name.Contains("revit")
                                || (!string.IsNullOrEmpty(file) && file.IndexOf("autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                                || name == "adsklicensingagent" || name == "wscommcntr4" || name == "lmu";

                        if (hit)
                        {
                            add("进程", p.ProcessName + " (PID " + p.Id + ")",
                                string.IsNullOrEmpty(file) ? "正在运行" : file);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, "  进程枚举失败：" + ex.Message);
            }
        }

        // ---------- 卸载项 ----------

        private static void ScanUninstallResidues(Action<string, string, string> add, Action<string> log)
        {
            var locations = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            foreach (var loc in locations)
            {
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
                                    string display = item.GetValue("DisplayName") == null ? "" : item.GetValue("DisplayName").ToString();
                                    string publisher = item.GetValue("Publisher") == null ? "" : item.GetValue("Publisher").ToString();
                                    if (display.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                                     || publisher.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        string hive = loc.Item1 == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                                        add("卸载项", hive + "\\" + loc.Item3 + "\\" + sub, display);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        // ---------- 快捷方式 ----------

        private static void ScanShortcutResidues(Action<string, string, string> add, Action<string> log)
        {
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
                        string name;
                        try { name = Path.GetFileName(f); } catch { continue; }
                        if (AutodeskKeywordInName(name))
                        {
                            add("快捷方式", f, "快捷方式");
                        }
                    }
                }
                catch { }
            }
        }
    }
}
