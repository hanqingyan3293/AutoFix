using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace AutodeskFix
{
    // 追加的修复项（按功能分类组织）
    internal static partial class RepairService
    {
        // ==================== 安装错误类 ====================

        /// <summary>270：AdskLicensing 组件残留，仅清理组件，不触碰文档与权限。</summary>
        public static string Fix270(Action<string> log)
        {
            try
            {
                StopLicensingStack(log);
                return "错误270 已修复，请尝试重新安装！";
            }
            catch (Exception ex)
            {
                return "修复 270 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1：AdskLicensing 残留且服务注册项损坏，额外移除服务注册。</summary>
        public static string FixError1(Action<string> log)
        {
            try
            {
                StopLicensingStack(log);
                Log(log, "移除 AdskLicensingService 服务注册项 ...");
                DeleteRegistryKey(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\AdskLicensingService", log);
                return "错误1 已修复！建议重启电脑后再安装。";
            }
            catch (Exception ex)
            {
                return "修复错误1时发生异常：" + ex.Message;
            }
        }

        /// <summary>1632：Windows Installer 缓存目录缺失。</summary>
        public static string FixError1632(Action<string> log)
        {
            try
            {
                const string dir = @"C:\Windows\Installer";
                if (Directory.Exists(dir))
                {
                    Log(log, "目录已存在：" + dir);
                }
                else
                {
                    FsCreateDirectory(dir, log);
                    Log(log, "已创建目录：" + dir);
                }
                return "错误1632 已修复，请尝试重新安装！";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复 1632 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复 1632 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1622：临时文件夹权限异常。</summary>
        public static string FixError1622(Action<string> log)
        {
            try
            {
                string temp = Path.GetTempPath();
                if (string.IsNullOrEmpty(temp) || !Directory.Exists(temp))
                {
                    return "修复 1622 失败：未找到临时文件夹。";
                }
                Log(log, "修复临时文件夹权限：" + temp);
                bool ok = GrantDirectoryFullControl(temp, log);
                return ok
                    ? "错误1622 已修复（临时文件夹权限），请尝试重新安装！"
                    : "修复 1622 部分失败，请确认以管理员身份运行。";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复 1622 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复 1622 时发生异常：" + ex.Message;
            }
        }

        // ==================== 权限与注册表类 ====================

        /// <summary>2503/2502：临时目录权限异常 + 临时文件堆积。</summary>
        public static string FixError2503(Action<string> log)
        {
            try
            {
                var notes = new List<string>();
                const string winTemp = @"C:\Windows\Temp";

                if (GrantDirInheritedACL(winTemp, log))
                {
                    notes.Add("已修复系统临时文件夹权限");
                }
                if (GrantDirInheritedACL(Path.GetTempPath(), log))
                {
                    notes.Add("已修复用户临时文件夹权限");
                }
                if (CleanTempFiles(winTemp, log))
                {
                    notes.Add("已清理系统临时文件");
                }
                if (CleanTempFiles(Path.GetTempPath(), log))
                {
                    notes.Add("已清理用户临时文件");
                }

                int year = DateTime.Now.Year;
                if (year < 2020 || year > 2030)
                {
                    notes.Add("警告：系统时间可能不正确，请校准系统时间");
                }

                if (notes.Count == 0)
                {
                    return "错误2503/2502 处理完成，请重新尝试安装！";
                }
                return "已修复错误2503/2502：" + Environment.NewLine + "  · " +
                       string.Join(Environment.NewLine + "  · ", notes.ToArray()) +
                       Environment.NewLine + "请重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复 2503/2502 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1935：重新注册 Windows Installer 并清理 .NET 临时缓存。</summary>
        public static string FixError1935(Action<string> log)
        {
            try
            {
                var notes = new List<string>();

                Log(log, "停止 msiserver ...");
                StopService("msiserver");
                RunCommand("sc", "stop msiserver", false);
                Thread.Sleep(1000);

                Log(log, "重新注册 Windows Installer ...");
                RunCommand("msiexec", "/unregister", false);
                Thread.Sleep(500);
                RunCommand("msiexec", "/regserver", false);
                Thread.Sleep(500);
                notes.Add("已重新注册 Windows Installer");

                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string[] caches =
                {
                    win + @"\Microsoft.NET\Framework\v2.0.50727\Temporary ASP.NET Files",
                    win + @"\Microsoft.NET\Framework64\v2.0.50727\Temporary ASP.NET Files",
                    win + @"\Microsoft.NET\Framework\v4.0.30319\Temporary ASP.NET Files",
                    win + @"\Microsoft.NET\Framework64\v4.0.30319\Temporary ASP.NET Files"
                };
                foreach (string c in caches)
                {
                    if (Directory.Exists(c))
                    {
                        DeleteDirectory(c, log);
                        notes.Add("已清理 .NET 缓存：" + c);
                    }
                }

                return "已修复错误1935/1706：" + Environment.NewLine + "  · " +
                       string.Join(Environment.NewLine + "  · ", notes.ToArray()) +
                       Environment.NewLine + "请重新尝试安装！";
            }
            catch (Exception ex)
            {
                return "修复 1935 时发生异常：" + ex.Message;
            }
        }

        // ==================== 组件与清理类 ====================

        /// <summary>安装时无限重启：清除 Windows Update 的挂起重启标记。</summary>
        public static string FixRestartLoop(Action<string> log)
        {
            try
            {
                DeleteRegistryKey(RegistryHive.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", log);
                return "无限重启问题已修复，请重新启动安装！";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复无限重启失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复无限重启时发生异常：" + ex.Message;
            }
        }

        /// <summary>2020 及以后版本安装闪退：清除 IFEO 里的 Debugger 劫持项。</summary>
        public static string FixInstallCrash2020Plus(Action<string> log)
        {
            try
            {
                const string basePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
                int removed = 0;
                foreach (string exe in CrashGuardExes)
                {
                    if (DeleteRegistryValue(RegistryHive.LocalMachine, basePath + "\\" + exe, "Debugger", log))
                    {
                        removed++;
                    }
                }
                return "安装闪退问题已修复（清除 " + removed + " 项劫持记录，适用于 2020 以后的版本），请重新启动安装！";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复安装闪退失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复安装闪退时发生异常：" + ex.Message;
            }
        }

        /// <summary>移除资源管理器里的 A360 Drive 盘符项。</summary>
        public static string RemoveA360Drive(Action<string> log)
        {
            try
            {
                const string subkey =
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{A7B36FF9-3BB0-426B-A737-A997B80466D5}";
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    RegDeleteTree(baseKey, subkey, log);
                }
                Log(log, "已移除 NameSpace 项：{A7B36FF9-...}");
                return "A360 Drive 盘符已删除！";
            }
            catch (UnauthorizedAccessException)
            {
                return "删除 A360 盘符失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "删除 A360 盘符时发生异常：" + ex.Message;
            }
        }

        /// <summary>2009-2021 版本：停止 FlexNet 许可服务并清除许可数据。</summary>
        public static string RemoveLicense2009_2021(Action<string> log)
        {
            try
            {
                Log(log, "停止 FlexNet 许可服务 ...");
                StopService("FlexNet Licensing Service");
                StopService("FlexNet Licensing Service 64");
                RunCommand("sc", "stop \"FlexNet Licensing Service\"", false);
                RunCommand("sc", "stop \"FlexNet Licensing Service 64\"", false);
                Thread.Sleep(1000);

                KillProcess("AdskLicensingAgent.exe");
                KillProcess("WSCommCntr4.exe");
                KillProcess("LMU.exe");
                Thread.Sleep(500);

                string[] files =
                {
                    @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data",
                    @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup",
                    @"C:\ProgramData\FLEXnet\adskflex_00691b00_tsf.data_backup.001",
                    @"C:\ProgramData\FLEXnet\adskflex_00691b00_event.log"
                };
                foreach (string f in files)
                {
                    DeleteFileSafe(f, log);
                }

                return "2009-2021 版本的 Autodesk 产品许可已删除！";
            }
            catch (Exception ex)
            {
                return "删除 2009-2021 许可时发生异常：" + ex.Message;
            }
        }

        /// <summary>2004-2008 版本：清除 Software Licenses 目录。</summary>
        public static string RemoveLicense2004_2008(Action<string> log)
        {
            try
            {
                DeleteDirectory(@"C:\ProgramData\Autodesk\Software Licenses", log);
                return "2004-2008 版本的 Autodesk 产品许可已删除！";
            }
            catch (Exception ex)
            {
                return "删除 2004-2008 许可时发生异常：" + ex.Message;
            }
        }

        /// <summary>移除 Autodesk Genuine Service（正版校验服务）的进程与目录。</summary>
        public static string RemoveGenuineService(Action<string> log)
        {
            try
            {
                Log(log, "结束 GenuineService.exe ...");
                KillProcess("GenuineService.exe");
                RunCommand("taskkill", "/f /im GenuineService.exe", false);
                Thread.Sleep(500);

                var dirs = new List<string>
                {
                    @"C:\Program Files\Autodesk\Genuine Service",
                    @"C:\ProgramData\Autodesk\Genuine Autodesk Service",
                    @"C:\ProgramData\Autodesk\Genuine Service"
                };

                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user))
                {
                    dirs.Add(@"C:\Users\" + user + @"\Autodesk\Genuine Service");
                }

                string localAppData = null;
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                    using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", false))
                    {
                        if (key != null)
                        {
                            object v = key.GetValue("Local AppData");
                            if (v != null)
                            {
                                localAppData = v.ToString();
                            }
                        }
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(localAppData))
                {
                    localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                }

                if (!string.IsNullOrEmpty(localAppData))
                {
                    dirs.Add(Path.Combine(localAppData, "Autodesk", "Genuine Autodesk Service"));
                    dirs.Add(Path.Combine(localAppData, "Programs", "Autodesk", "Genuine Service"));
                }

                foreach (string d in dirs)
                {
                    DeleteDirectory(d, log);
                }

                return "Autodesk Genuine Service 已移除！";
            }
            catch (Exception ex)
            {
                return "移除 Genuine Service 时发生异常：" + ex.Message;
            }
        }

        // ==================== 共用辅助 ====================

        /// <summary>1603/270/1 共用的 AdskLicensing 停止与清理流程。</summary>
        private static void StopLicensingStack(Action<string> log)
        {
            Log(log, "停止 AdskLicensingService ...");
            StopService("AdskLicensingService");
            RunCommand("sc", "stop AdskLicensingService", false);
            Thread.Sleep(1000);

            Log(log, "结束 AdskLicensingAgent / AdSSO 进程 ...");
            KillProcess("AdskLicensingAgent.exe");
            KillProcess("AdSSO.exe");
            Thread.Sleep(500);

            Log(log, "删除 AdskLicensing 残留目录 ...");
            DeleteDirectory(@"C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing", log);
            DeleteDirectory(@"C:\ProgramData\Autodesk\AdskLicensingService", log);
        }

        private static bool GrantDirectoryFullControl(string dir, Action<string> log)
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

                var rules = new List<FileSystemAccessRule>();
                if (user != null)
                {
                    rules.Add(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                }
                rules.Add(new FileSystemAccessRule(admins, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                rules.Add(new FileSystemAccessRule(users, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

                var root = new DirectoryInfo(dir);
                DirectorySecurity sec = root.GetAccessControl();
                foreach (FileSystemAccessRule r in rules)
                {
                    sec.AddAccessRule(r);
                }
                root.SetAccessControl(sec);

                // 子目录处理上限，避免超大目录耗时过长
                const int maxSubDirs = 1000;
                int done = 0;
                try
                {
                    foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
                    {
                        if (done >= maxSubDirs)
                        {
                            Log(log, "  已达子目录处理上限 " + maxSubDirs + "，其余跳过");
                            break;
                        }
                        try
                        {
                            var di = new DirectoryInfo(sub);
                            DirectorySecurity s = di.GetAccessControl();
                            foreach (FileSystemAccessRule r in rules)
                            {
                                s.AddAccessRule(r);
                            }
                            di.SetAccessControl(s);
                            done++;
                        }
                        catch { }
                    }
                }
                catch { }

                Log(log, "  已授权 " + dir + "（含 " + done + " 个子目录）");
                return true;
            }
            catch (Exception ex)
            {
                Log(log, "  授权失败：" + dir + " -> " + ex.Message);
                return false;
            }
        }

        private static bool CleanTempFiles(string dir, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return false;
            }
            if (DryRun)
            {
                int n = 0;
                try { n = Directory.GetFiles(dir).Length; } catch { }
                DryNote("清理临时文件 " + dir + "（" + n + " 个文件）", log);
                return true;
            }
            int removed = 0, skipped = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    try
                    {
                        File.SetAttributes(f, FileAttributes.Normal);
                        File.Delete(f);
                        removed++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }
            }
            catch { }

            Log(log, "  临时文件清理：" + dir + " 删除 " + removed + " 个，跳过 " + skipped + " 个（被占用）");
            return true;
        }

        private static void DeleteFileSafe(string path, Action<string> log)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log(log, "  跳过（不存在）：" + path);
                return;
            }
            if (DryRun)
            {
                DryNote("删除文件 " + path, log);
                return;
            }
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                Log(log, "  已删除：" + path);
            }
            catch (Exception ex)
            {
                Log(log, "  删除失败：" + path + " -> " + ex.Message);
            }
        }

        private static void DeleteRegistryKey(RegistryHive hive, string subKey, Action<string> log)
        {
            if (DryRun)
            {
                DryNote("删除注册表键 " + (hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU") + "\\" + subKey, log);
                return;
            }
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                {
                    baseKey.DeleteSubKeyTree(subKey, false);
                }
                Log(log, "  已删除注册表键：" + subKey);
            }
            catch (Exception ex)
            {
                Log(log, "  删除注册表键失败：" + subKey + " -> " + ex.Message);
            }
        }

        private static bool DeleteRegistryValue(RegistryHive hive, string subKey, string valueName, Action<string> log)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(subKey, true))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    object existing = key.GetValue(valueName);
                    if (existing == null)
                    {
                        return false;
                    }
                    if (DryRun)
                    {
                        DryNote("删除注册表值 " + subKey + " -> " + valueName, log);
                        return true;
                    }
                    key.DeleteValue(valueName, false);
                    Log(log, "  已清除劫持项：" + subKey + " -> " + valueName);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
