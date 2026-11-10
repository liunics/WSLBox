# Changelog

**English** · [简体中文](CHANGELOG.zh-CN.md)

## 1.0.0

Initial public release.

- Display WSL2 folders in Windows desktop boxes and open files with the corresponding Linux system's default applications through WSLg.
- Support multiple independent boxes, transparent backgrounds, icon and list views, sorting, filtering, locking, edge snapping, and collapse/expand.
- Support maximizing and restoring windows, save box size, position, opacity and layout, and double-click empty Windows desktop space to hide or show boxes.
- Select a WSL distribution from a dropdown, choose the bound folder with the native folder picker, return to the home directory, and validate paths. Return to the bound folder after browsing subdirectories.
- Display Linux file icons, desktop launcher icons and thumbnails.
- Drag and drop, copy, cut and paste between Windows and WSL, with native file-operation progress and conflict dialogs.
- Rename in place, create folders, choose Linux applications with Open with, and explicitly set a Linux default application.
- Move normal deletions to Linux Trash. Permanent deletion requires confirmation; unsupported trash operations preserve files and show an error.
- Integrate the native Windows file context menu with a separate Linux actions section. Empty box space and the header share a menu, with Close box and Exit at the bottom.
- Provide file-operation and navigation shortcuts, mouse side-button back/forward, help, and copying the current Linux path. Help does not reserve F1.
- Use an isolated WSLg and D-Bus launch environment to avoid interference from xrdp session settings.
- Reuse app-launch, metadata and thumbnail workers; coalesce identical concurrent reads; virtualize lists; cache icons; and release idle workers.
- Support Simplified Chinese, English and the system language after restart, with English and Chinese READMEs.
- Provide a consistent app icon, About dialog and environment diagnostics, with a private runtime-directory fallback when a systemd user runtime is unavailable.
- Embed the Linux backend, translations and icon into a versioned, standalone EXE, with complete source code and a software introduction website.
- Add optional Linux input-method initialization for configured Fcitx, Fcitx5 and IBus. Reuse an existing WSLg service or start the selected service on demand, without installing packages or changing the user's selection.
- Pass input-method settings to newly launched apps and the private D-Bus activation environment while retaining xrdp isolation and native application display backends.
- Keep applications opening when no input method is configured, a selected daemon is missing, or startup fails; provide localized status hints and optional diagnostics.
- Cache initialization and service checks to avoid rescanning or starting processes for every file open.
- Some applications disable input methods internally and require application-specific handling.
- Use the WSLBox name throughout the application and save settings in the dedicated WSLBox directory.
- Add a project hyperlink and on-demand update checking to About, with modern TLS defaults and detailed connection errors.
- Keep UI language global, simplify folder settings, center update controls and present bilingual help as grouped shortcut tables.
- Snap nearby boxes to one another with aligned edges while preserving independent screen-edge snapping.
- Restore boxes below other applications and preserve the current foreground window; handle maximized boxes correctly.
- Clean up previews and timers before closing, suppress closing transitions and protect saved bounds against late callbacks.
- Build and verify releases in GitHub Actions, publish versioned executables and checksums, and exclude website files from source archives.
