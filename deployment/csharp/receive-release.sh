#!/usr/bin/env bash
# The restricted SSH deploy key can only stream a release into this receiver.
set -euo pipefail
exec 9>/var/lock/cargohub-v2-deploy.lock
flock -w 600 9
stage=$(mktemp -d /opt/cargohub-v2/incoming.XXXXXX)
trap 'rm -rf "$stage"' EXIT
head -c 300000001 > "$stage/release.zip"
if [[ $(stat -c '%s' "$stage/release.zip") -gt 300000000 ]]; then
    echo 'Release exceeds the 300 MB limit.' >&2
    exit 1
fi
python3 /opt/cargohub-v2/deploy.py "$stage/release.zip" 2>&1 | tee -a /var/log/cargohub-deploy.log
