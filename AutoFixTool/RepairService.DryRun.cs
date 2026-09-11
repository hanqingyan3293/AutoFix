using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace AutoFix
{
    /// <summary>
    /// 预演模式（dry-run）：开启后所有破坏性操作只记录不执行，
    /// 便于在执行前确认实际影响范围。
    /// </summary>
    internal static partial class RepairService
    {
        /// <summary>是否为预演模式。true 时所有破坏性操作仅记录、不执行。</summary>
        internal static bool DryRun;

        /// <summary>预演日志输出目标（由界面在执行前设置）。</summary>
        internal static Action<string> DryRunLog;

        private static int _dryRunCount;

        /// <summary>进入预演记录。</summary>
        internal static void ResetDryRunCounter()
        {
            _dryRunCount = 0;
        }

        internal static int DryRunActionCount
        {
            get { return _dryRunCount; }
        }

        /// <summary>记录一条预演动作。返回 true 表示可以继续执行真实操作。</summary>
        private static bool ShouldDo(string what)
        {
            if (!DryRun)
            {
                return true;
            }
            _dryRunCount++;
            Action<string> sink = DryRunLog;
            if (sink != null)
            {
                try { sink("[预演] 将执行：" + what); } catch { }
            }
            return false;
        }

        private static void DryNote(string what, Action<string> log)
        {
            _dryRunCount++;
            if (log != null)
            {
                try { log("[预演] 将执行：" + what); } catch { }
            }
            if (DryRunLog != null && !ReferenceEquals(DryRunLog, log))
            {
                try { DryRunLog("[预演] 将执行：" + what); } catch { }
            }
        }

        /// <summary>统计注册表子键总数（用于预演时报告影响范围）。</summary>
        private static int CountSubKeysRecursive(RegistryKey key)
        {
            if (key == null)
            {
                return 0;
            }
            int n = 0;
            try
            {
                foreach (string sub in key.GetSubKeyNames())
                {
                    n++;
                    try
                    {
                        using (RegistryKey child = key.OpenSubKey(sub, false))
                        {
                            n += CountSubKeysRecursive(child);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        // ==================== 注册表写入（受预演控制） ====================

        private static void RegWriteValue(RegistryKey key, string name, object value,
            RegistryValueKind kind, Action<string> log)
        {
            if (key == null)
            {
                return;
            }
            string what = "写入注册表值 " + name + " = " + value;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            key.SetValue(name, value, kind);
        }

        private static void RegDeleteValue(RegistryKey key, string name, Action<string> log)
        {
            if (key == null)
            {
                return;
            }
            string what = "删除注册表值 " + name;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            key.DeleteValue(name, false);
        }

        private static void RegDeleteTree(RegistryKey baseKey, string subKey, Action<string> log)
        {
            if (baseKey == null)
            {
                return;
            }
            string what = "删除注册表键 " + subKey;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            baseKey.DeleteSubKeyTree(subKey, false);
        }

        // ==================== 文件系统写入（受预演控制） ====================

        private static void FsCreateDirectory(string path, Action<string> log)
        {
            string what = "创建目录 " + path;
            if (DryRun)
            {
                DryNote(what, log);
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

        private static void FsDeleteDirectory(string path, Action<string> log)
        {
            string what = "删除目录 " + path;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            try
            {
                if (Directory.Exists(path))
                {
                    new DirectoryInfo(path).Attributes = FileAttributes.Normal;
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    try { log("  删除失败：" + path + " -> " + ex.Message); } catch { }
                }
            }
        }

        private static void FsDeleteFile(string path, Action<string> log)
        {
            string what = "删除文件 " + path;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch { }
        }

        private static void FsWriteText(string path, string content, Action<string> log)
        {
            string what = "写入文件 " + path;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        }

        private static void FsWriteLines(string path, string[] lines, Action<string> log)
        {
            string what = "写入文件 " + path + "（" + lines.Length + " 行）";
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false));
        }

        private static void FsCopy(string source, string target, Action<string> log)
        {
            string what = "复制文件 " + source + " -> " + target;
            if (DryRun)
            {
                DryNote(what, log);
                return;
            }
            try { File.Copy(source, target, true); } catch { }
        }
    }
}
