using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutodeskFix
{
    // CAD 文件版本降级：调用本机已安装的 ODA File Converter。
    // 与原始实现不同之处：不联网、不自动下载安装 ODA，只使用本机已有的转换器。
    internal static partial class RepairService
    {
        /// <summary>可降级到的目标版本（ODA 支持的范围）。</summary>
        internal static readonly int[] CadTargetVersions = { 2000, 2004, 2007, 2010, 2013, 2018 };

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>把 CAD 文件降级到指定版本，输出为「原名_v版本」副本。</summary>
        public static string DowngradeCadFile(string filePath, int targetVersion, Action<string> log)
        {
            if (DryRun)
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return "文件不存在。";
                }
                string out0 = OutputPathFor(filePath, targetVersion);
                DryNote("调用 ODA File Converter 把文件转换为 AutoCAD " + targetVersion
                        + " 格式，输出 " + out0, log);
                return "[预演] 将把「" + Path.GetFileName(filePath) + "」降级为 AutoCAD "
                     + targetVersion + " 格式，输出「" + Path.GetFileName(out0) + "」。原文件不会被修改。";
            }

            string inDir = null;
            string outDir = null;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    return "文件不存在。";
                }

                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".dwg" && ext != ".dxf")
                {
                    return "仅支持 .dwg 与 .dxf 格式。";
                }

                bool supported = false;
                foreach (int v in CadTargetVersions)
                {
                    if (v == targetVersion)
                    {
                        supported = true;
                        break;
                    }
                }
                if (!supported)
                {
                    return "目标版本无效。支持：" + string.Join("、", Array.ConvertAll(CadTargetVersions, x => x.ToString()));
                }

                string converter = FindOdaFileConverter();
                if (string.IsNullOrEmpty(converter))
                {
                    return "未找到 ODA File Converter。\r\n\r\n"
                         + "本工具不联网下载，请先手动安装 ODA File Converter：\r\n"
                         + "https://www.opendesign.com/guestfiles/oda_file_converter\r\n\r\n"
                         + "安装后重新执行本功能即可。";
                }
                Log(log, "ODA File Converter：" + converter);

                // ODA 在临时目录中处理，处理完成后把结果移回
                inDir = Path.Combine(Path.GetTempPath(), "Autodesk-Fix_ODAIn_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                outDir = Path.Combine(Path.GetTempPath(), "Autodesk-Fix_ODAOut_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(inDir);
                Directory.CreateDirectory(outDir);

                string staged = Path.Combine(inDir, Path.GetFileName(filePath));
                File.Copy(filePath, staged, true);

                string outExt = ext.TrimStart('.').ToUpperInvariant();
                string odaType = outExt == "DXF" ? "DXF" : "DWG";
                string versionCode = OdaVersionCode(targetVersion);

                string args = "\"" + inDir + "\" \"" + outDir + "\" " + versionCode + " " + odaType + " 0 1";
                Log(log, "执行转换：" + versionCode + " / " + odaType);

                var psi = new ProcessStartInfo
                {
                    FileName = converter,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(converter)
                };

                string stdOut = "";
                string stdErr = "";
                int exitCode;

                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        return "无法启动 ODA File Converter。";
                    }

                    // ODA 可能弹出窗口，隐藏之
                    var hider = new Thread(() =>
                    {
                        try
                        {
                            int i = 0;
                            while (!p.HasExited && i < 50)
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                {
                                    ShowWindow(p.MainWindowHandle, 0);
                                }
                                Thread.Sleep(100);
                                p.Refresh();
                                i++;
                            }
                            while (!p.HasExited)
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                {
                                    ShowWindow(p.MainWindowHandle, 0);
                                }
                                Thread.Sleep(100);
                            }
                        }
                        catch { }
                    });
                    hider.IsBackground = true;
                    hider.Start();

                    var outReader = new Thread(() => { try { stdOut = p.StandardOutput.ReadToEnd(); } catch { } });
                    outReader.IsBackground = true;
                    outReader.Start();
                    var errReader = new Thread(() => { try { stdErr = p.StandardError.ReadToEnd(); } catch { } });
                    errReader.IsBackground = true;
                    errReader.Start();

                    bool exited = p.WaitForExit(60000);
                    if (outReader.IsAlive) { outReader.Join(2000); }
                    if (errReader.IsAlive) { errReader.Join(2000); }

                    if (!exited)
                    {
                        try { p.Kill(); } catch { }
                        return "转换超时（超过 60 秒）。文件可能过大或转换器异常。";
                    }
                    exitCode = p.ExitCode;
                }

                if (exitCode != 0)
                {
                    return "转换失败（退出代码 " + exitCode + "）。\r\n\r\n"
                         + (string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr);
                }

                string[] produced = Directory.GetFiles(outDir, "*.*", SearchOption.TopDirectoryOnly);
                if (produced.Length == 0)
                {
                    return "转换失败：转换器未生成输出文件。\r\n\r\n"
                         + (string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr);
                }

                string outputPath = OutputPathFor(filePath, targetVersion);
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                File.Move(produced[0], outputPath);

                var info = new FileInfo(outputPath);
                if (info.Length <= 0)
                {
                    try { File.Delete(outputPath); } catch { }
                    return "转换失败：输出文件为空。原文件可能已损坏或该版本不受支持。";
                }

                return "降级完成！\r\n\r\n"
                     + "原文件：" + Path.GetFileName(filePath) + "\r\n"
                     + "输出文件：" + Path.GetFileName(outputPath) + "（AutoCAD " + targetVersion + " 格式）\r\n"
                     + "文件大小：" + FormatSize(info.Length) + "\r\n\r\n"
                     + "原文件保持原样，请确认输出文件后自行替换。";
            }
            catch (Exception ex)
            {
                return "降级失败：" + ex.Message;
            }
            finally
            {
                CleanupTempDir(inDir);
                CleanupTempDir(outDir);
            }
        }

        private static void CleanupTempDir(string dir)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch { }
        }

        /// <summary>目标版本 → ODA 版本代码。</summary>
        internal static string OdaVersionCode(int year)
        {
            switch (year)
            {
                case 2000: return "ACAD2000";
                case 2004: return "ACAD2004";
                case 2007: return "ACAD2007";
                case 2010: return "ACAD2010";
                case 2013: return "ACAD2013";
                case 2018: return "ACAD2018";
                default: return "ACAD2013";
            }
        }

        private static string OutputPathFor(string inputPath, int targetVersion)
        {
            string dir = Path.GetDirectoryName(inputPath);
            string name = Path.GetFileNameWithoutExtension(inputPath);
            string ext = Path.GetExtension(inputPath);
            return Path.Combine(dir, name + "_v" + targetVersion + ext);
        }

        /// <summary>在本机查找 ODAFileConverter.exe。返回 null 表示未安装。</summary>
        internal static string FindOdaFileConverter()
        {
            var roots = new List<string>();
            try { roots.Add(@"C:\Program Files\ODA"); } catch { }
            try { roots.Add(@"C:\Program Files (x86)\ODA"); } catch { }
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ODA")); } catch { }
            try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ODA")); } catch { }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string root in roots)
            {
                if (!seen.Add(root) || string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    continue;
                }

                // 形如 ODA\ODAFileConverter 24.12.0\ODAFileConverter.exe
                try
                {
                    foreach (string sub in Directory.GetDirectories(root, "ODAFileConverter*", SearchOption.TopDirectoryOnly))
                    {
                        string exe = Path.Combine(sub, "ODAFileConverter.exe");
                        if (File.Exists(exe))
                        {
                            return exe;
                        }
                        try
                        {
                            foreach (string deeper in Directory.GetDirectories(sub, "*", SearchOption.AllDirectories))
                            {
                                string exe2 = Path.Combine(deeper, "ODAFileConverter.exe");
                                if (File.Exists(exe2))
                                {
                                    return exe2;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                string direct = Path.Combine(root, "ODAFileConverter", "ODAFileConverter.exe");
                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            return null;
        }
    }
}

