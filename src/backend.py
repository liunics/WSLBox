"""Small WSL helper for the Windows file box. No dependency on the GTK UI."""

import json
import os
import sys
import base64
import hashlib

ICON_SIZE = 64

if "WSLBOX_TRANSLATIONS" not in globals():
    from pathlib import Path as _LocalePath
    WSLBOX_TRANSLATIONS = json.loads((_LocalePath(__file__).parent / "locales" / "strings.json").read_text(encoding="utf-8-sig"))

def tr(key):
    language = "en-US" if os.environ.get("WSLBOX_LANGUAGE", "zh-CN").startswith("en") else "zh-CN"
    value = WSLBOX_TRANSLATIONS.get(key, {})
    return value.get(language, value.get("en-US", key))


# Source runs import the helper; Windows builds concatenate it ahead of this
# file so the portable app still needs only one backend.py.
if "InputMethodSession" not in globals():
    from input_methods import InputMethodSession

if "ThumbnailCache" not in globals():
    from thumbnails import ThumbnailCache


# A distro-wide user bus can inherit xrdp's DISPLAY and redirect activation.
# Keep one WSLg-only bus across helper restarts so application instances survive
# closing the Windows box and can be reused on the next launch.
_wslg_session_ready = False
_input_method_session = None


def wslg_environment(source):
    env = dict(source)
    for key in ("GDK_BACKEND", "QT_QPA_PLATFORM", "WAYLAND_SOCKET", "XAUTHORITY",
                "DBUS_SESSION_BUS_ADDRESS", "DBUS_STARTER_ADDRESS", "DBUS_STARTER_BUS_TYPE",
                "SESSION_MANAGER", "DESKTOP_AUTOSTART_ID", "XDG_SESSION_ID",
                "XDG_SESSION_PATH", "XDG_SEAT_PATH", "XDG_SESSION_DESKTOP",
                "XDG_CURRENT_DESKTOP", "GNOME_KEYRING_CONTROL"):
        env.pop(key, None)
    env["DISPLAY"] = ":0"
    env["WAYLAND_DISPLAY"] = "/mnt/wslg/runtime-dir/wayland-0"
    env["XDG_RUNTIME_DIR"] = "/run/user/" + str(os.getuid()) + "/wslbox-session-v2/runtime"
    env["XDG_SESSION_TYPE"] = "wayland"
    return env


def private_bus(env, directory):
    """Create/reuse a private session bus without touching the user's bus.

    dbus-daemon's direct Exec activation uses this environment. In particular,
    do not enable systemd activation, whose user manager is shared with xrdp.
    The bus lives until WSL shuts down; leaving WSLBox must not close GUI apps.
    """
    import fcntl
    import stat
    import subprocess
    def secure_directory(path):
        os.makedirs(path, mode=0o700, exist_ok=True)
        info = os.lstat(path)
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise RuntimeError(tr("backend.incorrect_permissions_on_the_wslbox_session_directory"))
    secure_directory(directory)
    # Portals, accessibility and keyring services must not reuse xrdp's
    # runtime sockets. Wayland uses an absolute WSLg path, so it still works.
    env["XDG_RUNTIME_DIR"] = os.path.join(directory, "runtime")
    secure_directory(env["XDG_RUNTIME_DIR"])
    address = "unix:path=" + os.path.join(directory, "bus")
    lock = os.open(os.path.join(directory, "lock"), os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(lock, "w") as locked:
        fcntl.flock(locked, fcntl.LOCK_EX)
        def alive():
            try:
                result = subprocess.run([
                    "/usr/bin/dbus-send", "--bus=" + address,
                    "--dest=org.freedesktop.DBus", "--type=method_call",
                    "--print-reply", "--reply-timeout=1500", "/org/freedesktop/DBus",
                    "org.freedesktop.DBus.GetId"],
                    env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=3)
                return result.returncode == 0
            except (OSError, subprocess.TimeoutExpired):
                return False
        if not alive():
            socket_path = os.path.join(directory, "bus")
            if os.path.lexists(socket_path):
                info = os.lstat(socket_path)
                if not stat.S_ISSOCK(info.st_mode) or info.st_uid != os.getuid():
                    raise RuntimeError(tr("backend.invalid_wslbox_session_socket"))
                # Never unlink a live bus merely because an RPC was slow.
                import socket
                with socket.socket(socket.AF_UNIX) as probe:
                    probe.settimeout(1)
                    try:
                        probe.connect(socket_path)
                    except (ConnectionRefusedError, FileNotFoundError):
                        os.unlink(socket_path)
                    else:
                        raise RuntimeError(tr("backend.the_wslbox_session_is_temporarily_unresponsive_try_again_shortly"))
            try:
                result = subprocess.run([
                    "/usr/bin/dbus-daemon", "--session", "--fork", "--nopidfile",
                    "--address=" + address, "--print-address=1"],
                    env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=8)
            except (OSError, subprocess.TimeoutExpired) as exc:
                raise RuntimeError(tr("backend.could_not_start_the_private_wslbox_d_bus_session_install_dbus")) from exc
            if result.returncode != 0 or not alive():
                raise RuntimeError(tr("backend.could_not_start_the_private_wslbox_d_bus_session") + result.stderr.decode(errors="replace").strip())
    return address


def session_runtime(env):
    """Use a private fallback when a distro has no systemd user runtime."""
    import stat
    import tempfile
    standard = "/run/user/" + str(os.getuid())
    try:
        info = os.stat(standard)
        if stat.S_ISDIR(info.st_mode) and info.st_uid == os.getuid() and os.access(standard, os.W_OK):
            return env["XDG_RUNTIME_DIR"]
    except OSError:
        pass
    base = os.path.join(tempfile.gettempdir(), "wslbox-runtime-" + str(os.getuid()))
    os.makedirs(base, mode=0o700, exist_ok=True)
    info = os.lstat(base)
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise RuntimeError(tr("backend.incorrect_permissions_on_the_private_wslbox_runtime_directory"))
    return os.path.join(base, "session-v2", "runtime")


def ensure_wslg_session():
    global _wslg_session_ready, _input_method_session
    if _wslg_session_ready:
        if _input_method_session is not None:
            _input_method_session.ensure(dict(os.environ), os.environ['XDG_RUNTIME_DIR'])
        return
    env = wslg_environment(os.environ)
    if not os.path.exists("/mnt/wslg/.X11-unix/X0"):
        raise RuntimeError(tr("backend.wslg_display_services_were_not_found_enable_wslg_in_windows"))
    _input_method_session = InputMethodSession(env)
    env = _input_method_session.environment(env)
    runtime = session_runtime(env)
    env["XDG_RUNTIME_DIR"] = runtime
    env["DBUS_SESSION_BUS_ADDRESS"] = private_bus(env, os.path.dirname(runtime))
    _input_method_session.update_activation(env)
    _input_method_session.ensure(env, runtime)
    # Set before importing GIO: its default session-bus connection is cached.
    os.environ.clear()
    os.environ.update(env)
    _wslg_session_ready = True


def desktop_entry(path, glib):
    keyfile = glib.KeyFile()
    keyfile.load_from_file(path, glib.KeyFileFlags.NONE)
    group = "Desktop Entry"
    keys = set(keyfile.get_keys(group)[0])
    result = {key: keyfile.get_string(group, key) if key in keys else None
              for key in ("Type", "Icon", "URL")}
    if "Name" in keys:
        result["Name"] = keyfile.get_locale_string(group, "Name", None)
    return result


def open_target(path, gio, glib):
    """Resolve local .desktop links, preserving application-launch semantics."""
    seen = set()
    while path.endswith(".desktop"):
        identity = os.path.realpath(path)
        if identity in seen or len(seen) >= 16:
            raise ValueError(tr("backend.shortcut_links_form_a_cycle_and_cannot_be_opened"))
        seen.add(identity)
        entry = desktop_entry(path, glib)
        if entry["Type"] == "Application":
            handler = gio.DesktopAppInfo.new_from_filename(path)
            if handler is None:
                raise ValueError(tr("backend.could_not_read_this_application_shortcut"))
            return handler, None
        if entry["Type"] != "Link" or not entry["URL"]:
            raise ValueError(tr("backend.the_shortcut_needs_a_valid_type_and_url"))
        url = entry["URL"]
        from urllib.parse import urlsplit
        if urlsplit(url).scheme:
            target = gio.File.new_for_uri(url)
            local = target.get_path()
            if local is None:
                return None, url
            path = local
        else:
            path = os.path.normpath(os.path.join(os.path.dirname(path), url))
    target = gio.File.new_for_path(path)
    info = target.query_info("standard::content-type", gio.FileQueryInfoFlags.NONE, None)
    content_type = info.get_content_type()
    handler = gio.AppInfo.get_default_for_type(content_type, False) if content_type else None
    return handler, target.get_uri()


class LinuxIcons:
    """Render Linux GIcons once per distinct icon, not once per file."""
    def __init__(self, thumbnails=True):
        self.images = {}
        self.cache = {}
        self.gio = self.gtk = self.pixbuf = self.glib = None
        self.thumbnails = thumbnails
        self.thumbnail_cache = None
        try:
            # Match the X11 GTK settings and the user's LXDE icon theme.
            # WSLg's Wayland settings can otherwise choose a different theme.
            if os.environ.get("DISPLAY"):
                os.environ.setdefault("GDK_BACKEND", "x11")
            import gi
            gi.require_version("Gtk", "3.0")
            gi.require_version("GdkPixbuf", "2.0")
            from gi.repository import Gio, Gtk, GdkPixbuf, GLib
            self.gio, self.pixbuf, self.glib = Gio, GdkPixbuf, GLib
            if Gtk.init_check([])[0]:
                self.gtk = Gtk
            if thumbnails:
                self.thumbnail_cache = ThumbnailCache(Gio, GdkPixbuf)
        except (ImportError, ValueError):
            pass

    def describe(self, path):
        if self.gio is None:
            return {}
        try:
            info = self.gio.File.new_for_path(path).query_info(
                "standard::icon,standard::content-type,standard::display-name", 0, None)
            icon = info.get_icon()
            name = info.get_display_name()
            link = os.path.islink(path)
            if path.endswith(".desktop"):
                entry = desktop_entry(path, self.glib)
                name = entry.get("Name")
                if entry["Icon"]:
                    icon = (self.gio.FileIcon.new(self.gio.File.new_for_path(entry["Icon"]))
                            if os.path.isabs(entry["Icon"]) else self.gio.ThemedIcon.new(entry["Icon"]))
                link = link or entry["Type"] == "Link"
            if os.path.isdir(path) and os.path.ismount(path) and path != "/":
                try:
                    icon = self.gio.File.new_for_path(path).find_enclosing_mount(None).get_icon() or icon
                except Exception:
                    pass
            if link:
                emblem = self.gio.Emblem.new(self.gio.ThemedIcon.new("emblem-symbolic-link"))
                icon = self.gio.EmblemedIcon.new(icon, emblem)
            mime = info.get_content_type() or ""
            key, pending = None, False
            cache = self.thumbnail_cache
            if cache is not None and not os.path.isdir(path) and not path.endswith(".desktop"):
                pix = cache.cached(path)
                if pix is not None:
                    key = self.encode_pixbuf(pix, "thumb", link)
                if key is None:
                    pending = cache.eligible(path, mime)
            return {"ContentType": mime, "IconKey": key or self.render(icon),
                    "DisplayName": name, "CanThumbnail": pending}
        except Exception:
            return {}

    def render(self, icon):
        if icon is None:
            return None
        identity = icon.to_string()
        if identity in self.cache:
            return self.cache[identity]
        key = None
        try:
            pix = None
            if self.gtk is not None:
                theme = self.gtk.IconTheme.get_default()
                found = None
                if isinstance(icon, self.gio.ThemedIcon):
                    # Prefer the exact MIME icon in inherited themes before
                    # accepting a generic icon from the current theme.
                    for name in icon.get_names():
                        found = theme.lookup_icon(name, ICON_SIZE, self.gtk.IconLookupFlags.FORCE_SIZE)
                        if found is not None:
                            break
                else:
                    found = theme.lookup_by_gicon(icon, ICON_SIZE, self.gtk.IconLookupFlags.FORCE_SIZE)
                if found is not None:
                    pix = found.load_icon()
            if pix is None and isinstance(icon, self.gio.FileIcon):
                pix = self.pixbuf.Pixbuf.new_from_file_at_size(icon.get_file().get_path(), ICON_SIZE, ICON_SIZE)
            if pix is not None:
                key = self.encode_pixbuf(pix, "linux")
        except Exception:
            pass
        self.cache[identity] = key
        return key

    def encode_pixbuf(self, pix, prefix, link=False):
        width, height = pix.get_width(), pix.get_height()
        scale = min(ICON_SIZE / max(1, width), ICON_SIZE / max(1, height))
        width, height = max(1, round(width * scale)), max(1, round(height * scale))
        pix = pix.scale_simple(width, height, self.pixbuf.InterpType.BILINEAR)
        canvas = self.pixbuf.Pixbuf.new(self.pixbuf.Colorspace.RGB, True, 8, ICON_SIZE, ICON_SIZE)
        canvas.fill(0)
        pix.copy_area(0, 0, width, height, canvas, (ICON_SIZE - width) // 2, (ICON_SIZE - height) // 2)
        if link and self.gtk is not None:
            try:
                emblem = self.gtk.IconTheme.get_default().load_icon("emblem-symbolic-link", 18, self.gtk.IconLookupFlags.FORCE_SIZE)
                emblem.composite(canvas, ICON_SIZE - 18, ICON_SIZE - 18, 18, 18, ICON_SIZE - 18, ICON_SIZE - 18,
                                 1, 1, self.pixbuf.InterpType.BILINEAR, 255)
            except Exception:
                pass
        ok, data = canvas.save_to_bufferv("png", [], [])
        if not ok:
            return None
        key = prefix + "-" + hashlib.sha256(data).hexdigest()
        self.images[key] = {"Key": key, "Png": base64.b64encode(data).decode("ascii")}
        return key


def folder_path(value):
    if not value or not os.path.isabs(value):
        raise ValueError(tr("backend.enter_an_absolute_wsl_folder_path_starting_with"))
    return os.path.normpath(value)


def list_folder(value, thumbnails=True):
    path = folder_path(value)
    entries = []
    icons = LinuxIcons(thumbnails)
    with os.scandir(path) as scan:
        for item in scan:
            if item.name.startswith("."):
                continue
            try:
                is_dir = item.is_dir()
            except OSError:
                is_dir = False
            entry = {"Name": item.name, "Path": item.path,
                     "IsDirectory": is_dir, "IsLink": item.is_symlink()}
            try:
                stat = item.stat(follow_symlinks=False)
                entry.update(Size=stat.st_size, Modified=stat.st_mtime_ns)
            except OSError:
                entry.update(Size=0, Modified=0)
            entry.update(icons.describe(item.path))
            entries.append(entry)
    entries.sort(key=lambda item: (not item["IsDirectory"], item["Name"].casefold(), item["Name"]))
    return {"Path": path, "Entries": entries, "Icons": list(icons.images.values())}


def thumbnail_batch(value):
    paths = json.loads(value)
    if not isinstance(paths, list) or len(paths) > 4:
        raise ValueError(tr("backend.at_most_four_files_are_allowed_per_thumbnail_batch"))
    icons = LinuxIcons()
    result = []
    if icons.thumbnail_cache is not None:
        for value in paths:
            path = folder_path(value)
            try:
                info = icons.gio.File.new_for_path(path).query_info("standard::content-type", 0, None)
                pix = icons.thumbnail_cache.generate(path, info.get_content_type() or "")
                if pix is not None:
                    key = icons.encode_pixbuf(pix, "thumb", os.path.islink(path))
                    result.append({"Path": path, "IconKey": key})
            except Exception:
                continue
    return {"Thumbnails": result, "Icons": list(icons.images.values())}


def detached_handler(handler, gio, glib):
    """Non-D-Bus GUI children must outlive the short wsl.exe request session."""
    desktop_type = getattr(gio, "DesktopAppInfo", None)
    if desktop_type is None or not isinstance(handler, desktop_type):
        return handler
    if handler.get_boolean("DBusActivatable"):
        return handler
    filename = handler.get_filename()
    command = handler.get_string("Exec")
    if not filename or not command or not os.path.exists("/usr/bin/setsid"):
        return handler
    keyfile = glib.KeyFile()
    keyfile.load_from_file(filename, glib.KeyFileFlags.NONE)
    # A keyfile-created app has no desktop filename for expansion of %k.
    command = command.replace("%k", glib.shell_quote(filename))
    keyfile.set_string("Desktop Entry", "Exec", "/usr/bin/setsid --fork -- " + command)
    return gio.DesktopAppInfo.new_from_keyfile(keyfile) or handler


def open_linux(value, application=None, set_default=False):
    ensure_wslg_session()
    from gi.repository import Gio, GLib

    path = folder_path(value)
    if application is None:
        handler, uri = open_target(path, Gio, GLib)
    else:
        handler = Gio.DesktopAppInfo.new(application)
        if handler is None:
            raise ValueError(tr("backend.the_selected_linux_application_is_no_longer_installed_choose_ano"))
        target = Gio.File.new_for_path(path)
        uri = target.get_uri()
        if set_default:
            info = target.query_info("standard::content-type", Gio.FileQueryInfoFlags.NONE, None)
            content_type = info.get_content_type()
            if not content_type or not handler.set_as_default_for_type(content_type):
                raise ValueError(tr("backend.could_not_set_the_linux_default_application_for_this_file_type"))
    return launch_request(handler, uri, Gio, GLib)


def launch_request(handler, uri, Gio, GLib):
    if handler is not None:
        handler = detached_handler(handler, Gio, GLib)
    # GUI children must neither consume the private request channel nor inherit
    # response pipes that could keep a short-lived request waiting for EOF.
    sys.stdout.flush()
    saved = [os.dup(0), os.dup(1), os.dup(2)]
    try:
        with open(os.devnull, "r+b") as sink:
            os.dup2(sink.fileno(), 0)
            os.dup2(sink.fileno(), 1)
            os.dup2(sink.fileno(), 2)
            await_application(Gio, GLib, handler, uri)
    finally:
        for fd, original in zip((0, 1, 2), saved):
            os.dup2(original, fd)
            os.close(original)
    response = {"Ok": True, "Application": handler.get_display_name() if handler else tr("backend.system_default_application")}
    if _input_method_session is not None:
        warning = _input_method_session.status()['warning']
        if warning: response['InputMethodWarning'] = tr('backend.ime_' + warning)
    return response


def await_application(gio, glib, handler, uri):
    """Keep the helper alive until D-Bus activation acknowledges the open.

    AppInfo.launch() can return before its async D-Bus Open request has been
    processed. A short-lived CLI must run the main context and await finish.
    """
    loop = glib.MainLoop()
    cancel = gio.Cancellable()
    state = {"done": False, "timed_out": False, "error": None}

    def completed(_source, result, _data):
        try:
            ok = (handler.launch_uris_finish(result) if handler is not None
                  else gio.AppInfo.launch_default_for_uri_finish(result))
            if not ok:
                raise RuntimeError(tr("backend.the_default_application_did_not_accept_the_request"))
        except Exception as exc:
            state["error"] = exc
        state["done"] = True
        loop.quit()

    def timed_out():
        state["timed_out"] = True
        state["error"] = TimeoutError(tr("backend.the_linux_default_application_timed_out_check_that_it_can_start_"))
        cancel.cancel()
        loop.quit()
        return False

    timeout = glib.timeout_add(12000, timed_out)
    try:
        if handler is not None:
            uris = uri if isinstance(uri, list) else [uri] if uri is not None else []
            handler.launch_uris_async(uris, None, cancel, completed, None)
        else:
            gio.AppInfo.launch_default_for_uri_async(uri, None, cancel, completed, None)
        if not state["done"]:
            loop.run()
    finally:
        if not state["timed_out"]:
            glib.source_remove(timeout)
    if state["error"] is not None:
        raise state["error"]


def linux_applications(value):
    from gi.repository import Gio
    path = folder_path(value)
    info = Gio.File.new_for_path(path).query_info("standard::content-type", Gio.FileQueryInfoFlags.NONE, None)
    content_type = info.get_content_type() or "application/octet-stream"
    default = Gio.AppInfo.get_default_for_type(content_type, False)
    default_id = default.get_id() if default else None
    suggested = Gio.AppInfo.get_all_for_type(content_type)
    recommended = {app.get_id() for app in suggested}
    if default_id:
        recommended.add(default_id)
    apps = ([default] if default else []) + list(suggested) + list(Gio.AppInfo.get_all())
    icons = LinuxIcons(False)
    result, seen = [], set()
    for app in apps:
        identity = app.get_id()
        if not identity or identity in seen or not isinstance(app, Gio.DesktopAppInfo):
            continue
        if identity not in recommended and not app.should_show():
            continue
        seen.add(identity)
        result.append({"Id": identity, "Name": app.get_display_name(),
                       "Command": app.get_commandline() or app.get_executable() or "",
                       "IconKey": icons.render(app.get_icon()),
                       "Recommended": identity in recommended, "IsDefault": identity == default_id})
    result.sort(key=lambda app: (not app["IsDefault"], not app["Recommended"], app["Name"].casefold()))
    return {"Applications": result, "ContentType": content_type, "Icons": list(icons.images.values())}


def entry_name(value):
    if not isinstance(value, str) or not value or value in (".", "..") or any(c in value for c in ("/", "\0", "\r", "\n")):
        raise ValueError(tr("backend.a_name_is_required_it_cannot_contain_or_line_breaks"))
    return value


def file_action(command, value):
    from gi.repository import Gio
    import shutil
    import stat
    payload = json.loads(value)
    if not isinstance(payload, dict):
        raise ValueError(tr("backend.invalid_file_operation_request"))
    if command in ("trash", "delete-permanently"):
        paths = payload.get("Paths")
        if not isinstance(paths, list) or not paths or len(paths) > 32:
            raise ValueError(tr("backend.each_file_operation_batch_needs_1_to_32_paths"))
        # Validate the entire batch before moving any item.
        paths = [folder_path(path) for path in paths]
        if "/" in paths:
            raise ValueError(tr("backend.the_filesystem_root_cannot_be_deleted"))
        failures, completed = [], 0
        for path in paths:
            try:
                if command == "trash":
                    if not Gio.File.new_for_path(path).trash(None):
                        raise RuntimeError(tr("backend.could_not_move_the_item_to_linux_trash_the_file_was_kept"))
                else:
                    mode = os.lstat(path).st_mode
                    if stat.S_ISDIR(mode):
                        shutil.rmtree(path)
                    else:
                        os.unlink(path)
                completed += 1
            except Exception as exc:
                failures.append({"Path": path, "Error": str(exc)})
        return {"Ok": not failures, "Completed": completed, "Failures": failures}
    path = folder_path(payload.get("Path"))
    name = entry_name(payload.get("Name"))
    if command == "mkdir":
        target = os.path.join(path, name)
        os.mkdir(target)
    elif command == "rename":
        if path == "/":
            raise ValueError(tr("backend.the_filesystem_root_cannot_be_renamed"))
        target = os.path.join(os.path.dirname(path), name)
        if path != target:
            # GIO's no-overwrite move preserves symlinks and rejects existing
            # destinations, including dangling symlinks. Never os.rename over it.
            Gio.File.new_for_path(path).move(Gio.File.new_for_path(target), Gio.FileCopyFlags.NONE, None, None, None)
    else:
        raise ValueError(tr("backend.unknown_file_operation"))
    return {"Ok": True, "Path": target}


def open_trash():
    ensure_wslg_session()
    from gi.repository import Gio, GLib
    handler = Gio.AppInfo.get_default_for_type("inode/directory", False)
    return launch_request(handler, "trash:///", Gio, GLib)


def diagnose_environment():
    import shutil
    checks = []
    def check(name, ok, detail, required=True):
        checks.append({"Name": name, "Ok": bool(ok), "Details": detail, "Required": required})
    check("Python 3", sys.version_info >= (3, 8), sys.version.split()[0])
    check("WSLg", os.path.exists("/mnt/wslg/.X11-unix/X0") and os.path.exists("/mnt/wslg/runtime-dir/wayland-0"),
          tr("backend.requires_windows_wsl2_wslg_graphics"))
    gi_ok = False
    try:
        import gi
        gi.require_version("Gtk", "3.0")
        gi.require_version("GdkPixbuf", "2.0")
        from gi.repository import Gio
        gi_ok = True
    except (ImportError, ValueError):
        pass
    check("GIO / GTK 3", gi_ok, "Ubuntu / Debian: python3-gi gir1.2-gtk-3.0")
    check("D-Bus", shutil.which("dbus-daemon") and shutil.which("dbus-send"), "Ubuntu / Debian: dbus")
    check("setsid", os.path.exists("/usr/bin/setsid"), tr("backend.used_to_launch_linux_gui_applications_independently"))
    check(tr("backend.thumbnail_generator"), shutil.which("gdk-pixbuf-thumbnailer"), tr("backend.optional_libgdk_pixbuf2_0_bin"), False)
    im = InputMethodSession(os.environ)
    command = im.PROVIDERS[im.provider][0] if im.provider else None
    installed = bool(command and shutil.which(command))
    running = bool(im.provider and any(p['provider'] == im.provider for p in im._running()))
    im_detail = (im.provider or im.state) + (': ' + (shutil.which(command) or 'not installed') if command else '')
    if installed: im_detail += '; ' + ('WSLg service running' if running else 'WSLg service not running (started on app launch)')
    check(tr('backend.ime_label'), installed and running, im_detail, False)
    return {"Ok": all(c["Ok"] for c in checks if c["Required"]), "Distro": os.environ.get("WSL_DISTRO_NAME", ""),
            "Home": os.path.expanduser("~"), "Checks": checks}


def main(argv):
    command = argv[0] if argv else "info"
    if command == "doctor":
        return diagnose_environment()
    if command == "applications" and len(argv) == 2:
        return linux_applications(argv[1])
    if command in ("trash", "delete-permanently", "rename", "mkdir") and len(argv) == 2:
        return file_action(command, argv[1])
    if command == "open-trash":
        return open_trash()
    if command == "info":
        return {"Distro": os.environ.get("WSL_DISTRO_NAME", ""),
                "Home": os.path.expanduser("~")}
    if command == "folder-info" and len(argv) == 2:
        path = folder_path(argv[1])
        try:
            with os.scandir(path) as scan:
                next(scan, None)
        except FileNotFoundError:
            raise ValueError(tr("backend.the_folder_does_not_exist_choose_another_folder"))
        except NotADirectoryError:
            raise ValueError(tr("backend.the_selected_path_is_not_a_folder"))
        except PermissionError:
            raise ValueError(tr("backend.you_do_not_have_permission_to_read_this_folder_choose_an_accessi"))
        return {"Path": path}
    if command == "list" and len(argv) == 2:
        return list_folder(argv[1])
    if command == "list-icons" and len(argv) == 2:
        return list_folder(argv[1], False)
    if command == "thumbnails" and len(argv) == 2:
        return thumbnail_batch(argv[1])
    if command == "open" and len(argv) == 2:
        return open_linux(argv[1])
    raise ValueError("Unknown command or missing path")


def serve_open():
    """A private stdin/stdout request channel for warmed file opening only."""
    ensure_wslg_session()
    from gi.repository import Gio, GLib
    print(json.dumps({"Ready": True, "Pid": os.getpid()}), flush=True)
    for line in sys.stdin:
        line = line.lstrip("\ufeff")
        if not line.strip():
            continue
        try:
            request = json.loads(line)
            if not isinstance(request, dict):
                raise ValueError("Invalid open request")
            command = request.get("Command", "open")
            if command == "open-trash":
                response = open_trash()
                print(json.dumps(response, ensure_ascii=False), flush=True)
                continue
            if command != "open" or not isinstance(request.get("Path"), str):
                raise ValueError("Invalid open request")
            context = GLib.MainContext.default()
            while context.pending():
                context.iteration(False)
            application = request.get("Application")
            if application is not None and not isinstance(application, str):
                raise ValueError("Invalid application ID")
            if not isinstance(request.get("SetDefault", False), bool):
                raise ValueError("Invalid default application flag")
            response = open_linux(request["Path"], application, request.get("SetDefault", False))
        except Exception as exc:
            response = {"Error": str(exc)}
        print(json.dumps(response, ensure_ascii=False), flush=True)


def serve_metadata(thumbnails=False):
    """Warm file operations; never activate GUI apps on the icon environment."""
    from gi.repository import GLib
    allowed = {"thumbnails"} if thumbnails else {"info", "folder-info", "list", "list-icons", "applications", "rename", "mkdir", "trash"}
    print(json.dumps({"Ready": True, "Pid": os.getpid()}), flush=True)
    for line in sys.stdin:
        line = line.lstrip("\ufeff")
        if not line.strip():
            continue
        try:
            request = json.loads(line)
            if not isinstance(request, dict) or request.get("Command") not in allowed:
                raise ValueError("Invalid metadata request")
            context = GLib.MainContext.default()
            while context.pending():
                context.iteration(False)
            command = request["Command"]
            value = request.get("Path")
            if command != "info" and not isinstance(value, str):
                raise ValueError("Missing request path")
            response = main([command] + ([value] if value is not None else []))
        except Exception as exc:
            response = {"Error": str(exc)}
        print(json.dumps(response, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    if sys.argv[1:] in (["serve-open"], ["serve-metadata"], ["serve-thumbnails"]):
        try:
            mode = sys.argv[1]
            if mode == "serve-open":
                serve_open()
            else:
                serve_metadata(mode == "serve-thumbnails")
        except Exception as exc:
            print(json.dumps({"Error": str(exc)}, ensure_ascii=False), flush=True)
            sys.exit(1)
        sys.exit(0)
    try:
        result = main(sys.argv[1:])
    except Exception as exc:
        print(json.dumps({"Error": str(exc)}, ensure_ascii=False))
        sys.exit(1)
    print(json.dumps(result, ensure_ascii=False))
