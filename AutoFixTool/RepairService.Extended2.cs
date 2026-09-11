using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutoFix
{
    // 第二批扩展：编码修复、组件卸载、文件版本查询、配置初始化、卷缓存清理、目录权限、hosts 修复
    internal static partial class RepairService
    {
        // ==================== 编码 / ARX ====================

        /// <summary>修正系统代码页（ACP / MACCP / OEMCP = 936），修复 AutoCAD ARX 加载异常。</summary>
        public static string FixArxError(Action<string> log)
        {
            try
            {
                const string path = @"SYSTEM\CurrentControlSet\Control\Nls\CodePage";
                const string want = "936";
                string[] names = { "ACP", "MACCP", "OEMCP" };

                bool already = true;
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey key = baseKey.OpenSubKey(path, false))
                    {
                        if (key == null)
                        {
                            already = false;
                        }
                        else
                        {
                            foreach (string n in names)
                            {
                                object v = key.GetValue(n);
                                if (v == null || v.ToString() != want)
                                {
                                    already = false;
                                    break;
                                }
                            }
                        }
                    }

                    if (already)
                    {
                        return "代码页已为 936，无需修复。";
                    }

                    using (RegistryKey key = baseKey.OpenSubKey(path, true) ?? baseKey.CreateSubKey(path))
                    {
                        if (key == null)
                        {
                            return "修复失败：无法打开 " + path;
                        }
                        foreach (string n in names)
                        {
                            RegWriteValue(key, n, want, RegistryValueKind.String, log);
                            Log(log, "  " + n + " = " + want);
                        }
                    }
                }

                return "代码页已修正为 936，需要重启电脑后生效。";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复 ARX 异常失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复 ARX 异常时发生异常：" + ex.Message;
            }
        }

        /// <summary>卸载 AdskLicensing：停止服务、结束进程、删除两个残留目录。</summary>
        public static string UninstallAdskLicensing(Action<string> log)
        {
            try
            {
                StopLicensingStack(log);
                return "AdskLicensing 已卸载，请尝试重新安装！";
            }
            catch (Exception ex)
            {
                return "卸载 AdskLicensing 时发生异常：" + ex.Message;
            }
        }

        // ==================== 文件版本查询 ====================

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
            {
                return "0 B";
            }
            double v = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024.0 && i < units.Length - 1)
            {
                v /= 1024.0;
                i++;
            }
            return v.ToString("0.##") + " " + units[i];
        }

        /// <summary>按扩展名分派，查询 CAD / Revit / Maya 文件版本。只读。</summary>
        public static string QueryFileVersion(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return "文件不存在。";
            }
            string ext = Path.GetExtension(filePath).ToUpperInvariant();
            switch (ext)
            {
                case ".DWG":
                case ".DXF":
                    return QueryDwgDxfVersion(filePath);
                case ".RVT":
                    return QueryRevitVersion(filePath);
                case ".MA":
                case ".MB":
                    return QueryMayaVersion(filePath);
                default:
                    return "不支持的文件类型：" + ext + "\r\n\r\n支持 .dwg / .dxf / .rvt / .ma / .mb";
            }
        }

        private static string QueryDwgDxfVersion(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                string ext = Path.GetExtension(filePath).ToUpperInvariant();
                string code = null;

                if (ext == ".DXF")
                {
                    using (var reader = new StreamReader(filePath, Encoding.ASCII))
                    {
                        string line;
                        int n = 0;
                        while ((line = reader.ReadLine()) != null && n < 200)
                        {
                            n++;
                            line = line.Trim();
                            if (line == "$ACADVER" || line == "ACADVER")
                            {
                                string v = reader.ReadLine();
                                if (v != null)
                                {
                                    v = v.Trim();
                                    if (v == "1" || v == "9")
                                    {
                                        string actual = reader.ReadLine();
                                        if (actual != null)
                                        {
                                            code = actual.Trim();
                                            break;
                                        }
                                    }
                                    else if (v.StartsWith("AC", StringComparison.OrdinalIgnoreCase))
                                    {
                                        code = v;
                                        break;
                                    }
                                }
                            }
                            else if (line.StartsWith("AC10", StringComparison.OrdinalIgnoreCase) && line.Length == 6)
                            {
                                code = line;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] head = new byte[6];
                        if (fs.Read(head, 0, 6) >= 6)
                        {
                            code = Encoding.ASCII.GetString(head).Trim('\0', ' ');
                        }
                    }
                }

                string header = "文件：" + Path.GetFileName(filePath) + "\r\n大小：" + FormatSize(info.Length) + "\r\n";

                if (string.IsNullOrEmpty(code))
                {
                    return "CAD 文件版本：无法识别\r\n\r\n" + header + "\r\n文件格式可能异常。";
                }

                var map = new Dictionary<string, string>
                {
                    { "AC1009", "AutoCAD R11/R12" },
                    { "AC1012", "AutoCAD R13" },
                    { "AC1014", "AutoCAD R14" },
                    { "AC1015", "AutoCAD 2000" },
                    { "AC1018", "AutoCAD 2004" },
                    { "AC1021", "AutoCAD 2007" },
                    { "AC1024", "AutoCAD 2010" },
                    { "AC1027", "AutoCAD 2013" },
                    { "AC1032", "AutoCAD 2018 及以上" }
                };

                string name;
                if (map.TryGetValue(code, out name))
                {
                    return "CAD 文件版本：" + name + "\r\n版本代码：" + code + "\r\n\r\n" + header;
                }
                return "CAD 文件版本：未知（代码 " + code + "）\r\n\r\n" + header;
            }
            catch (Exception ex)
            {
                return "查询 CAD 文件版本失败：" + ex.Message;
            }
        }

        private static string QueryRevitVersion(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                string header = "文件：" + Path.GetFileName(filePath) + "\r\n大小：" + FormatSize(info.Length) + "\r\n";

                int take = (int)Math.Min(65536L, info.Length);
                byte[] buf = new byte[take];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Read(buf, 0, take) < 100)
                    {
                        return "Revit 文件版本：无法识别（文件过小）\r\n\r\n" + header;
                    }
                }

                foreach (string encName in new[] { "Unicode", "UTF-8", "ASCII" })
                {
                    try
                    {
                        string text = Encoding.GetEncoding(encName).GetString(buf);
                        Match m = Regex.Match(text, @"(?<=Autodesk Revit )20\d{2}");
                        if (m.Success)
                        {
                            return "Revit 文件版本：" + m.Value + "\r\n\r\n" + header;
                        }
                        m = Regex.Match(text, @"Revit\s+(\d{4})");
                        if (m.Success)
                        {
                            return "Revit 文件版本：" + m.Groups[1].Value + "\r\n\r\n" + header;
                        }
                    }
                    catch { }
                }

                return "Revit 文件版本：无法识别\r\n\r\n" + header + "\r\n文件可能已损坏或不是标准 RVT。";
            }
            catch (Exception ex)
            {
                return "查询 Revit 文件版本失败：" + ex.Message;
            }
        }

        private static string QueryMayaVersion(string filePath)
        {
            try
            {
                var info = new FileInfo(filePath);
                string header = "文件：" + Path.GetFileName(filePath) + "\r\n大小：" + FormatSize(info.Length) + "\r\n";
                string ext = Path.GetExtension(filePath).ToUpperInvariant();

                int take = (int)Math.Min(65536L, info.Length);
                byte[] buf = new byte[take];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Read(buf, 0, take);
                }

                foreach (string encName in new[] { "UTF-8", "ASCII", "Unicode" })
                {
                    try
                    {
                        string text = Encoding.GetEncoding(encName).GetString(buf);
                        if (text.IndexOf("Maya", StringComparison.OrdinalIgnoreCase) < 0 &&
                            text.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        Match m = Regex.Match(text, @"Maya\s+(20\d{2})", RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            return "Maya 文件版本：" + m.Groups[1].Value + "\r\n\r\n" + header;
                        }
                        m = Regex.Match(text, @"(20\d{2})", RegexOptions.None);
                        if (m.Success)
                        {
                            return "Maya 文件版本（推测）：" + m.Groups[1].Value + "\r\n\r\n" + header;
                        }
                    }
                    catch { }
                }

                return "Maya 文件版本：无法识别\r\n\r\n" + header +
                       "\r\n提示：" + (ext == ".MB"
                           ? "MB 为二进制格式，版本信息通常需用 Maya 打开查看。"
                           : "MA 文件中未找到版本标记。");
            }
            catch (Exception ex)
            {
                return "查询 Maya 文件版本失败：" + ex.Message;
            }
        }

        // ==================== 配置初始化 ====================

        /// <summary>Maya 配置初始化：删除「我的文档\maya」下的用户配置。</summary>
        public static string InitializeMaya(Action<string> log)
        {
            try
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal), "maya");

                if (!Directory.Exists(root))
                {
                    return "未找到 Maya 配置文件夹，无需初始化。";
                }

                Log(log, "删除：" + root);
                FsDeleteDirectory(root, log);
                return "Maya 配置初始化完成！请重新启动 Maya，将生成全新配置。";
            }
            catch (UnauthorizedAccessException)
            {
                return "初始化 Maya 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "初始化 Maya 时发生异常：" + ex.Message;
            }
        }

        /// <summary>Revit 配置初始化：删除各版本用户配置目录与注册表 Shortcuts。</summary>
        public static string InitializeRevit(Action<string> log)
        {
            try
            {
                var dirs = new List<string>
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk", "Revit"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit")
                };

                int removed = 0;
                foreach (string root in dirs)
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }
                    foreach (string sub in Directory.GetDirectories(root, "Autodesk Revit *"))
                    {
                        Log(log, "删除：" + sub);
                        try
                        {
                            FsDeleteDirectory(sub, log);
                            removed++;
                        }
                        catch (Exception ex)
                        {
                            Log(log, "  删除失败：" + ex.Message);
                        }
                    }
                }

                // 清除 HKCU 下的 Revit 快捷方式与版本键
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                    {
                        using (RegistryKey sc = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\Revit\Shortcuts", false))
                        {
                            if (sc != null)
                            {
                                foreach (string sub in sc.GetSubKeyNames())
                                {
                                    RegDeleteTree(baseKey, @"SOFTWARE\Autodesk\Revit\Shortcuts\" + sub, log);
                                }
                                Log(log, "已清除 Revit Shortcuts 注册表项");
                            }
                        }
                        using (RegistryKey rv = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\Revit", false))
                        {
                            if (rv != null)
                            {
                                foreach (string sub in rv.GetSubKeyNames())
                                {
                                    if (sub.StartsWith("Autodesk Revit ", StringComparison.OrdinalIgnoreCase))
                                    {
                                        RegDeleteTree(baseKey, @"SOFTWARE\Autodesk\Revit\" + sub, log);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(log, "清除 Revit 注册表项失败：" + ex.Message);
                }

                if (removed == 0)
                {
                    return "未找到 Revit 用户配置目录，无需初始化。";
                }
                return "Revit 配置初始化完成（清理 " + removed + " 个版本目录）！请重新启动 Revit，将生成全新配置。";
            }
            catch (UnauthorizedAccessException)
            {
                return "初始化 Revit 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "初始化 Revit 时发生异常：" + ex.Message;
            }
        }

        /// <summary>3ds Max 配置初始化：删除 LocalAppData 下的 3dsMax 配置目录。</summary>
        public static string InitializeMax(Action<string> log)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Autodesk", "3dsMax");

                if (!Directory.Exists(dir))
                {
                    return "未找到 3ds Max 用户配置文件夹，无需初始化。";
                }

                Log(log, "删除：" + dir);
                FsDeleteDirectory(dir, log);
                return "3ds Max 配置初始化完成！请重新启动 3ds Max，将生成全新配置。";
            }
            catch (UnauthorizedAccessException)
            {
                return "初始化 3ds Max 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "初始化 3ds Max 时发生异常：" + ex.Message;
            }
        }

        /// <summary>AutoCAD 配置初始化：删除各版本用户配置目录与 HKCU 下 R* 键。</summary>
        public static string InitializeCad(Action<string> log)
        {
            try
            {
                int removed = 0;
                foreach (string baseDir in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                })
                {
                    string root = Path.Combine(baseDir, "Autodesk");
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }
                    foreach (string sub in Directory.GetDirectories(root))
                    {
                        try
                        {
                            if (!Path.GetFileName(sub).StartsWith("AutoCAD", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            Log(log, "删除：" + sub);
                            FsDeleteDirectory(sub, log);
                            removed++;
                        }
                        catch (Exception ex)
                        {
                            Log(log, "  删除失败：" + ex.Message);
                        }
                    }
                }

                // HKCUSOFTWAREAutodeskAutoCADR*
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                    using (RegistryKey acad = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD", false))
                    {
                        if (acad != null)
                        {
                            foreach (string sub in acad.GetSubKeyNames())
                            {
                                if (sub.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                                {
                                    RegDeleteTree(baseKey, @"SOFTWARE\Autodesk\AutoCAD\" + sub, log);
                                    Log(log, "已删除注册表键 HKCU\\SOFTWARE\\Autodesk\\AutoCAD\\" + sub);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(log, "清除 AutoCAD 注册表键失败：" + ex.Message);
                }

                if (removed == 0)
                {
                    return "未找到 AutoCAD 用户配置目录，无需初始化。";
                }
                return "AutoCAD 配置初始化完成（清理 " + removed + " 个目录）！请重新启动 AutoCAD，将生成全新配置。";
            }
            catch (UnauthorizedAccessException)
            {
                return "初始化 AutoCAD 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "初始化 AutoCAD 时发生异常：" + ex.Message;
            }
        }

        // ==================== 目录权限修复 ====================

        /// <summary>修复 Autodesk 关键目录的权限（当前用户 / Administrators / Users 完全控制 + 继承）。</summary>
        public static string RepairAutodeskFolderPermissions(Action<string> log)
        {
            try
            {
                var targets = new List<string>
                {
                    @"C:\Program Files\Autodesk",
                    @"C:\Program Files (x86)\Autodesk",
                    @"C:\ProgramData\Autodesk",
                    @"C:\Program Files\Common Files\Autodesk Shared",
                    @"C:\Program Files (x86)\Common Files\Autodesk Shared",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk")
                };

                int done = 0;
                foreach (string dir in targets)
                {
                    if (!Directory.Exists(dir))
                    {
                        Log(log, "  跳过（不存在）：" + dir);
                        continue;
                    }
                    if (GrantDirInheritedACL(dir, log))
                    {
                        done++;
                    }
                }

                return done == 0
                    ? "未找到需要处理的 Autodesk 目录。"
                    : "已修复 " + done + " 个 Autodesk 目录的权限（完全控制 + 继承）。";
            }
            catch (Exception ex)
            {
                return "修复目录权限时发生异常：" + ex.Message;
            }
        }

        // ==================== hosts 修复 ====================

        /// <summary>移除 hosts 中屏蔽 Autodesk 相关域名的条目，并刷新 DNS 缓存。只改命中项。</summary>
        public static string FixHostsAutodeskBlocks(Action<string> log)
        {
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string hosts = Path.Combine(win, @"System32\drivers\etc\hosts");
                if (!File.Exists(hosts))
                {
                    return "未找到 hosts 文件。";
                }

                string[] lines = File.ReadAllLines(hosts);
                var kept = new List<string>();
                var removed = new List<string>();

                foreach (string raw in lines)
                {
                    string trimmed = raw.Trim();
                    bool drop = false;

                    if (trimmed.Length > 0 && !trimmed.StartsWith("#"))
                    {
                        string lower = trimmed.ToLowerInvariant();
                        bool isBlock = lower.Contains("0.0.0.0") || lower.Contains("127.0.0.1")
                                    || lower.Contains("::1") || lower.Contains("255.255.255.255");
                        bool isAutodesk = lower.Contains("autodesk") || lower.Contains("adsk")
                                       || lower.Contains("genuine") || lower.Contains("autocad");
                        if (isBlock && isAutodesk)
                        {
                            drop = true;
                        }
                    }

                    if (drop)
                    {
                        removed.Add(trimmed);
                    }
                    else
                    {
                        kept.Add(raw);
                    }
                }

                if (removed.Count == 0)
                {
                    return "hosts 中未发现屏蔽 Autodesk 域名的条目，无需修复。";
                }

                // 先备份
                string backup = hosts + ".autofix.bak";
                FsCopy(hosts, backup, log);

                if (!DryRun)
                {
                    try { File.SetAttributes(hosts, FileAttributes.Normal); } catch { }
                }
                FsWriteLines(hosts, kept.ToArray(), log);

                foreach (string r in removed)
                {
                    Log(log, "  已移除：" + r);
                }

                RunCommand("ipconfig", "/flushdns", false);

                return "hosts 已修复：移除 " + removed.Count + " 条屏蔽 Autodesk 域名的记录（备份：" + backup + "），并已刷新 DNS 缓存。";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复 hosts 失败：需要管理员权限。请以管理员身份运行后重试。";
            }
            catch (Exception ex)
            {
                return "修复 hosts 时发生异常：" + ex.Message;
            }
        }

        // ==================== Windows 卷缓存（磁盘清理引擎） ====================

        internal sealed class VolumeCacheEntry
        {
            public string Key = "";
            public string Description = "";
            public long Size;
        }

        [ComImport, Guid("8d5e6603-54df-4e93-bc67-de082095df19"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEmptyVolumeCache
        {
            [PreserveSig]
            int Initialize(IntPtr hRegKey, [MarshalAs(UnmanagedType.LPWStr)] string volume,
                out IntPtr displayName, IntPtr iconLocation, out IntPtr description, ref uint flags);

            [PreserveSig]
            int GetSpaceUsed(out ulong spaceUsed);

            [PreserveSig]
            int Purge(ulong spaceUsed, out ulong spaceFreed);

            [PreserveSig]
            int ShowProperties(IntPtr hwnd);

            [PreserveSig]
            int Deactivate(ref uint flags);
        }

        private const string VolumeCachesRoot =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        /// <summary>只读扫描各卷缓存占用（调用 Windows 自带的磁盘清理引擎）。</summary>
        internal static List<VolumeCacheEntry> ScanVolumeCaches()
        {
            var list = new List<VolumeCacheEntry>();
            string volume = NormalizeVolume(Path.GetPathRoot(Environment.SystemDirectory));

            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(VolumeCachesRoot, false))
                {
                    if (root == null)
                    {
                        return list;
                    }
                    foreach (string key in root.GetSubKeyNames())
                    {
                        VolumeCacheEntry e = ScanOneCache(volume, key);
                        if (e != null)
                        {
                            list.Add(e);
                        }
                    }
                }
            }
            catch { }

            list.Sort((a, b) => b.Size.CompareTo(a.Size));
            return list;
        }

        private static VolumeCacheEntry ScanOneCache(string volume, string cacheKey)
        {
            IntPtr hKey = IntPtr.Zero;
            IEmptyVolumeCache cache = null;
            try
            {
                Guid clsid = ReadVolumeCacheClsid(cacheKey);
                if (clsid == Guid.Empty)
                {
                    return null;
                }

                string sub = VolumeCachesRoot + "\\" + cacheKey;
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, sub, 0u, 131097, out hKey) != 0)
                {
                    return null;
                }

                Type t = Type.GetTypeFromCLSID(clsid);
                if (t == null)
                {
                    return null;
                }
                cache = Activator.CreateInstance(t) as IEmptyVolumeCache;
                if (cache == null)
                {
                    return null;
                }

                uint flags = 0;
                IntPtr namePtr, descPtr;
                int hr = cache.Initialize(hKey, volume, out namePtr, IntPtr.Zero, out descPtr, ref flags);
                string display = PtrToStringAndFree(namePtr);
                PtrToStringAndFree(descPtr);

                if (hr != 0)
                {
                    return null;
                }
                if ((flags & 0x10u) != 0)
                {
                    uint f = 0;
                    cache.Deactivate(ref f);
                    return null;
                }

                ulong used;
                int hr2 = cache.GetSpaceUsed(out used);
                uint f2 = 0;
                cache.Deactivate(ref f2);

                if (hr2 != 0 || used == 0)
                {
                    return null;
                }

                return new VolumeCacheEntry
                {
                    Key = cacheKey,
                    Description = string.IsNullOrWhiteSpace(display) ? cacheKey : display,
                    Size = used > long.MaxValue ? long.MaxValue : (long)used
                };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (cache != null)
                {
                    try { Marshal.ReleaseComObject(cache); } catch { }
                }
                if (hKey != IntPtr.Zero)
                {
                    RegCloseKey(hKey);
                }
            }
        }

        /// <summary>清理指定的卷缓存，返回释放的字节数。</summary>
        internal static long PurgeVolumeCaches(IEnumerable<string> cacheKeys, Action<string> log)
        {
            if (DryRun)
            {
                long est = 0;
                var names = new List<string>();
                foreach (string key in cacheKeys)
                {
                    names.Add(key);
                    foreach (VolumeCacheEntry e in ScanVolumeCaches())
                    {
                        if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                        {
                            est += e.Size;
                            break;
                        }
                    }
                }
                DryNote("清理 " + names.Count + " 项卷缓存，预计释放 " + FormatSize(est), log);
                return est;
            }
            long total = 0;
            string volume = NormalizeVolume(Path.GetPathRoot(Environment.SystemDirectory));

            foreach (string key in cacheKeys)
            {
                IntPtr hKey = IntPtr.Zero;
                IEmptyVolumeCache cache = null;
                try
                {
                    Guid clsid = ReadVolumeCacheClsid(key);
                    if (clsid == Guid.Empty)
                    {
                        continue;
                    }

                    string sub = VolumeCachesRoot + "\\" + key;
                    if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, sub, 0u, 983103, out hKey) != 0)
                    {
                        continue;
                    }

                    Type t = Type.GetTypeFromCLSID(clsid);
                    if (t == null)
                    {
                        continue;
                    }
                    cache = Activator.CreateInstance(t) as IEmptyVolumeCache;
                    if (cache == null)
                    {
                        continue;
                    }

                    uint flags = 0;
                    IntPtr namePtr, descPtr;
                    if (cache.Initialize(hKey, volume, out namePtr, IntPtr.Zero, out descPtr, ref flags) != 0)
                    {
                        continue;
                    }
                    string display = PtrToStringAndFree(namePtr);
                    PtrToStringAndFree(descPtr);

                    ulong used;
                    if (cache.GetSpaceUsed(out used) != 0 || used == 0)
                    {
                        uint f = 0;
                        cache.Deactivate(ref f);
                        continue;
                    }

                    ulong freed;
                    int hr = cache.Purge(used, out freed);
                    uint f2 = 0;
                    cache.Deactivate(ref f2);

                    if (hr == 0)
                    {
                        long add = freed > (ulong)long.MaxValue ? long.MaxValue : (long)freed;
                        total += add;
                        Log(log, "  已清理：" + (string.IsNullOrWhiteSpace(display) ? key : display)
                                 + "  释放 " + FormatSize(add));
                    }
                }
                catch (Exception ex)
                {
                    Log(log, "  清理失败：" + key + " -> " + ex.Message);
                }
                finally
                {
                    if (cache != null)
                    {
                        try { Marshal.ReleaseComObject(cache); } catch { }
                    }
                    if (hKey != IntPtr.Zero)
                    {
                        RegCloseKey(hKey);
                    }
                }
            }

            return total;
        }

        private static Guid ReadVolumeCacheClsid(string cacheKey)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(VolumeCachesRoot + "\\" + cacheKey, false))
                {
                    if (key == null)
                    {
                        return Guid.Empty;
                    }
                    object v = key.GetValue(null);
                    if (v == null)
                    {
                        return Guid.Empty;
                    }
                    string s = v.ToString().Trim().Trim('{', '}');
                    Guid g;
                    return Guid.TryParse(s, out g) ? g : Guid.Empty;
                }
            }
            catch
            {
                return Guid.Empty;
            }
        }

        private static string NormalizeVolume(string volumeRoot)
        {
            if (string.IsNullOrWhiteSpace(volumeRoot))
            {
                return "C:";
            }
            string s = volumeRoot.TrimEnd('\\', '/');
            if (s.Length >= 2 && s[1] == ':')
            {
                return s.Substring(0, 2);
            }
            return s;
        }

        private static string PtrToStringAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                CoTaskMemFree(ptr);
            }
        }
    }
}
