# 参与贡献

感谢你有兴趣改进 Autodesk Fix Toolbox。本文档说明开发流程，以及本项目**特别重要**的安全约定。

## 开发环境

- Windows 10 / 11
- .NET SDK 8.0 或更高（用于构建 net472 / net40 两个目标框架）

```
dotnet build src/Autodesk-Fix.csproj      -c Release
dotnet build src/Autodesk-Fix.net40.csproj -c Release
```

构建应保持 **0 警告 0 错误**。

## 项目结构

```
src/
  MainForm.cs                     主窗体：分类导航 + 功能网格 + 日志
  RepairService*.cs               修复与清理逻辑（按主题拆分为多个 partial 文件）
  *Form.cs                        各功能窗口（结果页、卸载工作台、许可配置等）
  Assets/                         内嵌资源（产品密钥数据表）
docs/                             文档与维护者清单
```

`RepairService` 是 `partial class`，按主题分文件：

| 文件 | 内容 |
| --- | --- |
| `RepairService.cs` | 核心修复项、通用辅助（含预演拦截） |
| `RepairService.More.cs` | 安装错误修复（续） |
| `RepairService.Extended*.cs` | 扩展修复项、卷缓存、CAD 瘦身、还原点 |
| `RepairService.Detection.cs` | 只读环境检测 |
| `RepairService.Verify.cs` | 10 项清理后校验 |
| `RepairService.Uninstall*.cs` | 产品卸载与深度清理 |
| `RepairService.Cleanup.cs` | 深度清理的补充阶段 |
| `RepairService.Workbench.cs` | 12 项工作台其余功能、17 项验证、系统审计 |
| `RepairService.License.cs` | 许可配置 |
| `RepairService.DryRun.cs` | 预演模式与受控写入包装 |

## 安全约定（重要）

本工具以管理员权限运行并执行破坏性操作。新增或修改代码时必须遵守以下规则：

### 1. 所有破坏性操作必须受预演模式控制

不要直接调用 `File.Delete`、`RegistryKey.SetValue`、`ServiceController.Stop` 等。
使用已有的受控包装：

| 操作 | 使用 |
| --- | --- |
| 写注册表值 | `RegWriteValue` |
| 删注册表值 | `RegDeleteValue` |
| 删注册表键 | `RegDeleteTree` / `DeleteRegistryKey` |
| 建目录 | `FsCreateDirectory` |
| 删目录 | `FsDeleteDirectory` / `DeleteDirectory` |
| 删文件 | `FsDeleteFile` / `DeleteFileSafe` |
| 写文件 | `FsWriteText` / `FsWriteLines` |
| 复制文件 | `FsCopy` |

这些包装在预演模式下只记录、不执行。若确有必须绕过的情况，请在 PR 说明里给出理由。

### 2. 只读模块必须保持只读

`RepairService.Detection.cs`、`RepairService.Verify.cs`、`ResidueScan*.cs` 属于只读模块。
**不要在其中加入任何写入、创建、删除、停止服务或结束进程的调用。**
提交前可自查：

```
rg -n 'SetValue|DeleteValue|DeleteSubKey|CreateSubKey|WriteAll|Directory\.Delete|File\.Delete|SetAccessControl|Kill\(' src/RepairService.Detection.cs src/ResidueScan.cs
```

应当无输出。

### 3. 破坏性操作必须有确认

新增危险功能时，在 `FixItem.Warn` 中列出**具体动作**（确切的服务名、路径、注册表键），
而不是笼统的「确认执行吗」。默认按钮必须落在「取消」。

### 4. 影响范围可能超出 Autodesk 的操作要单独提示

若某操作会删除多厂商共用的组件或用户数据，应加入 `RiskyTargets.cs` 清单，
默认保留，由用户显式勾选才执行。

### 5. 修改前先备份

修改注册表分支前调用 `BackupRegistryBranch` 导出 `.reg`；覆盖用户文件前先复制 `.bak`。

## 新增一个修复项

1. 在 `RepairService.*.cs` 中实现方法，签名为 `static string Xxx(Action<string> log)`
2. 返回中文结果说明；失败路径的文案需包含「失败」或「异常」（界面据此判定为失败）
3. 所有破坏性操作走受控包装（见上）
4. 在 `MainForm.BuildCatalog()` 中把 `FixItem` 加入合适的分类，填写 `Desc` 与 `Warn`
5. 更新 `src/README.md` 的功能清单与项数
6. 在 `CHANGELOG.md` 的未发布段落中记一笔

## 提交与 PR

- 一个 PR 聚焦一件事，避免混杂无关改动
- 提交信息建议使用：`新增: …` / `修复: …` / `文档: …` / `重构: …`
- PR 描述请说明：改了什么、为什么、如何验证、是否影响既有行为
- 若改动了破坏性操作，请在描述中明确列出确认流程

## 报告问题

用 GitHub Issues 的模板提交。涉及安全问题时请改用 [SECURITY.md](SECURITY.md) 中的渠道。
