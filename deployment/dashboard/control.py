#!/usr/bin/env python3
"""Root-only broker. Exposes fixed status fields and six allowlisted app actions."""
import datetime
import fcntl
import json
from pathlib import Path
import subprocess
import sys
import time

BASE = Path('/opt/cargohub-v2')
COMPOSE = ['/usr/bin/docker', 'compose', '--project-directory', str(BASE)]


def run(args, timeout=12):
    return subprocess.run(args, check=True, capture_output=True, text=True,
                          timeout=timeout).stdout.strip()


def state():
    value = json.loads((BASE / 'deployment.json').read_text())
    if value.get('active_slot') not in ('blue', 'green'):
        raise ValueError('Invalid active API slot')
    return {k: value.get(k) for k in ('active_slot', 'version', 'previous', 'deployed_at', 'commit')}


def unit(name):
    output = run(['/usr/bin/systemctl', 'show', name, '--no-pager',
                  '-p', 'ActiveState', '-p', 'SubState', '-p', 'ActiveEnterTimestampMonotonic'])
    props = dict(line.split('=', 1) for line in output.splitlines() if '=' in line)
    active = props.get('ActiveState') == 'active'
    entered = int(props.get('ActiveEnterTimestampMonotonic') or 0) / 1_000_000
    return {'running': active, 'status': props.get('ActiveState', 'unknown'),
            'uptime': max(0, float(Path('/proc/uptime').read_text().split()[0]) - entered)
            if active and entered else 0}


def status():
    deployment = state()
    ids = run(['/usr/bin/docker', 'ps', '-aq', '--filter',
               'label=com.docker.compose.project=cargohub-v2']).split()
    containers = {}
    if ids:
        records = json.loads(run(['/usr/bin/docker', 'inspect', *ids]))
        running = [r['Id'] for r in records if r['State']['Running']]
        stats = {}
        if running:
            for line in run(['/usr/bin/docker', 'stats', '--no-stream', '--format', '{{json .}}',
                             *running], timeout=20).splitlines():
                value = json.loads(line)
                stats[value['Name']] = {'cpu': value['CPUPerc'], 'memory': value['MemUsage']}
        for record in records:
            service = record['Config']['Labels'].get('com.docker.compose.service')
            if service not in ('api-blue', 'api-green', 'db', 'proxy'):
                continue
            name = record['Name'].lstrip('/')
            started = record['State']['StartedAt']
            uptime = 0
            if record['State']['Running']:
                # Docker emits nanoseconds while datetime accepts microseconds.
                stamp = started.split('.')[0] + '+00:00'
                uptime = max(0, time.time() - datetime.datetime.fromisoformat(stamp).timestamp())
            containers[service] = {
                'running': record['State']['Running'], 'status': record['State']['Status'],
                'health': record['State'].get('Health', {}).get('Status'), 'uptime': uptime,
                **stats.get(name, {'cpu': '0.00%', 'memory': '0B / 0B'}),
            }
    return {'deployment': deployment, 'containers': containers,
            'python': unit('cargohub-python.service'),
            'runner': unit('actions.runner.JeffreyDoornbos-INF2C-Team-1.cargohub-vps.service')}


def action(service, operation):
    if service not in ('v1', 'v2') or operation not in ('start', 'stop', 'restart'):
        raise ValueError('Action not allowed')
    # Share the same lock as the release receiver, so actions cannot race a deployment.
    with open('/var/lock/cargohub-v2-deploy.lock', 'a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise ValueError('Er loopt een deployment of beheeractie. Probeer het straks opnieuw.')
        if service == 'v2':
            slot = state()['active_slot']
            run(COMPOSE + [operation, 'api-' + slot], timeout=40)
            if operation in ('start', 'restart'):
                # A restarted container may receive a different network address.
                run(COMPOSE + ['exec', '-T', 'proxy', 'nginx', '-t'])
                run(COMPOSE + ['exec', '-T', 'proxy', 'nginx', '-s', 'reload'])
        else:
            run(['/usr/bin/systemctl', operation, 'cargohub-python.service'], timeout=40)
        # The journal records the operation, never credentials or request bodies.
        run(['/usr/bin/logger', '-t', 'cargohub-dashboard',
             f'Application action: {service} {operation}'])
    return {'ok': True, 'message': f'{service.upper()} {operation} uitgevoerd.'}


def main():
    try:
        if sys.argv[1:] == ['status']:
            result = status()
        elif len(sys.argv) == 4 and sys.argv[1] == 'action':
            result = action(sys.argv[2], sys.argv[3])
        else:
            raise ValueError('Action not allowed')
        print(json.dumps(result))
    except ValueError as error:
        print(json.dumps({'error': str(error)}))
        sys.exit(1)
    except Exception:
        # Do not return subprocess stderr: it can contain server configuration.
        print(json.dumps({'error': 'Serveractie mislukt. Bekijk het beheerlog op de VPS.'}))
        sys.exit(1)


if __name__ == '__main__':
    main()
