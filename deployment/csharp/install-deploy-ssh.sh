#!/usr/bin/env bash
# Dedicated key-only SSH listener in the school's allowed 8000-9000 range.
# The existing administrative SSH daemon on port 22 is left unchanged.
set -euo pipefail
[[ $(id -u) -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
cat > /etc/ssh/sshd_config_cargohub_deploy <<'CONFIG'
Port 8422
ListenAddress 0.0.0.0
HostKey /etc/ssh/ssh_host_ed25519_key
PidFile /run/cargohub-deploy-sshd.pid
AllowUsers cargohub-deploy
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
PermitTTY no
DisableForwarding yes
AuthorizedKeysFile .ssh/authorized_keys
StrictModes yes
UsePAM yes
MaxAuthTries 3
ClientAliveInterval 30
ClientAliveCountMax 3
PrintMotd no
CONFIG
/usr/sbin/sshd -t -f /etc/ssh/sshd_config_cargohub_deploy
cat > /etc/systemd/system/cargohub-deploy-ssh.service <<'UNIT'
[Unit]
Description=CargoHub key-only SSH deployment listener
After=network.target ssh.service

[Service]
Type=simple
ExecStartPre=/usr/bin/install -d -m 0755 /run/sshd
ExecStart=/usr/sbin/sshd -D -e -f /etc/ssh/sshd_config_cargohub_deploy
Restart=on-failure
RestartSec=3

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable --now cargohub-deploy-ssh.service
if command -v ufw >/dev/null && ufw status | grep -q '^Status: active'; then
    ufw allow 8422/tcp comment 'CargoHub restricted SSH deployment'
fi
systemctl is-active --quiet cargohub-deploy-ssh.service
echo 'Key-only deploy SSH listener is active on port 8422.'
