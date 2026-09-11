using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace AutoFix
{
    /// <summary>许可方式。</summary>
    internal enum LicenseMethod { Network, Standalone, User, Reset }

    /// <summary>许可服务器类型。</summary>
    internal enum LicenseServerType { Single, Redundant, Distributed }

    /// <summary>
    /// Autodesk 许可配置：封装 Autodesk 官方的 AdskLicensingInstHelper.exe。
    ///
    /// 说明：许可方式切换（网络 / 单机 / 用户 / 重置）是 Autodesk 官方文档记载的
    /// 标准管理操作，本工具只是为其提供一个图形界面，不涉及任何授权绕过。
    /// </summary>
    internal static partial class RepairService
    {
        private const string LicensingHelperPath =
            @"C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing\Current\helper\AdskLicensingInstHelper.exe";

        private static List<KeyValuePair<string, string>> _productKeys;

        /// <summary>许可辅助程序是否存在。</summary>
        internal static bool IsLicensingHelperAvailable()
        {
            try { return File.Exists(LicensingHelperPath); } catch { return false; }
        }

        internal static string LicensingHelperPathForDisplay
        {
            get { return LicensingHelperPath; }
        }

        /// <summary>读取产品名称 → 产品密钥表（内嵌资源，2020–2027）。</summary>
        internal static List<KeyValuePair<string, string>> GetProductKeys()
        {
            if (_productKeys != null)
            {
                return _productKeys;
            }

            var list = new List<KeyValuePair<string, string>>();
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                string name = null;
                foreach (string n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith("AutodeskProducts.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        name = n;
                        break;
                    }
                }
                if (name != null)
                {
                    using (Stream s = asm.GetManifestResourceStream(name))
                    using (var r = new StreamReader(s, Encoding.UTF8))
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            string t = line.Trim();
                            if (t.Length == 0)
                            {
                                continue;
                            }
                            int i = t.LastIndexOf(';');
                            if (i <= 0 || i >= t.Length - 1)
                            {
                                continue;
                            }
                            list.Add(new KeyValuePair<string, string>(t.Substring(0, i).Trim(), t.Substring(i + 1).Trim()));
                        }
                    }
                }
            }
            catch { }

            list.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            _productKeys = list;
            return list;
        }

        /// <summary>该版本可用的年份列表。</summary>
        internal static readonly int[] LicenseYears = { 2027, 2026, 2025, 2024, 2023, 2022, 2021, 2020 };

        /// <summary>年份 → 产品特性代码（--prod_ver 参数）。</summary>
        internal static string FeatureCodeFor(int year)
        {
            if (year < 2020 || year > 2027)
            {
                return "";
            }
            return year + ".0.0.F";
        }

        /// <summary>筛选指定年份的产品。</summary>
        internal static List<KeyValuePair<string, string>> ProductsForYear(int year)
        {
            var all = GetProductKeys();
            var list = new List<KeyValuePair<string, string>>();
            string y = year.ToString();
            foreach (var kv in all)
            {
                if (kv.Key.IndexOf(y, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add(kv);
                }
            }
            return list;
        }

        /// <summary>构造 AdskLicensingInstHelper 命令行。</summary>
        internal static string BuildLicenseCommand(string productKey, int year,
            LicenseMethod method, LicenseServerType serverType, string servers)
        {
            string ver = FeatureCodeFor(year);
            var sb = new StringBuilder();
            sb.Append('"').Append(LicensingHelperPath).Append('"');
            sb.Append(" change");
            sb.Append(" --prod_key ").Append(productKey);
            sb.Append(" --prod_ver ").Append(ver);

            switch (method)
            {
                case LicenseMethod.Network:
                    sb.Append(" --lic_method NETWORK");
                    sb.Append(" --lic_server_type ").Append(serverType.ToString().ToUpperInvariant());
                    sb.Append(" --lic_servers ").Append(servers ?? "");
                    break;
                case LicenseMethod.Standalone:
                    sb.Append(" --lic_method STANDALONE");
                    break;
                case LicenseMethod.User:
                    sb.Append(" --lic_method USER");
                    break;
                case LicenseMethod.Reset:
                    sb.Append(" --lic_method \"\" --lic_server_type \"\" --lic_servers \"\"");
                    break;
            }
            return sb.ToString();
        }

        internal static string MethodDisplayName(LicenseMethod m)
        {
            switch (m)
            {
                case LicenseMethod.Network: return "网络许可";
                case LicenseMethod.Standalone: return "单机许可";
                case LicenseMethod.User: return "用户许可";
                default: return "重置许可";
            }
        }

        /// <summary>执行许可切换。</summary>
        internal static string ApplyLicense(string productKey, int year, LicenseMethod method,
            LicenseServerType serverType, string servers, Action<string> log)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(productKey))
                {
                    return "未选择产品。";
                }
                if (!IsLicensingHelperAvailable())
                {
                    return "未找到 AdskLicensingInstHelper.exe。\r\n\r\n"
                         + "路径：" + LicensingHelperPath + "\r\n\r\n"
                         + "请确认已安装 Autodesk Licensing 组件。";
                }
                if (method == LicenseMethod.Network && string.IsNullOrWhiteSpace(servers))
                {
                    return "选择网络许可时必须填写许可服务器地址。";
                }

                string cmd = BuildLicenseCommand(productKey, year, method, serverType, servers);
                Log(log, "命令：" + cmd);

                if (DryRun)
                {
                    DryNote("执行许可切换命令：" + cmd, log);
                    return "[预演] 将把产品 " + productKey + "（" + year + "）设为"
                         + MethodDisplayName(method) + "。\r\n\r\n命令：\r\n" + cmd;
                }

                // 1. 结束 AdSSO，避免其占用许可状态
                int killed = 0;
                try
                {
                    foreach (Process p in Process.GetProcessesByName("AdSSO"))
                    {
                        try { p.Kill(); killed++; } catch { }
                    }
                }
                catch { }
                if (killed > 0)
                {
                    Log(log, "已结束 AdSSO 进程 " + killed + " 个");
                }

                // 2. 网络许可前清除本机 FLEXlm 缓存
                if (method == LicenseMethod.Network)
                {
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software", true))
                        {
                            if (key != null)
                            {
                                key.DeleteSubKeyTree("FLEXlm License Manager", false);
                                Log(log, "已清除 HKCU\\Software\\FLEXlm License Manager");
                            }
                        }
                    }
                    catch { }
                }

                // 3. 执行命令
                string stdOut = "";
                string stdErr = "";
                int exitCode = -1;

                using (Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + cmd + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (p == null)
                    {
                        return "无法启动许可辅助程序。";
                    }
                    var so = new Thread(() => { try { stdOut = p.StandardOutput.ReadToEnd(); } catch { } });
                    so.IsBackground = true;
                    so.Start();
                    var se = new Thread(() => { try { stdErr = p.StandardError.ReadToEnd(); } catch { } });
                    se.IsBackground = true;
                    se.Start();

                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        return "许可切换超时（超过 60 秒）。";
                    }
                    exitCode = p.ExitCode;
                    if (so.IsAlive) { so.Join(2000); }
                    if (se.IsAlive) { se.Join(2000); }
                }

                Log(log, "退出代码：" + exitCode);
                if (!string.IsNullOrWhiteSpace(stdOut)) { Log(log, "输出：" + stdOut.Trim()); }
                if (!string.IsNullOrWhiteSpace(stdErr)) { Log(log, "错误：" + stdErr.Trim()); }

                string detail = "";
                if (!string.IsNullOrWhiteSpace(stdOut)) { detail += "\r\n\r\n" + stdOut.Trim(); }
                if (!string.IsNullOrWhiteSpace(stdErr)) { detail += "\r\n\r\n" + stdErr.Trim(); }

                // 4. 重置许可时，额外清除登录状态与身份服务数据库
                if (method == LicenseMethod.Reset)
                {
                    ResetIdentityState(log);
                }

                if (exitCode != 0)
                {
                    return "许可切换失败（退出代码 " + exitCode + "）。\r\n\r\n命令：\r\n" + cmd + detail;
                }

                return "许可已切换为「" + MethodDisplayName(method) + "」。\r\n\r\n"
                     + "产品：" + productKey + "（" + year + "）" + detail;
            }
            catch (Exception ex)
            {
                return "许可切换时发生异常：" + ex.Message;
            }
        }

        /// <summary>重置许可时清除本地登录状态与身份服务数据库。</summary>
        private static void ResetIdentityState(Action<string> log)
        {
            // 登录状态文件
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    string loginState = Path.Combine(local, "Autodesk", "Web Services", "LoginState.xml");
                    if (File.Exists(loginState))
                    {
                        FsDeleteFile(loginState, log);
                    }
                }
            }
            catch { }

            // 身份服务数据库（需先结束 AdskIdentityManager）
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local))
                {
                    return;
                }
                string db = Path.Combine(local, "Autodesk", "Identity Services", "idservices.db");
                if (!File.Exists(db))
                {
                    return;
                }

                if (DryRun)
                {
                    DryNote("结束 AdskIdentityManager 并重命名身份数据库 " + db, log);
                    return;
                }

                foreach (Process p in Process.GetProcessesByName("AdskIdentityManager"))
                {
                    try { p.Kill(); p.WaitForExit(5000); } catch { }
                }

                string renamed = db + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Move(db, renamed);
                    Log(log, "已重命名身份数据库：" + Path.GetFileName(renamed));
                }
                catch (Exception ex)
                {
                    Log(log, "重命名身份数据库失败：" + ex.Message);
                }
            }
            catch { }
        }
    }
}

