#!/usr/bin/env python3
"""Add only the dashboard redirect and ACME challenge route to the existing proxy."""
from pathlib import Path
import shutil
import subprocess

base = Path('/opt/cargohub-v2')
paths = [base / 'nginx/default.conf', base / 'nginx.conf.template']
original = {path: path.read_text() for path in paths}
block = '''
    # CargoHub dashboard and IP-certificate validation.
    location ^~ /.well-known/acme-challenge/ {
        proxy_pass http://host.docker.internal:8765;
    }
    location = /beheer { return 302 https://145.24.237.22/beheer/; }
    location /beheer/ { return 302 https://145.24.237.22$request_uri; }
'''
compose = ['docker', 'compose', '--project-directory', str(base)]
try:
    for path in paths:
        if 'location = /beheer' in original[path]:
            continue
        anchor = '    location = / { return 302 /api/v2; }'
        if anchor not in original[path]:
            raise RuntimeError('Unexpected Nginx configuration, review manually.')
        backup = path.with_name(path.name + '.before-dashboard')
        if not backup.exists():
            shutil.copy2(path, backup)
        path.write_text(original[path].replace(anchor, block + '\n' + anchor, 1))
    subprocess.run(compose + ['exec', '-T', 'proxy', 'nginx', '-t'], check=True)
    subprocess.run(compose + ['exec', '-T', 'proxy', 'nginx', '-s', 'reload'], check=True)
except Exception:
    for path, text in original.items():
        path.write_text(text)
    raise
print('Existing V1 and V2 routing retained; dashboard redirect and ACME route added.')
