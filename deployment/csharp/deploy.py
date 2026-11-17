#!/usr/bin/env python3
"""Apply a verified release through Compose, switching the proxy only after V2 is ready."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
import urllib.request
import zipfile

BASE = Path('/opt/cargohub-v2')
COMPOSE = ['docker', 'compose', '--project-directory', str(BASE), '-f', str(BASE/'compose.yml')]


def run(args, *, data=None, capture=False):
    return subprocess.run(args, input=data, check=True, stdout=subprocess.PIPE if capture else None).stdout


def sql(statement, *, capture=False):
    return run(COMPOSE + ['exec', '-T', 'db', 'psql', '-X', '-q', '-v', 'ON_ERROR_STOP=1',
                          '-U', 'cargohub', '-d', 'cargohub_v2', '-At'],
               data=statement.encode(), capture=capture)


def write_env(values):
    path = BASE/'.env.next'
    path.write_text(''.join(f'{key}={value}\n' for key, value in values.items()))
    path.chmod(0o600)
    path.replace(BASE/'.env')


def probe(port):
    for attempt in range(60):
        try:
            root = f'http://127.0.0.1:{port}'
            with urllib.request.urlopen(root+'/health/ready', timeout=3) as response:
                assert json.load(response)['version'] == 'v2'
            with urllib.request.urlopen(root+'/api/v2/warehouses', timeout=3) as response:
                assert isinstance(json.load(response), list)
            with urllib.request.urlopen(root+'/api/v2/swagger/v2/swagger.json', timeout=3) as response:
                document = json.load(response)
                assert document['info']['version'] == 'v2'
                assert '/api/v2/warehouses' in document['paths']
                assert not any(path.startswith('/api/v1/') for path in document['paths'])
            return
        except (OSError, ValueError, AssertionError):
            if attempt == 59:
                raise
            time.sleep(1)


def extract_release(archive):
    with zipfile.ZipFile(archive) as bundle:
        names = bundle.namelist()
        if len(names) != len(set(names)) or sum(x.file_size for x in bundle.infolist()) > 600_000_000:
            raise ValueError('Invalid or oversized release')
        for entry in bundle.infolist():
            path = Path(entry.filename)
            if path.is_absolute() or '..' in path.parts or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError('Unsafe archive entry')
        manifest = json.loads(bundle.read('manifest.json'))
        version = manifest['version']
        if manifest['schema_version'] != 1 or not re.fullmatch(r'2\.\d+\.\d+\.\d+', version):
            raise ValueError('Unsupported release manifest')
        if manifest['api_version'] != 'v2' or manifest['runtime'] != 'linux-x64':
            raise ValueError('Unexpected application/runtime')
        if set(names) != set(manifest['files']) | {'manifest.json'}:
            raise ValueError('Manifest does not cover every release file')
        for name, expected in manifest['files'].items():
            if hashlib.sha256(bundle.read(name)).hexdigest() != expected:
                raise ValueError(f'Release checksum mismatch: {name}')
        target = BASE/'releases'/version
        if target.exists():
            if (target/'manifest.json').read_bytes() != bundle.read('manifest.json'):
                raise ValueError('This version already exists with different contents')
        else:
            temporary = Path(tempfile.mkdtemp(prefix='.incoming-', dir=BASE/'releases'))
            bundle.extractall(temporary)
            temporary.rename(target)
        return target, manifest


def main():
    if os.geteuid() != 0:
        raise SystemExit('Run through the restricted SSH receiver or sudo.')
    release, manifest = extract_release(sys.argv[1])
    version = manifest['version']
    print(f'[{datetime.datetime.now(datetime.timezone.utc).isoformat()}] Deploying CargoHub {version}', flush=True)
    state_file = BASE/'deployment.json'
    state = json.loads(state_file.read_text()) if state_file.exists() else {}
    slot = 'green' if state.get('active_slot') == 'blue' else 'blue'
    port = 8501 if slot == 'blue' else 8502
    image = f'cargohub-api:{version}'
    env = dict(line.split('=', 1) for line in (BASE/'.env').read_text().splitlines() if '=' in line)
    run(COMPOSE + ['up', '-d', '--wait', '--wait-timeout', '120', 'db'])

    # Keep a restore point before schema changes. Deployment never resets the database.
    if sql("SELECT to_regclass('public.warehouse');", capture=True).strip():
        stamp = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')
        backup = BASE/'backups'/f'{stamp}-before-{version}.dump'
        with backup.open('wb') as output:
            subprocess.run(COMPOSE + ['exec', '-T', 'db', 'pg_dump', '-U', 'cargohub',
                                      '-d', 'cargohub_v2', '-Fc'], stdout=output, check=True)
        backup.chmod(0o600)
        print('Database backup created.', flush=True)
    print('Applying idempotent EF migrations.', flush=True)
    sql((release/'migrations.sql').read_text())
    sql('CREATE TABLE IF NOT EXISTS public.cargohub_seed_history (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP);')
    if not sql("SELECT 1 FROM public.cargohub_seed_history WHERE name='legacy-json';", capture=True).strip():
        print('Importing the original JSON test data once.', flush=True)
        sql((release/'import-legacy-json.sql').read_text())
        sql("INSERT INTO public.cargohub_seed_history(name) VALUES ('legacy-json');")
    else:
        print('Existing PostgreSQL data retained; initial import skipped.', flush=True)

    print(f'Building {image} and starting inactive slot {slot}.', flush=True)
    run(['docker', 'build', '--file', str(BASE/'Dockerfile'), '--tag', image, str(release)])
    env[f'CARGOHUB_IMAGE_{slot.upper()}'] = image
    write_env(env)
    run(COMPOSE + ['up', '-d', '--no-deps', 'api-'+slot])
    try:
        probe(port)
    except Exception:
        run(COMPOSE + ['logs', '--tail', '30', 'api-'+slot])
        raise
    print('Candidate health, database read and V2 Swagger checks passed.', flush=True)

    config = BASE/'nginx/default.conf'
    previous_config = config.read_bytes() if config.exists() else None
    candidate_config = (BASE/'nginx.conf.template').read_text().replace('__ACTIVE_SLOT__', slot)
    temporary = config.with_suffix('.next')
    temporary.write_text(candidate_config)
    temporary.replace(config)
    try:
        run(COMPOSE + ['up', '-d', '--no-deps', 'proxy'])
        run(COMPOSE + ['exec', '-T', 'proxy', 'nginx', '-t'])
        run(COMPOSE + ['exec', '-T', 'proxy', 'nginx', '-s', 'reload'])
        probe(80)
    except Exception:
        if previous_config is not None:
            config.write_bytes(previous_config)
            run(COMPOSE + ['exec', '-T', 'proxy', 'nginx', '-s', 'reload'])
        raise
    # Keep the preceding slot alive to let existing requests finish and allow rollback.
    new_state = {'active_slot': slot, 'version': version, 'image': image,
                 'commit': manifest['commit'], 'previous': state.get('version'),
                 'deployed_at': datetime.datetime.now(datetime.timezone.utc).isoformat()}
    state_file.write_text(json.dumps(new_state, indent=2)+'\n')
    print(f'Deployed {version}. Active: {slot}. V2 and Swagger are reachable. V1 stays on Python.', flush=True)


if __name__ == '__main__':
    main()
