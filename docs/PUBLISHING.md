# 发布清单（维护者）

按顺序执行。方括号内是需要你确认或替换的内容。

## 1. 替换占位符

仓库创建后，把 `OWNER/REPO` 替换为实际的 `用户名/仓库名`：

```
rg -n 'OWNER/REPO' .
```

应命中的文件：

- `CHANGELOG.md`（版本链接）
- `.github/ISSUE_TEMPLATE/config.yml`（安全报告链接）

## 2. 确认仓库内容干净

维护者应保有一份**私有的敏感词表**（不入库），用于发布前自查仓库中是否残留
非本项目的产品名称、内部文档标题或来源标记：

```
rg -i -f <私有词表文件> .
```

应无命中。

同时确认仓库中没有二进制或本地工作文件：

```
git ls-files | rg '\.exe$|\.dll$|\.zip$|\.7z$'
```

应无输出。（本机专有的工作目录通过 `.git/info/exclude` 排除，该文件不会被提交。）

## 3. 构建与验证

```
dotnet build AutoFixTool/AutoFix.csproj       -c Release
dotnet build AutoFixTool/AutoFix.net40.csproj -c Release
```

两个目标框架均应 **0 警告 0 错误**。

## 4. 计算校验值

```
certutil -hashfile AutoFixTool/bin/Release/net472/AutoFix.exe  SHA256
certutil -hashfile AutoFixTool/bin/Release/net40/AutoFix.exe   SHA256
```

把结果填入 `docs/RELEASE-NOTES-v1.0.0.md` 的「校验下载」一节。

## 5. 首次推送

```
git add -A
git commit -m "文档: 准备首次发布"
git branch -M main
git remote add origin https://github.com/OWNER/REPO.git
git push -u origin main
```

> 若使用个人访问令牌推送，请用 `git credential` 或环境变量方式提供，
> **不要**把令牌写进仓库文件、`.git/config` 的 URL 或提交历史。

## 6. 打标签并发 Release

```
git tag -a v1.0.0 -m "Autodesk Fix Toolbox v1.0.0"
git push origin v1.0.0
```

然后在 GitHub 上新建 Release：

- 选择 tag `v1.0.0`
- 标题：`Autodesk Fix Toolbox v1.0.0`
- 正文：粘贴 `docs/RELEASE-NOTES-v1.0.0.md` 的内容
- 上传两个 exe 作为 Assets，建议重命名以区分版本：
  - `AutoFix-net472-v1.0.0.exe`
  - `AutoFix-net40-v1.0.0.exe`

Excel/记事本都可能改动换行，建议直接复制 Markdown 原文。

## 7. 仓库设置

按 [GITHUB-ABOUT.md](GITHUB-ABOUT.md) 填写 Description、Topics、Social preview，
并开启 Private vulnerability reporting。

## 8. 发布后建议

- [ ] 在 Release 说明中说明「尚未在真实机器上完整验证，建议先测试」
- [ ] 确认 CI 通过（`.github/workflows/build.yml`）
- [ ] 若发现文档与实际行为不符，优先修文档
- [ ] 收到安全报告时按 [SECURITY.md](../SECURITY.md) 处理

## 关于截图

仓库当前**没有**界面截图（未在图形环境中运行过）。
建议在有 Windows 环境的机器上，以管理员身份运行程序，截取：

1. 主界面（左侧分类导航 + 功能网格 + 状态栏）
2. 预演模式下的日志输出
3. 某个确认框（体现「列出具体动作」与默认取消）
4. 检测结果页

放到 `docs/images/` 并在 README 中引用。截图时注意不要暴露个人信息（用户名、机器名、路径中的姓名）。
