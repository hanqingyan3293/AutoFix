using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace AutoFix
{
    // 卸载流程的补充阶段：锁定目录重试、多用户清理、服务刷新。
    // 阶段设计参考 autodesk-complete-uninstaller（MIT），实现为原生 C#。
    internal static partial class RepairService
    {
        /// <summary>
        /// 阶段 E3：重试删除被占用的目录。
        /// 先强制结束残留进程、重启资源管理器释放外壳扩展句柄，再删除；
        /// 仍失败则授予权限后重试；最后剩下的登记为「重启后清理」。
        /// </summary>
        internal static List<string> RetryLockedFolders(List<string> folders, Action<string> log)
        {
            var stillLocked = new List<string>();
            if (folders == null || folders.Count == 0)
            {
                return stillLocked;
            }

            var candidates = new List<string>();
            foreach (string d in folders)
            {
                if (!string.IsNullOrEmpty(d) && Directory.Exists(d))
                {
                    candidates.Add(d);
                }
            }
            if (candidates.Count == 0)
            {
                return stillLocked;
            }

            Log(log, "[阶段 E3] 重试删除被占用的目录：" + candidates.Count + " 个");

            if (DryRun)
            {
                foreach (string d in candidates)
                {
                    DryNote("重试删除被占用目录 " + d, log);
                }
                return stillLocked;
            }

            // 1) 再杀一轮进程
            foreach (string p in UninstallKillProcs)
            {
                KillProcess(p + ".exe");
            }
            StopService("WSearch");
            StopService("msiserver");

            // 2) 重启资源管理器，释放外壳扩展句柄
            Log(log, "  重启资源管理器以释放文件句柄 ...");
            RunCommand("taskkill", "/f /im explorer.exe", true);
            Thread.Sleep(2000);
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string exe = string.IsNullOrEmpty(win) ? "explorer.exe" : Path.Combine(win, "explorer.exe");
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exe,
                        UseShellExecute = true
                    }))
                {
                    if (p != null) { p.Dispose(); }
                }
            }
            catch { }
            Thread.Sleep(2000);

            // 3) 先删文件再删空目录，然后整体删除
            foreach (string d in candidates)
            {
                DeleteDirectoryContents(d, log);

                if (!Directory.Exists(d))
                {
                    continue;
                }
                DeleteDirectory(d, log);

                // 4) 仍失败 → 取所有权并授权后重试
                if (Directory.Exists(d))
                {
                    Log(log, "  仍被占用，授予权限后重试：" + d);
                    GrantDirInheritedACL(d, log);
                    DeleteDirectoryContents(d, log);
                    DeleteDirectory(d, log);
                }

                if (Directory.Exists(d))
                {
                    Log(log, "  仍无法删除，将安排重启后清理：" + d);
                    stillLocked.Add(d);
                }
                else
                {
                    Log(log, "  已删除：" + d);
                }
            }

            // 5) 仍锁定的登记到 RunOnce
            if (stillLocked.Count > 0)
            {
                ScheduleRebootCleanup(stillLocked, log);
            }

            return stillLocked;
        }

        /// <summary>删除目录下的文件与子目录，但保留目录本身。</summary>
        private static void DeleteDirectoryContents(string dir, Action<string> log)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return;
                }
                foreach (string f in Directory.GetFiles(dir))
                {
                    FsDeleteFile(f, log);
                }
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    DeleteDirectory(sub, log);
                }
            }
            catch (Exception ex)
            {
                Log(log, "  清理目录内容失败：" + dir + " -> " + ex.Message);
            }
        }

        /// <summary>把仍锁定的目录写进 RunOnce，重启登录时自动删除。</summary>
        private static void ScheduleRebootCleanup(List<string> folders, Action<string> log)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoFix");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                string script = Path.Combine(logDir, "reboot_cleanup.bat");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("timeout /t 10 /nobreak >nul");
                foreach (string d in folders)
                {
                    sb.AppendLine("if exist \"" + d + "\" rd /s /q \"" + d + "\"");
                }
                sb.AppendLine("del /f \"%~f0\"");

                if (DryRun)
                {
                    DryNote("写入重启清理脚本并登记 RunOnce：" + script + "（" + folders.Count + " 个目录）", log);
                    return;
                }

                File.WriteAllText(script, sb.ToString(), System.Text.Encoding.Default);

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", true))
                {
                    if (key != null)
                    {
                        key.SetValue("AutoFixAutodeskCleanup", "\"" + script + "\"", RegistryValueKind.String);
                        Log(log, "  已登记重启清理（RunOnce）：" + folders.Count + " 个目录");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, "  登记重启清理失败：" + ex.Message);
            }
        }

        // ==================== 多用户清理 ====================

        /// <summary>其他用户配置文件中的 Autodesk 残留（注册表 + AppData）。</summary>
        internal static string CleanOtherUserProfiles(Action<string> log)
        {
            if (DryRun)
            {
                int n0 = CountOtherProfiles();
                DryNote("清理其他用户配置文件的 Autodesk 注册表与 AppData（" + n0 + " 个配置文件）", log);
                return "[预演] 将清理 " + n0 + " 个其他用户配置文件中的 Autodesk 注册表与 AppData。";
            }

            int cleaned = 0, skipped = 0, notFound = 0;
            List<string> profiles;
            try
            {
                profiles = OtherProfilePaths();
            }
            catch (Exception ex)
            {
                return "读取用户配置文件列表失败：" + ex.Message;
            }

            if (profiles.Count == 0)
            {
                return "未发现其他用户配置文件。";
            }

            foreach (string profile in profiles)
            {
                string name;
                try { name = Path.GetFileName(profile.TrimEnd('\\', '/')); } catch { name = profile; }

                // 该用户的注册表配置单元是否已加载（已登录用户）
                string sid = SidForProfile(profile);
                bool loaded = false;
                if (!string.IsNullOrEmpty(sid))
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default))
                        using (RegistryKey k = baseKey.OpenSubKey(sid, false))
                        {
                            loaded = k != null;
                        }
                    }
                    catch { }
                }

                if (loaded)
                {
                    Log(log, "  跳过（该用户已登录）：" + name);
                    skipped++;
                    continue;
                }

                bool did = false;

                // 注册表：加载 NTUSER.DAT 后清理
                if (!string.IsNullOrEmpty(sid))
                {
                    string dat = Path.Combine(profile, "NTUSER.DAT");
                    if (File.Exists(dat))
                    {
                        string tempKey = "AutoFixTEMP_" + name;
                        bool mounted = false;
                        try
                        {
                            LoadUserHive(tempKey, dat);
                            mounted = true;
                        }
                        catch { }

                        if (mounted)
                        {
                            try
                            {
                                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default))
                                using (RegistryKey k = baseKey.OpenSubKey(tempKey + @"\Software\Autodesk", false))
                                {
                                    if (k != null)
                                    {
                                        k.Close();
                                        baseKey.DeleteSubKeyTree(tempKey + @"\Software\Autodesk", false);
                                        Log(log, "  " + name + "：已清除用户注册表中的 Autodesk");
                                        did = true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log(log, "  " + name + "：清除注册表失败 -> " + ex.Message);
                            }
                            finally
                            {
                                try { UnloadUserHive(tempKey); } catch { }
                            }
                        }
                    }
                }

                // AppData
                try
                {
                    string roaming = Path.Combine(profile, "AppData", "Roaming", "Autodesk");
                    string local = Path.Combine(profile, "AppData", "Local", "Autodesk");
                    if (Directory.Exists(roaming))
                    {
                        DeleteDirectory(roaming, log);
                        did = true;
                    }
                    if (Directory.Exists(local))
                    {
                        DeleteDirectory(local, log);
                        did = true;
                    }
                }
                catch { }

                if (did)
                {
                    cleaned++;
                }
                else
                {
                    notFound++;
                }
            }

            return "多用户清理完成：已清理 " + cleaned + " 个配置文件，"
                 + "跳过 " + skipped + " 个（用户已登录），无残留 " + notFound + " 个。";
        }

        /// <summary>枚举其他用户配置文件路径（排除当前用户）。</summary>
        private static List<string> OtherProfilePaths()
        {
            var list = new List<string>();
            string self = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(self))
            {
                self = Environment.GetEnvironmentVariable("USERPROFILE");
            }

            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList", false))
                {
                    if (key == null)
                    {
                        return list;
                    }
                    foreach (string sid in key.GetSubKeyNames())
                    {
                        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        try
                        {
                            using (RegistryKey sub = key.OpenSubKey(sid, false))
                            {
                                if (sub == null)
                                {
                                    continue;
                                }
                                object v = sub.GetValue("ProfileImagePath");
                                if (v == null)
                                {
                                    continue;
                                }
                                string path = v.ToString();
                                if (string.IsNullOrWhiteSpace(path))
                                {
                                    continue;
                                }
                                if (!string.IsNullOrEmpty(self)
                                    && string.Equals(path.TrimEnd('\\', '/'), self.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                                list.Add(path);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return list;
        }

        private static int CountOtherProfiles()
        {
            try { return OtherProfilePaths().Count; } catch { return 0; }
        }

        /// <summary>按配置文件路径反查 SID。</summary>
        private static string SidForProfile(string profilePath)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList", false))
                {
                    if (key == null)
                    {
                        return null;
                    }
                    foreach (string sid in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey sub = key.OpenSubKey(sid, false))
                            {
                                if (sub == null)
                                {
                                    continue;
                                }
                                object v = sub.GetValue("ProfileImagePath");
                                if (v == null)
                                {
                                    continue;
                                }
                                if (string.Equals(v.ToString().TrimEnd('\\', '/'),
                                        profilePath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                                {
                                    return sid;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

        private static readonly IntPtr HKEY_USERS = new IntPtr(-2147483645);

        private static void LoadUserHive(string subKeyName, string hiveFile)
        {
            int rc = RegLoadKey(HKEY_USERS, subKeyName, hiveFile);
            if (rc != 0)
            {
                throw new Exception("RegLoadKey 失败，返回码 " + rc);
            }
        }

        private static void UnloadUserHive(string subKeyName)
        {
            RegUnLoadKey(HKEY_USERS, subKeyName);
        }

        // ==================== 服务刷新 ====================

        /// <summary>刷新 Windows Installer 服务，清除内存中的重启挂起状态。</summary>
        internal static void FlushInstallerServices(Action<string> log)
        {
            Log(log, "[服务刷新] 重启 Windows Installer 服务 ...");
            StopService("msiserver");
            RunCommand("net", "stop msiserver /y", false);
            Thread.Sleep(1000);
            RunCommand("net", "start msiserver", false);
            Thread.Sleep(1000);
        }
    }
}
