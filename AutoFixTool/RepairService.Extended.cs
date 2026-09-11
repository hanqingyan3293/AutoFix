using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace AutoFix
{
    // 扩展修复项：Installer 数据清理、权限修复、语言切换
    internal static partial class RepairService
    {
        // ==================== 安装错误修复（续） ====================

        /// <summary>4000：公共文件夹路径 + SOFTWARE\Classes 权限。</summary>
        public static string FixError4000(Action<string> log)
        {
            try
            {
                RestoreCommonShellFolders(log);

                Log(log, "停止 msiserver ...");
                StopService("msiserver");
                RunCommand("sc", "stop msiserver", false);
                Thread.Sleep(1000);

                EnableTakeOwnershipPrivilege();
                byte[] sd = BuildInstallerSecurityDescriptor();

                const string classes = @"SOFTWARE\Classes";
                int ok = 0, fail = 0, removed = 0;

                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(classes, false))
                {
                    if (key == null)
                    {
                        return "修复 4000 失败：无法打开 SOFTWARE\\Classes。";
                    }

                    SetKeySecurity(HKEY_LOCAL_MACHINE, classes, sd, ref ok, ref fail);
                    SetRegistrySecurityRecursive(key, classes, HKEY_LOCAL_MACHINE, sd, ref ok, ref fail);
                    Log(log, "Classes 权限：成功 " + ok + " 项，失败 " + fail + " 项");

                    var targets = new List<KeyRef>();
                    CollectLeafKeys(key, classes, targets);
                    foreach (KeyRef t in targets)
                    {
                        if (DeleteRegKey(t.ParentPath, t.KeyName))
                        {
                            removed++;
                        }
                    }
                }

                return "错误4000 已修复，清理无效注册表项 " + removed + " 个。";
            }
            catch (Exception ex)
            {
                return "修复 4000 时发生异常：" + ex.Message;
            }
        }

        /// <summary>-9：与 4000 同一套处理。</summary>
        public static string FixErrorMinus9(Action<string> log)
        {
            string r = FixError4000(log);
            if (string.IsNullOrEmpty(r))
            {
                return "错误-9 已修复，请尝试重新安装！";
            }
            return r.Replace("错误4000", "错误-9").Replace("修复 4000", "修复 -9");
        }

        /// <summary>1327：Installer\Folders 中指向已不存在盘符的条目。</summary>
        public static string FixError1327(Action<string> log)
        {
            try
            {
                var ready = ReadyDriveLetters();
                const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders";
                int removed = 0;

                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                        using (RegistryKey key = baseKey.OpenSubKey(path, true))
                        {
                            if (key == null)
                            {
                                continue;
                            }
                            foreach (string name in key.GetValueNames())
                            {
                                try
                                {
                                    object v = key.GetValue(name);
                                    if (v == null)
                                    {
                                        continue;
                                    }
                                    string val = v.ToString();
                                    if (val.Length < 2 || val[1] != ':')
                                    {
                                        continue;
                                    }
                                    string drive = val.Substring(0, 2).ToUpperInvariant();
                                    if (ready.Contains(drive))
                                    {
                                        continue;
                                    }
                                    RegDeleteValue(key, name, log);
                                    removed++;
                                    Log(log, "  已移除无效条目：" + name + " = " + val);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                return removed > 0
                    ? "错误1327 已修复：清理 " + removed + " 个指向不存在磁盘的 Installer 记录，请重新尝试安装！"
                    : "错误1327 已修复（未发现指向失效磁盘的记录），请重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复 1327 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1308：安装源指向已不存在的磁盘。</summary>
        public static string FixError1308(Action<string> log)
        {
            try
            {
                int removed = ClearInvalidLastUsedSource(log);
                return removed > 0
                    ? "错误1308 已修复：清理 " + removed + " 个无效安装源，请重新尝试安装！"
                    : "错误1308 已修复（未发现无效安装源）。提示：如使用网络安装，请确认网络驱动器已正确映射。";
            }
            catch (Exception ex)
            {
                return "修复 1308 时发生异常：" + ex.Message;
            }
        }

        /// <summary>2：清理 Installer 临时文件 + 无效安装源。</summary>
        public static string FixError2(Action<string> log)
        {
            try
            {
                int tmp = RemoveInstallerTempFiles(log);
                int src = ClearInvalidLastUsedSource(log);
                return "错误2 已修复：清理临时文件 " + tmp + " 个，无效安装源 " + src + " 个，请重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复错误2时发生异常：" + ex.Message;
            }
        }

        /// <summary>2755：Installer 缓存损坏。清临时文件并重启 msiserver。</summary>
        public static string FixError2755(Action<string> log)
        {
            try
            {
                Log(log, "停止 msiserver ...");
                StopService("msiserver");
                RunCommand("sc", "stop msiserver", false);
                Thread.Sleep(1000);

                int tmp = RemoveInstallerTempFiles(log);

                Log(log, "启动 msiserver ...");
                RunCommand("sc", "start msiserver", false);
                Thread.Sleep(1000);

                return "错误2755 已修复：清理临时文件 " + tmp + " 个，已重启 Windows Installer 服务。\r\n\r\n"
                     + "提示：请确认安装包位于非加密目录，且路径中不含特殊字符。";
            }
            catch (Exception ex)
            {
                return "修复 2755 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1618：安装被占用。结束安装进程、清锁文件、重启 msiserver。</summary>
        public static string FixError1618(Action<string> log)
        {
            try
            {
                string[] procs = { "msiexec", "setup", "install", "instmsi", "msiinst" };
                int killed = 0;
                foreach (string p in procs)
                {
                    try
                    {
                        foreach (Process proc in Process.GetProcessesByName(p))
                        {
                            try
                            {
                                proc.Kill();
                                killed++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
                if (killed > 0)
                {
                    Log(log, "已结束 " + killed + " 个安装相关进程");
                }
                Thread.Sleep(1000);

                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                int locks = 0;
                string[] lockFiles =
                {
                    Path.Combine(win, @"System32\msi.dll.lock"),
                    Path.Combine(win, @"SysWOW64\msi.dll.lock")
                };
                foreach (string f in lockFiles)
                {
                    try
                    {
                        if (File.Exists(f))
                        {
                            FsDeleteFile(f, log);
                            locks++;
                        }
                    }
                    catch { }
                }
                try
                {
                    string installer = Path.Combine(win, "Installer");
                    if (Directory.Exists(installer))
                    {
                        foreach (string f in Directory.GetFiles(installer, "*.lock"))
                        {
                            try
                            {
                                FsDeleteFile(f, log);
                                locks++;
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                Log(log, "重启 msiserver ...");
                StopService("msiserver");
                RunCommand("sc", "stop msiserver", false);
                Thread.Sleep(1000);
                RunCommand("sc", "start msiserver", false);

                return "错误1618 已修复：结束进程 " + killed + " 个，删除锁文件 " + locks + " 个，已重启 Windows Installer。请重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复 1618 时发生异常：" + ex.Message;
            }
        }

        /// <summary>103：Autodesk Access / ODIS 组件残留。</summary>
        public static string FixError103(Action<string> log)
        {
            try
            {
                Log(log, "停止 Autodesk Access 服务 ...");
                StopService("Autodesk Access Service Host");
                RunCommand("sc", "stop \"Autodesk Access Service Host\"", false);
                Thread.Sleep(1000);

                KillProcess("AdskAccessCore.exe");
                KillProcess("AdskAccessUIHost.exe");
                KillProcess("AdskAccessServiceHost.exe");
                KillProcess("AdskAccessService.exe");
                Thread.Sleep(500);

                string[] keys =
                {
                    @"SOFTWARE\Autodesk\UPI2\{6E623072-F62E-408C-B4B6-E4A5EA3D3208}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A3158B3E-5F28-358A-BF1A-9532D8EBC811}",
                    @"SOFTWARE\Autodesk\ODIS"
                };
                foreach (string k in keys)
                {
                    DeleteRegistryKey(RegistryHive.LocalMachine, k, log);
                }

                var dirs = new List<string>
                {
                    @"C:\Program Files\Autodesk\AdODIS",
                    @"C:\ProgramData\Autodesk\ODIS",
                    @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Autodesk\Autodesk Access"
                };

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    dirs.Add(Path.Combine(localAppData, "Autodesk", "ODIS"));
                }

                foreach (string d in dirs)
                {
                    DeleteDirectory(d, log);
                }

                return "错误103 已修复。若安装仍报错误103，可继续修复错误4005（需联网下载 AdODIS）。";
            }
            catch (Exception ex)
            {
                return "修复 103 时发生异常：" + ex.Message;
            }
        }

        /// <summary>3：清理 C:\ProgramData 下名称异常的文件夹。</summary>
        public static string FixError3(Action<string> log)
        {
            try
            {
                const string programData = @"C:\ProgramData";
                if (!Directory.Exists(programData))
                {
                    return "修复错误3失败：C:\\ProgramData 不存在。";
                }

                var removed = new List<string>();
                foreach (string dir in Directory.GetDirectories(programData))
                {
                    string name;
                    try { name = Path.GetFileName(dir); } catch { continue; }

                    bool specialCache =
                        string.Equals(name, "Package Cache", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Packages", StringComparison.OrdinalIgnoreCase);

                    bool hasOddChar = false;
                    foreach (char ch in name)
                    {
                        if (!char.IsLetterOrDigit(ch) && ch != ' ' && ch != '_' && ch != '-' && ch != '.')
                        {
                            hasOddChar = true;
                            break;
                        }
                    }

                    if (!specialCache && !hasOddChar)
                    {
                        continue;
                    }

                    Log(log, "  删除：" + name);
                    try
                    {
                        FsDeleteDirectory(dir, log);
                        removed.Add(name);
                    }
                    catch (Exception ex)
                    {
                        Log(log, "  删除失败：" + name + " -> " + ex.Message);
                    }
                }

                return removed.Count > 0
                    ? "已修复错误3，删除 " + removed.Count + " 个异常文件夹，请尝试重新安装！\r\n\r\n删除的文件夹：\r\n  · "
                      + string.Join("\r\n  · ", removed.ToArray())
                    : "错误3 已修复（未发现名称异常的文件夹），请尝试重新安装！";
            }
            catch (Exception ex)
            {
                return "修复错误3时发生异常：" + ex.Message;
            }
        }

        /// <summary>1644：智能应用控制拦截。仅打开系统设置页，不修改任何设置。</summary>
        public static string FixError1644(Action<string> log)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "windowsdefender://smartapp/",
                    UseShellExecute = true
                });
                return "已打开「智能应用控制」设置页，请手动将选项设为「关闭」。\r\n\r\n"
                     + "1. 在页面中选择「关闭」（关闭后可能较难再次开启，请自行确认风险）；\r\n"
                     + "2. 关闭窗口后，重新运行安装程序。\r\n\r\n"
                     + "说明：本工具不会自动修改智能应用控制，仅打开系统页面供你操作。";
            }
            catch (Exception ex)
            {
                return "打开智能应用控制页面失败：" + ex.Message + "\r\n\r\n请手动进入：Windows 安全中心 → 应用和浏览器控制 → 智能应用控制。";
            }
        }

        // ==================== 权限修复（续） ====================

        /// <summary>1722：系统盘空间 + Autodesk 目录与注册表权限。</summary>
        public static string FixError1722(Action<string> log)
        {
            try
            {
                var notes = new List<string>();

                try
                {
                    string sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
                    long freeGb = new DriveInfo(sysRoot).AvailableFreeSpace / 1073741824L;
                    notes.Add(freeGb < 5
                        ? "警告：系统盘可用空间不足（" + freeGb + " GB），建议至少保留 5 GB"
                        : "系统盘可用空间：" + freeGb + " GB（充足）");
                }
                catch { }

                foreach (string dir in new[]
                {
                    @"C:\Program Files\Autodesk",
                    @"C:\Program Files (x86)\Autodesk",
                    @"C:\ProgramData\Autodesk"
                })
                {
                    if (GrantDirInheritedACL(dir, log))
                    {
                        notes.Add("已修复目录权限：" + dir);
                    }
                }

                foreach (string key in new[]
                {
                    @"SOFTWARE\Autodesk",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer",
                    @"SOFTWARE\Classes\Installer"
                })
                {
                    if (GrantRegistryFullControl(key, log))
                    {
                        notes.Add("已修复注册表权限：" + key);
                    }
                }

                return "已修复错误1722/1726/1727：" + Environment.NewLine + "  · "
                     + string.Join(Environment.NewLine + "  · ", notes.ToArray())
                     + Environment.NewLine + "请重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复 1722 时发生异常：" + ex.Message;
            }
        }

        /// <summary>5：目录与注册表权限综合修复。</summary>
        public static string FixError5(Action<string> log)
        {
            try
            {
                var notes = new List<string>();

                foreach (string dir in new[]
                {
                    @"C:\Program Files\Autodesk",
                    @"C:\Program Files (x86)\Autodesk",
                    @"C:\Program Files (x86)\Common Files\Autodesk Shared",
                    @"C:\ProgramData\Autodesk"
                })
                {
                    if (GrantDirInheritedACL(dir, log))
                    {
                        notes.Add("已修复目录权限：" + dir);
                    }
                }

                if (GrantDirInheritedACL(@"C:\Windows\Temp", log))
                {
                    notes.Add("已修复系统临时文件夹权限");
                }
                if (GrantDirInheritedACL(Path.GetTempPath(), log))
                {
                    notes.Add("已修复用户临时文件夹权限");
                }
                if (GrantDirInheritedACL(@"C:\Windows\Installer", log))
                {
                    notes.Add("已修复 Windows Installer 目录权限");
                }

                foreach (string key in new[]
                {
                    @"SOFTWARE\Autodesk",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer",
                    @"SOFTWARE\Classes\Installer",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
                })
                {
                    if (GrantRegistryFullControl(key, log))
                    {
                        notes.Add("已修复注册表权限：" + key);
                    }
                }

                notes.Add("提示：若问题仍在，可暂时关闭防病毒软件或防火墙后重试");

                return "已修复错误5：" + Environment.NewLine + "  · "
                     + string.Join(Environment.NewLine + "  · ", notes.ToArray())
                     + Environment.NewLine + "请以管理员身份重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复错误5时发生异常：" + ex.Message;
            }
        }

        // ==================== 扩展功能 ====================

        /// <summary>重置 .dwg / .dxf 文件关联，供重新选择打开方式。</summary>
        public static string FixCadFileAssociation(Action<string> log)
        {
            try
            {
                DeleteRegistryKey(RegistryHive.ClassesRoot, ".dwg", log);
                DeleteRegistryKey(RegistryHive.ClassesRoot, ".dxf", log);
                DeleteRegistryKey(RegistryHive.LocalMachine, @"SOFTWARE\Classes\.dwg", log);
                DeleteRegistryKey(RegistryHive.LocalMachine, @"SOFTWARE\Classes\.dxf", log);
                DeleteRegistryKey(RegistryHive.CurrentUser, @"SOFTWARE\Classes\.dwg", log);
                DeleteRegistryKey(RegistryHive.CurrentUser, @"SOFTWARE\Classes\.dxf", log);
                DeleteRegistryKey(RegistryHive.CurrentUser, @"SOFTWARE\Autodesk\DwgCommon\shellex\apps", log);
                DeleteRegistryKey(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.dwg", log);
                DeleteRegistryKey(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.dxf", log);

                return "CAD 文件关联已重置，请重新启动 CAD 软件并选择第一项进行关联！";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复 CAD 文件关联失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复 CAD 文件关联时发生异常：" + ex.Message;
            }
        }

        /// <summary>Maya 界面语言：true = 英文，false = 中文。</summary>
        public static string SetMayaLanguage(bool english, Action<string> log)
        {
            try
            {
                string value = english ? "en_US" : "zh_CN";
                if (DryRun)
                {
                    DryNote("写入系统环境变量 MAYA_UI_LANGUAGE = " + value, log);
                    return "[预演] 将把 Maya 界面语言设为" + (english ? "英文" : "中文") + "（系统环境变量 MAYA_UI_LANGUAGE）。";
                }
                Environment.SetEnvironmentVariable("MAYA_UI_LANGUAGE", value, EnvironmentVariableTarget.Machine);
                Log(log, "MAYA_UI_LANGUAGE = " + value + "（系统环境变量）");
                return "Maya 已切换为" + (english ? "英文" : "中文") + "，请重新启动软件！";
            }
            catch (UnauthorizedAccessException)
            {
                return "设置 Maya 语言失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "设置 Maya 语言时发生异常：" + ex.Message;
            }
        }

        /// <summary>3ds Max 界面语言：true = 英文，false = 中文。</summary>
        public static string SetMaxLanguage(bool english, Action<string> log)
        {
            try
            {
                string value = english ? "ENU" : "CHS";
                int changed = 0;

                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                        using (RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\3dsMax", true))
                        {
                            if (root == null)
                            {
                                continue;
                            }
                            foreach (string version in root.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey ver = root.OpenSubKey(version, true))
                                    {
                                        if (ver == null)
                                        {
                                            continue;
                                        }
                                        RegWriteValue(ver, "CurrentLanguage", value, RegistryValueKind.String, log);
                                        changed++;
                                        Log(log, "  HKCU\\" + view + "\\...\\3dsMax\\" + version + "  CurrentLanguage = " + value);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                if (changed == 0)
                {
                    return "未找到 3ds Max 的注册表配置（HKCU\\SOFTWARE\\Autodesk\\3dsMax）。请确认已安装并至少运行过一次。";
                }
                return "3ds Max 已切换为" + (english ? "英文" : "中文") + "（已修改 " + changed + " 处配置），请重新启动软件！";
            }
            catch (Exception ex)
            {
                return "设置 3ds Max 语言时发生异常：" + ex.Message;
            }
        }

        /// <summary>Revit 界面语言：true = 英文，false = 中文。通过修改桌面快捷方式的 /language 参数实现。</summary>
        public static string SetRevitLanguage(bool english, Action<string> log)
        {
            try
            {
                string lang = english ? "ENU" : "CHS";
                List<string> shortcuts = FindRevitShortcuts();
                if (shortcuts.Count == 0)
                {
                    return "未找到桌面上的 Revit 快捷方式，请确认 Revit 已安装并创建了桌面快捷方式。";
                }

                int changed = 0;
                foreach (string lnk in shortcuts)
                {
                    if (DryRun)
                    {
                        DryNote("修改快捷方式语言参数：" + lnk + " -> /language " + lang, log);
                        changed++;
                        continue;
                    }
                    if (SetShortcutLanguage(lnk, lang))
                    {
                        changed++;
                        Log(log, "  已修改快捷方式：" + lnk + " -> /language " + lang);
                    }
                    else
                    {
                        Log(log, "  修改失败：" + lnk);
                    }
                }

                if (changed == 0)
                {
                    return "修改 Revit 快捷方式失败，请检查快捷方式权限。";
                }
                return "Revit 已切换为" + (english ? "英文" : "中文") + "（已修改 " + changed + " 个快捷方式），请重新启动软件！";
            }
            catch (Exception ex)
            {
                return "设置 Revit 语言时发生异常：" + ex.Message;
            }
        }

        // ==================== 共用辅助 ====================

        /// <summary>写入 HKLM 公共文件夹路径并补建目录。</summary>
        private static void RestoreCommonShellFolders(Action<string> log)
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders";
            var map = new Dictionary<string, string>
            {
                { "Common Desktop",   @"C:\Users\Public\Desktop" },
                { "Common Documents", @"C:\Users\Public\Documents" },
                { "CommonMusic",      @"C:\Users\Public\Music" },
                { "CommonPictures",   @"C:\Users\Public\Pictures" },
                { "CommonVideo",      @"C:\Users\Public\Videos" }
            };

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, true)
                                       ?? Registry.LocalMachine.CreateSubKey(path))
                {
                    if (key != null)
                    {
                        foreach (KeyValuePair<string, string> kv in map)
                        {
                            RegWriteValue(key, kv.Key, kv.Value, RegistryValueKind.String, log);
                        }
                    }
                }
                foreach (string dir in map.Values)
                {
                    CreateDirectoryIfNotExists(dir);
                }
                Log(log, "已恢复公共文件夹路径映射");
            }
            catch (Exception ex)
            {
                Log(log, "恢复公共文件夹路径失败：" + ex.Message);
            }
        }

        private static HashSet<string> ReadyDriveLetters()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.IsReady)
                        {
                            set.Add(d.Name.Substring(0, 2).ToUpperInvariant());
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return set;
        }

        /// <summary>删除 Installer UserData 产品项中指向失效磁盘的 LastUsedSource。</summary>
        private static int ClearInvalidLastUsedSource(Action<string> log)
        {
            var ready = ReadyDriveLetters();
            const string root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products";
            int removed = 0;

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey products = baseKey.OpenSubKey(root, false))
                    {
                        if (products == null)
                        {
                            continue;
                        }
                        foreach (string product in products.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey src = baseKey.OpenSubKey(root + "\\" + product + "\\SourceList", true))
                                {
                                    if (src == null)
                                    {
                                        continue;
                                    }
                                    object v = src.GetValue("LastUsedSource");
                                    if (v == null)
                                    {
                                        continue;
                                    }
                                    string val = v.ToString();
                                    if (val.Length < 2 || val[1] != ':')
                                    {
                                        continue;
                                    }
                                    if (!ready.Contains(val.Substring(0, 2).ToUpperInvariant()))
                                    {
                                        RegDeleteValue(src, "LastUsedSource", log);
                                        removed++;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            if (removed > 0)
            {
                Log(log, "已清理 " + removed + " 个无效安装源路径");
            }
            return removed;
        }

        /// <summary>删除 C:\Windows\Installer 下的 *.tmp。</summary>
        private static int RemoveInstallerTempFiles(Action<string> log)
        {
            int removed = 0;
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string dir = Path.Combine(win, "Installer");
                if (!Directory.Exists(dir))
                {
                    return 0;
                }
                foreach (string f in Directory.GetFiles(dir, "*.tmp"))
                {
                    try
                    {
                        FsDeleteFile(f, log);
                        removed++;
                    }
                    catch { }
                }
                Log(log, "已清理 Windows Installer 临时文件 " + removed + " 个");
            }
            catch { }
            return removed;
        }

        /// <summary>为目录设置带继承的完全控制（不递归，靠继承下传）。</summary>
        private static bool GrantDirInheritedACL(string dir, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return false;
            }
            if (DryRun)
            {
                DryNote("修改目录权限（完全控制 + 继承）：" + dir, log);
                return true;
            }
            try
            {
                SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                var di = new DirectoryInfo(dir);
                DirectorySecurity sec = di.GetAccessControl();

                if (user != null)
                {
                    sec.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                }
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));

                di.SetAccessControl(sec);
                Log(log, "  已授权（含继承）：" + dir);
                return true;
            }
            catch (Exception ex)
            {
                Log(log, "  授权失败：" + dir + " -> " + ex.Message);
                return false;
            }
        }

        /// <summary>为注册表键及其子键授予完全控制。</summary>
        private static bool GrantRegistryFullControl(string path, Action<string> log)
        {
            if (DryRun)
            {
                DryNote("修改注册表键权限（完全控制 + 继承）：" + path, log);
                return true;
            }
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(path, true))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    SecurityIdentifier user = WindowsIdentity.GetCurrent().User;
                    var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                    var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                    RegistrySecurity sec = key.GetAccessControl();
                    if (user != null)
                    {
                        sec.AddAccessRule(new RegistryAccessRule(user, RegistryRights.FullControl,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                            PropagationFlags.None, AccessControlType.Allow));
                    }
                    sec.AddAccessRule(new RegistryAccessRule(admins, RegistryRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                    sec.AddAccessRule(new RegistryAccessRule(users, RegistryRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None, AccessControlType.Allow));
                    key.SetAccessControl(sec);

                    // 子键逐个处理（继承已生效，此处兜底显式规则的键）
                    try
                    {
                        foreach (string sub in key.GetSubKeyNames())
                        {
                            GrantRegistryFullControl(path + "\\" + sub, log);
                        }
                    }
                    catch { }

                    Log(log, "  已授权注册表键：" + path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log(log, "  注册表授权失败：" + path + " -> " + ex.Message);
                return false;
            }
        }

        private static List<string> FindRevitShortcuts()
        {
            var list = new List<string>();
            foreach (string folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            })
            {
                try
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    {
                        continue;
                    }
                    foreach (string f in Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        if (name.Contains("revit") && !list.Contains(f))
                        {
                            list.Add(f);
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        /// <summary>通过 WScript.Shell 改写快捷方式的 /language 参数。</summary>
        private static bool SetShortcutLanguage(string shortcutPath, string language)
        {
            try
            {
                if (!File.Exists(shortcutPath))
                {
                    return false;
                }
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return false;
                }

                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null,
                    shell, new object[] { shortcutPath });
                if (link == null)
                {
                    return false;
                }

                Type linkType = link.GetType();
                string target = (string)linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                string args = (string)linkType.InvokeMember("Arguments", BindingFlags.GetProperty, null, link, null) ?? "";

                if (string.IsNullOrEmpty(target))
                {
                    return false;
                }

                string t = target.Trim();
                string a = args.Trim();

                // TargetPath 里若混入了 /language，先摘出来
                if (t.Contains(" /language "))
                {
                    int idx = t.IndexOf(" /language ");
                    string tail = t.Substring(idx);
                    t = t.Substring(0, idx);
                    a = string.IsNullOrEmpty(a) ? tail : a + " " + tail;
                }

                if (a.Contains("/language"))
                {
                    a = Regex.Replace(a, @"/language\s+\w+", "/language " + language);
                }
                else
                {
                    a = string.IsNullOrEmpty(a) ? "/language " + language : a + " /language " + language;
                }

                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { t });
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { a });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
