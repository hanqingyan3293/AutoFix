# Autodesk Fix Toolbox

<img src="docs/images/icon/AutoFix_128.png" alt="Autodesk Fix Toolbox" width="96" height="96" align="right">

面向 Windows 的 Autodesk 产品**安装故障修复与残留清理**工具箱。图形界面，完全离线，无需登录。

## 特点

- **完全离线** —— 不发起任何网络请求，不下载任何文件
- **无需登录、无授权校验** —— 打开即用
- **预演模式** —— 所有修复可先只输出执行计划，确认无误后再实际执行
- **只读检测** —— 环境检测、残留扫描、系统审计、清理后校验四个模块全程只读，不改动系统
- **破坏性操作逐个确认** —— 默认按钮落在「取消」；删除用户项目文件要求手动输入 `YES`
- **共用组件保护** —— 被多个厂商共用的组件（如 FlexNet Publisher）由你逐项决定保留或删除，默认保留

## 功能概览

| 分类 | 项数 | 内容 |
| --- | --- | --- |
| 环境检测 | 1 | 系统环境、Autodesk 组件状态、已安装产品的只读体检（22 项检查） |
| 安装错误修复 | 19 | 1603、1625、1606、1632、1622、270、1、4000、-9、1327、1308、2、2755、1618、103、3、1644、ARX 异常、卸载 AdskLicensing |
| 权限与注册表 | 7 | 1402、2503、1935、1722、5、目录权限、hosts |
| 组件与清理 | 6 | 结束无限重启、安装闪退、Genuine Service、旧版许可、A360 盘符 |
| 扩展功能 | 16 | 文件关联、Maya / Revit / 3ds Max 语言切换、文件版本查询、CAD 瘦身与版本降级、配置初始化 |
| 产品卸载清理 | 12 | 卸载工作台：扫描、卸载、深度清理、17 项验证、系统审计、Desktop Connector、重启挂起、模板备份 |
| 许可管理 | 5 | 网络 / 单机 / 用户许可切换、重置许可、产品密钥查询 |
| 磁盘清理 | 1 | 调用 Windows 自带磁盘清理引擎处理卷缓存 |

合计 69 项。完整说明见 [AutoFixTool/README.md](AutoFixTool/README.md)。

## 环境要求

- Windows 10 / 11（兼容版亦支持 Windows 7 SP1 及以上）
- .NET Framework 4.7.2 或更高（兼容版仅需 4.0）
- 修复类功能需要管理员权限（程序清单已声明，启动时自动请求提权）

## 编译

```
git clone <本仓库地址>
cd <仓库目录>

# 标准版（net472）
dotnet build AutoFixTool/AutoFix.csproj -c Release

# 兼容版（net40，可运行于 Win7 SP1+）
dotnet build AutoFixTool/AutoFix.net40.csproj -c Release
```

产物路径：

```
AutoFixTool/bin/Release/net472/AutoFix.exe
AutoFixTool/bin/Release/net40/AutoFix.exe
```

无第三方 DLL 依赖，单文件即可运行。

## 使用

1. 以管理员身份运行 `AutoFix.exe`（清单已声明 `requireAdministrator`）
2. 建议先在顶栏开启**预演模式**，跑一遍目标功能，确认日志中「将执行」的清单符合预期
3. 关闭预演模式后正式执行；每个破坏性操作都会弹出确认框并列出**具体**动作
4. 首次运行会遇到 SmartScreen 提示（未签名程序），选择「更多信息 → 仍要运行」

## 安全设计

| 机制 | 说明 |
| --- | --- |
| 预演模式 | 33 处拦截点覆盖停服务、结束进程、删目录/文件、删注册表键/值、改 ACL、写文件、清缓存、语言切换、CAD 瘦身与降级等全部破坏性操作 |
| 确认框默认取消 | 误按回车即取消，不会执行 |
| 手输 YES | 全量清理、删除 Desktop Connector 工作区等不可恢复操作要求手动输入 |
| 只读优先 | 检测与审计模块零写入 |
| 操作前备份 | 修改注册表分支前导出 `.reg` 备份；改 hosts 前备份 `.autofix.bak` |

## 已知边界

- v1.0.0 尚未在真实机器上完成全量验证，行为描述来自代码实现。**建议先在测试机试用。**
- 需要联网的功能未纳入，以保持完全离线运行。
- CAD 版本降级需要本机已安装 ODA File Converter（程序不下载）。
- CAD 文件瘦身需要本机已安装 AutoCAD。
- 部分安全软件可能对「停服务、改注册表权限」这类行为告警。

## 项目结构

```
AutoFixTool/         主程序（WinForms，.NET Framework 4.0 / 4.7.2）
docs/                文档、发布说明、维护者清单
docs/images/icon/    图标资源（多尺寸 PNG + ICO + SVG 源文件）
```

## 文档

- [AutoFixTool/README.md](AutoFixTool/README.md) —— 功能详解、预演模式、检测结果页说明
- [CHANGELOG.md](CHANGELOG.md) —— 版本历史
- [CONTRIBUTING.md](CONTRIBUTING.md) —— 参与开发
- [SECURITY.md](SECURITY.md) —— 安全问题报告方式
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) —— 第三方来源与许可

## 许可

本项目以 [GNU AGPL-3.0](LICENSE) 许可发布。

项目图标（AutoFix.ico / AutoFix.svg 及各级 PNG）为本项目自有资源，
同样以 AGPL-3.0 发布。

```
Copyright (C) 2026 Autodesk Fix Toolbox contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published
by the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.
```

## 免责声明

本工具会执行停止服务、结束进程、删除文件与注册表项、修改访问权限等操作。
**使用者需自行确认操作内容并承担相应风险**，尤其在生产环境使用前请先备份、
或在测试机上验证。作者不对因使用本工具造成的任何数据丢失或系统异常负责。
