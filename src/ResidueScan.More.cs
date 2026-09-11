using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutodeskFix
{
    // 残留扫描的补充维度（对齐参考项目 15 项扫描）。
    internal static partial class RepairService
    {
        /// <summary>[5/15] 用户数据目录。</summary>
        private static void ScanDataFolders(Action<string, string, string> add, Action<string> log)
        {
            var dirs = new List<string>();
            try
            {
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk"));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autodesk"));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Autodesk"));
                dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments), "Autodesk"));
            }
            catch { }

            foreach (string d in dirs)
            {
                try
                {
                    if (!Directory.Exists(d))
                    {
                        continue;
                    }
                    long size = 0;
                    int files = 0;
                    try
                    {
                        foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        {
                            try { size += new FileInfo(f).Length; files++; } catch { }
                        }
                    }
                    catch { }
                    add("数据目录", d, files + " 个文件，" + FormatSize(size));
                }
                catch { }
            }

            // Desktop Connector 工作区（信息性）
            foreach (string d in DesktopConnectorFolders())
            {
                try
                {
                    if (Directory.Exists(d))
                    {
                        add("数据目录", d, "Desktop Connector 工作区（含项目文件，需单独处理）");
                    }
                }
                catch { }
            }
        }

        /// <summary>[6/15] 全盘搜索：列出 C 盘任意位置名称含 Autodesk 的目录。</summary>
        private static void ScanWholeSystem(Action<string, string, string> add, Action<string> log)
        {
            // 排除：回收站、下载目录中的安装包、本程序的日志目录
            var excludes = new List<string>
            {
                "\\$recycle.bin\\",
                "\\downloads\\",
                "autodesk_uninstaller"
            };

            int count = 0;
            try
            {
                foreach (string dir in SafeEnumerateDirectories(@"C:\\"))
                {
                    string name;
                    string lower;
                    try
                    {
                        name = Path.GetFileName(dir);
                        lower = dir.ToLowerInvariant();
                    }
                    catch { continue; }

                    if (!AutodeskKeywordInName(name))
                    {
                        continue;
                    }

                    bool skip = false;
                    foreach (string ex in excludes)
                    {
                        if (lower.Contains(ex))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip)
                    {
                        continue;
                    }

                    string detail = (lower.Contains("$recycle.bin"))
                        ? "位于回收站（已删除，待清空）"
                        : "全盘搜索发现";
                    if (lower.Contains("$recycle.bin"))
                    {
                        detail = "位于回收站（已删除，待清空）";
                    }
                    add("全盘搜索", dir, detail);
                    count++;
                    if (count >= 500)
                    {
                        add("全盘搜索", @"C:\\", "… 命中超过 500 项，已截断");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, "  全盘搜索中断：" + ex.Message);
            }
        }

        /// <summary>安全枚举目录树（跳过无权限目录，避免因单个目录异常中断整轮扫描）。</summary>
        private static IEnumerable<string> SafeEnumerateDirectories(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            int guard = 0;

            while (stack.Count > 0 && guard < 200000)
            {
                guard++;
                string cur = stack.Pop();
                string[] subs = null;
                try { subs = Directory.GetDirectories(cur); } catch { }
                if (subs == null)
                {
                    continue;
                }
                foreach (string s in subs)
                {
                    yield return s;
                    stack.Push(s);
                }
            }
        }

        /// <summary>[8/15] COM 注册：Classes\CLSID 与 TypeLib 中含 Autodesk 的项。</summary>
        private static void ScanComRegistry(Action<string, string, string> add, Action<string> log)
        {
            string[] roots =
            {
                @"SOFTWARE\Classes\CLSID",
                @"SOFTWARE\Classes\TypeLib",
                @"SOFTWARE\WOW6432Node\Classes\CLSID",
                @"SOFTWARE\WOW6432Node\Classes\TypeLib"
            };

            foreach (string root in roots)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey k = baseKey.OpenSubKey(root, false))
                    {
                        if (k == null)
                        {
                            continue;
                        }
                        int n = ScanKeyTreeForAutodesk(k, "HKLM\\" + root, 3, add, "COM 注册");
                        if (n > 0)
                        {
                            Log(log, "  " + root + "：" + n + " 项");
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>在注册表树中按默认值/名称查找含 Autodesk 的项（限深度，限量）。</summary>
        private static int ScanKeyTreeForAutodesk(RegistryKey root, string path, int maxDepth,
            Action<string, string, string> add, string type)
        {
            int count = 0;
            var stack = new Stack<Tuple<RegistryKey, string, int>>();
            stack.Push(Tuple.Create(root, path, 0));
            int guard = 0;

            while (stack.Count > 0 && guard < 20000)
            {
                guard++;
                var item = stack.Pop();
                RegistryKey k = item.Item1;
                string p = item.Item2;
                int depth = item.Item3;

                if (depth > 0)
                {
                    bool hit = false;
                    try
                    {
                        object def = k.GetValue(null);
                        if (def != null && def.ToString().IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hit = true;
                        }
                        if (!hit && p.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hit = true;
                        }
                    }
                    catch { }

                    if (hit)
                    {
                        add(type, p, "注册表项");
                        count++;
                        if (count >= 300)
                        {
                            break;
                        }
                    }
                }

                if (depth >= maxDepth)
                {
                    try { k.Close(); } catch { }
                    continue;
                }

                string[] subs = null;
                try { subs = k.GetSubKeyNames(); } catch { }
                if (subs != null)
                {
                    foreach (string s in subs)
                    {
                        try
                        {
                            RegistryKey child = k.OpenSubKey(s, false);
                            if (child != null)
                            {
                                stack.Push(Tuple.Create(child, p + "\\" + s, depth + 1));
                            }
                        }
                        catch { }
                    }
                }
                try { k.Close(); } catch { }
            }
            return count;
        }

        /// <summary>[9/15] HKCU\SOFTWARE\Classes 下的 Autodesk 类注册。</summary>
        private static void ScanUserClasses(Action<string, string, string> add, Action<string> log)
        {
            string[] names =
            {
                "DWGTrueView", "AutoCAD", "AutodeskDGN", "AutoLISPFile", "3dsFile",
                "dwgviewr", "cdc_auto_file", "CompleteR16PlotConfigurationFile",
                "acadlt", "adsk.idmgr", "adskidmgr"
            };

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Classes", false))
                {
                    if (root == null)
                    {
                        return;
                    }
                    foreach (string n in names)
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(n, false))
                            {
                                if (k != null)
                                {
                                    add("HKCU 类注册", "HKCU\\SOFTWARE\\Classes\\" + n, "命名类键");
                                }
                            }
                        }
                        catch { }
                    }
                    // .dgn 扩展名
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(".dgn", false))
                        {
                            if (k != null)
                            {
                                add("HKCU 类注册", "HKCU\\SOFTWARE\\Classes\\.dgn", "DGN 扩展名关联");
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>[10/15] 外壳扩展（Shell Extensions\Approved）。</summary>
        private static void ScanShellExtensions(Action<string, string, string> add, Action<string> log)
        {
            const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved";
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null)
                    {
                        return;
                    }
                    foreach (string guid in k.GetValueNames())
                    {
                        try
                        {
                            object v = k.GetValue(guid);
                            if (v != null && v.ToString().IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                add("外壳扩展", guid, v.ToString());
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>[11/15] Installer\Products 幽灵项。</summary>
        private static void ScanInstallerGhosts(Action<string, string, string> add, Action<string> log)
        {
            const string path = @"SOFTWARE\Classes\Installer\Products";
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = baseKey.OpenSubKey(path, false))
                {
                    if (root == null)
                    {
                        return;
                    }
                    int n = 0;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey p = root.OpenSubKey(sub, false))
                            {
                                if (p == null)
                                {
                                    continue;
                                }
                                object pn = p.GetValue("ProductName");
                                if (pn != null && pn.ToString().IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    add("Installer 幽灵项", "HKLM\\" + path + "\\" + sub, pn.ToString());
                                    n++;
                                    if (n >= 300)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>[12/15] 计划任务与环境变量。</summary>
        private static void ScanTasksAndEnv(Action<string, string, string> add, Action<string> log)
        {
            try
            {
                foreach (string t in AutodeskScheduledTasks())
                {
                    add("计划任务", FirstCsvField(t), "计划任务");
                }
            }
            catch { }

            foreach (string v in AutodeskEnvVarNames())
            {
                try
                {
                    string m = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.Machine);
                    if (!string.IsNullOrEmpty(m))
                    {
                        add("环境变量", "[系统] " + v, m);
                    }
                    string u = Environment.GetEnvironmentVariable(v, EnvironmentVariableTarget.User);
                    if (!string.IsNullOrEmpty(u))
                    {
                        add("环境变量", "[用户] " + v, u);
                    }
                }
                catch { }
            }

            // 系统 PATH 中的 Autodesk 条目
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey env = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", false))
                {
                    if (env != null)
                    {
                        object raw = env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        if (raw != null)
                        {
                            foreach (string part in raw.ToString().Split(';'))
                            {
                                if (part.Length > 0
                                    && (part.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                                     || part.IndexOf("AdODIS", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    add("环境变量", "系统 PATH", part);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>[13/15] IFEO 调试器劫持。</summary>
        private static void ScanIfeo(Action<string, string, string> add, Action<string> log)
        {
            const string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey root = baseKey.OpenSubKey(path, false))
                {
                    if (root == null)
                    {
                        return;
                    }
                    foreach (string exe in UninstallIfeoExes)
                    {
                        try
                        {
                            using (RegistryKey sub = root.OpenSubKey(exe, false))
                            {
                                if (sub == null)
                                {
                                    continue;
                                }
                                object dbg = sub.GetValue("Debugger");
                                if (dbg != null)
                                {
                                    add("IFEO 劫持", exe, "Debugger = " + dbg);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>[14/15] PendingFileRenameOperations 中的 Autodesk 条目。</summary>
        private static void ScanPendingRename(Action<string, string, string> add, Action<string> log)
        {
            const string path = @"SYSTEM\CurrentControlSet\Control\Session Manager";
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null)
                    {
                        return;
                    }
                    string[] vals = null;
                    try { vals = k.GetValue("PendingFileRenameOperations") as string[]; } catch { }
                    if (vals == null)
                    {
                        return;
                    }
                    // REG_MULTI_SZ 成对出现：源路径、目标路径
                    for (int i = 0; i + 1 < vals.Length; i += 2)
                    {
                        string src = vals[i] ?? "";
                        string dst = vals[i + 1] ?? "";
                        if (src.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                         || src.IndexOf("AdODIS", StringComparison.OrdinalIgnoreCase) >= 0
                         || dst.IndexOf("Autodesk", StringComparison.OrdinalIgnoreCase) >= 0
                         || dst.IndexOf("AdODIS", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            add("待处理重命名", src, string.IsNullOrEmpty(dst) ? "（待删除）" : "→ " + dst);
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>[15/15] hosts 中的 Autodesk 条目。</summary>
        private static void ScanHosts(Action<string, string, string> add, Action<string> log)
        {
            try
            {
                foreach (string line in AutodeskHostsEntries())
                {
                    add("hosts 条目", line, "会屏蔽许可服务器");
                }
            }
            catch { }
        }
    }
}

