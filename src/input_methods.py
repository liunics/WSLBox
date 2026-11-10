"""Optional Linux input-method integration for the WSLg application session."""
import fcntl
import os
from pathlib import Path
import re
import shutil
import subprocess
import time


class InputMethodSession:
    IM_KEYS = ('GTK_IM_MODULE', 'QT_IM_MODULE', 'XMODIFIERS')
    PROVIDERS = {'fcitx': ('fcitx', ['-d', '-s', '0']),
                 'fcitx5': ('fcitx5', ['-d']),
                 'ibus': ('ibus-daemon', ['--daemonize', '--xim'])}

    def __init__(self, source, home=None):
        self.source = dict(source)
        self.home = Path(home or source.get('HOME') or Path.home())
        self.provider, self.state = self._select()
        self.warning = None
        self._next_check = 0
        self._activation_warning = False

    def _configured(self):
        for path in (self.home / '.xinputrc', Path('/etc/X11/xinit/xinputrc')):
            try:
                text = path.read_text(encoding='utf-8')
            except (OSError, UnicodeError):
                continue
            match = re.search(r'^\s*run_im\s+[\"\']?([\w-]+)', text, re.M)
            if match and match[1] not in ('auto', 'default'):
                return match[1]
        return None

    def _select(self):
        if self.source.get('WSLBOX_INPUT_METHOD', '').lower() == 'none': return None, 'disabled'
        configured = self._configured()
        explicit = [self.source.get(key, '').lower() for key in ('GTK_IM_MODULE', 'QT_IM_MODULE')]
        modifier = self.source.get('XMODIFIERS', '')
        if '@im=fcitx' in modifier: explicit.append('fcitx')
        elif '@im=ibus' in modifier: explicit.append('ibus')
        family = next((value for value in explicit if value in ('fcitx', 'ibus')), None)
        if family == 'fcitx':
            if configured in ('fcitx', 'fcitx5'): return configured, 'selected'
            active = {p['provider'] for p in self._running() if p['provider'].startswith('fcitx')}
            if len(active) == 1: return active.pop(), 'selected'
            return ('fcitx5' if shutil.which('fcitx5') and not shutil.which('fcitx') else 'fcitx'), 'selected'
        if family == 'ibus': return 'ibus', 'selected'
        if configured in self.PROVIDERS: return configured, 'selected'
        if configured in ('none', 'disable'): return None, 'disabled'
        if configured: return None, 'unsupported'
        # Respect a running WSLg input method when no explicit selection exists.
        running = self._running()
        providers = {p['provider'] for p in running}
        if len(providers) == 1: return providers.pop(), 'selected'
        return None, 'unconfigured'

    @staticmethod
    def _display(value):
        if value.startswith('unix/'): value = value[5:]
        elif value.startswith('unix'): value = value[4:]
        return value.split('.')[0]

    def _running(self):
        result = []
        names = {'fcitx': 'fcitx', 'fcitx5': 'fcitx5', 'ibus-daemon': 'ibus'}
        try:
            entries = list(Path('/proc').iterdir())
        except OSError:
            return result
        for path in entries:
            if not path.name.isdigit(): continue
            try:
                if path.stat().st_uid != os.getuid(): continue
                name = (path / 'comm').read_text().strip()
                if name not in names: continue
                values = dict(item.split('=', 1) for item in (path / 'environ').read_bytes().decode(errors='replace').split('\0') if '=' in item)
                display = values.get('DISPLAY', '')
                if self._display(display) != ':0': continue
                result.append({'provider': names[name], 'pid': int(path.name)})
            except (OSError, ValueError):
                continue
        return result

    def environment(self, env):
        result = dict(env)
        if self.provider:
            module = 'ibus' if self.provider == 'ibus' else 'fcitx'
            for key, value in (('GTK_IM_MODULE', module), ('QT_IM_MODULE', module), ('XMODIFIERS', '@im=' + module)):
                # Explicit toolkit-specific overrides remain authoritative.
                if not result.get(key): result[key] = value
        return result

    def update_activation(self, env):
        """Refresh only this private bus, including when it predates IM setup."""
        values = {key: env.get(key, '') for key in self.IM_KEYS}
        import json
        try:
            result = subprocess.run(['/usr/bin/gdbus', 'call', '--address', env['DBUS_SESSION_BUS_ADDRESS'],
                '--dest', 'org.freedesktop.DBus', '--object-path', '/org/freedesktop/DBus',
                '--method', 'org.freedesktop.DBus.UpdateActivationEnvironment', json.dumps(values)],
                env=env, stdin=subprocess.DEVNULL, capture_output=True, timeout=2)
            self._activation_warning = result.returncode != 0
        except (OSError, subprocess.TimeoutExpired):
            self._activation_warning = True

    def ensure(self, env, runtime):
        """Bounded checks, one cross-worker startup lock, no shell/profile execution."""
        now = time.monotonic()
        if now < self._next_check: return self.status()
        self._next_check = now + 60
        self.warning = None
        if not self.provider:
            if self.state != 'disabled': self.warning = 'unconfigured'
            return self.status()
        command, arguments = self.PROVIDERS[self.provider]
        executable = shutil.which(command)
        if not executable:
            self.state = 'missing'; self.warning = 'missing'
            return self.status()
        try:
            lock_path = Path(runtime) / 'input-method.lock'
            fd = os.open(lock_path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
            with os.fdopen(fd, 'w') as lock:
                # A different opener may already be initializing this display.
                try: fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    self.state = 'pending'; self._next_check = now + 2
                    return self.status()
                running = self._running()
                if any(p['provider'] == self.provider for p in running):
                    self.state = 'running'
                    return self.status()
                if self.provider.startswith('fcitx') and any(p['provider'].startswith('fcitx') for p in running):
                    self.state = 'conflict'; self.warning = 'conflict'
                    return self.status()
                daemon_env = dict(env)
                # Use the X11 frontend for the daemon's candidate UI. Applications
                # retain their original Wayland/X11 choice and IM modules.
                daemon_env.pop('WAYLAND_DISPLAY', None)
                result = subprocess.run([executable, *arguments], env=daemon_env, stdin=subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=2, start_new_session=True)
                if result.returncode != 0: raise RuntimeError('input method daemon exited')
                deadline = time.monotonic() + 1.5
                while True:
                    if any(p['provider'] == self.provider for p in self._running()):
                        self.state = 'started'; return self.status()
                    if time.monotonic() >= deadline: raise RuntimeError('input method daemon not ready')
                    time.sleep(.05)
        except (OSError, RuntimeError, subprocess.TimeoutExpired):
            self.state = 'failed'; self.warning = 'failed'; self._next_check = now + 10
        return self.status()

    def status(self):
        return {'provider': self.provider, 'state': self.state,
                'warning': None if self.state == 'disabled' else (self.warning or ('activation' if self._activation_warning else None))}
