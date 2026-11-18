#!/usr/bin/env bash
# Existing HTTP proxy must already serve the ACME path through the private dashboard.
set -euo pipefail
[[ $(id -u) -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
if [[ ! -x /opt/cargohub-certbot/bin/certbot ]]; then
    apt-get update -qq
    DEBIAN_FRONTEND=noninteractive apt-get install -y python3-venv
    python3 -m venv /opt/cargohub-certbot
    /opt/cargohub-certbot/bin/pip install 'certbot>=5.4,<7'
fi
/opt/cargohub-certbot/bin/certbot certonly --non-interactive --agree-tos \
    --register-unsafely-without-email --preferred-profile shortlived \
    --webroot --webroot-path /var/lib/cargohub-acme --ip-address 145.24.237.22 \
    --cert-name 145.24.237.22
docker compose -f /opt/cargohub-dashboard/https-compose.yml up -d --force-recreate
install -d -m 755 /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/cargohub-https <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
docker compose -f /opt/cargohub-dashboard/https-compose.yml exec -T https nginx -t 2>&1
docker compose -f /opt/cargohub-dashboard/https-compose.yml exec -T https nginx -s reload 2>&1
EOF
chmod 755 /etc/letsencrypt/renewal-hooks/deploy/cargohub-https
cat > /etc/systemd/system/cargohub-certificate-renew.service <<'EOF'
[Unit]
Description=Renew the CargoHub short-lived IP certificate
After=network-online.target
[Service]
Type=oneshot
ExecStart=/opt/cargohub-certbot/bin/certbot renew --quiet
EOF
cat > /etc/systemd/system/cargohub-certificate-renew.timer <<'EOF'
[Unit]
Description=Check CargoHub certificate renewal twice a day
[Timer]
OnCalendar=*-*-* 03,15:00:00
RandomizedDelaySec=1800
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now cargohub-certificate-renew.timer
echo 'HTTPS enabled with automatic certificate renewal.'
