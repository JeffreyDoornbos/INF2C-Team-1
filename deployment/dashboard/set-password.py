#!/usr/bin/env python3
"""Change the shared dashboard password interactively without printing it."""
import getpass
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess

if os.geteuid() != 0:
    raise SystemExit('Gebruik sudo om het beheerwachtwoord te wijzigen.')
password = getpass.getpass('Nieuw dashboardwachtwoord (minimaal 12 tekens): ')
if len(password) < 12:
    raise SystemExit('Gebruik minimaal 12 tekens.')
if password != getpass.getpass('Herhaal het nieuwe wachtwoord: '):
    raise SystemExit('De wachtwoorden verschillen.')
path = Path('/etc/cargohub-dashboard/config.json')
config = json.loads(path.read_text())
config['salt'] = secrets.token_hex(16)
config['password_hash'] = hashlib.scrypt(password.encode(), salt=bytes.fromhex(config['salt']),
                                        n=16384, r=8, p=1).hex()
path.write_text(json.dumps(config, indent=2) + '\n')
Path('/root/cargohub-dashboard-initial-password.txt').unlink(missing_ok=True)
subprocess.run(['systemctl', 'restart', 'cargohub-dashboard'], check=True)
print('Wachtwoord gewijzigd. Bestaande dashboardsessies zijn uitgelogd.')
