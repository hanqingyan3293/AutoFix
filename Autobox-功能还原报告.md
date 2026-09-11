# Autobox 1.0.4 逆向分析与功能还原报告

> 生成方式：对 `Autobox1.0.4.exe` 做静态逆向（ILSpy 反编译 + 元数据/PE 分析），并对反编译产物做了重新编译验证。
> 本报告仅做功能提取与技术评估，不包含任何绕过授权校验的实现。

---

## 1. 样本指纹

| 项 | 值 |
| --- | --- |
| 文件名 | Autobox1.0.4.exe |
| 大小 | 3,603,008 字节 |
| SHA256 | `3AEA479B0702D4A19EE5C4B4511CEEC3B15A3B2D03A60CEC27CE92943006DA68` |
| 格式 | PE32 (x86)，CLR 程序集 (.NET Framework 4.0) |
| 编译器 | 未混淆（类名/方法名完整） |
| 公司 | Qianxun |
| 产品 | Autobox 1.0.4.0 |
| 说明 | 专注于 Autodesk 系列软件的清理和修复工作 |
| 版权 | Copyright © Qianxun 2026 |
| 界面 | WinForms + AntdUI（自绘无边框窗体） |
| 内嵌资源 | QRCoder(二维码)、AntdUI、AutoboxUpdate.exe(独立更新器) |

PE 结构：`.text` 3.53 MB（托管代码）+ `.rsrc` + `.reloc`；文件偏移 `0x36127C` 处内嵌一个 59 KB 的原生 PE。

---

## 2. 程序集组成

一个单文件程序集，含三个逻辑部分：

| 命名空间 | 内容 | 性质 |
| --- | --- | --- |
| `Autobox` | 应用主体（约 70 个类） | 自有代码 |
| `AntdUI` | 界面框架（窗体阴影、Win32 封装、自绘控件） | 第三方库内嵌 |
| `QRCoder` | 二维码生成（充值收款码） | 第三方库内嵌 |

外部引用：`System.Core`、`System.ServiceProcess`、`System.Management`、`System.Web.Extensions`、`Microsoft.CSharp`。

入口 `Program.Main`：单实例互斥体 `Autobox_SingleInstance_Mutex`（重复启动会把已有窗口置前），随后 `Application.Run(new Form1())`。

---

## 3. 总体架构

```
Form1 (主窗体, 290KB, 79 个交互处理器)
 ├── 导航按钮 imageButton1..19  →  首页 / 软件清理 / 错误修复 / 磁盘清理 / 扩展功能 / 充值 / 客服 ...
 │
 ├── 清理层   ICleaner 接口 ── 15 个产品清理器
 │             Clean(year) / Clean(year, cleanSharedComponents) / CleanAll()
 │
 ├── 修复层   ErrorRepairService（~40 个 Fix* 方法）
 │             RuntimeRepairService / VCRuntimeManager / FolderPermissionRepair
 │             WindowsVolumeCacheCleaner / CadFontInstallService
 │
 ├── 扫描层   Runtime（注册表/文件/服务/进程/卸载项/快捷方式残留检测）
 │             DiskCleanupService（40+ 类系统垃圾）
 │
 ├── 服务层   LoginService / PermissionService / RateLimitService
 │             UpdateService / DownloadConfigService / HttpClientHelper
 │             AnnouncementForm / PromotionService
 │
 ├── 安全层   MachineCodeGenerator / SecureStorage（AES+HMAC 本地状态）
 │
 └── 辅助     Logger / DebugHelper / LoadingHelper / ErrorMessageHelper
               自定义控件（ImageButton, BlueProgressBar, BorderlessTabControl ...）
```

清理器统一实现 `ICleaner`：

```csharp
void Clean(int year, UpdateProgressDelegate, LogMessageDelegate);
void Clean(int year, bool cleanSharedComponents, UpdateProgressDelegate, LogMessageDelegate, ShowMessageBoxDelegate);
void CleanAll(bool cleanSharedComponents, UpdateProgressDelegate, LogMessageDelegate, ShowMessageBoxDelegate);
```

每个产品清理器内部按年份拆成独立方法（`CleanAutoCAD2016`、`CleanRevit2024` …），逐个删除注册表项、目录、服务、卸载信息。

---

## 4. 功能模块清单

### 4.1 软件清理（15 个产品 × 年份矩阵）

| 清理器 | 产品 | 覆盖年份 | 年数 |
| --- | --- | --- | --- |
| AutoCADCleaner | AutoCAD | 2006–2027 | 22 |
| AutoCADArchitectureCleaner | AutoCAD Architecture | 2014–2027 | 14 |
| AutoCADElectricalCleaner | AutoCAD Electrical | 2014–2027 | 14 |
| AutoCADMechanicalCleaner | AutoCAD Mechanical | 2014–2027 | 14 |
| RevitCleaner | Revit | 2012–2027 | 16 |
| Civil3DCleaner | Civil 3D | 2014–2026 | 13 |
| InventorCleaner | Inventor | 2014–2027 | 14 |
| MaxCleaner | 3ds Max | 2012–2027 | 16 |
| MayaCleaner | Maya | 2011–2027（缺 2021） | 16 |
| MotionBuilderCleaner | MotionBuilder | 2018–2027（缺 2021） | 9 |
| MudboxCleaner | Mudbox | 2018–2027（缺 2021） | 9 |
| NavisworksCleaner | Navisworks | 2014–2027 | 14 |
| Plant3DCleaner | Plant 3D | 2014–2027 | 14 |
| PowerMillCleaner | PowerMill | 2017–2026 | 10 |
| （共享组件） | `cleanSharedComponents` 开关 | 全版本 | — |

每个年份方法的动作集合一致：停止服务 → 结束进程 → 删注册表键 → 删安装目录/用户目录 → 清卸载项。通用工具方法：`DeleteRegistryKey(32/64)`、`DeleteDirectory`、`DeleteFile`、`StopService`、`KillProcess`、`IsRootDirectory`（根目录保护）。

### 4.2 错误修复（ErrorRepairService，约 40 项）

安装类错误：1603、1625、1622、1632、1606、1618、1644、1722、1909（`btnFix1909_Click`）、1935、2503、2755、1327、1308、270、103、1402/1406、4000、4005、16、2、5、3、1、-9
其他：`FixRestartLoop`（重启循环）、`FixWebView2`、`FixCrash2020_2026`、`FixARXError`、`FixAdskInstall`、`FixAdskUninstall`、`FixCadFileAssociation`（文件关联）、`AddCadClassicMode`（CAD 经典模式）

清理类：`RemoveA360Drive`（删 A360 盘符）、`RemoveLicense2009_2021`、`RemoveLicense2004_2008`（删许可）、`RemoveGenuineService`（强制删除 AGS 正版校验服务）

语言切换/初始化：Maya 中英切换 + 初始化、Revit 中英切换 + 初始化、3ds Max 中英切换 + 初始化、`InitializeCad`

### 4.3 文件工具

| 功能 | 方法 |
| --- | --- |
| 查询 CAD 文件版本 | `GetCadFileVersion` |
| 查询 Revit 文件版本 | `GetRevitFileVersion` |
| 查询 Maya 文件版本 | `GetMayaFileVersion` |
| CAD 文件降级 | `DowngradeCadFile` |
| CAD 文件瘦身 | `ShrinkCadFile` |
| 下载并运行 CAD 病毒扫描 | `DownloadAndLaunchCadVirusScan` |

### 4.4 残留检测（Runtime，2 万行级）

独立窗体 `ResidueDetectionForm`，检测维度：

- 注册表残留：`SearchUninstallKeys`、`SearchInstallerProducts`、`SearchInstallerUserDataProducts`、`SearchRegistry`（按 Autodesk 关键字递归）
- 文件系统残留：`DetectFileSystemResidues`
- 服务残留：`DetectServiceResidues`
- 进程残留：`DetectProcessResidues`
- 卸载项残留：`DetectUninstallResidues`
- 安装目录识别：`DetectInstallDirectories` / `ScanAutodeskRegistryForInstallDirs`
- 桌面与开始菜单：`DetectDesktopAndStartMenuResidues`

辅助逻辑：`ConvertInstallerCodeToGuid`（Installer 产品码 → GUID）、`ContainsAutodeskKeyword`、`IsExcludedProduct`。

### 4.5 运行库修复

`RuntimeRepairService`（检测 + 单项修复 + 批量修复）、`VCRuntimeManager`（VC++ 2005/2008/2010/2012/2013/2015-2022 的检测/安装/卸载/注册表修复/DLL 提取）、`RuntimeRepairForm`。

修复对象含 `.NET Framework 2.0/3.0/3.5/4.0 Client/4.0 Full`、`DirectX`、`Windows Installer` 等。

### 4.6 磁盘清理（DiskCleanupService，40+ 类型）

系统类：临时文件、系统缓存、浏览器缓存、缩略图、回收站、Prefetch、Windows.old、休眠文件、Windows Update 备份/数据库/补丁、Windows Installer 缓存、搜索日志/索引、内存转储、字体缓存、IIS 日志、系统日志、完整性日志、错误报告、CryptoAPI 缓存、图标缓存、Defender 扫描记录、事件日志归档、安装日志、性能日志、帮助缓存、媒体播放器缓存、WinSxS 备份、还原点、传递优化、卷缓存（`WindowsVolumeCacheCleaner`）

应用类：Office 临时、Visual Studio、.NET 日志、Java、Adobe、Steam、Discord、微信/QQ 大文件、OneDrive、腾讯/阿里/百度系、迅雷、百度网盘、IDE、Node(npm)、Python(pip)、Docker、Epic/GOG/Origin/Ubisoft/BattleNet/Riot/原神、优酷/爱奇艺、NVIDIA/Intel 缓存

界面提供「扫描 → 勾选 → 清理」两段式流程（`ScanDisk` / `CleanSelectedItems` / `CleanDisk`）。

### 4.7 其他修复

- `CadFontInstallService` / `CadFontInstallForm`：下载 `cad_fonts.zip` 并安装 CAD 常用字体
- `LoginService.IsHostsFileTampered` / `FixHostsFileInteractive` / `HostsRepairProgressForm`：hosts 文件劫持检测与修复
- `FolderPermissionRepair`：目录权限修复
- `WindowsVolumeCacheCleaner`：Windows 卷缓存清理

### 4.8 更新与公告

- `UpdateService`：`GetCurrentVersion` → `CheckUpdate` → `IsNewerVersion` → `DownloadUpdate` → `InstallUpdate`；`ResolveDownloadedUpdatePath` 定位下载包
- `UpdateCheckForm`：检测更新界面
- `UpdaterResourceHelper`：释放内嵌的 `AutoboxUpdate.exe` 并交接更新
- `AutoboxUpdate.exe`（内嵌，.NET 4.0）：`UpdateProgressForm`，等待父进程退出 → 带重试复制文件 → 清理下载包。支持 `--source/--target` 参数
- `AnnouncementForm`：服务端公告（顶部公告 + 普通公告）
- `PromotionService`：推广/运营位

### 4.9 账号与授权

窗体：`LoginForm`、`RegisterForm`、`ForgotPasswordForm`、`ChangePasswordForm`、`RechargeForm`、`RechargeAgreementDialog`

服务：`LoginService`（登录、登出、token、会员到期、公告、网络诊断、北京时间校准）、`PermissionService`（功能权限校验 + 缓存）、`RateLimitService`（登录/注册频率限制）、`MachineCodeGenerator`（机器码）、`SecureStorage`（本地登录态加密存储）

### 4.10 AI 助手

`TongYiQianWenService`：调用阿里云百炼（DashScope）通义千问

- 端点：`https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation`
- 模型：`qwen-turbo`，`temperature=0.7`，`max_tokens=2000`，`top_p=0.8`
- 系统提示词：`你是一个专业的Autodesk软件技术支持助手，帮助用户解决软件安装、使用和错误修复等问题。请用友好、专业的语气回答问题。`
- 界面：`ButtonSendMessage_Click` / `ButtonClearChat_Click`，支持多轮对话（`ChatMessage`/`ChatMessageRole`）

---

## 5. 服务端接口契约

基址：`https://www.rj818.com/`（全部同时提供 `http://` 回退）

| 接口 | 方法 | 参数 | 用途 |
| --- | --- | --- | --- |
| `/Autodesk/AuthApi.ashx?action=login` | POST | `username, password, machineCode, jiqima` | 登录 |
| `/Autodesk/AuthApi.ashx?action=getexpire` | GET | `username` | 查会员到期时间 |
| `/Autodesk/AuthApi.ashx?action=changepassword` | POST | — | 修改密码 |
| `/Autodesk/AuthApi.ashx?action=resetpassword` | POST | — | 重置密码 |
| `/Autodesk/AuthApi.ashx?action=ping` | GET | — | 连通性检测 |
| `/Autodesk/PermissionApi.ashx?action=check` | POST | `username, machineCode` | 功能授权校验 |
| `/Autodesk/RechargeApi.ashx` | POST | — | 充值/开卡 |
| `/Autodesk/SettingApi.ashx?action=announcement` | GET | — | 公告（缓存 300 s） |
| `?action=topannouncement&t=` | GET | — | 顶部公告 |
| `?action=updateinfo` | GET | — | 更新信息 |
| `?action=downloadurls` / `fonturls` / `odaurls` | GET | — | 下载地址配置 |
| `?action=disableallfunctions` | GET | — | 全局停用开关 |
| `?action=operationlog` | POST | — | 操作日志上报 |
| `?action=rechargeagreement` | GET | — | 充值协议 |

权限接口返回 `status` 语义：`ok` / `expired`（已无使用时间，请充值）/ `disabled_all`（系统维护）/ `needvip`|`no`（需开通 VIP）/ `notlogin`（登录态失效或机器码不匹配）/ `error`。

资源下载：`/download/fonts/cad_fonts.zip`、`/download/AutoboxCadScan.exe`、`/download/info.txt`、`/help/index.html`、`/VC/index.html`、`/kefu/wx.png`、`/zhifu`（充值页）；第三方组件指向 Autodesk/微软官方地址（AdODIS、AdskLicensing、WebView2、VC++ Redist、InteroperabilityEngineManager）。

---

## 6. 授权与安全机制

### 6.1 机器码生成（MachineCodeGenerator）

```
machineCode = MD5( SMBIOS_UUID(32位十六进制) + BaseBoardProduct + CPU_ProcessorId )
```

- UUID 优先通过 `GetSystemFirmwareTable('RSMB')` 直接读 SMBIOS 表解析（不依赖 WMI），失败回退 `Win32_ComputerSystemProduct.UUID`
- 主板型号来自 `HKLM\HARDWARE\DESCRIPTION\System\BIOS\BaseBoardProduct`，回退 WMI `Win32_BaseBoard`
- CPU 序列号来自 WMI `Win32_Processor.ProcessorId`
- 全部失败时回退 `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`
- 另有 `GetMachineCodeLegacy()` 旧算法（兼容历史版本）

### 6.2 本地登录态存储（SecureStorage）

- 位置：`HKCU\Software\Autobox`，值 `LoginState` + `LoginStateHMAC`（另有 `IsLoggedIn`/`LoggedInUsername`/`IsVip`/`LoginToken` 兼容值）
- 加密：`AES-256-CBC`，密钥 = `PBKDF2(machineCode, SALT, 10000 次)` 派生 32 字节
- 完整性：独立 `HMACSHA256` 密钥；`IsDataTampered()` 校验
- 明文格式：`IsLoggedIn=1|Username=x|IsVip=0|Token=y|ExpireTime=z|SaveTime=...`

### 6.3 授权判定链

`Form1` 每个功能入口 → `PermissionService.CheckPermission()` → 依次检查「已登录」→「全局停用开关」→「5 分钟用户缓存」→ 向服务端 POST 校验（`username + machineCode`）。返回失败时按 `status` 弹「请先登录」/「已无使用时间，请充值后使用」/「此功能需要开通 VIP」。

要点：**授权是服务端判定的，客户端不持有可离线推演的注册码算法**。会员有效期由服务端 `expireTime` 决定，客户端还用网络北京时间做本地时钟校准，防止改系统时间。

### 6.4 发现的安全问题（建议处理）

1. **硬编码 API 密钥**：`TongYiQianWenService.DEFAULT_API_KEY` 内联了 DashScope 密钥（`sk-7ac1…be3d`），任何拿到 exe 的人都能提取并盗用，建议改为服务端代签并立即轮换。
2. **TLS 证书校验被完全关闭**：`SendHttpRequest` 中 `ServerCertificateValidationCallback` 恒返回 `true`，中间人可截获/篡改 AI 请求与响应。
3. **明文 HTTP 回退**：所有授权接口都提供 `http://` 回退（含登录、权限校验），降级路径可被劫持。
4. **充值入口为外部网页**：`/zhifu` 跳转外部页面，建议确认回跳校验。
5. **客户端删除动作覆盖面大**：清理器直接操作 HKLM 注册表、服务、系统目录，`IsRootDirectory` 是唯一防线，建议复核路径白名单与二次确认逻辑，避免误删。

---

## 7. 资源与数据

- 界面资源：`Autobox.Properties.Resources`，含 `acad`（byte[]）与 90+ 张按钮/背景位图（`btn01..`、`c_bg`、`shouye_*`、`xiufu_*`、`chongzhi_*`、`VIPuser` 等三态图）
- 窗体资源：`Autobox.Form1.resx`(155KB)、`LoginForm.resx`、`RegisterForm.resx`、`RechargeForm.resx`
- 内嵌可执行：`AutoboxUpdate.exe`（LogicalName 直嵌，运行时释放）
- 图标/清单：`app.ico`、`app.manifest`
- 运行期日志：`<安装目录>\Logs\log_yyyyMMdd.txt`（INFO/WARNING/ERROR + 异常堆栈）

---

## 8. 还原成果与验证

| 产物 | 路径 | 说明 |
| --- | --- | --- |
| 反编译源码工程 | `decompiled/` | 107 个文件，完整 C# 源码 + 资源 + csproj |
| 编译产物 | `decompiled/bin/Debug/net472/Autobox.exe` | 3,689,472 字节 |

**编译验证：0 错误，39 警告。** 说明反编译结果在结构上是完整、自洽的。

为完成编译做了两处**纯构建层**调整（不影响功能语义）：

1. `TargetFramework` 由 `net40` 改为 `net472` —— 本机无 .NET Framework 4.0 目标包，且 `System.Resources.Extensions` 最低支持 net462。net472 是本程序集的兼容超集。
2. 新增 `GenerateResourceUsePreserializedResources=true` 与 `System.Resources.Extensions` 引用 —— 用于处理 resx 中的位图资源（SDK 工具链要求）。

若要严格还原 net40 原样构建，需在装有 .NET Framework 4.0 Targeting Pack 的 VS 环境中编译。

---

## 9. 结论

该程序是**结构清晰、未混淆的正规商业工具**，功能边界明确：Autodesk 全系软件的清理、错误修复、残留检测、运行库修复 + 磁盘清理 + 文件工具 + 账号授权 + AI 客服。

功能还原已完整：全部 15 个产品清理器的年份矩阵与动作逻辑、约 40 项错误修复、40+ 类磁盘清理、7 类残留检测、完整服务端接口契约、机器码算法与本地加密存储方案均已提取，并通过重新编译验证。

建议下一步（可选）：
- 用 `decompiled/` 工程直接作为还原基线，把 `net40` 目标包补上以做二进制级对照
- 优先处理第 6.4 节的 5 项安全问题，尤其是硬编码密钥与 TLS 校验关闭
- 若要让清理器可维护，建议把各年份方法里硬编码的注册表/路径抽成声明式规则表（当前是逐年复制，重复度很高）

