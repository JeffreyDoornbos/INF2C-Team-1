#!/usr/bin/env python3
"""Register an unprivileged, main-only deploy worker using a short-lived registration token.

Run as root: install-runner.py OFFICIAL_ASSET_METADATA_JSON REGISTRATION_TOKEN_JSON
The GitHub personal token and VM password must never be copied to the server.
"""
import hashlib
import json
import os
from pathlib import Path
import pwd
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.request

if os.geteuid() != 0:
    raise SystemExit('Run as root.')
asset = json.loads(Path(sys.argv[1]).read_text())
registration = Path(sys.argv[2])
token = json.loads(registration.read_text())['token']
if not asset['url'].startswith('https://github.com/actions/runner/releases/download/'):
    raise SystemExit('Only official GitHub Actions runner assets are accepted.')
if not asset.get('digest', '').startswith('sha256:'):
    raise SystemExit('An official SHA-256 digest is required.')
base = Path('/opt/cargohub-runner')
if (base/'.runner').exists():
    raise SystemExit('Runner is already configured.')
try:
    pwd.getpwnam('cargohub-runner')
except KeyError:
    subprocess.run(['useradd', '--system', '--create-home', '--home-dir', str(base),
                    '--shell', '/bin/bash', 'cargohub-runner'], check=True)
with tempfile.TemporaryDirectory(prefix='cargohub-runner-') as temporary:
    archive = Path(temporary)/'runner.tar.gz'
    digest = hashlib.sha256()
    print('Downloading official GitHub deploy runner '+asset['version'], flush=True)
    with urllib.request.urlopen(asset['url'], timeout=120) as response, archive.open('wb') as output:
        while chunk := response.read(1024*1024):
            output.write(chunk)
            digest.update(chunk)
    if digest.hexdigest() != asset['digest'].removeprefix('sha256:'):
        raise SystemExit('Runner archive checksum mismatch.')
    with tarfile.open(archive) as bundle:
        bundle.extractall(base, filter='data')
subprocess.run(['chown', '-R', 'cargohub-runner:cargohub-runner', str(base)], check=True)
subprocess.run(['runuser', '-u', 'cargohub-runner', '--', str(base/'config.sh'),
                '--unattended', '--url', 'https://github.com/JeffreyDoornbos/INF2C-Team-1',
                '--token', token, '--name', 'cargohub-vps', '--labels', 'cargohub-vps',
                '--work', '_work'], cwd=base, check=True)
registration.unlink()
hook = Path('/usr/local/lib/cargohub-runner-main-only.sh')
hook.write_text('''#!/usr/bin/env bash
set -euo pipefail
if [[ ${GITHUB_REPOSITORY:-} != JeffreyDoornbos/INF2C-Team-1 || ${GITHUB_REF:-} != refs/heads/main || ${GITHUB_EVENT_NAME:-} == pull_request ]]; then
    echo 'This VPS worker only accepts trusted main-branch deployment jobs.' >&2
    exit 1
fi
''')
hook.chmod(0o755)
with (base/'.env').open('a') as environment:
    environment.write('\nACTIONS_RUNNER_HOOK_JOB_STARTED='+str(hook)+'\n')
subprocess.run([str(base/'svc.sh'), 'install', 'cargohub-runner'], cwd=base, check=True)
subprocess.run([str(base/'svc.sh'), 'start'], cwd=base, check=True)
print('Deploy runner installed as cargohub-runner without Docker-group or sudo access.', flush=True)
