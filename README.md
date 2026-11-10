<p align="center"><img src="src/assets/icon.svg" width="96" alt="WSLBox icon"></p>
<h1 align="center">WSLBox</h1>
<p align="center"><strong>English</strong> · <a href="README.zh-CN.md">简体中文</a></p>
<p align="center">Linux folders on your Windows desktop.</p>
<p align="center"><a href="https://liunics.github.io/WSLBox/">Website</a> · <a href="https://github.com/liunics/WSLBox/releases">Download</a> · <a href="https://github.com/liunics/WSLBox/issues">Report an issue</a></p>

WSLBox displays WSL2 folders in transparent Windows desktop boxes. Browse Linux files, drag and drop them to and from Windows Explorer, and open them with Linux applications through WSLg. A full Linux desktop is not required.

![WSLBox desktop box](assets/window-en.png)

## Download and run

Download **`WSLBox-v<version>.exe`** from [Releases](https://github.com/liunics/WSLBox/releases) and run it. The Linux backend, language catalog, and icon are embedded; no adjacent `backend.py` is needed. `Source code (zip)` is for inspecting or building the source.

On first launch, WSLBox discovers installed distributions and the Linux home directory. Use **Folder settings** to choose a distribution and folder. No developer-specific username or path is required.

Primarily tested on **Windows 11 x64 with Ubuntu WSL2**. ARM64, Windows Server, and other distributions have not received the same validation.

## Requirements

- Windows 11, or a Windows 10 version supporting WSLg (Microsoft specifies build 19044 or later).
- A working WSL2 distribution and WSLg installation.
- `/usr/bin/python3`, GIO / GTK 3, and D-Bus inside Linux.
- Your Linux applications and their file associations installed in that distribution.

Ubuntu / Debian example:

```bash
sudo apt update
sudo apt install python3 python3-gi gir1.2-gtk-3.0 dbus libglib2.0-bin libgdk-pixbuf2.0-bin
```

The last package provides thumbnail generation; some formats also need a corresponding `.thumbnailer`. A Linux file manager such as PCManFM or Nautilus can browse and restore the Linux trash.

[Microsoft WSLg setup instructions](https://learn.microsoft.com/windows/wsl/tutorials/gui-apps)

## Features

- Multiple independent boxes with transparency, icon/list views, sorting, filtering, thumbnails, locking, edge snapping, and collapse/expand.
- Double-click an empty area of the Windows desktop to hide/show boxes; disable this from the tray menu if preferred.
- Windows/WSL drag and drop, with native Windows conflict and progress dialogs for copy, cut, and paste.
- Linux default applications and **Linux Open with**, including an explicit option to change the Linux default.
- Inline rename and Linux trash. If trash is unsupported, the file is preserved and an error is shown.
- Reused metadata/thumbnail workers, coalesced concurrent reads, virtualized lists, and idle read-worker cleanup.

## Language and icon

Choose **System**, **简体中文**, or **English** in **Folder settings** or **Language / 语言** in the menu. The preference applies to all boxes after restarting WSLBox. It does not change Linux locale, theme, or display protocol. Native Windows menus/dialogs follow Windows; external Linux applications retain their own language settings.

The original vector icon combines a folder, a small penguin, four Windows-style panes, and a right arrow to represent connecting Windows to Linux files.

## Common actions

| Action | Shortcut or gesture |
|---|---|
| Open a file / enter a directory | Double-click or Enter |
| Open the current directory in Windows Explorer | Double-click empty space inside a box |
| Copy the current Linux directory path | Header copy button |
| Return to the configured root | Alt+Home |
| Back / forward | Mouse side buttons or Alt+Left / Right |
| Refresh | F5 or Ctrl+R |
| Rename | F2; Enter to confirm, Esc to cancel |
| Select all | Ctrl+A |
| New folder | Ctrl+Shift+N |
| New box | Ctrl+N |
| Move to Linux trash | Delete |
| Permanently delete | Shift+Delete, with confirmation |
| Folder settings | Ctrl+L |

Dragging in copies by default; hold Shift to move. Help is available from the menu and does not reserve F1. Settings are stored separately from the executable at `%LOCALAPPDATA%\WSLBox\layout.json`.

## Portability and limitations

You can share the single EXE with others who have the required environment. It does not bundle WSL, a Python interpreter, Linux graphical libraries, Flair/gedit, or your box configuration.

- Hidden files are currently excluded.
- Symlinks can be opened; drag-out and cut have protective restrictions. Windows file operations cannot fully preserve every Linux permission, extended attribute, hard link, or special-file property.
- List mode is virtualized; icon mode still has room for improvement with very large directories.
- Native context-menu speed and compatibility depend on installed Windows Shell extensions.
- Boxes are transparent WPF windows, not embedded into Explorer's desktop layer. Special Win+D handling and login autostart are not provided.
- Releases are not commercially code-signed. Download from this repository and compare the executable against `SHA256SUMS.txt` if desired.

<details>
<summary>Optional troubleshooting: check runtime dependencies</summary>

This is not required for normal use. If an application cannot start, run the commands below in **Windows PowerShell**, from the folder containing the downloaded EXE. The EXE creates the temporary report; no pre-existing JSON file is required. Source downloads do not contain the compiled EXE.

```powershell
$report = Join-Path $env:TEMP "wslbox-check.json"
Start-Process ".\WSLBox-v1.0.0.exe" -ArgumentList @("--diagnose", "`"$report`"") -Wait
(Get-Content $report -Raw | ConvertFrom-Json).Checks | Format-Table Name, Ok, Required, Details -Wrap
```

Only the check table is displayed. Passing these checks confirms basic dependencies; it does not guarantee every Linux application or input method is configured correctly.

</details>

## Linux input methods

WSLBox follows an explicitly configured Fcitx, Fcitx5 or IBus selection, reuses an existing WSLg service, or starts the selected service on demand. Input methods are optional: a missing service or startup failure does not prevent an application from opening; a status hint and diagnostics are available. Applications retain their native Wayland/X11 backend.

Install the input-method frontend required by each target toolkit inside Linux. Restart WSLBox and the affected app after changing the Linux input-method selection. Apps that explicitly disable input methods internally, such as some Flair versions, need separate application-level handling.

## Source code

Application source is in `src/`. The source ZIP is available from Releases; to use the software, download the EXE.

[Changelog](CHANGELOG.md)

## License

WSLBox is licensed under the [GNU GPL v3](LICENSE). WSLBox is not an official Microsoft product.
