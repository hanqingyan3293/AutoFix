# GitHub 仓库设置建议

创建仓库后，在其 **Settings / About** 处填入以下内容。

## 仓库名称（建议）

```
autodesk-fix-toolbox
```

## Description（简介，限 350 字符以内）

推荐（中文，约 80 字）：

```
Windows 上的 Autodesk 产品安装故障修复与残留清理工具箱。免费、完全离线、无需登录：安装错误修复、残留检测、产品卸载清理、许可切换、磁盘清理，带预演模式与逐项确认。
```

英文备选：

```
An offline Windows toolbox for repairing and cleaning up Autodesk product installations. Free, no login: install-error fixes, remnant scanning, product uninstall, license switching, disk cleanup - with dry-run mode and per-action confirmation.
```

## Website

留空，或填项目文档页（如有）。

## Topics（标签）

逐个添加：

```
autodesk
autocad
revit
3ds-max
maya
inventor
uninstaller
cleanup-tool
windows
winforms
dotnet-framework
csharp
repair-tool
troubleshooting
offline
agpl
```

## Social preview

建议准备一张 1280×640 的预览图。可直接使用仓库内的图标资源：

- `docs/images/icon/AutoFix_512.png` 或 `AutoFix_1024.png`（白底圆角 + 黑色 F 标记）
- 叠加程序主界面截图（左侧分类导航 + 功能网格 + 日志区）与标题文字

注意：**不要使用 Autodesk 的商标图形或官方 Logo**，文字标注「Autodesk Fix Toolbox」即可。

## Releases

发布 v1.0.0 时：

- Tag：`v1.0.0`
- Release title：`Autodesk Fix Toolbox v1.0.0`
- 说明正文：直接使用 [RELEASE-NOTES-v1.0.0.md](RELEASE-NOTES-v1.0.0.md)
- 上传资源：两个 exe（建议按版本重命名，例如 `AutoFix-net472-v1.0.0.exe`、`AutoFix-net40-v1.0.0.exe`）

建议勾选：`Set as the latest release`。

## 其他建议设置

| 位置 | 建议 |
| --- | --- |
| Settings / General | 关闭 `Wikis`（文档放在仓库内） |
| Settings / General | 按需开启 `Discussions` |
| Settings / Security | 开启 `Private vulnerability reporting`（对应 SECURITY.md） |
| Settings / Security | 开启 `Dependabot alerts`（虽无第三方依赖，仍有 GitHub Actions 版本更新提醒） |
| Settings / Branches | 为默认分支添加保护规则，要求 CI 通过 |
