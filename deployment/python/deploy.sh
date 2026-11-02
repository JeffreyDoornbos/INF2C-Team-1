#!/usr/bin/env bash
# Run locally. SSH and sudo passwords, when needed, are entered in your terminal.
set -euo pipefail
umask 077
target=${1:-ubuntu-1098684@145.24.237.22}
root=$(cd "$(dirname "$0")/../.." && pwd)
build_dir=$(mktemp -d "${TMPDIR:-/tmp}/cargohub-upload.XXXXXX")
trap 'rm -r "$build_dir"' EXIT

echo "Preparing the original Python API for $target"
COPYFILE_DISABLE=1 tar --no-xattrs --exclude='__pycache__' --exclude='*.pyc' --exclude='.DS_Store' -czf "$build_dir/python-api.tar.gz" \
    -C "$root" api data \
    -C "$root/deployment/python" start-api.sh

remote_dir=$(ssh "$target" 'umask 077; mktemp -d /tmp/cargohub-deploy.XXXXXX')
if [[ ! "$remote_dir" =~ ^/tmp/cargohub-deploy\.[a-zA-Z0-9]+$ ]]; then
    echo 'Unexpected upload directory; deployment stopped.' >&2
    exit 1
fi
scp "$build_dir/python-api.tar.gz" "$root/deployment/python/install-server.sh" "$target:$remote_dir/"
ssh -t "$target" "sudo /bin/bash '$remote_dir/install-server.sh' '$remote_dir/python-api.tar.gz'"

echo 'Installation complete. Checking port 3000 from this computer...'
host=${target##*@}
code=$(curl --connect-timeout 5 --max-time 10 -s -o /dev/null -w '%{http_code}' \
    "http://$host:3000/api/v1/warehouses") || {
    echo "The service passed its local server test, but http://$host:3000 is not reachable from this computer. Check the VPS or school network firewall." >&2
    exit 1
}
if [[ "$code" != 401 ]]; then
    echo "Unexpected external HTTP status: $code. Expected 401 without an API key." >&2
    exit 1
fi
echo "External test passed. API URL: http://$host:3000/api/v1/warehouses"
echo 'Supply the existing API_KEY header in Bruno to receive HTTP 200.'
