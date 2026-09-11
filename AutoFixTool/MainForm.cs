using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AutoFix
{
    /// <summary>
    /// Autodesk 修复工具箱。
    /// 布局：左侧分类导航 + 右侧功能按钮网格 + 底部执行日志。
    /// 界面与配色为独立设计。
    /// </summary>
    internal sealed class MainForm : Form
    {
        private sealed class FixItem
        {
            public string Name;
            public string Desc;
            public string Warn;                                   // 确认框内容；为空表示无需确认
            public Func<Action<string>, string> Run;
            public bool IsDetect;                                 // 打开结果窗口而非直接执行
            public bool NeedsFile;                                // 执行前先选择文件
            public bool NeedsFiles;                               // 执行前先选择多个文件
            public bool IsVolumeCache;                            // 扫描卷缓存后弹出清理窗口
            public bool IsResidueScan;                            // 残留扫描后弹出结果窗口
            public string[] Choices;                              // 执行前让用户选择一项
            public string ChoicePrompt;
            public bool IsLicense;                                // 打开许可配置窗口
            public LicenseMethod LicenseMethod = LicenseMethod.Standalone;
            public bool IsLicenseLookup;                          // 只读产品密钥查询
            public bool IsProductUninstall;                       // 打开产品卸载窗口
            public bool IsVerify;                                 // 清理后校验
            public bool IsDeepCleanOnly;                          // 仅深度清理残留
            public bool IsVerify17;                               // 17 项完整验证
            public bool IsAudit;                                  // 完整系统审计（只读）
            public bool IsDesktopConnector;                        // Desktop Connector 工作区
            public bool IsRestartPending;                          // 修复重启挂起
            public bool IsBackup;                                  // 备份模板与设置
            public bool IsProductScan;                             // 独立扫描产品
            public bool FullClean;                                  // 全量卸载并深度清理
        }

        private sealed class Category
        {
            public string Name;
            public string Intro;
            public List<FixItem> Items = new List<FixItem>();
        }

        private static readonly Color ColSide = Color.FromArgb(35, 42, 54);
        private static readonly Color ColSideText = Color.FromArgb(200, 208, 220);
        private static readonly Color ColAccent = Color.FromArgb(45, 127, 249);
        private static readonly Color ColContent = Color.FromArgb(247, 248, 250);
        private static readonly Color ColHeader = Color.FromArgb(27, 33, 48);
        private static readonly Color ColMuted = Color.FromArgb(120, 130, 146);

        // 网格参数：按钮尺寸固定，列数按窗口可用宽度自动计算
        private const int BtnW = 152;
        private const int BtnH = 44;
        private const int GapX = 10;
        private const int GapY = 10;

        private readonly List<Category> _cats = new List<Category>();
        private ListBox _nav;
        private Label _catTitle;
        private Label _catIntro;
        private Label _hint;
        private Panel _gridHost;
        private int _gridCols = -1;
        private RichTextBox _log;
        private Label _status;
        private bool _busy;
        private string _pendingFile;
        private string[] _pendingFiles;
        private CheckBox _dryRunBox;
        private string _pendingChoice;

        public MainForm()
        {
            BuildCatalog();
            BuildUi();
        }

        private void BuildCatalog()
        {
            var detect = new Category
            {
                Name = "环境检测",
                Intro = "只读扫描：系统环境、Autodesk 组件状态、已安装产品。不会修改任何设置。"
            };
            detect.Items.Add(new FixItem
            {
                Name = "检测并查看报告",
                Desc = "扫描系统环境、Autodesk 组件状态与已安装产品，在结果页中查看并支持导出。",
                IsDetect = true
            });

            var install = new Category
            {
                Name = "安装错误修复",
                Intro = "针对安装过程中出现的错误代码。多数与 AdskLicensing 组件残留、权限或系统目录状态有关。"
            };
            install.Items.Add(new FixItem
            {
                Name = "错误1603修复",
                Desc = "安装致命错误。清理 AdskLicensing 残留，并在检测到文档路径失效时一并修复。",
                Warn = "  · 停止 AdskLicensingService\r\n" +
                       "  · 强制结束 AdskLicensingAgent.exe / AdSSO.exe\r\n" +
                       "  · 永久删除两个 AdskLicensing 残留目录\r\n" +
                       "  · 若「我的文档」指向失效分区，恢复为默认目录\r\n" +
                       "  · 为 ProductInformation.pit 补足权限（不删除该文件）",
                Run = RepairService.Fix1603
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1625修复",
                Desc = "系统策略限制安装。修复 Installer 分支权限，并清理无效注册表项。",
                Warn = "  · 停止 msiserver\r\n" +
                       "  · 重设 Installer 分支权限（含子项）\r\n" +
                       "  · 删除 Products / Features / Patches 下无效注册表项（不可撤销）",
                Run = RepairService.Fix1625
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1606修复",
                Desc = "无法访问网络位置。恢复公共文件夹在注册表中的路径映射。",
                Warn = "  · 写入 HKLM 公共文件夹路径注册表项\r\n" +
                       "  · 补建 C:\\Users\\Public\\ 下缺失的目录",
                Run = RepairService.Fix1606
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1632修复",
                Desc = "Windows Installer 缓存目录缺失。补建 C:\\Windows\\Installer。",
                Warn = "  · 在 C:\\Windows 下创建 Installer 目录（已存在则跳过）",
                Run = RepairService.FixError1632
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1622修复",
                Desc = "临时文件夹权限异常。为当前用户、管理员组、用户组授予完全控制。",
                Warn = "  · 修改用户临时文件夹的访问权限（授予完全控制）\r\n" +
                       "  · 含最多 1000 个子目录",
                Run = RepairService.FixError1622
            });
            install.Items.Add(new FixItem
            {
                Name = "错误270修复",
                Desc = "AdskLicensing 组件残留。只清理组件，不触碰文档与权限。",
                Warn = "  · 停止 AdskLicensingService\r\n" +
                       "  · 强制结束 AdskLicensingAgent.exe / AdSSO.exe\r\n" +
                       "  · 永久删除两个 AdskLicensing 残留目录",
                Run = RepairService.Fix270
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1修复",
                Desc = "AdskLicensing 残留且服务注册项损坏。额外移除服务注册。",
                Warn = "  · 停止 AdskLicensingService\r\n" +
                       "  · 强制结束 AdskLicensingAgent.exe\r\n" +
                       "  · 永久删除两个 AdskLicensing 残留目录\r\n" +
                       "  · 删除系统服务注册项 AdskLicensingService",
                Run = RepairService.FixError1
            });
            install.Items.Add(new FixItem
            {
                Name = "错误4000修复",
                Desc = "公共文件夹路径异常 + SOFTWARE\\Classes 权限损坏。",
                Warn = "  · 写入 HKLM 公共文件夹路径并补建目录\r\n" +
                       "  · 停止 msiserver\r\n" +
                       "  · 取得 SOFTWARE\\Classes 所有权并重设完全控制权限\r\n" +
                       "  · 删除 SOFTWARE\\Classes 下的所有叶子键（范围很大，请谨慎）",
                Run = RepairService.FixError4000
            });
            install.Items.Add(new FixItem
            {
                Name = "错误-9修复",
                Desc = "与错误4000同一套处理（公共文件夹 + Classes 权限）。",
                Warn = "  · 与「错误4000修复」完全相同的操作\r\n" +
                       "  · 其中包含删除 SOFTWARE\\Classes 下的所有叶子键（范围很大，请谨慎）",
                Run = RepairService.FixErrorMinus9
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1327修复",
                Desc = "Installer\\Folders 中残留了指向已不存在磁盘的记录。",
                Warn = "  · 删除 Installer\\Folders 中指向失效盘符的注册表值\r\n" +
                       "  · 不删除任何文件",
                Run = RepairService.FixError1327
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1308修复",
                Desc = "安装源指向已不存在的磁盘或网络位置。",
                Warn = "  · 清除各产品 SourceList 中指向失效盘符的 LastUsedSource\r\n" +
                       "  · 不删除任何文件",
                Run = RepairService.FixError1308
            });
            install.Items.Add(new FixItem
            {
                Name = "错误2修复",
                Desc = "Installer 临时文件堆积 + 无效安装源。",
                Warn = "  · 删除 C:\\Windows\\Installer 下的 *.tmp 文件\r\n" +
                       "  · 清除各产品 SourceList 中的无效安装源",
                Run = RepairService.FixError2
            });
            install.Items.Add(new FixItem
            {
                Name = "错误2755修复",
                Desc = "Installer 缓存损坏。清临时文件并重启 Windows Installer 服务。",
                Warn = "  · 停止并重新启动 msiserver\r\n" +
                       "  · 删除 C:\\Windows\\Installer 下的 *.tmp 文件",
                Run = RepairService.FixError2755
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1618修复",
                Desc = "安装被其他安装进程占用。结束相关进程并清除锁文件。",
                Warn = "  · 强制结束 msiexec / setup / install / instmsi / msiinst 进程\r\n" +
                       "  · 删除 msi.dll.lock 与 Installer 目录下的 *.lock\r\n" +
                       "  · 停止并重新启动 msiserver",
                Run = RepairService.FixError1618
            });
            install.Items.Add(new FixItem
            {
                Name = "错误103修复",
                Desc = "Autodesk Access / ODIS 组件残留。",
                Warn = "  · 停止 Autodesk Access Service Host\r\n" +
                       "  · 强制结束 4 个 Adsk 进程\r\n" +
                       "  · 删除 3 个 UPI2 / Uninstall / ODIS 注册表键\r\n" +
                       "  · 永久删除 AdODIS / ODIS / Autodesk Access 目录",
                Run = RepairService.FixError103
            });
            install.Items.Add(new FixItem
            {
                Name = "错误3修复",
                Desc = "清理 C:\\ProgramData 下名称异常的文件夹（含 Package Cache / Packages）。",
                Warn = "  · 永久删除 C:\\ProgramData 下名称含特殊字符的文件夹\r\n" +
                       "  · 以及名为 Package Cache / Packages 的文件夹\r\n" +
                       "  · 该判断基于名称特征，可能涉及非 Autodesk 目录，请谨慎",
                Run = RepairService.FixError3
            });
            install.Items.Add(new FixItem
            {
                Name = "错误1644修复",
                Desc = "智能应用控制拦截安装。仅打开系统设置页，不修改任何设置。",
                Warn = "  · 打开「智能应用控制」设置页供你手动关闭\r\n" +
                       "  · 本工具不会自动修改该系统设置",
                Run = RepairService.FixError1644
            });
            install.Items.Add(new FixItem
            {
                Name = "ARX异常修复",
                Desc = "AutoCAD ARX 模块加载异常。修正系统代码页为 936（简体中文）。",
                Warn = "  · 写入 HKLM\\SYSTEM\\CurrentControlSet\\Control\\Nls\\CodePage\r\n" +
                       "  · 将 ACP / MACCP / OEMCP 三项设为 936\r\n" +
                       "  · 需重启电脑后生效",
                Run = RepairService.FixArxError
            });
            install.Items.Add(new FixItem
            {
                Name = "卸载AdskLicensing",
                Desc = "停止 AdskLicensing 服务并删除其残留目录，供重新安装。",
                Warn = "  · 停止 AdskLicensingService\r\n" +
                       "  · 强制结束 AdskLicensingAgent.exe / AdSSO.exe\r\n" +
                       "  · 永久删除两个 AdskLicensing 残留目录",
                Run = RepairService.UninstallAdskLicensing
            });

            var perm = new Category
            {
                Name = "权限与注册表",
                Intro = "针对「无法打开注册表项」「权限不足」「缓存损坏」一类问题，会调整注册表或目录的访问权限。"
            };
            perm.Items.Add(new FixItem
            {
                Name = "错误1402修复",
                Desc = "无法打开注册表项。取得所有权并为 Installer 分支重设完全控制权限。",
                Warn = "  · 停止 msiserver\r\n" +
                       "  · 取得 Installer 注册表分支所有权\r\n" +
                       "  · 重设完全控制权限并启用继承（含全部子项）",
                Run = RepairService.Fix1402
            });
            perm.Items.Add(new FixItem
            {
                Name = "错误2503修复",
                Desc = "临时目录权限异常且临时文件堆积。修复权限并清理临时文件。",
                Warn = "  · 修复 C:\\Windows\\Temp 与用户临时目录权限\r\n" +
                       "  · 删除两个临时目录下的文件（被占用的会跳过）",
                Run = RepairService.FixError2503
            });
            perm.Items.Add(new FixItem
            {
                Name = "错误1935修复",
                Desc = "程序集组件注册损坏。重新注册 Windows Installer 并清理 .NET 临时缓存。",
                Warn = "  · 停止 msiserver\r\n" +
                       "  · 重新注册 Windows Installer（msiexec /unregister + /regserver）\r\n" +
                       "  · 删除 .NET 的 Temporary ASP.NET Files 缓存目录",
                Run = RepairService.FixError1935
            });

            perm.Items.Add(new FixItem
            {
                Name = "错误1722修复",
                Desc = "系统盘空间检查 + Autodesk 目录与注册表权限修复。",
                Warn = "  · 报告系统盘剩余空间\r\n" +
                       "  · 为 Autodesk 三个安装目录授予完全控制（含继承）\r\n" +
                       "  · 为 Autodesk / Installer / Classes\\Installer 注册表键授予完全控制",
                Run = RepairService.FixError1722
            });
            perm.Items.Add(new FixItem
            {
                Name = "错误5修复",
                Desc = "权限综合修复。覆盖 Autodesk 目录、临时目录、Installer 目录与注册表键。",
                Warn = "  · 为 4 个 Autodesk 目录授予完全控制\r\n" +
                       "  · 为系统临时目录、用户临时目录、Windows\\Installer 授予完全控制\r\n" +
                       "  · 为 4 个注册表分支授予完全控制（含全部子键）",
                Run = RepairService.FixError5
            });
            perm.Items.Add(new FixItem
            {
                Name = "目录权限修复",
                Desc = "为 Autodesk 关键安装目录授予完全控制权限（含继承）。",
                Warn = "  · 对 7 个 Autodesk 目录授予「当前用户 / Administrators / Users」完全控制\r\n" +
                       "  · 通过继承下传到子目录，不逐层遍历文件",
                Run = RepairService.RepairAutodeskFolderPermissions
            });
            perm.Items.Add(new FixItem
            {
                Name = "hosts修复",
                Desc = "移除 hosts 中屏蔽 Autodesk 域名的条目并刷新 DNS 缓存。",
                Warn = "  · 删除 hosts 中把 Autodesk / adsk / genuine / autocad 域名\r\n" +
                       "    指向 0.0.0.0 / 127.0.0.1 / ::1 的行\r\n" +
                       "  · 修改前备份为 hosts.autofix.bak\r\n" +
                       "  · 只删除命中上述特征的条目，其余内容原样保留",
                Run = RepairService.FixHostsAutodeskBlocks
            });

            var comp = new Category
            {
                Name = "组件与清理",
                Intro = "处理安装时无限重启、安装程序闪退，以及移除 Autodesk 附加组件与许可数据。"
            };
            comp.Items.Add(new FixItem
            {
                Name = "结束无限重启",
                Desc = "安装过程中反复重启。清除 Windows Update 的挂起重启标记。",
                Warn = "  · 删除注册表键 RebootRequired（挂起的重启标记）",
                Run = RepairService.FixRestartLoop
            });
            comp.Items.Add(new FixItem
            {
                Name = "安装闪退修复",
                Desc = "安装程序启动即闪退（2020 及以后版本）。清除 IFEO 中的调试器劫持项。",
                Warn = "  · 从 Image File Execution Options 中清除 19 个安装相关程序的 Debugger 项",
                Run = RepairService.FixInstallCrash2020Plus
            });
            comp.Items.Add(new FixItem
            {
                Name = "强制删除AGS",
                Desc = "结束 GenuineService 进程并删除其全部安装目录。",
                Warn = "  · 强制结束 GenuineService.exe\r\n" +
                       "  · 永久删除 Program Files / ProgramData / 用户目录下的 Genuine Service 目录",
                Run = RepairService.RemoveGenuineService
            });
            comp.Items.Add(new FixItem
            {
                Name = "删除许可09-21",
                Desc = "2009-2021 版本的许可数据。停止 FlexNet 服务并清除许可文件。",
                Warn = "  · 停止 FlexNet Licensing Service（含 64 位）\r\n" +
                       "  · 结束 AdskLicensingAgent.exe / WSCommCntr4.exe / LMU.exe\r\n" +
                       "  · 永久删除 4 个 FLEXnet 许可数据文件",
                Run = RepairService.RemoveLicense2009_2021
            });
            comp.Items.Add(new FixItem
            {
                Name = "删除许可04-08",
                Desc = "2004-2008 版本的许可数据。",
                Warn = "  · 永久删除目录 C:\\ProgramData\\Autodesk\\Software Licenses",
                Run = RepairService.RemoveLicense2004_2008
            });
            comp.Items.Add(new FixItem
            {
                Name = "删A360盘符",
                Desc = "移除资源管理器中的 A360 Drive 盘符项。",
                Warn = "  · 删除 HKLM 下的 NameSpace 注册表项 {A7B36FF9-...}",
                Run = l => RepairService.RemoveA360Drive(l)
            });

            var ext = new Category
            {
                Name = "扩展功能",
                Intro = "文件关联重置，以及 Maya / Revit / 3ds Max 的界面语言切换。语言切换会写入系统级配置，需重启对应软件生效。"
            };
            ext.Items.Add(new FixItem
            {
                Name = "CAD文件关联修复",
                Desc = "重置 .dwg / .dxf 的文件关联，供重新选择打开方式。",
                Warn = "  · 删除 .dwg / .dxf 在 HKCR、HKLM、HKCU 下的关联注册表键\r\n" +
                       "  · 删除 HKCU 下 Autodesk\\DwgCommon 的 shellex\\apps 项\r\n" +
                       "  · 删除 HKCU 下 Explorer\\FileExts 中的 .dwg / .dxf 用户选择记录",
                Run = RepairService.FixCadFileAssociation
            });
            ext.Items.Add(new FixItem
            {
                Name = "Maya切换英文",
                Desc = "把 Maya 界面语言设为英文。",
                Warn = "  · 写入系统环境变量 MAYA_UI_LANGUAGE = en_US",
                Run = l => RepairService.SetMayaLanguage(true, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "Maya切换中文",
                Desc = "把 Maya 界面语言设为中文。",
                Warn = "  · 写入系统环境变量 MAYA_UI_LANGUAGE = zh_CN",
                Run = l => RepairService.SetMayaLanguage(false, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "Revit切换英文",
                Desc = "通过修改桌面 Revit 快捷方式的 /language 参数切换为英文。",
                Warn = "  · 修改桌面（含公共桌面）中所有 Revit 快捷方式的目标参数\r\n" +
                       "  · 快捷方式将被改写为 /language ENU",
                Run = l => RepairService.SetRevitLanguage(true, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "Revit切换中文",
                Desc = "通过修改桌面 Revit 快捷方式的 /language 参数切换为中文。",
                Warn = "  · 修改桌面（含公共桌面）中所有 Revit 快捷方式的目标参数\r\n" +
                       "  · 快捷方式将被改写为 /language CHS",
                Run = l => RepairService.SetRevitLanguage(false, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "3dsMax切换英文",
                Desc = "把 3ds Max 界面语言设为英文。",
                Warn = "  · 写入 HKCU\\SOFTWARE\\Autodesk\\3dsMax 下各版本的 CurrentLanguage = ENU",
                Run = l => RepairService.SetMaxLanguage(true, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "3dsMax切换中文",
                Desc = "把 3ds Max 界面语言设为中文。",
                Warn = "  · 写入 HKCU\\SOFTWARE\\Autodesk\\3dsMax 下各版本的 CurrentLanguage = CHS",
                Run = l => RepairService.SetMaxLanguage(false, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "查询文件版本",
                Desc = "读取 CAD（.dwg/.dxf）、Revit（.rvt）、Maya（.ma/.mb）文件的版本信息。只读。",
                NeedsFile = true,
                Run = l => RepairService.QueryFileVersion(_pendingFile)
            });
            ext.Items.Add(new FixItem
            {
                Name = "批量查询版本",
                Desc = "一次选择多个文件，批量读取 CAD / Revit / Maya 文件的版本信息。只读。",
                NeedsFiles = true,
                Run = l => RepairService.QueryFileVersions(_pendingFiles, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "CAD文件瘦身",
                Desc = "通过 AutoCAD 脚本清理图纸冗余数据，原文件不动，结果另存为「原名_瘦身」。",
                NeedsFile = true,
                Warn = "  · 需要本机已安装 AutoCAD，且 AutoCAD 必须处于关闭状态\r\n" +
                       "  · 会启动一次 AutoCAD（隐藏窗口）执行清理脚本，最长等待 3 分钟\r\n" +
                       "  · 结果另存为「原名_瘦身.dwg」，原文件保持原样\r\n" +
                       "  · 处理期间请勿手动操作 AutoCAD",
                Run = l => RepairService.ShrinkCadFile(_pendingFile, l)
            });
            ext.Items.Add(new FixItem
            {
                Name = "CAD版本降级",
                Desc = "把高版本 CAD 文件降级为低版本，供旧版 AutoCAD 打开。使用本机已安装的 ODA File Converter。",
                NeedsFile = true,
                Choices = new[] { "AutoCAD 2000", "AutoCAD 2004", "AutoCAD 2007", "AutoCAD 2010", "AutoCAD 2013", "AutoCAD 2018" },
                ChoicePrompt = "选择目标版本（降级为该版本后，旧版 AutoCAD 可打开）：",
                Warn = "  · 需要本机已安装 ODA File Converter（本工具不联网下载）\r\n" +
                       "  · 转换在临时目录中进行，最长等待 60 秒\r\n" +
                       "  · 输出为「原名_v版本.dwg」副本，原文件保持原样\r\n" +
                       "  · 降级会丢失高版本特有的对象信息，请在副本上确认后再替换",
                Run = l =>
                {
                    int year = ParseYear(_pendingChoice);
                    return RepairService.DowngradeCadFile(_pendingFile, year, l);
                }
            });
            ext.Items.Add(new FixItem
            {
                Name = "Maya配置初始化",
                Desc = "删除「我的文档\\maya」下的用户配置，让 Maya 重新生成默认配置。",
                Warn = "  · 永久删除「我的文档\\maya」整个目录（不可撤销）\r\n" +
                       "  · 其中的自定义脚本、工具架、偏好设置都会丢失",
                Run = RepairService.InitializeMaya
            });
            ext.Items.Add(new FixItem
            {
                Name = "Revit配置初始化",
                Desc = "删除各版本 Revit 用户配置目录与注册表快捷方式。",
                Warn = "  · 永久删除 LocalAppData 与 AppData 下「Autodesk Revit *」版本目录\r\n" +
                       "  · 删除 HKCU\\SOFTWARE\\Autodesk\\Revit 下的版本键与 Shortcuts\r\n" +
                       "  · 不可撤销",
                Run = RepairService.InitializeRevit
            });
            ext.Items.Add(new FixItem
            {
                Name = "3dsMax配置初始化",
                Desc = "删除 3ds Max 用户配置目录。",
                Warn = "  · 永久删除 LocalAppData\\Autodesk\\3dsMax 整个目录（不可撤销）\r\n" +
                       "  · 其中的自定义设置、快捷键、界面布局都会丢失",
                Run = RepairService.InitializeMax
            });
            ext.Items.Add(new FixItem
            {
                Name = "CAD配置初始化",
                Desc = "删除各版本 AutoCAD 用户配置目录与 HKCU 下 R* 键。",
                Warn = "  · 永久删除 AppData 与 LocalAppData 下名称以 AutoCAD 开头的目录\r\n" +
                       "  · 删除 HKCU\\SOFTWARE\\Autodesk\\AutoCAD 下的 R* 版本键\r\n" +
                       "  · 不可撤销",
                Run = RepairService.InitializeCad
            });

            var disk = new Category
            {
                Name = "磁盘清理",
                Intro = "调用 Windows 自带的磁盘清理引擎处理系统卷缓存。先只读扫描，勾选后才清理。"
            };
            disk.Items.Add(new FixItem
            {
                Name = "卷缓存清理",
                Desc = "扫描并清理 Windows 卷缓存（回收站、临时文件、Windows Update 缓存等）。",
                IsVolumeCache = true
            });

            var uninst = new Category
            {
                Name = "产品卸载清理",
                Intro = "Autodesk 卸载与清理工作台。覆盖扫描、卸载、深度清理、验证、审计、"
                      + "组件工作区与重启挂起处理。所有破坏性操作都会先出确认框；"
                      + "建议先开顶栏的预演模式看一遍影响范围。"
            };
            uninst.Items.Add(new FixItem
            {
                Name = "1 扫描已安装产品",
                Desc = "只读扫描，列出本机已安装的 Autodesk 产品与版本。",
                IsProductScan = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "2 卸载选中产品",
                Desc = "勾选要卸载的产品，按「停进程/服务 → 多轮卸载 → 复查」执行。",
                IsProductUninstall = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "3 全量卸载并深度清理",
                Desc = "勾选产品后卸载，并自动执行完整深度清理（目录/快捷方式/缓存/注册表/服务/任务/防火墙）。",
                IsProductUninstall = true,
                FullClean = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "4 仅深度清理残留",
                Desc = "不卸载任何产品，只清理残留。适用于产品已卸载但残留导致无法重装的情况。",
                IsDeepCleanOnly = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "5 完整验证（17 项）",
                Desc = "只读验证 17 个维度：产品、进程、服务、目录、注册表、许可、环境变量、"
                     + "幽灵项、快捷方式、计划任务、防火墙、IFEO、PFRO、hosts、Desktop Connector。",
                IsVerify17 = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "6 创建系统还原点",
                Desc = "在执行高危操作前创建还原点。",
                Warn = "  · 调用系统还原接口创建一个还原点\r\n" +
                       "  · 需要系统盘已开启「系统保护」，否则会失败\r\n" +
                       "  · 创建过程可能需要数十秒",
                Run = RepairService.CreateRestorePoint
            });
            uninst.Items.Add(new FixItem
            {
                Name = "7 搜索全部残留",
                Desc = "只读扫描六类 Autodesk 残留：注册表、文件目录、服务、进程、卸载项、快捷方式。",
                IsResidueScan = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "8 完整系统审计",
                Desc = "只读预览：列出完整清理会删除的全部内容（17 个分类），先看清楚再决定。",
                IsAudit = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "9 清理Desktop Connector工作区",
                Desc = "删除 Desktop Connector 的本地同步目录（%USERPROFILE%\\DC 与 \\ACCDocs）。",
                IsDesktopConnector = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "10 错误103诊断修复",
                Desc = "10 项只读诊断，定位错误 103 的成因：ODIS 锁文件、IFEO 劫持、组件版本、"
                     + "Access 服务、ODIS 基础设施、.pit、TEMP、VC++、事件日志、hosts。",
                IsVerify = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "11 修复重启挂起",
                Desc = "检查并清除全部 5 类挂起的重启标记，然后刷新 Windows Installer。",
                IsRestartPending = true
            });
            uninst.Items.Add(new FixItem
            {
                Name = "12 备份模板与设置",
                Desc = "把 Autodesk 用户配置、模板、工作空间备份到指定目录（复制，不修改原文件）。",
                IsBackup = true
            });

            var lic = new Category
            {
                Name = "许可管理",
                Intro = "封装 Autodesk 官方的 AdskLicensingInstHelper.exe，在图形界面中切换许可方式。"
                      + "适用于 2020 及以后版本；需已安装 Autodesk Licensing 组件。"
            };
            lic.Items.Add(new FixItem
            {
                Name = "切换为网络许可",
                Desc = "把指定产品设为网络许可，需填写许可服务器。",
                IsLicense = true,
                LicenseMethod = LicenseMethod.Network
            });
            lic.Items.Add(new FixItem
            {
                Name = "切换为单机许可",
                Desc = "把指定产品设为单机（序列号）许可。",
                IsLicense = true,
                LicenseMethod = LicenseMethod.Standalone
            });
            lic.Items.Add(new FixItem
            {
                Name = "切换为用户许可",
                Desc = "把指定产品设为命名用户许可。",
                IsLicense = true,
                LicenseMethod = LicenseMethod.User
            });
            lic.Items.Add(new FixItem
            {
                Name = "重置许可",
                Desc = "清除指定产品的许可配置，并重置本机登录状态与身份服务数据库。",
                IsLicense = true,
                LicenseMethod = LicenseMethod.Reset
            });
            lic.Items.Add(new FixItem
            {
                Name = "查询产品密钥",
                Desc = "按年份浏览 Autodesk 产品名称与产品密钥对照表（2020–2027，共 1073 条）。只读。",
                IsLicenseLookup = true
            });

            _cats.Add(detect);
            _cats.Add(install);
            _cats.Add(perm);
            _cats.Add(comp);
            _cats.Add(ext);
            _cats.Add(uninst);
            _cats.Add(lic);
            _cats.Add(disk);
        }

        private void BuildUi()
        {
            Text = "Autodesk 修复工具箱";
            ClientSize = new Size(980, 680);
            MinimumSize = new Size(880, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ColContent;
            Font = new Font("Microsoft YaHei UI", 9f);

            // ---- 顶栏 ----
            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = ColHeader };
            header.Controls.Add(new Label
            {
                Text = "Autodesk 修复工具箱",
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
                Location = new Point(20, 10),
                AutoSize = true
            });
            header.Controls.Add(new Label
            {
                Text = "免费 · 无需登录 · 离线运行",
                ForeColor = Color.FromArgb(140, 152, 172),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Location = new Point(22, 35),
                AutoSize = true
            });
            _dryRunBox = new CheckBox
            {
                Text = "预演模式（只列计划，不实际执行）",
                ForeColor = Color.FromArgb(210, 220, 235),
                Font = new Font("Microsoft YaHei UI", 9f),
                Location = new Point(0, 18),
                Width = 260,
                Height = 22,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _dryRunBox.CheckedChanged += (s, e) =>
            {
                RepairService.DryRun = _dryRunBox.Checked;
                RepairService.DryRunLog = AppendLog;
                AppendLog(_dryRunBox.Checked
                    ? "已开启预演模式：所有修复只输出将执行的操作，不会改动系统。"
                    : "已关闭预演模式：修复将实际执行。");
                UpdateStatus();
            };
            header.Controls.Add(_dryRunBox);
            header.Resize += (s, e) =>
            {
                _dryRunBox.Left = Math.Max(280, header.ClientSize.Width - _dryRunBox.Width - 18);
            };

            // ---- 状态栏 ----
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(238, 240, 244) };
            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                ForeColor = Color.FromArgb(90, 100, 116)
            };
            footer.Controls.Add(_status);

            // ---- 左侧分类导航 ----
            var side = new Panel { Dock = DockStyle.Left, Width = 190, BackColor = ColSide, Padding = new Padding(0, 12, 0, 0) };
            _nav = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = ColSide,
                ForeColor = ColSideText,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 44,
                IntegralHeight = false,
                Font = new Font("Microsoft YaHei UI", 10f)
            };
            foreach (Category c in _cats)
            {
                _nav.Items.Add(c.Name);
            }
            _nav.DrawItem += NavDrawItem;
            _nav.SelectedIndexChanged += (s, e) => ShowCategory();
            side.Controls.Add(_nav);

            // ---- 日志区 ----
            var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 196, Padding = new Padding(20, 0, 20, 12) };
            var logTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Text = "执行日志",
                ForeColor = ColMuted,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                WordWrap = false
            };
            logPanel.Controls.Add(_log);
            logPanel.Controls.Add(logTitle);

            // ---- 内容区 ----
            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 8) };

            _catTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(38, 44, 56)
            };
            _catIntro = new Label
            {
                Dock = DockStyle.Top,
                Height = 46,
                ForeColor = Color.FromArgb(88, 96, 112),
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            _hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                ForeColor = ColMuted,
                Font = new Font("Microsoft YaHei UI", 7.5f),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _gridHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = ColContent,
                Padding = new Padding(2, 8, 2, 8)
            };
            _gridHost.Resize += (s, e) => LayoutGrid();

            content.Controls.Add(_gridHost);
            content.Controls.Add(_hint);
            content.Controls.Add(_catIntro);
            content.Controls.Add(_catTitle);

            Controls.Add(content);
            Controls.Add(logPanel);
            Controls.Add(side);
            Controls.Add(footer);
            Controls.Add(header);

            _nav.SelectedIndex = 0;
            _status.Text = RepairService.IsAdministrator()
                ? "已获得管理员权限 · 就绪"
                : "未以管理员身份运行 —— 修复功能需要管理员权限";
        }

        private void NavDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var b = new SolidBrush(selected ? ColAccent : ColSide))
            {
                e.Graphics.FillRectangle(b, e.Bounds);
            }
            if (selected)
            {
                using (var b = new SolidBrush(Color.White))
                {
                    e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.X, e.Bounds.Y + 10, 3, e.Bounds.Height - 20));
                }
            }
            TextRenderer.DrawText(e.Graphics, _nav.Items[e.Index].ToString(), _nav.Font,
                new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height),
                selected ? Color.White : ColSideText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        /// <summary>切换分类：重建按钮网格。</summary>
        private void ShowCategory()
        {
            int idx = _nav.SelectedIndex;
            if (idx < 0 || idx >= _cats.Count)
            {
                return;
            }
            Category cat = _cats[idx];
            _catTitle.Text = cat.Name;
            _catIntro.Text = cat.Intro;

            _gridHost.SuspendLayout();
            _gridHost.Controls.Clear();

            foreach (FixItem item in cat.Items)
            {
                var btn = new Button
                {
                    Text = item.Name,
                    Size = new Size(BtnW, BtnH),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(52, 60, 76),
                    Font = new Font("Microsoft YaHei UI", 9.5f),
                    Cursor = Cursors.Hand,
                    UseVisualStyleBackColor = false,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(212, 218, 228);
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 243, 255);

                FixItem captured = item;
                btn.MouseEnter += (s, e) =>
                {
                    _hint.Text = captured.Desc + (string.IsNullOrEmpty(captured.Warn) ? "" : "   [需要确认]");
                };
                btn.Click += (s, e) => OnRun(captured);

                _gridHost.Controls.Add(btn);
            }

            _gridHost.ResumeLayout();
            _gridCols = -1;
            LayoutGrid();

            // 若正在执行任务，新分类的按钮也要保持禁用，
            // 避免看起来可用（实际点击会被 _busy 拦住）造成误导。
            if (_busy)
            {
                SetButtonsEnabled(false);
            }

            _hint.Text = "";
            UpdateStatus();
        }

        /// <summary>按容器可用宽度自动计算列数，并重新排布功能按钮。</summary>
        private void LayoutGrid()
        {
            if (_gridHost == null)
            {
                return;
            }

            int avail = _gridHost.ClientSize.Width - _gridHost.Padding.Horizontal;
            if (avail < BtnW)
            {
                avail = BtnW;
            }

            int cols = (avail + GapX) / (BtnW + GapX);
            if (cols < 1)
            {
                cols = 1;
            }

            // 列数未变则不必重排；这也避免滚动条出现引发的反复重排
            if (cols == _gridCols && _gridHost.Controls.Count > 0)
            {
                return;
            }
            _gridCols = cols;

            _gridHost.SuspendLayout();
            try
            {
                int x = _gridHost.Padding.Left;
                int y = _gridHost.Padding.Top;
                int col = 0;

                foreach (Control c in _gridHost.Controls)
                {
                    c.Location = new Point(x, y);

                    col++;
                    if (col >= cols)
                    {
                        col = 0;
                        x = _gridHost.Padding.Left;
                        y += BtnH + GapY;
                    }
                    else
                    {
                        x += BtnW + GapX;
                    }
                }
            }
            finally
            {
                _gridHost.ResumeLayout();
            }
        }

        private void OnRun(FixItem item)
        {
            if (_busy)
            {
                return;
            }

            if (item.IsDetect)
            {
                RunDetection();
                return;
            }

            if (item.IsVolumeCache)
            {
                RunVolumeCache();
                return;
            }

            if (item.IsResidueScan)
            {
                RunResidueScan();
                return;
            }

            if (item.IsLicense || item.IsLicenseLookup)
            {
                using (var f = new LicenseForm(
                    item.IsLicenseLookup ? LicenseMethod.Standalone : item.LicenseMethod,
                    item.IsLicenseLookup))
                {
                    f.ShowDialog(this);
                }
                UpdateStatus();
                return;
            }

            if (item.IsProductUninstall)
            {
                RunProductUninstall(item.FullClean);
                return;
            }

            if (item.IsVerify)
            {
                RunVerify();
                return;
            }

            if (item.IsProductScan)
            {
                RunProductScan();
                return;
            }

            if (item.IsDeepCleanOnly)
            {
                RunDeepCleanOnly();
                return;
            }

            if (item.IsVerify17)
            {
                RunVerify17();
                return;
            }

            if (item.IsAudit)
            {
                RunAudit();
                return;
            }

            if (item.IsDesktopConnector)
            {
                RunDesktopConnector();
                return;
            }

            if (item.IsRestartPending)
            {
                RunRestartPending();
                return;
            }

            if (item.IsBackup)
            {
                RunBackup();
                return;
            }

            if (item.IsProductUninstall)
            {
                RunProductUninstall(false);
                return;
            }

            if (item.NeedsFiles)
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "选择要查询的文件（可多选）",
                    Multiselect = true,
                    Filter = "CAD / Revit / Maya 文件|*.dwg;*.dxf;*.rvt;*.ma;*.mb|" +
                             "AutoCAD 图形 (*.dwg;*.dxf)|*.dwg;*.dxf|" +
                             "Revit 项目 (*.rvt)|*.rvt|" +
                             "Maya 文件 (*.ma;*.mb)|*.ma;*.mb|" +
                             "所有文件 (*.*)|*.*"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    _pendingFiles = dlg.FileNames;
                }
            }

            if (item.Choices != null && item.Choices.Length > 0)
            {
                using (var dlg = new ChoiceForm(item.Name, item.ChoicePrompt ?? "请选择：", item.Choices))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    _pendingChoice = dlg.SelectedChoice;
                }
            }

            string picked = null;
            if (item.NeedsFile)
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "选择要查询的文件",
                    Filter = "CAD / Revit / Maya 文件|*.dwg;*.dxf;*.rvt;*.ma;*.mb|" +
                             "AutoCAD 图形 (*.dwg;*.dxf)|*.dwg;*.dxf|" +
                             "Revit 项目 (*.rvt)|*.rvt|" +
                             "Maya 文件 (*.ma;*.mb)|*.ma;*.mb|" +
                             "所有文件 (*.*)|*.*"
                })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    picked = dlg.FileName;
                }
            }
            _pendingFile = picked;

            // 预演模式不做任何实际改动，因此不要求管理员权限
            if (item.Warn != null && !RepairService.DryRun && !RepairService.IsAdministrator())
            {
                MessageBox.Show(this,
                    "该操作需要管理员权限。\r\n\r\n请关闭本程序，右键选择「以管理员身份运行」后重试。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (item.Warn != null)
            {
                string text = "「" + item.Name + "」将执行以下操作：" +
                              Environment.NewLine + Environment.NewLine +
                              item.Warn + Environment.NewLine + Environment.NewLine +
                              "确认执行？";
                if (MessageBox.Show(this, text, "确认执行", MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                {
                    AppendLog("已取消：" + item.Name);
                    return;
                }
            }

            Run(item);
        }

        private void Run(FixItem item)
        {
            _busy = true;
            SetButtonsEnabled(false);
            AppendLog("—— " + item.Name + " 开始 ——");
            _status.Text = RepairService.DryRun ? "预演中，请稍候..." : "执行中，请稍候...";

            if (RepairService.DryRun)
            {
                RepairService.ResetDryRunCounter();
            }

            var t = new Thread(() =>
            {
                string result;
                bool failed;
                try
                {
                    result = item.Run(AppendLog) ?? "";
                    failed = result.Contains("失败") || result.Contains("异常");
                }
                catch (Exception ex)
                {
                    result = "执行时发生异常：" + ex.Message;
                    failed = true;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 结束 ——");

                        if (RepairService.DryRun)
                        {
                            int n = RepairService.DryRunActionCount;
                            string head = "[预演模式] 共 " + n + " 项操作，均未实际执行。" +
                                          Environment.NewLine +
                                          "详细清单见下方日志（每行以「[预演] 将执行」开头）。" +
                                          Environment.NewLine + Environment.NewLine;
                            MessageBox.Show(this, head + result, "预演结果",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(this, result, failed ? "执行失败" : "操作完成",
                                MessageBoxButtons.OK, failed ? MessageBoxIcon.Hand : MessageBoxIcon.Information);
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>只读检测，完成后在独立窗口中展示报告。</summary>
        private void RunDetection()
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在检测，请稍候...";
            AppendLog("—— 开始检测 ——");

            var t = new Thread(() =>
            {
                EnvironmentReport report = null;
                string error = null;
                try
                {
                    report = RepairService.Detect(AppendLog);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 检测结束 ——");

                        if (report == null)
                        {
                            MessageBox.Show(this, "检测失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }

                        using (var f = new ResultForm(report))
                        {
                            f.ShowDialog(this);
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>只读扫描卷缓存 → 用户勾选确认 → 执行清理。</summary>
        private void RunVolumeCache()
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在扫描卷缓存...";
            AppendLog("—— 扫描卷缓存 ——");

            var t = new Thread(() =>
            {
                List<RepairService.VolumeCacheEntry> found = null;
                string error = null;
                try
                {
                    found = RepairService.ScanVolumeCaches();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (found == null)
                        {
                            _busy = false;
                            SetButtonsEnabled(true);
                            UpdateStatus();
                            MessageBox.Show(this, "扫描失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }

                        AppendLog("扫描完成，发现 " + found.Count + " 项可清理缓存");
                        foreach (RepairService.VolumeCacheEntry e in found)
                        {
                            AppendLog("  · " + e.Description);
                        }

                        using (var f = new VolumeCacheForm(found))
                        {
                            f.ShowDialog(this);
                            if (!f.Confirmed || f.SelectedKeys == null)
                            {
                                _busy = false;
                                SetButtonsEnabled(true);
                                UpdateStatus();
                                AppendLog("已取消卷缓存清理");
                                return;
                            }

                            var keys = new List<string>(f.SelectedKeys);
                            _status.Text = "正在清理，请稍候...";
                            AppendLog("—— 开始清理卷缓存 ——");

                            var t2 = new Thread(() =>
                            {
                                long freed = 0;
                                string err2 = null;
                                try
                                {
                                    freed = RepairService.PurgeVolumeCaches(keys, AppendLog);
                                }
                                catch (Exception ex)
                                {
                                    err2 = ex.Message;
                                }

                                try
                                {
                                    BeginInvoke((Action)(() =>
                                    {
                                        _busy = false;
                                        SetButtonsEnabled(true);
                                        UpdateStatus();
                                        AppendLog("—— 清理结束 ——");

                                        string msg = err2 != null
                                            ? "清理时发生异常：" + err2
                                            : "卷缓存清理完成，共释放 " + FormatBytes(freed) + "。";
                                        MessageBox.Show(this, msg, err2 != null ? "执行失败" : "操作完成",
                                            MessageBoxButtons.OK,
                                            err2 != null ? MessageBoxIcon.Hand : MessageBoxIcon.Information);
                                    }));
                                }
                                catch { }
                            });
                            t2.IsBackground = true;
                            t2.Start();
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>从「AutoCAD 2013」之类的选项中取出版份数字。</summary>
        private static int ParseYear(string choice)
        {
            if (string.IsNullOrEmpty(choice))
            {
                return 2013;
            }
            foreach (string part in choice.Split(' '))
            {
                int y;
                if (int.TryParse(part, out y) && y >= 1990 && y <= 2100)
                {
                    return y;
                }
            }
            return 2013;
        }

        private static string FormatBytes(long bytes)
        {
            double v = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024.0 && i < units.Length - 1)
            {
                v /= 1024.0;
                i++;
            }
            return v.ToString("0.##") + " " + units[i];
        }

        /// <summary>只读扫描 Autodesk 残留，完成后弹出结果页。</summary>
        private void RunResidueScan()
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在扫描残留，请稍候...";
            AppendLog("—— 开始残留扫描 ——");

            var t = new Thread(() =>
            {
                List<ResidueFinding> found = null;
                string error = null;
                try
                {
                    found = RepairService.ScanResidues(AppendLog);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 扫描结束 ——");

                        if (found == null)
                        {
                            MessageBox.Show(this, "扫描失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }

                        using (var f = new ResidueForm(found))
                        {
                            f.ShowDialog(this);
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>扫描已安装产品 → 勾选确认 → 按阶段卸载。</summary>
        private void RunProductUninstall(bool forceDeepClean)
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在扫描已安装产品...";
            AppendLog("—— 扫描已安装产品 ——");

            var t = new Thread(() =>
            {
                List<ProductItem> found = null;
                string error = null;
                try
                {
                    found = RepairService.ScanInstalledProducts();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (found == null)
                        {
                            _busy = false;
                            SetButtonsEnabled(true);
                            UpdateStatus();
                            MessageBox.Show(this, "扫描失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }

                        AppendLog("扫描完成，检测到 " + found.Count + " 个 Autodesk 产品");
                        foreach (ProductItem p in found)
                        {
                            AppendLog("  · " + p.Name + (string.IsNullOrEmpty(p.Version) ? "" : "  " + p.Version));
                        }

                        using (var f = new ProductUninstallForm(found))
                        {
                            f.ShowDialog(this);
                            if (!f.Confirmed || f.SelectedProducts == null)
                            {
                                _busy = false;
                                SetButtonsEnabled(true);
                                UpdateStatus();
                                AppendLog("已取消产品卸载");
                                return;
                            }

                            var sel = f.SelectedProducts;
                            bool rp = f.CreateRestorePoint;
                            bool deep = f.DeepClean;
                            bool multi = f.MultiUser;
                            if (forceDeepClean)
                            {
                                deep = true;
                            }

                            _status.Text = RepairService.DryRun ? "预演中，请稍候..." : "正在卸载，请稍候...";
                            AppendLog("—— 开始卸载 " + sel.Count + " 个产品 ——");
                            if (RepairService.DryRun)
                            {
                                RepairService.ResetDryRunCounter();
                            }

                            var t2 = new Thread(() =>
                            {
                                string result;
                                bool failed;
                                try
                                {
                                    result = RepairService.UninstallProducts(sel, rp, deep, multi, AppendLog);
                                    failed = result.Contains("失败");
                                }
                                catch (Exception ex)
                                {
                                    result = "卸载时发生异常：" + ex.Message;
                                    failed = true;
                                }

                                try
                                {
                                    BeginInvoke((Action)(() =>
                                    {
                                        _busy = false;
                                        SetButtonsEnabled(true);
                                        UpdateStatus();
                                        AppendLog("—— 卸载结束 ——");

                                        if (RepairService.DryRun)
                                        {
                                            string head = "[预演模式] 共 " + RepairService.DryRunActionCount
                                                        + " 项操作，均未实际执行。" + Environment.NewLine
                                                        + "详细清单见下方日志。" + Environment.NewLine + Environment.NewLine;
                                            MessageBox.Show(this, head + result, "预演结果",
                                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                                        }
                                        else
                                        {
                                            MessageBox.Show(this, result, failed ? "执行失败" : "操作完成",
                                                MessageBoxButtons.OK, failed ? MessageBoxIcon.Hand : MessageBoxIcon.Information);
                                        }
                                    }));
                                }
                                catch { }
                            });
                            t2.IsBackground = true;
                            t2.Start();
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>只读执行 10 项清理后校验，完成后弹出结果窗口。</summary>
        private void RunVerify()
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在校验，请稍候...";
            AppendLog("—— 开始清理后校验 ——");

            var t = new Thread(() =>
            {
                List<VerifyItem> items = null;
                string error = null;
                try
                {
                    items = RepairService.RunVerification(AppendLog);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 校验结束 ——");

                        if (items == null)
                        {
                            MessageBox.Show(this, "校验失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }

                        using (var f = new VerifyForm(items))
                        {
                            f.ShowDialog(this);
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>通用后台执行：处理忙碌状态、日志与结果弹窗。</summary>
        private void RunWork(string busyStatus, string logBegin, Func<string> work)
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = busyStatus;
            AppendLog(logBegin);

            if (RepairService.DryRun)
            {
                RepairService.ResetDryRunCounter();
            }

            var t = new Thread(() =>
            {
                string result;
                bool failed;
                try
                {
                    result = work() ?? "";
                    failed = result.Contains("失败") || result.Contains("异常");
                }
                catch (Exception ex)
                {
                    result = "执行时发生异常：" + ex.Message;
                    failed = true;
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 结束 ——");

                        if (RepairService.DryRun)
                        {
                            string head = "[预演模式] 共 " + RepairService.DryRunActionCount
                                        + " 项操作，均未实际执行。" + Environment.NewLine
                                        + "详细清单见下方日志。" + Environment.NewLine + Environment.NewLine;
                            MessageBox.Show(this, head + result, "预演结果",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(this, result, failed ? "执行失败" : "操作完成",
                                MessageBoxButtons.OK, failed ? MessageBoxIcon.Hand : MessageBoxIcon.Information);
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>#1 只读扫描已安装产品。</summary>
        private void RunProductScan()
        {
            RunWork("正在扫描产品...", "—— 扫描已安装产品 ——", () =>
            {
                List<ProductItem> list = RepairService.ScanInstalledProducts();
                if (list.Count == 0)
                {
                    AppendLog("未检测到已安装的 Autodesk 产品。");
                    return "未检测到已安装的 Autodesk 产品。";
                }
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("检测到 " + list.Count + " 个 Autodesk 产品：");
                sb.AppendLine();
                foreach (ProductItem p in list)
                {
                    string line = p.Name + (string.IsNullOrEmpty(p.Version) ? "" : "  " + p.Version);
                    AppendLog("  · " + line);
                    sb.AppendLine("  · " + line);
                }
                return sb.ToString();
            });
        }

        /// <summary>#4 仅深度清理残留。</summary>
        private void RunDeepCleanOnly()
        {
            bool multi = MessageBox.Show(this,
                "是否同时清理其他用户配置文件中的 Autodesk 残留？" + Environment.NewLine + Environment.NewLine
                + "选择「是」将同时处理其他用户的注册表与 AppData（已登录用户会跳过）。",
                "多用户清理", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            string warn = "「仅深度清理残留」将执行以下操作：" + Environment.NewLine + Environment.NewLine
                + "  · 结束 23 个 Autodesk 相关进程、停止 5 个服务" + Environment.NewLine
                + "  · 永久删除 7 个残留目录（含 Program Files / ProgramData 下的 Autodesk）" + Environment.NewLine
                + "  · 清理桌面与开始菜单快捷方式" + Environment.NewLine
                + "  · 清理缓存目录" + Environment.NewLine
                + "  · 删除服务注册、计划任务、防火墙规则" + Environment.NewLine
                + "  · 清理 IFEO 劫持项、HKCU 类键、注册表分支、相关环境变量" + Environment.NewLine
                + (multi ? "  · 清理其他用户配置文件" + Environment.NewLine : "")
                + "  · 刷新 Windows Installer 服务" + Environment.NewLine + Environment.NewLine
                + "⚠ 不会卸载任何产品，但会删除残留目录与注册表，不可撤销。" + Environment.NewLine + Environment.NewLine
                + "确认执行？";

            if (MessageBox.Show(this, warn, "确认深度清理",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                AppendLog("已取消深度清理");
                return;
            }

            if (!RepairService.DryRun && !RepairService.IsAdministrator())
            {
                MessageBox.Show(this, "该操作需要管理员权限。\r\n\r\n请以管理员身份重新运行本程序。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RunWork(RepairService.DryRun ? "预演中..." : "正在深度清理...", "—— 深度清理残留 ——",
                () => RepairService.DeepCleanOnly(multi, AppendLog));
        }

        /// <summary>#5 17 项完整验证。</summary>
        private void RunVerify17()
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在执行 17 项验证...";
            AppendLog("—— 17 项完整验证 ——");

            var t = new Thread(() =>
            {
                List<VerifyItem> items = null;
                string error = null;
                try { items = RepairService.RunFullVerification(AppendLog); }
                catch (Exception ex) { error = ex.Message; }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 验证结束 ——");
                        if (items == null)
                        {
                            MessageBox.Show(this, "验证失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }
                        using (var f = new VerifyForm(items)) { f.ShowDialog(this); }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>#8 完整系统审计（只读预览）。</summary>
        private void RunAudit()
        {
            _busy = true;
            SetButtonsEnabled(false);
            _status.Text = "正在审计系统足迹...";
            AppendLog("—— 完整系统审计（只读） ——");

            var t = new Thread(() =>
            {
                string report = null;
                string error = null;
                try { report = RepairService.RunSystemAudit(AppendLog); }
                catch (Exception ex) { error = ex.Message; }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _busy = false;
                        SetButtonsEnabled(true);
                        UpdateStatus();
                        AppendLog("—— 审计结束 ——");
                        if (report == null)
                        {
                            MessageBox.Show(this, "审计失败：" + error, "执行失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Hand);
                            return;
                        }
                        using (var f = new AuditForm(report)) { f.ShowDialog(this); }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>#9 清理 Desktop Connector 工作区（需手输 YES）。</summary>
        private void RunDesktopConnector()
        {
            string desc = RepairService.DescribeDesktopConnectorWorkspace();
            if (desc == null)
            {
                MessageBox.Show(this, "未发现 Desktop Connector 本地工作区（%USERPROFILE%\\DC 或 \\ACCDocs）。",
                    "无需处理", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!RepairService.DryRun && !RepairService.IsAdministrator())
            {
                MessageBox.Show(this, "该操作需要管理员权限。\r\n\r\n请以管理员身份重新运行本程序。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (RepairService.DryRun)
            {
                RunWork("预演中...", "—— Desktop Connector 工作区（预演） ——",
                    () => RepairService.CleanDesktopConnectorWorkspace(AppendLog));
                return;
            }

            using (var f = new TextConfirmForm("删除 Desktop Connector 工作区", desc, "YES"))
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                {
                    AppendLog("已取消 Desktop Connector 工作区清理");
                    return;
                }
            }

            RunWork("正在清理工作区...", "—— 清理 Desktop Connector 工作区 ——",
                () => RepairService.CleanDesktopConnectorWorkspace(AppendLog));
        }

        /// <summary>#11 修复重启挂起。</summary>
        private void RunRestartPending()
        {
            string warn = "「修复重启挂起」将检查并清除以下标记：" + Environment.NewLine + Environment.NewLine
                + "  · PendingFileRenameOperations" + Environment.NewLine
                + "  · WindowsUpdate\\Auto Update\\RebootRequired" + Environment.NewLine
                + "  · WindowsUpdate\\Orchestrator\\RebootRequired" + Environment.NewLine
                + "  · Updates\\UpdateExeVolatile（置 0）" + Environment.NewLine
                + "  · Component Based Servicing\\RebootPending" + Environment.NewLine
                + "  · 刷新 Windows Installer 服务" + Environment.NewLine + Environment.NewLine
                + "说明：这些标记会阻止 Autodesk 安装，清除通常不需要真正重启。" + Environment.NewLine + Environment.NewLine
                + "确认执行？";

            if (MessageBox.Show(this, warn, "确认修复重启挂起",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                AppendLog("已取消修复重启挂起");
                return;
            }

            if (!RepairService.DryRun && !RepairService.IsAdministrator())
            {
                MessageBox.Show(this, "该操作需要管理员权限。\r\n\r\n请以管理员身份重新运行本程序。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RunWork("正在处理重启标记...", "—— 修复重启挂起 ——",
                () => RepairService.FixRestartPending(AppendLog));
        }

        /// <summary>#12 备份模板与设置。</summary>
        private void RunBackup()
        {
            string dest;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择备份保存位置";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                dest = dlg.SelectedPath;
            }

            RunWork("正在备份...", "—— 备份 Autodesk 模板与设置 ——",
                () => RepairService.BackupAutodeskData(dest, AppendLog));
        }

        private void UpdateStatus()
        {
            int idx = _nav.SelectedIndex;
            int count = (idx >= 0 && idx < _cats.Count) ? _cats[idx].Items.Count : 0;
            string perm = RepairService.IsAdministrator()
                ? "已获得管理员权限"
                : "未以管理员身份运行";

            if (RepairService.DryRun)
            {
                _status.Text = "预演模式：只输出执行计划，不会改动系统 · 共 " + count + " 项";
            }
            else
            {
                _status.Text = perm + " · 共 " + count + " 项";
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            foreach (Control c in _gridHost.Controls)
            {
                var b = c as Button;
                if (b != null)
                {
                    b.Enabled = enabled;
                }
            }
        }

        private void AppendLog(string message)
        {
            if (_log == null || _log.IsDisposed)
            {
                return;
            }
            if (_log.InvokeRequired)
            {
                try { _log.BeginInvoke((Action)(() => AppendLog(message))); } catch { }
                return;
            }
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
    }
}
