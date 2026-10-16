# Markdown 编辑器

一个开箱即用的 **Windows 桌面 Markdown 编辑器**：双击 `.md` 直接打开，左边写源码、右边所见即所得，截图 `Ctrl+V` 直接内嵌进文档。

单文件 exe，约 1 MB，不依赖 Node / 浏览器 / 网络。

![platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![license](https://img.shields.io/badge/license-MIT-green)

---

## 功能

- **双栏布局**：左侧 Markdown 源码，右侧所见即所得文档区；左侧面板可一键收起，收起后文档区全宽。
- **右侧直接编辑**：在右侧文档里选中文字，点工具栏即可加粗、斜体、下划线、删除线、高亮、改字号、设标题、列表、引用、表格——不用记语法。
- **截图内嵌**：`Ctrl+V` 粘贴截图、或把图片拖进窗口，图片会自动压缩并以 **base64 内嵌进 `.md` 文件**。图片数据存在文件内部，换电脑、发给别人都不会丢图。
- **离线渲染**：自研 Markdown 渲染引擎，标题 / 列表 / 表格 / 代码 / 引用 / 任务列表 / 高亮全部本地渲染，不联网。
- **文件关联**：一键把本程序注册为 `.md` 的打开方式，之后双击任意 `.md` 就用它打开。
- **编辑体验**：撤销 / 重做、字数与行数统计、草稿自动保存、深色 / 浅色主题、分栏拖动、滚动同步。
- **复制即带格式**：从右侧文档区 `Ctrl+C`，粘到 Word、语雀、飞书、WPS 会保留加粗、字号、列表、表格等格式。

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11（64 位） |
| .NET Framework | 4.8.1（Windows 10 1903+ 与 Windows 11 已自带） |
| WebView2 Runtime | Windows 11 与较新版 Windows 10 已自带；缺失时从微软官网安装一次即可 |

## 下载与使用

1. 在 **Releases** 页面下载 `MarkdownEditor.exe`。
2. 双击运行，直接开始写；或把 `.md` 文件拖进窗口。
3. **设为 .md 的默认打开方式**：点工具栏右侧的「关联 .md」按钮，系统会弹出「打开方式」窗口 ——
   - 在列表中选择 **Markdown 编辑器**（若没看到，点「更多应用」→ 拉到底「在这台电脑上查找其他应用」，选择 `MarkdownEditor.exe`）；
   - 勾选 **「始终使用此应用打开 .md 文件」**；
   - 点「确定」。以后双击任意 `.md` 就由本程序打开。

## 常用快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl + V` | 粘贴截图 / 图片，自动内嵌 |
| `Ctrl + B` / `I` / `U` | 加粗 / 斜体 / 下划线 |
| `Ctrl + K` | 插入链接 |
| `Ctrl + Z` / `Ctrl + Y` | 撤销 / 重做 |
| `Ctrl + S` | 保存（首次会弹出选择保存位置） |
| `Ctrl + O` / `Ctrl + N` | 打开文件 / 新建文档 |
| `Ctrl + 1` / `2` / `3` | 一级 / 二级 / 三级标题 |
| `Tab` / `Shift + Tab` | 缩进 / 取消缩进 |

## 从源码构建

需要 Windows 与 .NET Framework 4.x（系统自带 `csc.exe`，无需安装 Visual Studio）。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

脚本会自动从 NuGet 下载 WebView2 SDK，并用内嵌资源编译出单文件 exe：`dist\MarkdownEditor.exe`。

## Markdown 兼容性

本编辑器使用标准的 **CommonMark + GFM（GitHub Flavored Markdown）** 语法，与 GitHub、GitLab、语雀、Typora、Obsidian、VS Code 一致：标题、加粗、斜体、删除线、引用、有序 / 无序列表、表格、代码块、任务列表、链接。

有三项是扩展能力，在 GitHub 网页渲染时不可用：

| 特性 | 本地编辑器（本程序 / Typora / Obsidian / VS Code） | GitHub 网页渲染 |
| --- | --- | --- |
| 标题、加粗、列表、表格、代码、引用 | ✅ | ✅ |
| 自定义字号 `<span style="font-size:..px">` | ✅ | ❌ style 被安全策略过滤，字号丢失、文字保留 |
| 内嵌图片 `data:image/png;base64,…` | ✅ | ❌ 不支持 `data:` URI，图片不显示 |
| `==高亮==` | ✅ | ❌ 扩展语法，按普通文字显示 |

也就是说：**任何 Markdown 编辑器都能正常打开这些文件**；上传到 GitHub 后正文正常显示，只有上述三项扩展会降级。

## 项目结构

```
build.ps1            一键构建脚本（下载 WebView2 SDK + 编译）
src/Host.cs          C# 宿主：WinForms 窗口 + WebView2 + 文件读写 + 文件关联
src/ui/index.html    编辑器界面：Markdown 渲染引擎、工具栏、所见即所得、图片内嵌
```

## 实现说明

- 宿主使用 .NET Framework 4.8.1 + WinForms + WebView2，用系统自带 `csc.exe` 直接编译成 **单文件 exe**。
- WebView2 的两个托管程序集与原生加载器以**嵌入资源**打包进 exe，运行时释放到 `%LOCALAPPDATA%\MarkdownEditor` 并解析加载，因此发布只需一个 exe。
- 界面是纯 HTML/CSS/JS，通过 `SetVirtualHostNameToFolderMapping` 挂到虚拟域名 `https://md.local/` 下运行，JS 与 C# 之间用 `postMessage` 的 JSON 协议通信。
- 文件关联只写 `HKCU\Software\Classes`，无需管理员权限，不修改系统默认项之外的任何注册表。

## 许可证

[MIT](LICENSE) © LZjun
