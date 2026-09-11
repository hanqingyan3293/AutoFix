using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutoFix
{
    internal enum CheckLevel { Ok, Warn, Info }

    /// <summary>一条体检项。</summary>
    internal sealed class CheckItem
    {
        public string Group;
        public string Name;
        public string Value;
        public string Note;
        public CheckLevel Level;
    }

    /// <summary>一个已安装的 Autodesk 产品。</summary>
    internal sealed class ProductInfo
    {
        public string Name = "";
        public string GroupKey = "";
        public string Version = "";
        public string Publisher = "";
        public string InstallLocation = "";
        public string InstallDate = "";
        public string Architecture = "";
        public string UninstallString = "";
        public long EstimatedSizeKB;
    }

    /// <summary>检测报告：环境体检项 + 已安装产品列表。</summary>
    internal sealed class EnvironmentReport
    {
        public List<CheckItem> Checks = new List<CheckItem>();
        public List<ProductInfo> Products = new List<ProductInfo>();
        public string GeneratedAt = "";
    }

    internal static partial class RepairService
    {
        /// <summary>与「安装闪退修复」共用：会被 IFEO 劫持的安装相关程序。</summary>
        internal static readonly string[] CrashGuardExes =
        {
            "AcEventSync.exe", "AcQMod.exe", "ADPClientService.exe", "AdpSDKUtil.exe",
            "AdskAccessDialogUtility.exe", "AdskAccessService.exe", "AdskAccessServiceHost.exe",
            "AdskIdentityManager.exe", "AdskInstallerUpdateCheck.exe", "AdskUpdateCheck.exe",
            "AdSSO.exe", "Autodesk Access UI Host.exe", "DownloadManager.exe",
            "GenuineService.exe", "install_helper_tool.exe", "install_manager.exe",
            "ProcessManager.exe", "AdskAccessCore.exe", "LogAnalyzer.exe"
        };

        private const string AdskLicensingDir1 = @"C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing";
        private const string AdskLicensingDir2 = @"C:\ProgramData\Autodesk\AdskLicensingService";
        private const string PitFile = @"C:\ProgramData\Autodesk\Adlm\ProductInformation.pit";

        /// <summary>只读检测：系统环境 + Autodesk 组件状态 + 已安装产品。不修改任何设置。</summary>
        public static EnvironmentReport Detect(Action<string> log)
        {
            var r = new EnvironmentReport { GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };

            Log(log, "系统环境 ...");
            AddSystemChecks(r);
            AddTimeCheck(r);
            AddDriveChecks(r);

            Log(log, "网络与安全 ...");
            AddHostsCheck(r);
            AddSecurityCheck(r);

            Log(log, "目录与路径 ...");
            AddPathChecks(r);

            Log(log, "Autodesk 组件 ...");
            AddAutodeskChecks(r);
            AddSharedComponentChecks(r);

            Log(log, "Windows Installer 相关 ...");
            AddInstallerChecks(r);

            Log(log, "运行库 ...");
            AddRuntimeChecks(r);

            Log(log, "已安装产品 ...");
            r.Products = DetectProducts();
            Log(log, "产品扫描完成，共 " + r.Products.Count + " 项");

            return r;
        }

        // ---------- 系统环境 ----------

        private static void AddSystemChecks(EnvironmentReport r)
        {
            const string g = "系统环境";

            string os = "未知";
            try
            {
                os = Environment.OSVersion.VersionString;
            }
            catch { }
            Add(r, g, "操作系统", os, null, CheckLevel.Info);

            bool x64Os = false, x64Proc = false;
            try
            {
                x64Os = Environment.Is64BitOperatingSystem;
                x64Proc = Environment.Is64BitProcess;
            }
            catch { }
            Add(r, g, "系统架构",
                x64Os ? "64 位" : "32 位",
                x64Proc ? "本程序以 64 位运行" : "本程序以 32 位运行",
                CheckLevel.Info);

            bool admin = IsAdministrator();
            Add(r, g, "当前权限",
                admin ? "管理员" : "普通用户",
                admin ? null : "修复功能需要管理员权限",
                admin ? CheckLevel.Ok : CheckLevel.Warn);

            string dotnet = GetDotNetVersion();
            Add(r, g, ".NET Framework", dotnet, null,
                string.IsNullOrEmpty(dotnet) ? CheckLevel.Warn : CheckLevel.Info);
        }

        private static string GetDotNetVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", false))
                {
                    if (k != null)
                    {
                        object ver = k.GetValue("Version");
                        if (ver != null && !string.IsNullOrWhiteSpace(ver.ToString()))
                        {
                            return "v" + ver.ToString().Trim().TrimStart('v');
                        }
                        object rel = k.GetValue("Release");
                        if (rel != null)
                        {
                            return "4.x（Release " + rel + "）";
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5", false))
                {
                    if (k != null)
                    {
                        object ver = k.GetValue("Version");
                        if (ver != null)
                        {
                            return "v" + ver.ToString().Trim().TrimStart('v');
                        }
                    }
                }
            }
            catch { }

            return "未检测到 3.5 / 4.x";
        }

        // ---------- 目录与路径 ----------

        private static void AddPathChecks(EnvironmentReport r)
        {
            const string g = "目录与路径";

            string temp = "";
            try { temp = Path.GetTempPath(); } catch { }
            Add(r, g, "临时目录", string.IsNullOrEmpty(temp) ? "未获取到" : temp, null,
                string.IsNullOrEmpty(temp) ? CheckLevel.Warn : CheckLevel.Info);

            // 「我的文档」实际位置与有效性
            string docs = GetPersonalDocumentsFromRegistry();
            if (string.IsNullOrWhiteSpace(docs))
            {
                Add(r, g, "我的文档", "未读取到", "注册表中缺少 Personal 项", CheckLevel.Warn);
            }
            else
            {
                string normalized = null, drive = null;
                bool missing = false;
                try { missing = TryGetMissingDriveLetter(docs, out normalized, out drive); } catch { }
                if (missing)
                {
                    Add(r, g, "我的文档", docs,
                        "指向的磁盘 " + drive + " 不存在（可能引发错误1603）", CheckLevel.Warn);
                }
                else
                {
                    Add(r, g, "我的文档", docs, null, CheckLevel.Ok);
                }
            }

            const string winInstaller = @"C:\Windows\Installer";
            bool wi = false;
            try { wi = Directory.Exists(winInstaller); } catch { }
            Add(r, g, "Windows\\Installer", wi ? "存在" : "缺失",
                wi ? null : "缺失可能引发错误1632", wi ? CheckLevel.Ok : CheckLevel.Warn);
        }

        // ---------- Autodesk 组件 ----------

        private static void AddAutodeskChecks(EnvironmentReport r)
        {
            const string g = "Autodesk 组件";

            string svc = GetServiceState("AdskLicensingService");
            Add(r, g, "AdskLicensingService", svc,
                svc == "未安装" ? null : (svc == "正在运行" ? "修复前会先停止该服务" : null),
                svc == "未安装" ? CheckLevel.Info : CheckLevel.Ok);

            bool d1 = false, d2 = false;
            try { d1 = Directory.Exists(AdskLicensingDir1); } catch { }
            try { d2 = Directory.Exists(AdskLicensingDir2); } catch { }
            int dirCount = (d1 ? 1 : 0) + (d2 ? 1 : 0);
            Add(r, g, "AdskLicensing 目录",
                dirCount == 0 ? "未发现" : dirCount + " 个存在",
                dirCount == 0 ? null : "错误1603 / 270 / 1 会删除这两个目录",
                dirCount == 0 ? CheckLevel.Info : CheckLevel.Warn);

            bool pit = false;
            try { pit = File.Exists(PitFile); } catch { }
            if (!pit)
            {
                Add(r, g, "ProductInformation.pit", "不存在", null, CheckLevel.Info);
            }
            else
            {
                bool canDel = false;
                try { canDel = HasDeleteLikeAccess(PitFile); } catch { }
                Add(r, g, "ProductInformation.pit",
                    canDel ? "存在，权限正常" : "存在，当前用户缺少删除级权限",
                    canDel ? null : "错误1603 会为其补足权限",
                    canDel ? CheckLevel.Ok : CheckLevel.Warn);
            }

            int genuine = 0;
            foreach (string d in GenuineServiceDirs())
            {
                try { if (Directory.Exists(d)) genuine++; } catch { }
            }
            Add(r, g, "Genuine Service 目录",
                genuine == 0 ? "未发现" : genuine + " 个存在",
                genuine == 0 ? null : "可用「强制删除AGS」移除",
                genuine == 0 ? CheckLevel.Info : CheckLevel.Warn);

            int lic = 0;
            foreach (string f in FlexNetLicenseFiles())
            {
                try { if (File.Exists(f)) lic++; } catch { }
            }
            Add(r, g, "FLEXnet 许可数据",
                lic == 0 ? "未发现" : lic + " 个文件存在",
                lic == 0 ? null : "可用「删除许可09-21」清理",
                lic == 0 ? CheckLevel.Info : CheckLevel.Warn);
        }

        // ---------- Windows Installer 相关 ----------

        private static void AddInstallerChecks(EnvironmentReport r)
        {
            const string g = "Windows Installer";

            string ms = GetServiceState("msiserver");
            Add(r, g, "Windows Installer 服务", ms, null,
                ms == "未安装" ? CheckLevel.Warn : CheckLevel.Info);

            bool reboot = false;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey sub = k.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", false))
                {
                    reboot = sub != null;
                }
            }
            catch { }
            Add(r, g, "挂起的重启标记",
                reboot ? "存在" : "不存在",
                reboot ? "可能引发安装时反复重启，可用「结束无限重启」清除" : null,
                reboot ? CheckLevel.Warn : CheckLevel.Ok);

            int hijack = 0;
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey basePath = k.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", false))
                {
                    if (basePath != null)
                    {
                        foreach (string exe in CrashGuardExes)
                        {
                            try
                            {
                                using (RegistryKey sub = basePath.OpenSubKey(exe, false))
                                {
                                    if (sub != null && sub.GetValue("Debugger") != null)
                                    {
                                        hijack++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            Add(r, g, "调试器劫持项",
                hijack == 0 ? "未发现" : hijack + " 项存在",
                hijack == 0 ? null : "可能引发安装闪退，可用「安装闪退修复」清除",
                hijack == 0 ? CheckLevel.Ok : CheckLevel.Warn);
        }

        // ---------- 时间 ----------

        private static void AddTimeCheck(EnvironmentReport r)
        {
            const string g = "系统环境";
            string now = "";
            try
            {
                now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { }

            string tz = "";
            try
            {
                tz = TimeZoneInfo.Local.DisplayName;
            }
            catch { }

            int year = 0;
            try { year = DateTime.Now.Year; } catch { }
            bool sane = year >= 2020 && year <= 2030;

            Add(r, g, "系统时间", string.IsNullOrEmpty(now) ? "未获取到" : now,
                sane ? (string.IsNullOrEmpty(tz) ? null : tz)
                     : "年份异常（" + year + "），可能导致安装校验失败",
                sane ? CheckLevel.Ok : CheckLevel.Warn);
        }

        // ---------- 磁盘 ----------

        private static void AddDriveChecks(EnvironmentReport r)
        {
            const string g = "系统环境";
            const long lowGb = 5;

            DriveInfo[] drives = null;
            try { drives = DriveInfo.GetDrives(); } catch { }
            if (drives == null)
            {
                Add(r, g, "磁盘空间", "未获取到", null, CheckLevel.Info);
                return;
            }

            int reported = 0;
            foreach (DriveInfo d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady)
                    {
                        continue;
                    }
                    double freeGb = d.AvailableFreeSpace / 1073741824.0;
                    double totalGb = d.TotalSize / 1073741824.0;
                    bool low = freeGb < lowGb;
                    Add(r, g, "磁盘 " + d.Name.TrimEnd('\\'),
                        "剩余 " + freeGb.ToString("0.#") + " GB / 共 " + totalGb.ToString("0.#") + " GB",
                        low ? "剩余空间不足 " + lowGb + " GB，可能影响安装" : null,
                        low ? CheckLevel.Warn : CheckLevel.Ok);
                    reported++;
                }
                catch { }
            }

            if (reported == 0)
            {
                Add(r, g, "磁盘空间", "未发现固定磁盘", null, CheckLevel.Info);
            }
        }

        // ---------- 网络与安全 ----------

        private static void AddHostsCheck(EnvironmentReport r)
        {
            const string g = "网络与安全";
            string path = null;
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(win))
                {
                    path = Path.Combine(win, @"System32\drivers\etc\hosts");
                }
            }
            catch { }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Add(r, g, "hosts 文件", "未找到", null, CheckLevel.Info);
                return;
            }

            List<string> suspicious = new List<string>();
            int entries = 0;
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }
                    entries++;
                    string lower = line.ToLowerInvariant();
                    if (lower.Contains("autodesk") || lower.Contains("adsk") ||
                        lower.Contains("genuine") || lower.Contains("rj818"))
                    {
                        suspicious.Add(line);
                    }
                }
            }
            catch
            {
                Add(r, g, "hosts 文件", "读取失败", null, CheckLevel.Info);
                return;
            }

            if (suspicious.Count == 0)
            {
                Add(r, g, "hosts 文件",
                    entries == 0 ? "无自定义条目" : entries + " 条自定义条目，未见异常",
                    null, CheckLevel.Ok);
            }
            else
            {
                Add(r, g, "hosts 文件",
                    suspicious.Count + " 条可疑重定向",
                    "含 Autodesk 相关域名重定向，可能影响联网校验",
                    CheckLevel.Warn);
            }
        }

        private static void AddSecurityCheck(EnvironmentReport r)
        {
            const string g = "网络与安全";
            List<string> names = new List<string>();

            try
            {
                string wql = "SELECT displayName FROM AntiVirusProduct";
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\SecurityCenter2", wql))
                {
                    foreach (System.Management.ManagementBaseObject mo in searcher.Get())
                    {
                        try
                        {
                            object dn = mo["displayName"];
                            if (dn != null && !string.IsNullOrWhiteSpace(dn.ToString()))
                            {
                                string n = dn.ToString().Trim();
                                if (!names.Contains(n))
                                {
                                    names.Add(n);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (names.Count == 0)
            {
                Add(r, g, "安全软件", "未读取到", "无法枚举，可能不影响使用", CheckLevel.Info);
                return;
            }

            bool thirdParty = false;
            foreach (string n in names)
            {
                string lower = n.ToLowerInvariant();
                if (!lower.Contains("defender") && !lower.Contains("microsoft"))
                {
                    thirdParty = true;
                }
            }

            Add(r, g, "安全软件", string.Join("、", names.ToArray()),
                thirdParty ? "检测到第三方安全软件，可能拦截安装程序的注册表/服务操作" : null,
                thirdParty ? CheckLevel.Warn : CheckLevel.Ok);
        }

        // ---------- Autodesk 共享组件 ----------

        private static void AddSharedComponentChecks(EnvironmentReport r)
        {
            const string g = "Autodesk 组件";
            AddComponentProbe(r, g, "AdODIS（安装框架）", "AdODIS");
            AddComponentProbe(r, g, "AdskIdentityManager（身份组件）", "Identity Manager");
            AddComponentProbe(r, g, "Autodesk Access（更新组件）", "Autodesk Access");
        }

        private static void AddComponentProbe(EnvironmentReport r, string group,
            string display, string keyword)
        {
            string version = FindUninstallVersion(keyword);
            if (version == null)
            {
                Add(r, group, display, "未安装", null, CheckLevel.Info);
            }
            else
            {
                Add(r, group, display,
                    string.IsNullOrEmpty(version) ? "已安装" : "已安装 " + version,
                    null, CheckLevel.Ok);
            }
        }

        /// <summary>在注册表卸载项中按关键字查找组件版本；未找到返回 null。</summary>
        private static string FindUninstallVersion(string keyword)
        {
            var locations = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
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
                                    string name = ReadStr(item, "DisplayName");
                                    if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        return ReadStr(item, "DisplayVersion").Trim();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        // ---------- 运行库 ----------

        private static void AddRuntimeChecks(EnvironmentReport r)
        {
            const string g = "运行库";

            var found = new List<string>();
            var locations = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
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
                                    string name = ReadStr(item, "DisplayName");
                                    if (name.IndexOf("Visual C++", StringComparison.OrdinalIgnoreCase) < 0)
                                    {
                                        continue;
                                    }
                                    string ver = ReadStr(item, "DisplayVersion").Trim();
                                    string full = string.IsNullOrEmpty(ver) ? name.Trim() : name.Trim() + " " + ver;
                                    if (!found.Contains(full))
                                    {
                                        found.Add(full);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            if (found.Count == 0)
            {
                Add(r, g, "VC++ 运行库", "未检测到",
                    "缺少运行库可能导致 Autodesk 软件启动失败", CheckLevel.Warn);
            }
            else
            {
                found.Sort(StringComparer.OrdinalIgnoreCase);
                bool has2015Plus = false;
                foreach (string f in found)
                {
                    if (f.IndexOf("2015", StringComparison.OrdinalIgnoreCase) >= 0
                     || f.IndexOf("2017", StringComparison.OrdinalIgnoreCase) >= 0
                     || f.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0
                     || f.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        has2015Plus = true;
                    }
                }

                Add(r, g, "VC++ 运行库",
                    "已安装 " + found.Count + " 项",
                    string.Join("、", found.ToArray()),
                    has2015Plus ? CheckLevel.Ok : CheckLevel.Warn);
            }
        }



        /// <summary>把产品名规整为分组键：去掉年份与版本号。</summary>
        internal static string GroupKeyOf(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }
            string s = name;
            try
            {
                s = Regex.Replace(s, @"\b(19|20)\d{2}\b", " ");
                s = Regex.Replace(s, @"\bv?\d+(?:\.\d+)+\b", " ", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            }
            catch { }
            return string.IsNullOrWhiteSpace(s) ? name.Trim() : s;
        }

        // ---------- 已安装产品 ----------


        private static List<ProductInfo> DetectProducts()
        {
            var result = new List<ProductInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] keywords =
            {
                "Autodesk", "AutoCAD", "Revit", "3ds Max", "Maya", "Inventor",
                "Civil 3D", "Navisworks", "Plant 3D", "Mudbox", "MotionBuilder", "PowerMill",
                "AutoCAD Architecture", "AutoCAD Electrical", "AutoCAD Mechanical"
            };

            var locations = new List<Tuple<RegistryHive, RegistryView, string>>
            {
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry64,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.LocalMachine, RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry64,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(RegistryHive.CurrentUser, RegistryView.Registry32,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
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

                                    string name = ReadStr(item, "DisplayName");
                                    if (string.IsNullOrWhiteSpace(name))
                                    {
                                        continue;
                                    }

                                    bool match = false;
                                    foreach (string kw in keywords)
                                    {
                                        if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            match = true;
                                            break;
                                        }
                                    }
                                    if (!match)
                                    {
                                        continue;
                                    }

                                    string version = ReadStr(item, "DisplayVersion");
                                    string key = name.Trim() + "|" + version.Trim();
                                    if (!seen.Add(key))
                                    {
                                        continue;
                                    }

                                    var p = new ProductInfo
                                    {
                                        Name = name.Trim(),
                                        GroupKey = GroupKeyOf(name.Trim()),
                                        Version = version.Trim(),
                                        Publisher = ReadStr(item, "Publisher").Trim(),
                                        InstallLocation = ReadStr(item, "InstallLocation").Trim(),
                                        InstallDate = FormatInstallDate(ReadStr(item, "InstallDate")),
                                        UninstallString = ReadStr(item, "UninstallString").Trim(),
                                        Architecture = loc.Item2 == RegistryView.Registry64 ? "64 位" : "32 位"
                                    };

                                    object size = item.GetValue("EstimatedSize");
                                    if (size != null)
                                    {
                                        long kb;
                                        if (long.TryParse(size.ToString(), out kb))
                                        {
                                            p.EstimatedSizeKB = kb;
                                        }
                                    }

                                    result.Add(p);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            result.Sort((a, b) =>
            {
                int c = string.Compare(a.GroupKey, b.GroupKey, StringComparison.OrdinalIgnoreCase);
                if (c != 0)
                {
                    return c;
                }
                c = string.Compare(a.Version, b.Version, StringComparison.OrdinalIgnoreCase);
                if (c != 0)
                {
                    return c;
                }
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static string ReadStr(RegistryKey key, string name)
        {
            try
            {
                object v = key.GetValue(name);
                return v == null ? "" : v.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string FormatInstallDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Length != 8)
            {
                return raw == null ? "" : raw.Trim();
            }
            string s = raw.Trim();
            return s.Substring(0, 4) + "-" + s.Substring(4, 2) + "-" + s.Substring(6, 2);
        }

        // ---------- 共用读取 ----------

        private static void Add(EnvironmentReport r, string group, string name, string value,
            string note, CheckLevel level)
        {
            r.Checks.Add(new CheckItem
            {
                Group = group,
                Name = name,
                Value = value ?? "",
                Note = note,
                Level = level
            });
        }

        private static string GetServiceState(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    ServiceControllerStatus st = sc.Status;
                    switch (st)
                    {
                        case ServiceControllerStatus.Running: return "正在运行";
                        case ServiceControllerStatus.Stopped: return "已停止";
                        case ServiceControllerStatus.StartPending: return "启动中";
                        case ServiceControllerStatus.StopPending: return "停止中";
                        case ServiceControllerStatus.Paused: return "已暂停";
                        default: return st.ToString();
                    }
                }
            }
            catch
            {
                return "未安装";
            }
        }

        private static IEnumerable<string> GenuineServiceDirs()
        {
            yield return @"C:\Program Files\Autodesk\Genuine Service";
            yield return @"C:\ProgramData\Autodesk\Genuine Autodesk Service";
            yield return @"C:\ProgramData\Autodesk\Genuine Service";

            string user = null;
            try { user = Environment.UserName; } catch { }
            if (!string.IsNullOrEmpty(user))
            {
                yield return @"C:\Users\" + user + @"\Autodesk\Genuine Service";
            }

            string localAppData = null;
            try
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            catch { }
            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return Path.Combine(localAppData, "Autodesk", "Genuine Autodesk Service");
                yield return Path.Combine(localAppData, "Programs", "Autodesk", "Genuine Service");
            }
        }

        private static IEnumerable<string> FlexNetLicenseFiles()
        {
            yield return @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data";
            yield return @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup";
            yield return @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup.001";
            yield return @"C:\ProgramData\FLEXnet\adskflex_00691b00_event.log";
        }
    }
}
