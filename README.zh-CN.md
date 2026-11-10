<p align="center"><img src="src/assets/icon.svg" width="96" alt="WSLBox 图标"></p>
<h1 align="center">WSLBox</h1>
<p align="center"><a href="README.md">English</a> · <strong>简体中文</strong></p>
<p align="center">把 Linux 文件夹放到 Windows 桌面。</p>
<p align="center"><a href="https://liunics.github.io/WSLBox/">软件介绍</a> · <a href="https://github.com/liunics/WSLBox/releases">下载</a> · <a href="https://github.com/liunics/WSLBox/issues">问题反馈</a></p>

WSLBox 是面向 WSL2 / WSLg 的 Windows 桌面文件格子。在透明窗口中浏览 Linux 文件夹，与 Windows 资源管理器拖放，并使用 Linux 应用打开文件。无需启动 LXDE 等完整 Linux 桌面。

![WSLBox 桌面格子](assets/window.png)

## 下载与运行

从 [Releases](https://github.com/liunics/WSLBox/releases) 下载 **`WSLBox-v版本号.exe`**，双击运行。程序已内嵌 Linux 后端和图标，不需要旁边放置 `backend.py`。`Source code (zip)` 是源码下载，用于查看或自行构建。

首次运行会读取本机 WSL 发行版及用户主目录。打开“文件夹设置”选择发行版和要展示的文件夹；不需要使用开发者的用户名或路径。

主要验证环境是 **Windows 11 x64 + Ubuntu WSL2**。ARM64、Windows Server 和其他发行版尚未完成相同覆盖的验证。

## 环境要求

- Windows 11，或支持 WSLg 的 Windows 10（微软要求 Build 19044+）。
- 已安装并可启动的 WSL2 发行版及可用的 WSLg。
- Linux 端的 `/usr/bin/python3`、GIO / GTK 3 和 D-Bus。
- 需要打开文件的 Linux 应用及相应文件关联，需在发行版内安装、配置。

Ubuntu / Debian 示例：

```bash
sudo apt update
sudo apt install python3 python3-gi gir1.2-gtk-3.0 dbus libglib2.0-bin libgdk-pixbuf2.0-bin
```

最后一个软件包用于缩略图生成；具体格式还可能需要对应的 `.thumbnailer`。若要通过 Linux 文件管理器查看并恢复回收站，可安装 PCManFM、Nautilus 等文件管理器。

[微软 WSLg 配置说明](https://learn.microsoft.com/windows/wsl/tutorials/gui-apps)

## 功能

- 多个独立格子，透明背景、图标/列表、排序、筛选、缩略图、锁定、贴边与收起。
- 双击 Windows 桌面空白处隐藏/显示格子，托盘可关闭此功能。
- Windows 与 WSL 之间拖放；复制/剪切/粘贴保留原生文件冲突与进度界面。
- Linux 默认打开与“Linux 打开方式”，支持明确设置默认应用。
- 原位重命名；普通删除使用 Linux 回收站，不支持回收时保留文件并报错。
- 读取与缩略图进程复用、重复读取合并、列表虚拟化、闲置读取进程释放。

## 界面语言

在“文件夹设置”或菜单的 **Language / 语言** 中选择“跟随系统”、简体中文或 English，保存后重新启动 WSLBox 生效。此选项对所有格子生效；不会修改 Linux 的语言、主题或图形协议。Windows 原生菜单和对话框使用 Windows 自身的语言，外部 Linux 应用使用它们自己的语言配置。

图标为重新绘制的文件夹、小企鹅、Windows 四格与向右箭头，表示从 Windows 桌面连接 Linux 文件。

## 常用操作

| 操作 | 方法 |
|---|---|
| 打开文件/进入目录 | 双击或 Enter |
| 在 Windows 资源管理器打开当前目录 | 双击格子内部空白处 |
| 复制当前 Linux 目录路径 | 顶栏复制按钮 |
| 返回绑定目录 | Alt+Home |
| 后退/前进 | 鼠标侧键或 Alt+←/→ |
| 刷新 | F5 或 Ctrl+R |
| 重命名 | F2，Enter 确认、Esc 取消 |
| 全选 | Ctrl+A |
| 新建文件夹 | Ctrl+Shift+N |
| 新建格子 | Ctrl+N |
| 移到 Linux 回收站 | Delete |
| 永久删除 | Shift+Delete，需确认 |
| 文件夹设置 | Ctrl+L |

拖入默认复制，按 Shift 移动。帮助从菜单打开，不占用 F1。设置保存在用户的 `%LOCALAPPDATA%\WSLBox\layout.json`，与 EXE 分开。

## 通用性与限制

可以把单个 EXE 拷给具备上述环境的人。它不包含 WSL、Linux 图形库或 Flair/gedit 等应用，也不包含你的格子配置。

- 隐藏文件暂不显示。
- 符号链接允许打开，拖出和剪切有保护限制；Windows 文件操作不能完整保留全部 Linux 权限、扩展属性、硬链接或特殊文件语义。
- 列表模式已虚拟化；图标模式的超大目录仍有优化空间。
- Windows 原生右键速度与安装的 Shell 扩展有关，第三方扩展兼容性可能不同。
- 格子是透明 WPF 窗口，尚未挂载到 Explorer 桌面层；未提供特殊 Win+D 行为和登录自启动。
- 当前发布 EXE 未做商业代码签名；请从本仓库 Release 获取，并可对照 SHA256SUMS.txt 核验文件。

<details>
<summary>可选排错：检查运行依赖</summary>

正常使用不需要运行这些命令。应用无法启动时，可在下载的 EXE 所在目录打开 **Windows PowerShell** 执行。程序会自动生成临时报告，不需要提前准备 JSON 文件；源码下载不包含编译后的 EXE。

```powershell
$report = Join-Path $env:TEMP "wslbox-check.json"
Start-Process ".\WSLBox-v1.0.0.exe" -ArgumentList @("--diagnose", "`"$report`"") -Wait
(Get-Content $report -Raw | ConvertFrom-Json).Checks | Format-Table Name, Ok, Required, Details -Wrap
```

这里仅显示检查表。检查通过只代表基础依赖可用，不保证每个 Linux 应用及输入法都已正确配置。

</details>

## Linux 输入法

WSLBox 跟随 Linux 中明确配置的 Fcitx、Fcitx5 或 IBus，复用已有的 WSLg 输入法服务，或按需启动所选服务。输入法是可选功能：缺少服务或启动失败不会阻止应用打开，可查看状态提示与环境诊断。应用保留原生 Wayland/X11 后端。

目标应用所需的 GTK、Qt 等输入法前端应在 Linux 中安装。更改 Linux 输入法选择后，重启 WSLBox 和目标应用。部分 Flair 版本会在内部禁用输入法，仍需单独处理该应用。

## 源码

程序源码位于 `src/`，可从 Releases 下载源码 ZIP；使用软件请下载 EXE。

[更新记录](CHANGELOG.zh-CN.md)

## 许可证

WSLBox 使用 [GNU GPL v3](LICENSE) 许可证。WSLBox 不是微软官方产品。
