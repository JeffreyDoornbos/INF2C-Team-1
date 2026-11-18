#!/usr/bin/env python3
"""Local administration UI behind an HTTPS proxy; Python standard library only."""
import base64
from collections import defaultdict, deque
import hashlib
import hmac
from http import cookies
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import logging
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import threading
import time
from urllib.parse import urlsplit

HERE = Path(__file__).resolve().parent
HELPER = ['/usr/bin/sudo', '-n', '/usr/local/sbin/cargohub-dashboard-control']
COOKIE = '__Secure-cargohub_admin'
SESSION_SECONDS = 8 * 60 * 60


def password_hash(password, salt):
    return hashlib.scrypt(password.encode(), salt=bytes.fromhex(salt), n=16384, r=8, p=1).hex()


def helper(*args):
    result = subprocess.run(HELPER + list(args), text=True, capture_output=True, timeout=45)
    data = json.loads(result.stdout)
    if result.returncode:
        raise ValueError(data.get('error', 'Serveractie mislukt.'))
    return data


class State:
    def __init__(self, config, start_collector=True):
        self.config = config
        self.sessions = {}
        self.attempts = defaultdict(deque)
        self.lock = threading.Lock()
        self.action_lock = threading.Lock()
        self.snapshot = {'loading': True}
        self.last_cpu = None
        self.stop = threading.Event()
        if start_collector:
            threading.Thread(target=self.collect_loop, daemon=True).start()

    def collect_loop(self):
        while not self.stop.is_set():
            try:
                result = helper('status')
                counters = list(map(int, Path('/proc/stat').read_text().splitlines()[0].split()[1:9]))
                total, idle = sum(counters), counters[3] + counters[4]
                cpu = 0.0
                if self.last_cpu:
                    delta = total - self.last_cpu[0]
                    cpu = round(100 * (1 - (idle - self.last_cpu[1]) / delta), 1) if delta else 0.0
                self.last_cpu = total, idle
                memory = {line.split(':')[0]: int(line.split()[1]) * 1024
                          for line in Path('/proc/meminfo').read_text().splitlines()}
                disk = shutil.disk_usage('/')
                result['host'] = {
                    'cpu': max(0, min(100, cpu)), 'cpu_count': os.cpu_count(),
                    'memory_total': memory['MemTotal'],
                    'memory_used': memory['MemTotal'] - memory['MemAvailable'],
                    'disk_total': disk.total, 'disk_used': disk.used,
                    'uptime': float(Path('/proc/uptime').read_text().split()[0]),
                    'load': list(os.getloadavg()),
                }
                result.update(sampled_at=time.time(), loading=False)
                with self.lock:
                    self.snapshot = result
            except Exception:
                logging.exception('Could not collect server status')
                with self.lock:
                    self.snapshot = {**self.snapshot, 'error': 'Status verversen mislukt.'}
            self.stop.wait(5)

    def session(self, value):
        with self.lock:
            item = self.sessions.get(value)
            if item and item['expires'] > time.time():
                return item.copy()
            self.sessions.pop(value, None)
        return None


class Handler(BaseHTTPRequestHandler):
    server_version = 'CargoHub'
    sys_version = ''

    @property
    def state(self):
        return self.server.state

    def reply(self, status, value, content_type='application/json; charset=utf-8', extra=None):
        data = json.dumps(value).encode() if content_type.startswith('application/json') else value
        self.send_response(status)
        for name, value in {
            'Content-Type': content_type, 'Content-Length': str(len(data)),
            'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff',
            'Referrer-Policy': 'no-referrer', 'X-Frame-Options': 'DENY',
            'Content-Security-Policy': "default-src 'self'; style-src 'self'; script-src 'self'; "
                                     "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; "
                                     "base-uri 'none'; form-action 'self'",
            **(extra or {}),
        }.items():
            self.send_header(name, value)
        self.end_headers()
        self.wfile.write(data)

    def identity(self):
        try:
            jar = cookies.SimpleCookie(self.headers.get('Cookie', ''))
            token = jar[COOKIE].value if COOKIE in jar else ''
        except cookies.CookieError:
            token = ''
        return token, self.state.session(token)

    def do_GET(self):
        path = urlsplit(self.path).path
        if path.startswith('/.well-known/acme-challenge/'):
            name = path.removeprefix('/.well-known/acme-challenge/')
            if not name or len(name) > 180 or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-' for c in name):
                return self.reply(404, {'error': 'Niet gevonden.'})
            target = Path(self.state.config['acme_root']) / '.well-known/acme-challenge' / name
            try:
                return self.reply(200, target.read_bytes(), 'text/plain; charset=utf-8')
            except FileNotFoundError:
                return self.reply(404, {'error': 'Niet gevonden.'})
        files = {'/beheer': ('index.html', 'text/html'), '/beheer/': ('index.html', 'text/html'),
                 '/beheer/app.js': ('app.js', 'text/javascript'),
                 '/beheer/style.css': ('style.css', 'text/css')}
        if path in files:
            filename, media = files[path]
            return self.reply(200, (HERE / 'static' / filename).read_bytes(), media + '; charset=utf-8')
        if path == '/beheer/api/status':
            _, identity = self.identity()
            if not identity:
                return self.reply(401, {'error': 'Log eerst in.'})
            with self.state.lock:
                data = {**self.state.snapshot, 'csrf': identity['csrf'],
                        'username': self.state.config['username']}
            return self.reply(200, data)
        return self.reply(404, {'error': 'Niet gevonden.'})

    def do_POST(self):
        # Browser mutations must come from our HTTPS origin; no CORS is enabled.
        if self.headers.get('Origin') != self.state.config['origin']:
            return self.reply(403, {'error': 'Ongeldige oorsprong.'})
        if self.headers.get('Content-Type', '').split(';')[0] != 'application/json':
            return self.reply(415, {'error': 'Verstuur JSON.'})
        try:
            size = int(self.headers.get('Content-Length', 0))
            if not 0 < size <= 4096:
                return self.reply(413, {'error': 'Ongeldige aanvraaggrootte.'})
            body = json.loads(self.rfile.read(size))
            if not isinstance(body, dict):
                raise ValueError()
        except (ValueError, TypeError):
            return self.reply(400, {'error': 'Ongeldige JSON.'})
        path = urlsplit(self.path).path
        if path == '/beheer/api/login':
            return self.login(body)
        token, identity = self.identity()
        if not identity:
            return self.reply(401, {'error': 'Log eerst in.'})
        csrf = self.headers.get('X-CSRF-Token', '')
        if not hmac.compare_digest(csrf.encode(), identity['csrf'].encode()):
            return self.reply(403, {'error': 'Ververs de pagina en probeer opnieuw.'})
        if path == '/beheer/api/logout':
            with self.state.lock:
                self.state.sessions.pop(token, None)
            return self.reply(200, {'ok': True}, extra={
                'Set-Cookie': f'{COOKIE}=; Max-Age=0; Path=/beheer; Secure; HttpOnly; SameSite=Strict'})
        if path == '/beheer/api/action':
            service, action = body.get('service'), body.get('action')
            if service not in ('v1', 'v2') or action not in ('start', 'stop', 'restart'):
                return self.reply(400, {'error': 'Deze actie is niet toegestaan.'})
            if not self.state.action_lock.acquire(blocking=False):
                return self.reply(409, {'error': 'Er loopt al een beheeractie.'})
            try:
                result = helper('action', service, action)
                logging.info('Admin action by %s: %s %s', self.state.config['username'], service, action)
                return self.reply(200, result)
            except (ValueError, subprocess.TimeoutExpired) as error:
                return self.reply(409, {'error': str(error) if isinstance(error, ValueError)
                                       else 'De actie duurt langer. Controleer de status.'})
            finally:
                self.state.action_lock.release()
        return self.reply(404, {'error': 'Niet gevonden.'})

    def login(self, body):
        # Only the fixed local reverse proxies may reach the private listener.
        address = self.headers.get('X-Real-IP', self.client_address[0])
        now = time.time()
        with self.state.lock:
            attempts = self.state.attempts[address]
            while attempts and attempts[0] < now - 300:
                attempts.popleft()
            if len(attempts) >= 6:
                return self.reply(429, {'error': 'Te veel pogingen. Wacht vijf minuten.'})
            attempts.append(now)
        username, password = body.get('username', ''), body.get('password', '')
        if not isinstance(username, str) or not isinstance(password, str):
            return self.reply(401, {'error': 'Gebruikersnaam of wachtwoord onjuist.'})
        valid_password = hmac.compare_digest(password_hash(password, self.state.config['salt']),
                                             self.state.config['password_hash'])
        if not valid_password or not hmac.compare_digest(username.encode(), self.state.config['username'].encode()):
            return self.reply(401, {'error': 'Gebruikersnaam of wachtwoord onjuist.'})
        token = secrets.token_urlsafe(32)
        with self.state.lock:
            self.state.sessions = {key: value for key, value in self.state.sessions.items()
                                   if value['expires'] > now}
            self.state.sessions[token] = {'expires': now + SESSION_SECONDS, 'csrf': secrets.token_urlsafe(32)}
            self.state.attempts.pop(address, None)
        return self.reply(200, {'ok': True}, extra={
            'Set-Cookie': f'{COOKIE}={token}; Max-Age={SESSION_SECONDS}; Path=/beheer; Secure; HttpOnly; SameSite=Strict'})


def main():
    logging.basicConfig(level=logging.INFO, format='%(asctime)s %(levelname)s %(message)s')
    config = json.loads(Path(os.environ.get('CARGOHUB_DASHBOARD_CONFIG',
                                            '/etc/cargohub-dashboard/config.json')).read_text())
    server = ThreadingHTTPServer((config['listen'], 8765), Handler)
    server.state = State(config)
    logging.info('Dashboard listening on private address %s:8765', config['listen'])
    server.serve_forever()


if __name__ == '__main__':
    main()
