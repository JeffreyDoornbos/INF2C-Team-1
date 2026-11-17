#!/usr/bin/env bash
# One-time Ubuntu bootstrap, invoked by an administrator over SSH.
set -euo pipefail
[[ $(id -u) -eq 0 ]] || { echo 'Run with sudo.' >&2; exit 1; }
source_dir=$(cd "$(dirname "$0")" && pwd)
public_key=${1:?Provide the deploy public-key file}
base=/opt/cargohub-v2
command -v docker >/dev/null
docker compose version
if [[ -e "$base/compose.yml" ]]; then
    echo 'CargoHub V2 is already installed; use the release receiver for updates.' >&2
    exit 1
fi
install -d -m 755 "$base" "$base/nginx" "$base/releases"
install -d -m 700 "$base/backups"
install -m 644 "$source_dir/compose.yml" "$source_dir/Dockerfile" "$source_dir/nginx.conf.template" "$base/"
install -m 755 "$source_dir/deploy.py" "$source_dir/receive-release.sh" "$base/"
python3 - "$base" <<'PY'
import pathlib,secrets,sys
path=pathlib.Path(sys.argv[1])/'.env'
path.write_text('POSTGRES_PASSWORD='+secrets.token_hex(32)+'\n')
path.chmod(0o600)
PY
if ! id cargohub-deploy >/dev/null 2>&1; then
    useradd --create-home --shell /bin/bash cargohub-deploy
fi
install -d -m 700 -o cargohub-deploy -g cargohub-deploy /home/cargohub-deploy/.ssh
python3 - "$public_key" <<'PY'
from pathlib import Path
import sys
key=Path(sys.argv[1]).read_text().strip()
if not key.startswith('ssh-ed25519 '): raise SystemExit('An Ed25519 deploy public key is required')
path=Path('/home/cargohub-deploy/.ssh/authorized_keys')
entry='restrict,command="sudo -n /opt/cargohub-v2/receive-release.sh" '+key+'\n'
existing=path.read_text() if path.exists() else ''
if entry not in existing: path.write_text(existing+entry)
path.chmod(0o600)
PY
chown cargohub-deploy:cargohub-deploy /home/cargohub-deploy/.ssh/authorized_keys
echo 'cargohub-deploy ALL=(root) NOPASSWD: /opt/cargohub-v2/receive-release.sh' > /etc/sudoers.d/cargohub-v2-deploy
chmod 440 /etc/sudoers.d/cargohub-v2-deploy
visudo -cf /etc/sudoers.d/cargohub-v2-deploy
bash "$source_dir/install-deploy-ssh.sh"
touch /var/log/cargohub-deploy.log
chmod 644 /var/log/cargohub-deploy.log
systemctl enable --now docker
echo 'Bootstrap complete. Stream a versioned ZIP to the restricted SSH deploy user.'
