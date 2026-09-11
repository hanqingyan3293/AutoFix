# Fix1603 — Autodesk 1603 错误修复工具

从 `Autobox 1.0.4` 的 `ErrorRepairService.FixError1603()` 逆向还原而来，**不含任何登录/授权校验**，可直接运行。

## 编译

```
dotnet build Fix1603/Fix1603.csproj -c Release
```

产物：`Fix1603/bin/Release/net472/Fix1603.exe`（19 KB，需 .NET Framework 4.7.2）

## 运行

双击运行，或在命令行执行（会弹 UAC）：

```
Fix1603.exe          # 交互式：预扫描 + 总确认 + 每项危险操作二次确认
Fix1603.exe --yes    # 无人值守：跳过全部确认（危险，仅限自动化场景）
```

清单已声明 `requireAdministrator`，启动即提权。日志写入
`%APPDATA%\Fix1603\Logs\Fix1603_yyyyMMdd.log`。

## 确认机制

**默认不执行**——所有确认框都是「输入 Y 才继续，直接回车跳过」，回车即放弃该项操作。

运行时的确认层级（两层）：

1. **预扫描**：启动后先只读检测，列出本次**实际**会执行的操作（不存在的项不列出）。
2. **总确认**：列出全部待执行项，确认后才进入执行阶段；拒绝则「未做任何更改」直接退出。
3. **逐项二次确认**：每类危险操作执行前再单独确认，并列出**具体目标**（确切的目录路径、文件路径、账户名）。

| 确认点 | 覆盖的操作 | 确认框列出的内容 |
| --- | --- | --- |
| 总确认 | 全部 | 本次实际待执行的项清单 |
| 危险操作 1/3 | 停服务、杀进程 | 服务名 + 会被强制结束的进程名（提示未保存数据会丢失） |
| 危险操作 2/3 | 删目录 | 每个将被永久删除的目录完整路径（提示不进回收站、不可撤销） |
| 危险操作 3/3 | 改 .pit 权限 | 文件路径 + 将被授予完全控制的账户（说明不会删除该文件） |
| 文档路径修复 | 改注册表 / 文档库 / 重启资源管理器 | 提示会重启资源管理器，桌面任务栏会短暂消失 |

被跳过的项会在末尾「执行结果」中标记为「已跳过」，与「已执行」「无需处理」区分开。

> `--yes` 会跳过**全部**确认（含总确认），这是刻意留给脚本/批量场景的无人值守开关。
> 若不希望存在这个口子，删掉 `Confirm()` 里的 `if (autoYes)` 分支即可，改动仅一处。

## 修复原理（原程序逻辑，按执行顺序）

| 步骤 | 动作 | 说明 |
| --- | --- | --- |
| 0 | 「我的文档」路径自检 | 读 `HKCU\...\Explorer\Shell Folders\Personal`，若指向的盘符已不存在则询问并修复 |
| 0a | 恢复默认文档目录 | 写 `User Shell Folders\Personal` 与 `{F42EE2E4-CD92-456a-9728-B14CB08064DD}` = `%USERPROFILE%\Documents`；`Shell Folders\Personal` = 物理路径；广播 `WM_SETTINGCHANGE` |
| 0b | 修 `Documents.library-ms` | XML 中失效路径引用替换为新路径，先备份为 `.autobox.bak` |
| 0c | 重启资源管理器 | `taskkill /f /im explorer.exe` 后重新拉起 |
| 1 | 停止 `AdskLicensingService` | ServiceController 停止 + `sc stop`，等待 1 s |
| 2 | 结束进程 | `AdskLicensingAgent.exe`、`AdSSO.exe`，等待 0.5 s |
| 3 | 删除残留目录 | `C:\Program Files (x86)\Common Files\Autodesk Shared\AdskLicensing`、`C:\ProgramData\Autodesk\AdskLicensingService` |
| 4 | 修 `ProductInformation.pit` 权限 | 若当前用户无删除级权限，授予 当前用户 / Administrators / SYSTEM 完全控制（不删除、不重命名该文件） |

## 关于 1603

Windows Installer 的 `1603` 是「安装过程中发生致命错误」的笼统码，Autodesk 系列安装中最常见的三个诱因是：
系统账户/文件夹权限不足、`AdskLicensing` 组件残留或损坏、用户文档路径失效。

本工具覆盖后两项：清掉 `AdskLicensing` 残留并修复 `.pit` 权限，同时处理「文档指向已删除分区」这一常见隐藏诱因。

如果修完仍报 1603，通常是权限层面问题，需要单独检查（见下）。

## 未覆盖的 1603 分支

原程序在此处还有配套的权限修复能力，本工具未纳入（可按需补）：

- `FolderPermissionRepair` — 目录权限修复
- `ErrorRepairService` 中的 `SeTakeOwnershipPrivilege` 提权取所有权逻辑（原代码在 1603 前置流程附近）
- `FixWebView2` / `FixAdskInstall` — 相关组件缺失分支

需要的话可以继续把这几项也还原进来。
