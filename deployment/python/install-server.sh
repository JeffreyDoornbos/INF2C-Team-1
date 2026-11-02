#!/usr/bin/env bash
# Run on Ubuntu through deploy.sh, using sudo.
set -euo pipefail

base=/opt/cargohub-python
service=cargohub-python
archive=${1:?Provide the uploaded archive path}

if [[ $(id -u) -ne 0 ]]; then
    echo 'Run this installer with sudo.' >&2
    exit 1
fi
command -v systemctl >/dev/null
command -v python3 >/dev/null
python3 -c 'import sys; assert sys.version_info >= (3, 10), "Python 3.10 or newer is required"'
if [[ -e "$base" || -e /etc/systemd/system/cargohub-python.service ]]; then
    echo 'An existing CargoHub installation was found. Stopping to avoid overwriting code or data.' >&2
    exit 1
fi
if ss -H -ltn 'sport = :3000' | grep -q .; then
    echo 'Port 3000 is already in use. No running application has been stopped.' >&2
    exit 1
fi
if getent passwd "$service" >/dev/null; then
    echo 'The cargohub-python account already exists. Stopping to avoid using another service account.' >&2
    exit 1
fi

# Complete preflight before installing a new service.
stage=$(mktemp -d /opt/cargohub-python-install.XXXXXX)
tar -xzf "$archive" -C "$stage"
test -f "$stage/api/main.py"
test -f "$stage/data/user.json"
test -f "$stage/start-api.sh"
python3 - "$stage" <<'PY'
import ast, json, pathlib, sys
root = pathlib.Path(sys.argv[1])
for path in (root / 'api').rglob('*.py'):
    ast.parse(path.read_text())
for path in (root / 'data').glob('*.json'):
    json.loads(path.read_text())
PY
useradd --system --user-group --home-dir "$base" --shell /usr/sbin/nologin "$service"
mv "$stage" "$base"
chown -R root:root "$base"
chmod 755 "$base"
chmod -R u=rwX,go=rX "$base/api"
chmod 755 "$base/start-api.sh"
chown -R "$service:$service" "$base/data"
chmod 700 "$base/data"
chmod 600 "$base"/data/*.json

cat > /etc/systemd/system/cargohub-python.service <<'UNIT'
[Unit]
Description=CargoHub original Python API
After=network.target

[Service]
Type=simple
User=cargohub-python
Group=cargohub-python
WorkingDirectory=/opt/cargohub-python
ExecStart=/bin/bash /opt/cargohub-python/start-api.sh
Environment=PYTHONDONTWRITEBYTECODE=1
Restart=on-failure
RestartSec=3
TimeoutStopSec=10
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/opt/cargohub-python/data
UMask=0077

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now "$service.service"

# Confirm both unauthenticated and authenticated access without logging keys.
python3 - "$base" <<'PY'
import json, pathlib, sys, time, urllib.error, urllib.request
root = pathlib.Path(sys.argv[1])
users = json.loads((root / 'data/user.json').read_text())
key = next(u['api_key'] for u in users if u['endpoint_access'].get('warehouses', {}).get('get'))
url = 'http://127.0.0.1:3000/api/v1/warehouses'
for attempt in range(20):
    try:
        request = urllib.request.Request(url, headers={'API_KEY': key})
        with urllib.request.urlopen(request, timeout=3) as response:
            assert response.status == 200
            assert json.load(response) == json.loads((root / 'data/warehouse.json').read_text())
        break
    except (OSError, AssertionError):
        if attempt == 19:
            raise
        time.sleep(0.5)
try:
    urllib.request.urlopen(url, timeout=3)
except urllib.error.HTTPError as error:
    assert error.code == 401, error.code
else:
    raise AssertionError('An unauthenticated request should return 401')
print('Local check passed: HTTP 200 with a valid key and HTTP 401 without a key.')
PY

if command -v ufw >/dev/null && ufw status | grep -q '^Status: active'; then
    ufw allow 3000/tcp comment 'CargoHub school API'
fi
systemctl is-active --quiet "$service.service"
systemctl is-enabled --quiet "$service.service"
echo 'CargoHub is running on port 3000 and starts automatically after reboot.'
echo 'Status: sudo systemctl status cargohub-python'
echo 'Logs: sudo journalctl -u cargohub-python -n 50 --no-pager'

# Remove only our upload artifacts, which include the original API keys.
python3 - "$archive" <<'PY'
import pathlib, sys
archive = pathlib.Path(sys.argv[1])
directory = archive.parent
if directory.parent == pathlib.Path('/tmp') and directory.name.startswith('cargohub-deploy.'):
    for name in ('python-api.tar.gz', 'install-server.sh'):
        (directory / name).unlink(missing_ok=True)
    directory.rmdir()
PY
