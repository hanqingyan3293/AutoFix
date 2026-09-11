using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutoFix
{
    /// <summary>一条校验项。</summary>
    internal sealed class VerifyItem
    {
        public int Index;
        public string Name = "";
        public string Status = "";        // 正常 / 异常 / 提示
        public string Detail = "";
        public bool IsIssue;
    }

    // 清理后校验：10 项检查，判断系统是否已具备干净重装条件。
    // 检查项参考 autodesk-complete-uninstaller（MIT），实现为原生 C#。
    internal static partial class RepairService
    {
        private const string OdisLockFile =
            @"C:\ProgramData\Autodesk\ODIS\AdODISInstaller.run.lock";
        private const string OdisInstallerExe =
            @"C:\Program Files\Autodesk\AdODIS\V1\Installer.exe";
        private const string AccessServiceHostExe =
            @"C:\Program Files\Autodesk\AdODIS\V1\Setup\AdskAccessServiceHost.exe";
        private const string OdisDataFolder =
            @"C:\ProgramData\Autodesk\ODIS";

        /// <summary>执行 10 项校验（只读）。</summary>
        internal static List<VerifyItem> RunVerification(Action<string> log)
        {
            var list = new List<VerifyItem>();
            int idx = 0;

            void Add(string name, string status, string detail, bool issue)
            {
                list.Add(new VerifyItem
                {
                    Index = ++idx,
                    Name = name,
                    Status = status,
                    Detail = detail,
                    IsIssue = issue
                });
            }

            bool hasProducts = false;
            try { hasProducts = ScanInstalledProducts().Count > 0; } catch { }

            // 1. ODIS 锁文件
            Log(log, "[1/10] 检查 ODIS 锁文件 ...");
            try
            {
                if (File.Exists(OdisLockFile))
                {
                    Add("ODIS 锁文件", "异常",
                        "存在 " + OdisLockFile + "，安装程序可能处于卡死状态，建议删除后重试", true);
                }
                else
                {
                    Add("ODIS 锁文件", "正常", "未发现锁文件", false);
                }
            }
            catch (Exception ex) { Add("ODIS 锁文件", "提示", "无法检查：" + ex.Message, false); }

            // 2. IFEO 调试器劫持
            Log(log, "[2/10] 检查 IFEO 调试器劫持 ...");
            try
            {
                var hit = new List<string>();
                using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = key.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", false))
                {
                    if (root != null)
                    {
                        foreach (string exe in UninstallIfeoExes)
                        {
                            try
                            {
                                using (RegistryKey sub = root.OpenSubKey(exe, false))
                                {
                                    if (sub != null && sub.GetValue("Debugger") != null)
                                    {
                                        hit.Add(exe);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                if (hit.Count > 0)
                {
                    Add("IFEO 调试器劫持", "异常",
                        hit.Count + " 个程序被劫持：" + string.Join("、", hit.ToArray())
                        + "（可用「安装闪退修复」清除）", true);
                }
                else
                {
                    Add("IFEO 调试器劫持", "正常", "未发现调试器重定向", false);
                }
            }
            catch (Exception ex) { Add("IFEO 调试器劫持", "提示", "无法检查：" + ex.Message, false); }

            // 3. Autodesk Access / ODIS 版本
            Log(log, "[3/10] 读取组件版本 ...");
            try
            {
                string accessVer = FileVersion(AccessServiceHostExe);
                string odisVer = FileVersion(OdisInstallerExe);
                string detail = "Autodesk Access：" + (accessVer ?? "未安装")
                              + "；ODIS Installer：" + (odisVer ?? "未安装");
                bool missing = accessVer == null;
                Add("组件版本", missing ? "提示" : "正常", detail, false);
            }
            catch (Exception ex) { Add("组件版本", "提示", "无法读取：" + ex.Message, false); }

            // 4. AdskAccessServiceHost 服务
            Log(log, "[4/10] 检查 AdskAccessServiceHost 服务 ...");
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController("AdskAccessServiceHost"))
                {
                    var st = sc.Status;
                    if (st == System.ServiceProcess.ServiceControllerStatus.Running)
                    {
                        Add("AdskAccessServiceHost", "正常", "正在运行", false);
                    }
                    else
                    {
                        Add("AdskAccessServiceHost", "异常",
                            "服务已安装但状态为 " + st + "，可能影响 Autodesk Access", true);
                    }
                }
            }
            catch
            {
                Add("AdskAccessServiceHost", "提示", "未安装该服务", false);
            }

            // 5. ODIS 基础设施
            Log(log, "[5/10] 检查 ODIS 基础设施 ...");
            try
            {
                bool exeOk = File.Exists(OdisInstallerExe);
                bool dirOk = Directory.Exists(OdisDataFolder);
                if (exeOk && dirOk)
                {
                    Add("ODIS 基础设施", "正常", "Installer.exe 与数据目录均存在", false);
                }
                else if (!hasProducts)
                {
                    Add("ODIS 基础设施", "提示",
                        "组件缺失，但本机未安装 Autodesk 产品，属正常（干净系统）", false);
                }
                else
                {
                    string what = (!exeOk ? "Installer.exe 缺失；" : "") + (!dirOk ? "数据目录缺失" : "");
                    Add("ODIS 基础设施", "异常",
                        what.TrimEnd('；') + " —— 本机仍有 Autodesk 产品，基础设施不完整", true);
                }
            }
            catch (Exception ex) { Add("ODIS 基础设施", "提示", "无法检查：" + ex.Message, false); }

            // 6. ProductInformation.pit
            Log(log, "[6/10] 检查 ProductInformation.pit ...");
            try
            {
                var found = new List<string>();
                foreach (string pit in PitPaths())
                {
                    if (File.Exists(pit))
                    {
                        long size = 0;
                        try { size = new FileInfo(pit).Length; } catch { }
                        found.Add(pit + "（" + size + " 字节）");
                    }
                }
                if (found.Count > 0)
                {
                    Add("ProductInformation.pit", "异常",
                        "存在 " + found.Count + " 个，可能已损坏，建议删除后由安装程序重建：\r\n  · "
                        + string.Join("\r\n  · ", found.ToArray()), true);
                }
                else
                {
                    Add("ProductInformation.pit", "正常", "新老路径均不存在（安装时会自动重建）", false);
                }
            }
            catch (Exception ex) { Add("ProductInformation.pit", "提示", "无法检查：" + ex.Message, false); }

            // 7. TMP / TEMP 路径
            Log(log, "[7/10] 检查 TEMP 路径 ...");
            try
            {
                string tmp = Environment.GetEnvironmentVariable("TEMP")
                          ?? Environment.GetEnvironmentVariable("TMP") ?? "";
                string norm = tmp.Replace('/', '\\').ToLowerInvariant();
                if (norm.Contains(@"\appdata\local\temp"))
                {
                    Add("TEMP 路径", "正常", tmp, false);
                }
                else
                {
                    Add("TEMP 路径", "异常",
                        "非默认位置：" + tmp + "\r\n非默认 TEMP 常导致安装失败，建议改回 %LOCALAPPDATA%\\Temp", true);
                }
            }
            catch (Exception ex) { Add("TEMP 路径", "提示", "无法读取：" + ex.Message, false); }

            // 8. VC++ 2015-2022 x64 运行库
            Log(log, "[8/10] 检查 VC++ 运行库 ...");
            try
            {
                bool found = false;
                foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                        using (RegistryKey r = key.OpenSubKey(
                            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", false))
                        {
                            if (r != null && r.GetValue("Major") != null)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    catch { }
                }
                if (found)
                {
                    Add("VC++ 运行库", "正常", "VC++ 2015-2022 x64 已安装", false);
                }
                else
                {
                    Add("VC++ 运行库", "异常",
                        "未检测到 VC++ 2015-2022 x64，Autodesk 安装可能失败", true);
                }
            }
            catch (Exception ex) { Add("VC++ 运行库", "提示", "无法检查：" + ex.Message, false); }

            // 9. 事件查看器（近 7 天 Autodesk 错误）
            Log(log, "[9/10] 检查事件日志 ...");
            try
            {
                var hits = new List<string>();
                DateTime since = DateTime.Now.AddDays(-7);
                using (var ev = new EventLog("Application"))
                {
                    int count = ev.Entries.Count;
                    int scanned = 0;
                    for (int i = count - 1; i >= 0 && scanned < 3000; i--, scanned++)
                    {
                        EventLogEntry e;
                        try { e = ev.Entries[i]; } catch { continue; }
                        if (e.TimeGenerated < since)
                        {
                            break;
                        }
                        if (e.EntryType != EventLogEntryType.Error)
                        {
                            continue;
                        }
                        string msg;
                        try { msg = e.Message ?? ""; } catch { continue; }
                        if (Regex.IsMatch(msg, "Autodesk|ODIS|AdskAccess|AdODIS",
                                RegexOptions.IgnoreCase))
                        {
                            string first = msg.Split('\n')[0];
                            if (first.Length > 110)
                            {
                                first = first.Substring(0, 110) + "…";
                            }
                            hits.Add(e.TimeGenerated.ToString("MM-dd HH:mm") + " [" + e.Source + "] " + first);
                            if (hits.Count >= 10)
                            {
                                break;
                            }
                        }
                    }
                }
                if (hits.Count > 0)
                {
                    Add("事件日志", "异常",
                        "近 7 天有 " + hits.Count + " 条 Autodesk 相关错误：\r\n  · "
                        + string.Join("\r\n  · ", hits.ToArray()), true);
                }
                else
                {
                    Add("事件日志", "正常", "近 7 天无 Autodesk 相关错误", false);
                }
            }
            catch (Exception ex)
            {
                Add("事件日志", "提示", "无法读取（可能需要管理员权限）：" + ex.Message, false);
            }

            // 10. hosts 中的 Autodesk 条目
            Log(log, "[10/10] 检查 hosts ...");
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string hosts = Path.Combine(win, @"System32\drivers\etc\hosts");
                var entries = new List<string>();
                if (File.Exists(hosts))
                {
                    foreach (string raw in File.ReadAllLines(hosts))
                    {
                        string t = raw.Trim();
                        if (t.Length == 0 || t.StartsWith("#"))
                        {
                            continue;
                        }
                        if (t.IndexOf("autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            entries.Add(t);
                        }
                    }
                }
                if (entries.Count > 0)
                {
                    Add("hosts 条目", "异常",
                        entries.Count + " 条 Autodesk 条目会屏蔽许可服务器：\r\n  · "
                        + string.Join("\r\n  · ", entries.ToArray())
                        + "\r\n（可用「hosts修复」清除）", true);
                }
                else
                {
                    Add("hosts 条目", "正常", "未见 Autodesk 相关条目", false);
                }
            }
            catch (Exception ex) { Add("hosts 条目", "提示", "无法读取：" + ex.Message, false); }

            int issues = 0;
            foreach (VerifyItem v in list)
            {
                if (v.IsIssue)
                {
                    issues++;
                }
            }
            Log(log, "校验完成：" + list.Count + " 项，" + issues + " 项异常");
            return list;
        }

        private static string FileVersion(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                var vi = FileVersionInfo.GetVersionInfo(path);
                string v = vi.ProductVersion;
                if (string.IsNullOrWhiteSpace(v))
                {
                    v = vi.FileVersion;
                }
                return string.IsNullOrWhiteSpace(v) ? "已安装（版本未知）" : v;
            }
            catch { return null; }
        }
    }
}
