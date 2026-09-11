// Autodesk 1603 错误修复工具
// 从 Autobox 1.0.4 (ErrorRepairService.FixError1603) 逆向还原，无授权校验依赖，可直接运行。
//
// 修复原理（按执行顺序）：
//   1. 「我的文档」指向已不存在的磁盘时，恢复为 %USERPROFILE%\Documents
//   2. 停止并清理 AdskLicensing 服务与进程
//   3. 删除 AdskLicensing 残留目录
//   4. 修复 ProductInformation.pit 的删除级权限
// 需以管理员身份运行（清单已声明 requireAdministrator）。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Fix1603
{
    internal static class Program
    {
        private const string PitPath = @"C:\ProgramData\Autodesk\Adlm\ProductInformation.pit";
        private const string AdskLicensingDir1 = @"C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing";
        private const string AdskLicensingDir2 = @"C:\ProgramData\Autodesk\AdskLicensingService";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "Autodesk 1603 错误修复工具";

            bool autoYes = args.Any(a => a.Equals("--yes", StringComparison.OrdinalIgnoreCase)
                                       || a.Equals("-y", StringComparison.OrdinalIgnoreCase));

            Console.WriteLine("==================================================");
            Console.WriteLine(" Autodesk 1603 错误修复工具");
            Console.WriteLine(" 原理：清理 AdskLicensing + 修复文档路径/权限");
            Console.WriteLine("==================================================");
            Console.WriteLine();

            if (!IsAdministrator())
            {
                Console.WriteLine("[!] 当前未以管理员身份运行，部分操作会失败。");
                Console.WriteLine("    请右键 -> 以管理员身份运行。");
                Console.WriteLine();
            }

            // ---------- 预扫描：先列出本次实际会执行的操作，供用户确认 ----------
            bool dir1Exists = Directory.Exists(AdskLicensingDir1);
            bool dir2Exists = Directory.Exists(AdskLicensingDir2);
            bool pitExists = File.Exists(PitPath);
            bool pitNeedsFix = pitExists && !CurrentUserHasDeleteLikeAccessToFile(PitPath);
            bool svcExists = ServiceExists("AdskLicensingService");
            bool agentRunning = IsProcessRunning("AdskLicensingAgent");
            bool ssoRunning = IsProcessRunning("AdSSO");
            bool needSvcStep = svcExists || agentRunning || ssoRunning;
            bool needDirStep = dir1Exists || dir2Exists;

            var plan = new List<string>();
            if (needSvcStep)
            {
                plan.Add("停止系统服务 / 结束进程（AdskLicensingService、AdskLicensingAgent.exe、AdSSO.exe）");
            }
            if (needDirStep)
            {
                plan.Add("永久删除 AdskLicensing 残留目录（不进回收站）");
            }
            if (pitNeedsFix)
            {
                plan.Add("修改 ProductInformation.pit 访问权限（授予完全控制，不删除文件）");
            }

            Console.WriteLine("本次检测到的可执行操作：");
            if (plan.Count == 0)
            {
                Console.WriteLine("  （无：未发现 AdskLicensing 残留，服务与进程均不存在）");
            }
            else
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    Console.WriteLine("  " + (i + 1) + ". " + plan[i]);
                }
                Console.WriteLine();
                Console.WriteLine("  另外：若「我的文档」指向已失效的磁盘，会单独询问是否修复（修复时会重启资源管理器）。");
            }
            Console.WriteLine();

            if (plan.Count > 0 && !Confirm(
                    "总确认：即将对系统进行以下更改",
                    string.Join(Environment.NewLine, plan.Select(p => "      · " + p).ToArray())
                        + Environment.NewLine + "      以上操作会影响系统服务、进程与文件，删除不可撤销。",
                    autoYes))
            {
                Console.WriteLine("已取消，未做任何更改。");
                Console.WriteLine();
                Console.WriteLine("按任意键退出...");
                if (!autoYes)
                {
                    try { Console.ReadKey(true); } catch { }
                }
                return 0;
            }

            bool svcDone = false;
            bool dirDone = false;
            bool pitDone = false;
            string docRepair = null;

            try
            {
                // ---------- 步骤 0：文档路径（内部自带确认） ----------
                docRepair = RepairMyDocumentsIfPointingToMissingDrive(autoYes);

                // ---------- 步骤 1：停止服务 + 结束进程（二次确认） ----------
                if (needSvcStep)
                {
                    var detail = new StringBuilder();
                    if (svcExists)
                    {
                        detail.AppendLine("      · 停止系统服务 AdskLicensingService（最多等待 10 秒）");
                    }
                    if (agentRunning)
                    {
                        detail.AppendLine("      · 强制结束进程 AdskLicensingAgent.exe（其中未保存的操作会丢失）");
                    }
                    if (ssoRunning)
                    {
                        detail.AppendLine("      · 强制结束进程 AdSSO.exe（其中未保存的操作会丢失）");
                    }

                    if (Confirm("危险操作 1/3：停止服务并结束进程", detail.ToString().TrimEnd(), autoYes))
                    {
                        Console.WriteLine("[1/4] 停止 AdskLicensingService ...");
                        StopService("AdskLicensingService");
                        ExecuteCommand("sc", "stop AdskLicensingService", false);
                        System.Threading.Thread.Sleep(1000);

                        Console.WriteLine("[2/4] 结束 AdskLicensingAgent / AdSSO 进程 ...");
                        KillProcess("AdskLicensingAgent.exe");
                        KillProcess("AdSSO.exe");
                        System.Threading.Thread.Sleep(500);
                        svcDone = true;
                    }
                    else
                    {
                        Console.WriteLine("[1-2/4] 已跳过（服务与进程均未改动）");
                    }
                }
                else
                {
                    Console.WriteLine("[1-2/4] 服务与进程均不存在，无需处理");
                }

                // ---------- 步骤 2：删除残留目录（二次确认） ----------
                if (needDirStep)
                {
                    var detail = new StringBuilder();
                    detail.AppendLine("      · 永久删除以下目录及其全部内容（不进回收站，无法撤销）：");
                    if (dir1Exists)
                    {
                        detail.AppendLine("          " + AdskLicensingDir1);
                    }
                    if (dir2Exists)
                    {
                        detail.AppendLine("          " + AdskLicensingDir2);
                    }

                    if (Confirm("危险操作 2/3：删除 AdskLicensing 残留目录", detail.ToString().TrimEnd(), autoYes))
                    {
                        Console.WriteLine("[3/4] 删除 AdskLicensing 残留目录 ...");
                        if (dir1Exists) { Console.WriteLine("      " + AdskLicensingDir1); }
                        DeleteDirectory(AdskLicensingDir1);
                        if (dir2Exists) { Console.WriteLine("      " + AdskLicensingDir2); }
                        DeleteDirectory(AdskLicensingDir2);
                        dirDone = true;
                    }
                    else
                    {
                        Console.WriteLine("[3/4] 已跳过（目录未删除）");
                    }
                }
                else
                {
                    Console.WriteLine("[3/4] 残留目录不存在，无需删除");
                }

                // ---------- 步骤 3：修改文件权限（二次确认） ----------
                if (pitNeedsFix)
                {
                    string detail =
                        "      · 修改以下文件的访问控制列表（ACL），授予「完全控制」：" + Environment.NewLine +
                        "          当前用户 / Administrators / SYSTEM" + Environment.NewLine +
                        "      · 目标文件：" + PitPath + Environment.NewLine +
                        "      · 该文件不会被删除，也不会被重命名";

                    if (Confirm("危险操作 3/3：修改文件权限", detail, autoYes))
                    {
                        Console.WriteLine("[4/4] 检查 ProductInformation.pit 权限 ...");
                        Log("ProductInformation.pit 缺少删除级权限，正在授予完全控制（不删除、不重命名）");
                        EnsureFileFullControl(PitPath);
                        Console.WriteLine("      已授予完全控制权限。");
                        pitDone = true;
                    }
                    else
                    {
                        Console.WriteLine("[4/4] 已跳过（权限未改动）");
                    }
                }
                else
                {
                    Console.WriteLine("[4/4] ProductInformation.pit 无需调整");
                }

                // ---------- 结果汇总：区分「已执行 / 已跳过 / 无需处理」 ----------
                Console.WriteLine();
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine(" 执行结果");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("   服务与进程处理：" + (svcDone ? "已执行" : (needSvcStep ? "已跳过" : "无需处理")));
                Console.WriteLine("   残留目录删除：" + (dirDone ? "已执行" : (needDirStep ? "已跳过" : "无需处理")));
                Console.WriteLine("   .pit 权限修复：" + (pitDone ? "已执行" : (pitNeedsFix ? "已跳过" : "无需处理")));
                Console.WriteLine("   文档路径修复：" + (string.IsNullOrEmpty(docRepair) ? "未执行" : "已执行"));
                Console.WriteLine();

                if (!string.IsNullOrEmpty(docRepair))
                {
                    Console.WriteLine(docRepair);
                    Console.WriteLine();
                }
                Console.WriteLine("修复流程结束，请尝试重新安装！");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("修复错误1603时发生异常：" + ex.Message);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            if (!autoYes)
            {
                try { Console.ReadKey(true); } catch { }
            }
            return 0;
        }

        /// <summary>危险操作确认。默认不执行（直接回车即跳过），仅明确输入 Y 才继续。</summary>
        private static bool Confirm(string title, string body, bool autoYes)
        {
            Console.WriteLine();
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("  [!] " + title);
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(body);
            Console.WriteLine();

            if (autoYes)
            {
                Console.WriteLine("      (--yes 已指定，自动确认执行)");
                Console.WriteLine();
                return true;
            }

            Console.Write("      确认执行？输入 Y 继续，直接回车则跳过: ");
            string input;
            try { input = (Console.ReadLine() ?? "").Trim(); }
            catch { input = ""; }

            bool ok = input.Equals("Y", StringComparison.OrdinalIgnoreCase)
                   || input.Equals("YES", StringComparison.OrdinalIgnoreCase);

            Console.WriteLine(ok ? "      -> 已确认执行" : "      -> 已跳过");
            Console.WriteLine();
            return ok;
        }

        private static bool ServiceExists(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    ServiceControllerStatus unused = sc.Status; // 服务不存在时此处抛异常
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName.Replace(".exe", "")).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // ---------- 核心修复：文档路径 ----------

        private static string RepairMyDocumentsIfPointingToMissingDrive(bool autoYes)
        {
            try
            {
                string normalizedPath;
                string driveLetter;
                if (!TryGetMissingLocalDriveLetter(GetExpandedPersonalDocumentsPathFromRegistry(), out normalizedPath, out driveLetter))
                {
                    return null;
                }

                Log("「我的文档」指向的磁盘 " + driveLetter + " 不存在。路径: [" + normalizedPath + "]");
                Console.WriteLine();
                Console.WriteLine("[!] 检测到「我的文档」指向不存在的磁盘 " + driveLetter);
                Console.WriteLine("    当前路径：" + normalizedPath);
                Console.WriteLine();
                Console.WriteLine("    请先手动打开「我的文档」，确认能否正常打开：");
                Console.WriteLine("      - 打不开 -> 输入 Y 自动修复为默认目录");
                Console.WriteLine("      - 能打开 -> 输入 N 保持不变");
                Console.WriteLine();
                Console.WriteLine("    注意：确认修复后将写入注册表、可能修改文档库文件，");
                Console.WriteLine("          并重启资源管理器（桌面与任务栏会短暂消失后自动恢复）。");
                Console.WriteLine();

                bool ok;
                if (autoYes)
                {
                    ok = true;
                    Console.WriteLine("    (--yes 已指定，自动确认)");
                }
                else
                {
                    Console.Write("    是否修复文档位置？[Y/N]: ");
                    string input = (Console.ReadLine() ?? "").Trim();
                    ok = input.Equals("Y", StringComparison.OrdinalIgnoreCase)
                      || input.Equals("yes", StringComparison.OrdinalIgnoreCase);
                }

                if (!ok)
                {
                    Log("用户取消修复文档位置");
                    Console.WriteLine("    已跳过文档位置修复。");
                    Console.WriteLine();
                    return null;
                }

                return ApplyMyDocumentsRepairToDefault(normalizedPath, driveLetter);
            }
            catch (Exception ex)
            {
                Log("文档路径确认/修复异常: " + ex.Message);
                return null;
            }
        }

        private static string ApplyMyDocumentsRepairToDefault(string previousFullPath, string missingDriveLetter)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(profile))
            {
                profile = Environment.GetEnvironmentVariable("USERPROFILE");
            }
            if (string.IsNullOrWhiteSpace(profile))
            {
                Log("无法解析 USERPROFILE，跳过文档路径修复");
                return null;
            }

            string newDocs = Path.Combine(profile, "Documents");
            ApplyDefaultMyDocumentsShellFolders(newDocs);
            RepairDocumentsLibraryMsIfReferencesObsoletePath(previousFullPath, newDocs);
            RestartExplorerShellAfterDocumentsRepair();

            Console.WriteLine("    已恢复「我的文档」为：" + newDocs);
            Console.WriteLine();

            return "已将「我的文档」恢复为系统默认目录：" + newDocs
                 + "。说明：原先指向「" + previousFullPath + "」，但磁盘 " + missingDriveLetter
                 + " 在当前系统中不存在（常见于分区已删除、盘符已取消）。已尝试重启资源管理器使路径生效；若仍异常可注销或重启电脑。";
        }

        private static void RestartExplorerShellAfterDocumentsRepair()
        {
            try
            {
                ExecuteCommand("taskkill", "/f /im explorer.exe", true);
                System.Threading.Thread.Sleep(800);
                string windir = Environment.GetEnvironmentVariable("WINDIR");
                string exe = string.IsNullOrWhiteSpace(windir) ? "explorer.exe" : Path.Combine(windir, "explorer.exe");
                Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
                Log("已结束并重新启动资源管理器进程");
            }
            catch (Exception ex)
            {
                Log("重启资源管理器失败: " + ex.Message);
            }
        }

        private static void RepairDocumentsLibraryMsIfReferencesObsoletePath(string obsoleteFullPath, string defaultPhysicalDocuments)
        {
            if (string.IsNullOrWhiteSpace(obsoleteFullPath) || string.IsNullOrWhiteSpace(defaultPhysicalDocuments))
            {
                return;
            }
            try
            {
                string libPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Libraries\Documents.library-ms");

                if (!File.Exists(libPath))
                {
                    return;
                }

                string xml = File.ReadAllText(libPath, Encoding.UTF8);
                if (!LibraryXmlLikelyContainsPath(xml, obsoleteFullPath))
                {
                    return;
                }

                string updated = ReplaceObsoletePathLiteralsInLibraryXml(xml, obsoleteFullPath, defaultPhysicalDocuments);
                if (!string.Equals(updated, xml, StringComparison.Ordinal))
                {
                    string backup = libPath + ".autobox.bak";
                    try { File.Copy(libPath, backup, true); } catch { }
                    File.WriteAllText(libPath, updated, new UTF8Encoding(false));
                    Log("已更新 Documents.library-ms 中对失效路径的引用（备份：" + backup + "）");
                }
            }
            catch (Exception ex)
            {
                Log("更新 Documents.library-ms 失败: " + ex.Message);
            }
        }

        private static bool LibraryXmlLikelyContainsPath(string xml, string fullPath)
        {
            if (string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(fullPath))
            {
                return false;
            }
            foreach (string literal in EnumeratePathLiteralsForLibraryXml(fullPath))
            {
                if (xml.IndexOf(literal, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<string> EnumeratePathLiteralsForLibraryXml(string fullPath)
        {
            yield return fullPath;
            yield return fullPath.Replace('\\', '/');
            yield return "file:///" + fullPath.Replace('\\', '/').TrimStart('/');
        }

        private static string ReplaceObsoletePathLiteralsInLibraryXml(string xml, string fromFullPath, string toFullPath)
        {
            string fromFileUrl = "file:///" + fromFullPath.Replace('\\', '/').TrimStart('/');
            string toFileUrl = "file:///" + toFullPath.Replace('\\', '/').TrimStart('/');

            var pairs = new[]
            {
                Tuple.Create(fromFileUrl, toFileUrl),
                Tuple.Create(fromFullPath.Replace('\\', '/'), toFullPath.Replace('\\', '/')),
                Tuple.Create(fromFullPath, toFullPath)
            };

            string result = xml;
            foreach (var p in pairs.OrderByDescending(p => p.Item1.Length))
            {
                if (result.IndexOf(p.Item1, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string pattern = "(?i)" + Regex.Escape(p.Item1);
                    result = Regex.Replace(result, pattern, p.Item2.Replace("$", "$$"));
                }
            }
            return result;
        }

        private static bool TryGetMissingLocalDriveLetter(string expandedPath, out string normalizedPath, out string driveLetter)
        {
            normalizedPath = null;
            driveLetter = null;

            if (string.IsNullOrWhiteSpace(expandedPath))
            {
                return false;
            }
            try
            {
                normalizedPath = Path.GetFullPath(expandedPath.Trim());
            }
            catch
            {
                return false;
            }

            string root = Path.GetPathRoot(normalizedPath);
            if (string.IsNullOrEmpty(root) || root.Length < 3 || root[1] != ':')
            {
                return false;
            }

            driveLetter = char.ToUpperInvariant(normalizedPath[0]) + ":";

            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (string.Equals(d.Name.TrimEnd('\\'), driveLetter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetExpandedPersonalDocumentsPathFromRegistry()
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

        private static void ApplyDefaultMyDocumentsShellFolders(string defaultPhysicalDocuments)
        {
            if (string.IsNullOrWhiteSpace(defaultPhysicalDocuments))
            {
                return;
            }
            try { Directory.CreateDirectory(defaultPhysicalDocuments); } catch { }

            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                    {
                        using (RegistryKey userShell = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders", true))
                        {
                            if (userShell != null)
                            {
                                userShell.SetValue("Personal", @"%USERPROFILE%\Documents", RegistryValueKind.ExpandString);
                                userShell.SetValue("{F42EE2E4-CD92-456a-9728-B14CB08064DD}", @"%USERPROFILE%\Documents", RegistryValueKind.ExpandString);
                            }
                        }
                        using (RegistryKey shell = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders", true))
                        {
                            if (shell != null)
                            {
                                shell.SetValue("Personal", defaultPhysicalDocuments, RegistryValueKind.String);
                            }
                        }
                    }
                }
                catch { }
            }

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        // ---------- 权限 ----------

        private static bool CurrentUserHasDeleteLikeAccessToFile(string filePath)
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

        private static void EnsureFileFullControl(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
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

        // ---------- 通用辅助 ----------

        private static bool IsAdministrator()
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

        private static void StopService(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
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

        private static void ExecuteCommand(string command, string arguments, bool waitForExit)
        {
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (waitForExit && p != null)
                    {
                        p.WaitForExit();
                    }
                }
            }
            catch { }
        }

        private static void KillProcess(string processName)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(processName.Replace(".exe", "")))
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

        private static void DeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }
            try
            {
                new DirectoryInfo(directoryPath).Attributes = FileAttributes.Normal;
                Directory.Delete(directoryPath, true);
            }
            catch { }
        }

        private static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Fix1603", "Logs");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string path = Path.Combine(dir, "Fix1603_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                File.AppendAllText(path,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "] " + message + "\r\n",
                    Encoding.UTF8);
            }
            catch { }
        }
    }
}
