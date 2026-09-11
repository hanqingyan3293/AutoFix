# 第三方来源与许可

本项目在开发过程中参考了以下开源项目。**未复制其代码实现**，仅借鉴数据与流程设计；
项目内也不包含任何第三方二进制文件。

---

## 1. autodesk-complete-uninstaller

- 仓库：<https://github.com/bequiet11/autodesk-complete-uninstaller>
- 许可：MIT License，Copyright (c) 2026 bequiet11
- 借鉴内容：卸载流程的阶段划分（还原点 → 停进程/服务 → 多轮卸载 → 强制清除顽固项 → 共享组件
  → 删除目录 → 重试锁定目录 → 快捷方式 → 缓存 → 服务/任务/防火墙 → 注册表 → Genuine Service），
  以及进程名、服务名、HKCU 命名类键、IFEO 检查目标、目录与注册表分支清单；
  残留扫描的 15 个维度划分；17 项验证与系统审计的检查项；错误 103 的 10 项诊断；
  重启挂起的 5 类标记。

```
MIT License

Copyright (c) 2026 bequiet11

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 2. ExtrabbitCode.AdskLicensingModifier

- 仓库：<https://github.com/ExtrabbitCode/AdskLicensingModifier>
- 许可：MIT License
- 借鉴内容：`AdskLicensingInstHelper.exe` 的命令行参数用法（`change --prod_key … --prod_ver …
  --lic_method …`），以及**产品名称与产品密钥对照表**。

### 包含的数据文件

`AutoFixTool/Assets/AutodeskProducts.txt`（2020–2027，共 1073 条产品名称与密钥对照）来自该项目，
按 MIT 要求随附版权与许可说明，详见
[AutoFixTool/Assets/README-来源与许可.txt](AutoFixTool/Assets/README-来源与许可.txt)。

```
MIT License

Copyright (c) ExtrabbitCode

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 未包含的第三方组件

以下内容**不在本仓库内**，程序运行时会调用使用者本机已安装的版本：

| 组件 | 用途 | 说明 |
| --- | --- | --- |
| ODA File Converter | CAD 文件版本降级 | 需使用者自行安装，程序不下载 |
| AutoCAD | CAD 文件瘦身 | 需使用者本机已安装 |
| Autodesk Licensing (`AdskLicensingInstHelper.exe`) | 许可方式切换 | Autodesk 官方组件 |
| Windows 卷缓存清理引擎 (`IEmptyVolumeCache`) | 磁盘清理 | Windows 自带 COM 接口 |

## 关于 Autodesk

本项目是独立的第三方工具，与 Autodesk, Inc. 无隶属、赞助或背书关系。
"Autodesk"、"AutoCAD"、"Revit"、"3ds Max"、"Maya"、"Inventor"、"Navisworks" 等
为其各自权利人的商标，本项目仅出于兼容性说明目的进行指称性使用。

