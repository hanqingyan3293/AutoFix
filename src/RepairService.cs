using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutodeskFix
{
    /// <summary>
    /// Autodesk 安装故障修复逻辑（独立实现，无任何登录/授权依赖）。
    /// 覆盖常见的 MSI/注册表类安装错误。
    /// </summary>
    internal static partial class RepairService
    {
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(-2147483646);
        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(-2147483647);

        private const int KEY_READ = 131097;
        private const int KEY_WRITE = 131078;
        private const int KEY_WRITE_DAC = 262144;
        private const int KEY_WRITE_OWNER = 524288;
        private const int KEY_WOW64_64KEY = 256;

        private const int OWNER_SECURITY_INFORMATION = 1;
        private const int DACL_SECURITY_INFORMATION = 4;

        private const int SE_PRIVILEGE_ENABLED = 2;
        private const uint TOKEN_ADJUST_PRIVILEGES = 32u;
        private const uint TOKEN_QUERY = 8u;

        private const string UserShellFolderDocumentsGuid = "{F42EE2E4-CD92-456a-9728-B14CB08064DD}";

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        // ==================== 公开修复入口 ====================

        /// <summary>1603：安装致命错误。清理 AdskLicensing 残留 + 修复失效的文档路径 + 修 .pit 权限。</summary>
        public static string Fix1603(Action<string> log)
        {
            try
            {
                string docNote = RepairDocumentsIfPointingToMissingDrive(log);

                Log(log, "停止 AdskLicensingService ...");
                StopService("AdskLicensingService");
                RunCommand("sc", "stop AdskLicensingService", false);
                System.Threading.Thread.Sleep(1000);

                Log(log, "结束 AdskLicensingAgent / AdSSO 进程 ...");
                KillProcess("AdskLicensingAgent.exe");
                KillProcess("AdSSO.exe");
                System.Threading.Thread.Sleep(500);

                Log(log, "删除 AdskLicensing 残留目录 ...");
                DeleteDirectory(@"C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing", log);
                DeleteDirectory(@"C:\ProgramData\Autodesk\AdskLicensingService", log);

                // ProductInformation.pit 有两代路径，都需覆盖：
                //   新版：%LOCALAPPDATA%\Autodesk\Web Services\ProductInformation.pit
                //   旧版：C:\ProgramData\Autodesk\Adlm\ProductInformation.pit
                foreach (string pit in PitPaths())
                {
                    if (File.Exists(pit) && !HasDeleteLikeAccess(pit))
                    {
                        Log(log, "缺少删除级权限，正在授予完全控制（不删除、不重命名）：" + pit);
                        GrantFullControl(pit);
                    }
                }

                return "错误1603 已修复。请尝试重新安装！" + (docNote ?? "");
            }
            catch (Exception ex)
            {
                return "修复 1603 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1402：注册表权限不足。为 Installer 相关分支重设所有权与完全控制权限。</summary>
        public static string Fix1402(Action<string> log)
        {
            try
            {
                Log(log, "停止 msiserver ...");
                StopService("msiserver");
                RunCommand("sc", "stop msiserver", false);
                System.Threading.Thread.Sleep(1000);

                EnableTakeOwnershipPrivilege();

                byte[] sd = BuildInstallerSecurityDescriptor();

                int ok = 0, fail = 0;
                SetBranchPermissions(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Components",
                    RegistryHive.LocalMachine, RegistryView.Registry64, sd, ref ok, ref fail, log);
                SetBranchPermissions(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Components",
                    RegistryHive.LocalMachine, RegistryView.Registry32, sd, ref ok, ref fail, log);
                SetBranchPermissions(@"SOFTWARE\Classes\Installer\Components",
                    RegistryHive.LocalMachine, RegistryView.Registry64, sd, ref ok, ref fail, log);
                SetBranchPermissions(@"SOFTWARE\Classes\Installer\Components",
                    RegistryHive.LocalMachine, RegistryView.Registry32, sd, ref ok, ref fail, log);
                SetBranchPermissions(@"SOFTWARE\Microsoft\Installer\Components",
                    RegistryHive.CurrentUser, RegistryView.Default, sd, ref ok, ref fail, log);

                if (ok <= 0)
                {
                    return "修复 1402 失败：未能为目标注册表键设置权限，请确认以管理员身份运行。";
                }
                if (fail > 0)
                {
                    return "错误1402 部分修复：成功 " + ok + " 项，失败 " + fail + " 项。可重试一次或重启后再试。";
                }
                return "错误1402 已修复：已为注册表键及其子项设置完全控制权限并启用继承。";
            }
            catch (UnauthorizedAccessException)
            {
                return "修复 1402 失败：需要管理员权限。";
            }
            catch (Exception ex)
            {
                return "修复 1402 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1625：策略限制安装。给 Installer 数据分支修权限，并清理无效的空注册表项。</summary>
        public static string Fix1625(Action<string> log)
        {
            try
            {
                Log(log, "停止 msiserver ...");
                StopService("msiserver");
                RunCommand("sc", "stop msiserver", false);
                System.Threading.Thread.Sleep(1000);

                EnableTakeOwnershipPrivilege();
                byte[] sd = BuildInstallerSecurityDescriptor();

                string[] branches =
                {
                    @"SOFTWARE\Classes\Installer\Products",
                    @"SOFTWARE\Classes\Installer\Features",
                    @"SOFTWARE\Classes\Installer\Patches"
                };

                int removed = 0;
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (string branch in branches)
                    {
                        using (RegistryKey key = baseKey.OpenSubKey(branch, false))
                        {
                            if (key == null)
                            {
                                continue;
                            }

                            int ok = 0, fail = 0;
                            SetKeySecurity(HKEY_LOCAL_MACHINE, branch, sd, ref ok, ref fail);
                            SetRegistrySecurityRecursive(key, branch, HKEY_LOCAL_MACHINE, sd, ref ok, ref fail);
                            Log(log, branch + "：权限成功 " + ok + " 项，失败 " + fail + " 项");

                            List<KeyRef> targets = new List<KeyRef>();
                            if (branch.Contains("Products"))
                            {
                                CollectLeafKeys(key, branch, targets);
                            }
                            else
                            {
                                CollectEmptyValueKeys(key, branch, targets);
                            }

                            foreach (KeyRef t in targets)
                            {
                                if (DeleteRegKey(t.ParentPath, t.KeyName))
                                {
                                    removed++;
                                }
                            }
                        }
                    }
                }

                return "错误1625 已修复，清理无效注册表项 " + removed + " 个。";
            }
            catch (Exception ex)
            {
                return "修复 1625 时发生异常：" + ex.Message;
            }
        }

        /// <summary>1606：公共文件夹路径注册表缺失或错误。恢复 Public 各目录映射。</summary>
        public static string Fix1606(Action<string> log)
        {
            try
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

                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path, true)
                                       ?? Registry.LocalMachine.CreateSubKey(path))
                {
                    if (key == null)
                    {
                        return "修复 1606 失败：无法打开注册表键 " + path;
                    }
                    foreach (KeyValuePair<string, string> kv in map)
                    {
                        RegWriteValue(key, kv.Key, kv.Value, RegistryValueKind.String, log);
                        Log(log, "写入 " + kv.Key + " = " + kv.Value);
                    }
                }

                foreach (string dir in map.Values)
                {
                    CreateDirectoryIfNotExists(dir);
                }

                return "错误1606 已修复，请尝试重新安装！";
            }
            catch (Exception ex)
            {
                return "修复 1606 时发生异常：" + ex.Message;
            }
        }


        // ==================== 1603 的文档路径修复 ====================

        private static string RepairDocumentsIfPointingToMissingDrive(Action<string> log)
        {
            try
            {
                string normalized, drive;
                if (!TryGetMissingDriveLetter(GetPersonalDocumentsFromRegistry(), out normalized, out drive))
                {
                    return null;
                }

                Log(log, "检测到「我的文档」指向不存在的磁盘 " + drive + "，路径：" + normalized);
                ApplyDefaultDocumentsFolder(normalized);
                return "（同时已把「我的文档」恢复为默认目录，原路径位于不存在的磁盘 " + drive + "）";
            }
            catch (Exception ex)
            {
                Log(log, "文档路径修复异常：" + ex.Message);
                return null;
            }
        }

        private static string GetPersonalDocumentsFromRegistry()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    using (RegistryKey key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", false))
                    {
                        if (key != null)
                        {
                            object v = key.GetValue("Personal");
                            if (v != null)
                            {
                                return Environment.ExpandEnvironmentVariables(v.ToString());
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private static bool TryGetMissingDriveLetter(string expanded, out string normalized, out string drive)
        {
            normalized = null;
            drive = null;
            if (string.IsNullOrWhiteSpace(expanded))
            {
                return false;
            }
            try
            {
                normalized = Path.GetFullPath(expanded.Trim());
            }
            catch
            {
                return false;
            }

            string root = Path.GetPathRoot(normalized);
            if (string.IsNullOrEmpty(root) || root.Length < 3 || root[1] != ':')
            {
                return false;
            }

            drive = char.ToUpperInvariant(normalized[0]) + ":";
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (string.Equals(d.Name.TrimEnd('\\'), drive, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static void ApplyDefaultDocumentsFolder(string previousPath)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = Environment.GetEnvironmentVariable("USERPROFILE");
            }
            if (string.IsNullOrWhiteSpace(profile))
            {
                return;
            }

            string newDocs = Path.Combine(profile, "Documents");
            FsCreateDirectory(newDocs, null);

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    {
                        using (RegistryKey usf = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", true))
                        {
                            if (usf != null)
                            {
                                RegWriteValue(usf, "Personal", @"%USERPROFILE%\Documents", RegistryValueKind.ExpandString, null);
                                RegWriteValue(usf, UserShellFolderDocumentsGuid, @"%USERPROFILE%\Documents", RegistryValueKind.ExpandString, null);
                            }
                        }
                        using (RegistryKey sf = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", true))
                        {
                            if (sf != null)
                            {
                                RegWriteValue(sf, "Personal", newDocs, RegistryValueKind.String, null);
                            }
                        }
                    }
                }
                catch { }
            }

            // 同步修复文档库 XML 中对失效路径的引用
            try
            {
                string lib = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Libraries\Documents.library-ms");
                if (File.Exists(lib))
                {
                    string xml = File.ReadAllText(lib, Encoding.UTF8);
                    string updated = ReplacePathLiterals(xml, previousPath, newDocs);
                    if (!string.Equals(updated, xml, StringComparison.Ordinal))
                    {
                        FsCopy(lib, lib + ".autodesk-fix.bak", null);
                        FsWriteText(lib, updated, null);
                    }
                }
            }
            catch { }

            try
            {
                foreach (string target in new[] { "Environment", "Shell Folders" })
                {
                    UIntPtr _;
                    SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, target, 0x0002, 500, out _);
                }
            }
            catch { }
        }

        private static string ReplacePathLiterals(string xml, string from, string to)
        {
            string fromUrl = "file:///" + from.Replace('\\', '/').TrimStart('/');
            string toUrl = "file:///" + to.Replace('\\', '/').TrimStart('/');

            var pairs = new[]
            {
                new KeyValuePair<string, string>(fromUrl, toUrl),
                new KeyValuePair<string, string>(from.Replace('\\', '/'), to.Replace('\\', '/')),
                new KeyValuePair<string, string>(from, to)
            };

            string result = xml;
            foreach (var p in pairs)
            {
                if (result.IndexOf(p.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = Regex.Replace(result, "(?i)" + Regex.Escape(p.Key), p.Value.Replace("$", "$$"));
                }
            }
            return result;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        // ==================== 注册表权限 ====================

        private static byte[] BuildInstallerSecurityDescriptor()
        {
            NTAccount admins = (NTAccount)new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Translate(typeof(NTAccount));
            NTAccount system = (NTAccount)new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Translate(typeof(NTAccount));
            NTAccount world = (NTAccount)new SecurityIdentifier(WellKnownSidType.WorldSid, null).Translate(typeof(NTAccount));

            var sec = new RegistrySecurity();
            sec.SetOwner(admins);
            sec.SetAccessRuleProtection(false, false);

            foreach (NTAccount acct in new[] { admins, system, world })
            {
                sec.AddAccessRule(new RegistryAccessRule(acct, RegistryRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly, AccessControlType.Allow));
                sec.AddAccessRule(new RegistryAccessRule(acct, RegistryRights.FullControl,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
            }

            return sec.GetSecurityDescriptorBinaryForm();
        }

        private static void SetBranchPermissions(string path, RegistryHive hive, RegistryView view,
            byte[] sd, ref int ok, ref int fail, Action<string> log)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = baseKey.OpenSubKey(path, false))
                {
                    if (key == null)
                    {
                        return;
                    }
                    IntPtr hiveHandle = HiveHandle(hive);
                    SetKeySecurity(hiveHandle, path, sd, ref ok, ref fail);
                    SetRegistrySecurityRecursive(key, path, hiveHandle, sd, ref ok, ref fail);
                    Log(log, path + " [" + view + "]：成功 " + ok + " 项，失败 " + fail + " 项");
                }
            }
            catch (Exception ex)
            {
                Log(log, path + " 权限处理异常：" + ex.Message);
            }
        }

        private static IntPtr HiveHandle(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE;
        }

        private static void SetKeySecurity(IntPtr hive, string path, byte[] sd, ref int ok, ref int fail)
        {
            if (DryRun)
            {
                DryNote("设置注册表键权限（所有权 + 完全控制）：" + path, null);
                ok++;
                return;
            }
            IntPtr hKey = IntPtr.Zero;
            try
            {
                if (RegOpenKeyEx(hive, path, 0u, KEY_READ | KEY_WRITE_DAC | KEY_WRITE_OWNER | KEY_WOW64_64KEY, out hKey) == 0
                    && hKey != IntPtr.Zero)
                {
                    if (RegSetKeySecurity(hKey, OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION, sd) == 0)
                    {
                        ok++;
                    }
                    else
                    {
                        fail++;
                    }
                }
                else
                {
                    fail++;
                }
            }
            finally
            {
                if (hKey != IntPtr.Zero)
                {
                    RegCloseKey(hKey);
                }
            }
        }

        private static void SetRegistrySecurityRecursive(RegistryKey parent, string parentPath, IntPtr hive,
            byte[] sd, ref int ok, ref int fail)
        {
            if (DryRun)
            {
                int n = 0;
                try { n = CountSubKeysRecursive(parent); } catch { }
                if (n > 0)
                {
                    DryNote("递归设置注册表权限，共 " + n + " 个子键：" + parentPath, null);
                    ok += n;
                }
                return;
            }
            try
            {
                if (parent == null)
                {
                    return;
                }
                foreach (string sub in parent.GetSubKeyNames())
                {
                    try
                    {
                        SetKeySecurity(hive, parentPath + "\\" + sub, sd, ref ok, ref fail);
                        using (RegistryKey child = parent.OpenSubKey(sub, false))
                        {
                            if (child != null)
                            {
                                SetRegistrySecurityRecursive(child, parentPath + "\\" + sub, hive, sd, ref ok, ref fail);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern uint RegSetKeySecurity(IntPtr hKey, int SecurityInformation, byte[] pSecurityDescriptor);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint RegDeleteKeyEx(IntPtr hKey, string lpSubKey, int samDesired, int Reserved);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private static void EnableTakeOwnershipPrivilege()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                {
                    var state = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Attributes = SE_PRIVILEGE_ENABLED };
                    if (LookupPrivilegeValue(null, "SeTakeOwnershipPrivilege", out state.Luid))
                    {
                        AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }
            catch { }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }

        // ==================== 无效注册表项收集 / 删除 ====================

        private class KeyRef
        {
            public string ParentPath;
            public string KeyName;
            public KeyRef(string parentPath, string keyName)
            {
                ParentPath = parentPath;
                KeyName = keyName;
            }
        }

        /// <summary>收集没有任何命名值的空叶子键。</summary>
        private static void CollectEmptyValueKeys(RegistryKey parent, string parentPath, List<KeyRef> targets)
        {
            if (parent == null)
            {
                return;
            }
            foreach (string sub in parent.GetSubKeyNames())
            {
                try
                {
                    using (RegistryKey child = parent.OpenSubKey(sub, false))
                    {
                        if (child == null)
                        {
                            continue;
                        }
                        string[] subs = child.GetSubKeyNames();
                        if (subs != null && subs.Length != 0)
                        {
                            continue;
                        }
                        string[] values = child.GetValueNames();
                        int named = 0;
                        if (values != null)
                        {
                            foreach (string v in values)
                            {
                                if (!string.IsNullOrEmpty(v))
                                {
                                    named++;
                                }
                            }
                        }
                        if (named != 0)
                        {
                            continue;
                        }

                        object def = null;
                        try { def = child.GetValue(null); } catch { }
                        if (def == null || (def is string && string.IsNullOrEmpty((string)def)))
                        {
                            targets.Add(new KeyRef(parentPath, sub));
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>收集没有子键的叶子键（Products 分支用）。</summary>
        private static void CollectLeafKeys(RegistryKey parent, string parentPath, List<KeyRef> targets)
        {
            if (parent == null)
            {
                return;
            }
            foreach (string sub in parent.GetSubKeyNames())
            {
                try
                {
                    bool isLeaf = false;
                    using (RegistryKey child = parent.OpenSubKey(sub, false))
                    {
                        if (child != null)
                        {
                            try
                            {
                                string[] subs = child.GetSubKeyNames();
                                isLeaf = subs == null || subs.Length == 0;
                            }
                            catch
                            {
                                isLeaf = false;
                            }
                        }
                    }
                    if (isLeaf)
                    {
                        targets.Add(new KeyRef(parentPath, sub));
                    }
                    else
                    {
                        using (RegistryKey child = parent.OpenSubKey(sub, false))
                        {
                            if (child != null)
                            {
                                CollectLeafKeys(child, parentPath + "\\" + sub, targets);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static bool DeleteRegKey(string parentPath, string keyName)
        {
            if (DryRun)
            {
                DryNote("删除注册表键 " + parentPath + "\\" + keyName, null);
                return true;
            }
            IntPtr parent = IntPtr.Zero;
            try
            {
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, parentPath, 0u, KEY_WRITE, out parent) == 0 && parent != IntPtr.Zero)
                {
                    if (RegDeleteKeyEx(parent, keyName, KEY_WOW64_64KEY, 0) == 0)
                    {
                        return true;
                    }
                }
            }
            catch { }
            finally
            {
                if (parent != IntPtr.Zero)
                {
                    RegCloseKey(parent);
                }
            }

            // 回退：用托管 API 删除
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(parentPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl))
                {
                    if (key != null)
                    {
                        key.DeleteSubKeyTree(keyName, false);
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // ==================== 文件权限 ====================

        private static bool HasDeleteLikeAccess(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return true;
            }
            try
            {
                AuthorizationRuleCollection rules = File.GetAccessControl(filePath)
                    .GetAccessRules(true, true, typeof(SecurityIdentifier));

                WindowsIdentity current = WindowsIdentity.GetCurrent();
                var sids = new HashSet<SecurityIdentifier>();
                if (current.User != null)
                {
                    sids.Add(current.User);
                }
                foreach (IdentityReference g in current.Groups)
                {
                    SecurityIdentifier sid = g as SecurityIdentifier;
                    if (sid != null)
                    {
                        sids.Add(sid);
                    }
                }

                foreach (FileSystemAccessRule rule in rules)
                {
                    if (rule.AccessControlType != AccessControlType.Allow)
                    {
                        continue;
                    }
                    SecurityIdentifier rsid = rule.IdentityReference as SecurityIdentifier;
                    if (rsid == null || !sids.Contains(rsid))
                    {
                        continue;
                    }
                    FileSystemRights r = rule.FileSystemRights;
                    if ((r & FileSystemRights.FullControl) != 0
                     || (r & FileSystemRights.Modify) != 0
                     || (r & FileSystemRights.Delete) != 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void GrantFullControl(string filePath)
        {
            if (!ShouldDo("修改文件权限（完全控制）" + filePath))
            {
                return;
            }
            try
            {
                var fi = new FileInfo(filePath);
                FileSecurity sec = fi.GetAccessControl();
                SecurityIdentifier user = WindowsIdentity.GetCurrent()?.User;
                var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

                if (user != null)
                {
                    sec.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                }
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
                fi.SetAccessControl(sec);
            }
            catch { }
        }

        // ==================== 通用辅助 ====================

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void StopService(string name)
        {
            if (!ShouldDo("停止服务 " + name))
            {
                return;
            }
            try
            {
                using (ServiceController sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
            }
            catch { }
        }

        private static void RunCommand(string file, string args, bool wait)
        {
            if (!ShouldDo("执行命令 " + file + " " + args))
            {
                return;
            }
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
                    if (wait && p != null)
                    {
                        p.WaitForExit();
                    }
                }
            }
            catch { }
        }

        private static void KillProcess(string name)
        {
            if (DryRun)
            {
                int n = 0;
                try { n = Process.GetProcessesByName(name.Replace(".exe", "")).Length; } catch { }
                if (n > 0)
                {
                    ShouldDo("强制结束进程 " + name + "（" + n + " 个）");
                }
                return;
            }
            try
            {
                foreach (Process p in Process.GetProcessesByName(name.Replace(".exe", "")))
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void DeleteDirectory(string path, Action<string> log)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                Log(log, "  跳过（不存在）：" + path);
                return;
            }
            if (DryRun)
            {
                DryNote("删除目录 " + path, log);
                return;
            }
            try
            {
                new DirectoryInfo(path).Attributes = FileAttributes.Normal;
                Directory.Delete(path, true);
                Log(log, "  已删除：" + path);
            }
            catch (Exception ex)
            {
                Log(log, "  删除失败：" + path + " -> " + ex.Message);
            }
        }

        private static void CreateDirectoryIfNotExists(string path)
        {
            if (DryRun)
            {
                if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                {
                    DryNote("创建目录 " + path, null);
                }
                return;
            }
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch { }
        }

        private static void Log(Action<string> log, string message)
        {
            try { if (log != null) log(message); } catch { }
        }
    }
}
