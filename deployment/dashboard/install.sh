#!/usr/bin/env bash
# Install the private monitoring service. Invoke as root after copying this folder.
set -euo pipefail
[[ $(id -u) -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
source_dir=$(cd "$(dirname "$0")" && pwd)
base=/opt/cargohub-dashboard
id cargohub-monitor >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin cargohub-monitor
install -d -m 755 "$base" "$base/static" /var/lib/cargohub-acme/.well-known/acme-challenge
install -d -m 750 -o root -g cargohub-monitor /etc/cargohub-dashboard
install -m 755 "$source_dir/server.py" "$base/server.py"
install -m 755 "$source_dir/set-password.py" "$base/set-password.py"
install -m 644 "$source_dir/static/"* "$base/static/"
install -m 755 "$source_dir/control.py" /usr/local/sbin/cargohub-dashboard-control
install -m 644 "$source_dir/https-compose.yml" "$base/https-compose.yml"
install -m 644 "$source_dir/https.conf" "$base/https.conf"
python3 - <<'PY'
import grp, hashlib, json, os, pathlib, secrets, subprocess
config=pathlib.Path('/etc/cargohub-dashboard/config.json')
gateway=subprocess.check_output(['docker','network','inspect','bridge','--format','{{(index .IPAM.Config 0).Gateway}}'],text=True).strip()
if not config.exists():
    password=secrets.token_urlsafe(24)
    salt=secrets.token_hex(16)
    digest=hashlib.scrypt(password.encode(),salt=bytes.fromhex(salt),n=16384,r=8,p=1).hex()
    config.write_text(json.dumps({'listen':gateway,'origin':'https://145.24.237.22',
        'username':'groep1','salt':salt,'password_hash':digest,
        'acme_root':'/var/lib/cargohub-acme'},indent=2)+'\n')
    os.chown(config,0,grp.getgrnam('cargohub-monitor').gr_gid)
    config.chmod(0o640)
    initial=pathlib.Path('/root/cargohub-dashboard-initial-password.txt')
    initial.write_text('Gebruikersnaam: groep1\nWachtwoord: '+password+'\n')
    initial.chmod(0o600)
text=pathlib.Path('/opt/cargohub-dashboard/https.conf').read_text().replace('__DASHBOARD_IP__',gateway)
pathlib.Path('/opt/cargohub-dashboard/https.conf').write_text(text)
PY
cat > /etc/sudoers.d/cargohub-dashboard <<'EOF'
cargohub-monitor ALL=(root) NOPASSWD: /usr/local/sbin/cargohub-dashboard-control status
cargohub-monitor ALL=(root) NOPASSWD: /usr/local/sbin/cargohub-dashboard-control action v1 start, /usr/local/sbin/cargohub-dashboard-control action v1 stop, /usr/local/sbin/cargohub-dashboard-control action v1 restart
cargohub-monitor ALL=(root) NOPASSWD: /usr/local/sbin/cargohub-dashboard-control action v2 start, /usr/local/sbin/cargohub-dashboard-control action v2 stop, /usr/local/sbin/cargohub-dashboard-control action v2 restart
EOF
chmod 440 /etc/sudoers.d/cargohub-dashboard
visudo -cf /etc/sudoers.d/cargohub-dashboard
cat > /etc/systemd/system/cargohub-dashboard.service <<'EOF'
[Unit]
Description=CargoHub authenticated administration dashboard
After=network-online.target docker.service
Requires=docker.service

[Service]
Type=simple
User=cargohub-monitor
Group=cargohub-monitor
WorkingDirectory=/opt/cargohub-dashboard
ExecStart=/usr/bin/python3 /opt/cargohub-dashboard/server.py
Restart=always
RestartSec=5
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadOnlyPaths=/opt/cargohub-v2 /etc/cargohub-dashboard /var/lib/cargohub-acme
ReadWritePaths=/run/lock
RestrictAddressFamilies=AF_INET AF_UNIX
# sudo is intentionally allowed only for the seven exact broker commands above.
# NoNewPrivileges cannot be set here because it would disable that broker.

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now cargohub-dashboard
systemctl restart cargohub-dashboard
echo 'Private dashboard installed. Login is stored in /root/cargohub-dashboard-initial-password.txt.'
