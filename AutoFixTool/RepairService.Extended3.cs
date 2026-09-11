using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace AutoFix
{
    // 第三批扩展：CAD 文件瘦身、系统还原点、批量版本查询
    internal static partial class RepairService
    {
        // ==================== CAD 文件瘦身 ====================

        /// <summary>
        /// 通过 AutoCAD 脚本（OPEN → PURGE → QSAVE → QUIT）清理图纸冗余数据。
        /// 原文件不动，结果另存为「原名_瘦身.dwg」。
        /// </summary>
        public static string ShrinkCadFile(string filePath, Action<string> log)
        {
            if (DryRun)
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return "文件不存在。";
                }
                string dir0 = Path.GetDirectoryName(filePath);
                string nm0 = Path.GetFileNameWithoutExtension(filePath);
                string out0 = Path.Combine(dir0, nm0 + "_瘦身" + Path.GetExtension(filePath));
                DryNote("启动 AutoCAD 执行清理脚本（OPEN → PURGE×2 → QSAVE → QUIT），结果另存为 " + out0, log);
                return "[预演] 将对「" + Path.GetFileName(filePath) + "」执行 CAD 瘦身，结果另存为「"
                     + Path.GetFileName(out0) + "」。原文件不会被修改。";
            }
            string scriptPath = null;
            string outputPath = null;

            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return "文件不存在。";
                }

                string ext = Path.GetExtension(filePath).ToUpperInvariant();
                if (ext != ".DWG" && ext != ".DXF")
                {
                    return "仅支持 .dwg 与 .dxf 文件。";
                }

                if (Process.GetProcessesByName("acad").Length > 0)
                {
                    return "检测到 AutoCAD 正在运行。\r\n\r\n请先关闭 AutoCAD 再使用瘦身功能，避免文件占用或界面卡顿。";
                }

                string acad = FindAutoCadExe();
                if (string.IsNullOrEmpty(acad))
                {
                    return "未找到 AutoCAD 安装路径。请确认已安装 AutoCAD，且注册表中有 AcadLocation 记录。";
                }
                Log(log, "AutoCAD：" + acad);

                long originalSize = new FileInfo(filePath).Length;

                string dir = Path.GetDirectoryName(filePath);
                string name = Path.GetFileNameWithoutExtension(filePath);
                outputPath = Path.Combine(dir, name + "_瘦身" + Path.GetExtension(filePath));

                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }

                Log(log, "复制副本：" + outputPath);
                File.Copy(filePath, outputPath, true);

                // 生成 AutoCAD 脚本
                scriptPath = Path.Combine(Path.GetTempPath(), "autofix_slim_" + Guid.NewGuid().ToString("N") + ".scr");
                string target = outputPath.Replace("\\", "/");
                var sb = new StringBuilder();
                sb.Append("OPEN \"").Append(target).AppendLine("\"");
                sb.AppendLine("-PURGE");
                sb.AppendLine("A");
                sb.AppendLine("*");
                sb.AppendLine("N");
                sb.AppendLine("-PURGE");
                sb.AppendLine("A");
                sb.AppendLine("*");
                sb.AppendLine("N");
                sb.AppendLine("_QSAVE");
                sb.AppendLine("QUIT");
                File.WriteAllText(scriptPath, sb.ToString(), Encoding.UTF8);

                Log(log, "启动 AutoCAD 执行清理脚本 ...");
                using (Process p = Process.Start(new ProcessStartInfo
                {
                    FileName = acad,
                    Arguments = "/nologo /b \"" + scriptPath + "\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(acad),
                    UseShellExecute = false
                }))
                {
                    if (p == null)
                    {
                        return "无法启动 AutoCAD 进程。";
                    }
                    if (!p.WaitForExit(180000))
                    {
                        try { p.Kill(); } catch { }
                        return "AutoCAD 处理超时（超过 3 分钟）。";
                    }
                }
                Thread.Sleep(2000);

                // 结束刚启动的 AutoCAD 残留进程
                try
                {
                    foreach (Process p in Process.GetProcessesByName("acad"))
                    {
                        try
                        {
                            if ((DateTime.Now - p.StartTime).TotalSeconds < 200)
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                        finally
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }
                catch { }

                if (!File.Exists(outputPath))
                {
                    return "瘦身失败：未生成输出文件，请确认 AutoCAD 能正常启动。";
                }

                long newSize = new FileInfo(outputPath).Length;
                if (newSize == 0)
                {
                    try { File.Delete(outputPath); } catch { }
                    return "瘦身失败：输出文件为空。";
                }
                if (newSize >= originalSize)
                {
                    try { File.Delete(outputPath); } catch { }
                    return "该文件已很干净，无需瘦身（瘦身后未变小）。原文件未改动。";
                }

                long saved = originalSize - newSize;
                double pct = (double)saved / originalSize * 100.0;
                return "文件瘦身完成！\r\n\r\n"
                     + "原文件：" + Path.GetFileName(filePath) + "（" + FormatSize(originalSize) + "）\r\n"
                     + "新文件：" + Path.GetFileName(outputPath) + "（" + FormatSize(newSize) + "）\r\n"
                     + "节省空间：" + FormatSize(saved) + "（" + pct.ToString("F1") + "%）\r\n\r\n"
                     + "原文件保持原样，请自行确认新文件后替换。";
            }
            catch (Exception ex)
            {
                return "瘦身失败：" + ex.Message;
            }
            finally
            {
                if (!string.IsNullOrEmpty(scriptPath))
                {
                    try { if (File.Exists(scriptPath)) File.Delete(scriptPath); } catch { }
                }
            }
        }

        /// <summary>枚举注册表找出 AutoCAD 可执行文件。返回 null 表示未找到。</summary>
        internal static string FindAutoCadExe()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey root = baseKey.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD", false))
                    {
                        if (root == null)
                        {
                            continue;
                        }
                        foreach (string rv in root.GetSubKeyNames())
                        {
                            if (!rv.StartsWith("R", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            using (RegistryKey verKey = root.OpenSubKey(rv, false))
                            {
                                if (verKey == null)
                                {
                                    continue;
                                }
                                foreach (string pid in verKey.GetSubKeyNames())
                                {
                                    try
                                    {
                                        using (RegistryKey prod = verKey.OpenSubKey(pid, false))
                                        {
                                            if (prod == null)
                                            {
                                                continue;
                                            }
                                            string loc = prod.GetValue("AcadLocation") as string;
                                            if (!string.IsNullOrEmpty(loc))
                                            {
                                                string exe = Path.Combine(loc.Trim(), "acad.exe");
                                                if (File.Exists(exe))
                                                {
                                                    return exe;
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        // ==================== 系统还原点 ====================

        /// <summary>创建一个系统还原点，作为高危操作前的回退点。</summary>
        public static string CreateRestorePoint(Action<string> log)
        {
            if (DryRun)
            {
                DryNote("创建系统还原点：AutoFix 修复前备份 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), log);
                return "[预演] 将创建一个系统还原点。";
            }
            try
            {
                string desc = "AutoFix 修复前备份 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                using (var mc = new ManagementClass(@"\\.\root\default:SystemRestore"))
                {
                    ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = desc;
                    inParams["RestorePointType"] = 0;   // APPLICATION_INSTALL
                    inParams["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE

                    Log(log, "正在创建还原点：" + desc);
                    ManagementBaseObject outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                    uint rc = outParams == null ? 9999u : Convert.ToUInt32(outParams["ReturnValue"]);

                    if (rc == 0)
                    {
                        return "系统还原点已创建：" + desc;
                    }
                    return RestorePointErrorText(rc);
                }
            }
            catch (Exception ex)
            {
                return "创建还原点失败：" + ex.Message + "\r\n\r\n"
                     + "常见原因：系统保护未开启（可在「系统属性 → 系统保护」中启用系统盘保护）。";
            }
        }

        private static string RestorePointErrorText(uint code)
        {
            string hint;
            switch (code)
            {
                case 1:
                case 1058:
                    hint = "系统还原服务未运行，或系统保护未开启。";
                    break;
                case 5:
                    hint = "需要管理员权限。";
                    break;
                default:
                    hint = "返回码：" + code;
                    break;
            }
            return "创建还原点失败（" + hint + "）\r\n\r\n请在「系统属性 → 系统保护」中确认系统盘已启用保护。";
        }

        // ==================== 批量文件版本查询 ====================

        /// <summary>批量查询文件版本，逐行汇总。</summary>
        public static string QueryFileVersions(IEnumerable<string> paths, Action<string> log)
        {
            var list = new List<string>(paths ?? new string[0]);
            if (list.Count == 0)
            {
                return "未选择文件。";
            }

            var sb = new StringBuilder();
            int ok = 0, unknown = 0;

            foreach (string p in list)
            {
                Log(log, "查询：" + Path.GetFileName(p));
                string r = QueryFileVersion(p);
                sb.AppendLine("── " + Path.GetFileName(p));

                if (r.Contains("无法识别"))
                {
                    unknown++;
                }
                else
                {
                    ok++;
                }

                foreach (string line in r.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Trim().Length > 0)
                    {
                        sb.AppendLine("   " + line.Trim());
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("共 " + list.Count + " 个文件：识别 " + ok + " 个，未能识别 " + unknown + " 个。");
            return sb.ToString();
        }
    }
}
