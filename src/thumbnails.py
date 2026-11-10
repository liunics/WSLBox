"""Freedesktop thumbnail cache and bounded thumbnailer jobs for WSLBox."""
import configparser
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import tempfile
import time


class ThumbnailCache:
    MAX_BYTES = 64 * 1024 * 1024
    TIMEOUT = 3

    def __init__(self, gio, pixbuf):
        self.gio, self.pixbuf = gio, pixbuf
        base = Path(os.environ.get("XDG_CACHE_HOME") or Path.home() / ".cache")
        self.root = base / "thumbnails"
        self.failures = base / "WSLBox" / "thumbnail-failures"
        self.handlers = self._handlers()

    @staticmethod
    def _handlers():
        roots = [Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local/share")]
        roots += [Path(p) for p in os.environ.get("XDG_DATA_DIRS", "/usr/local/share:/usr/share").split(":") if p]
        seen, handlers = set(), {}
        for root in roots:
            for file in sorted((root / "thumbnailers").glob("*.thumbnailer")):
                if file.name in seen:
                    continue
                seen.add(file.name)
                try:
                    cfg = configparser.ConfigParser(interpolation=None, strict=False)
                    cfg.read(file, encoding="utf-8")
                    entry = cfg["Thumbnailer Entry"]
                    command = shlex.split(entry.get("Exec", ""))
                    if not command or not shutil.which(entry.get("TryExec") or command[0]):
                        continue
                    for mime in entry.get("MimeType", "").split(";"):
                        if mime:
                            handlers.setdefault(mime, command)
                except (OSError, ValueError, KeyError, configparser.Error):
                    continue
        return handlers

    def identity(self, path):
        uri = self.gio.File.new_for_path(path).get_uri()
        return uri, hashlib.md5(uri.encode("utf-8")).hexdigest()

    def cached(self, path):
        try:
            source = os.stat(path)
            uri, key = self.identity(path)
            for size in ("large", "normal", "x-large", "xx-large"):
                file = self.root / size / (key + ".png")
                if not file.is_file():
                    continue
                try:
                    pix = self.pixbuf.Pixbuf.new_from_file(str(file))
                except Exception:
                    continue
                if pix.get_option("tEXt::Thumb::URI") != uri:
                    continue
                if pix.get_option("tEXt::Thumb::MTime") != str(int(source.st_mtime)):
                    continue
                exact = pix.get_option("tEXt::WSLBox::MTimeNS")
                length = pix.get_option("tEXt::Thumb::Size")
                if exact is not None and exact != str(source.st_mtime_ns):
                    continue
                if length is not None and length != str(source.st_size):
                    continue
                return pix.apply_embedded_orientation() or pix
        except Exception:
            pass
        return None

    def eligible(self, path, mime):
        if not (mime.startswith("image/") or mime in self.handlers):
            return False
        try:
            source = os.stat(path)
            if not os.path.isfile(path) or source.st_size > self.MAX_BYTES:
                return False
            if os.path.commonpath([os.path.realpath(path), str(self.root.resolve())]) == str(self.root.resolve()):
                return False
            _, key = self.identity(path)
            marker = self.failures / (key + ".json")
            if marker.exists():
                try:
                    data = json.loads(marker.read_text())
                    if not isinstance(data, dict):
                        raise ValueError("invalid failure marker")
                    if data.get("mtime") == source.st_mtime_ns and data.get("size") == source.st_size and time.time() - data.get("time", 0) < 60:
                        return False
                except (OSError, ValueError, TypeError):
                    pass
            return mime in self.handlers or bool(shutil.which("gdk-pixbuf-thumbnailer"))
        except (OSError, ValueError, TypeError):
            return False

    def generate(self, path, mime):
        cached = self.cached(path)
        if cached is not None:
            return cached
        if not self.eligible(path, mime):
            return None
        uri, key = self.identity(path)
        before = os.stat(path)
        directory = self.root / "normal"
        directory.mkdir(parents=True, exist_ok=True, mode=0o700)
        fd, temporary = tempfile.mkstemp(suffix=".png", prefix=".wslbox-", dir=directory)
        os.close(fd)
        try:
            template = self.handlers.get(mime) or ["gdk-pixbuf-thumbnailer", "-s", "%s", "%u", "%o"]
            values = {"%": "%", "s": "128", "u": uri, "i": path, "o": temporary}
            command = [re.sub(r"%([%suio])", lambda m: values[m[1]], arg) for arg in template]
            result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=self.TIMEOUT,
                                    stdin=subprocess.DEVNULL, check=False)
            if result.returncode:
                raise ValueError("thumbnailer failed")
            pix = self.pixbuf.Pixbuf.new_from_file_at_size(temporary, 128, 128)
            pix = pix.apply_embedded_orientation() or pix
            after = os.stat(path)
            if (before.st_mtime_ns, before.st_size) != (after.st_mtime_ns, after.st_size):
                return None
            pix.savev(temporary, "png", ["tEXt::Thumb::URI", "tEXt::Thumb::MTime", "tEXt::Thumb::Size", "tEXt::WSLBox::MTimeNS"],
                      [uri, str(int(after.st_mtime)), str(after.st_size), str(after.st_mtime_ns)])
            os.replace(temporary, directory / (key + ".png"))
            return pix
        except Exception:
            try:
                self.failures.mkdir(parents=True, exist_ok=True, mode=0o700)
                (self.failures / (key + ".json")).write_text(json.dumps({"mtime": before.st_mtime_ns, "size": before.st_size, "time": time.time()}))
            except OSError:
                pass
            return None
        finally:
            try:
                os.unlink(temporary)
            except FileNotFoundError:
                pass
