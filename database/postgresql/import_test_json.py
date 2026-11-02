"""CH-013: validate legacy JSON, import accepted records into an EMPTY test DB.

Standard library only. Reuses the existing EF entity conversion rules. No source
writes, upserts, deletions, secret import or implicit reset. See CH-013-import.md.
"""
import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

import generate_legacy_import as legacy

FOREIGN_KEYS = {
    'location': {'warehouse_id': 'warehouse'},
    'item': {'supplier_id': 'supplier', 'item_line_id': 'item_line',
             'item_group_id': 'item_group', 'item_type_id': 'item_type'},
    'inventory': {'item_id': 'item', 'location_id': 'location'},
    'order': {'client_id': 'client', 'ship_to_client_id': 'client',
              'bill_to_client_id': 'client', 'warehouse_id': 'warehouse'},
    'order_item': {'order_id': 'order', 'item_id': 'item'},
    'shipment': {'order_id': 'order'},
    'shipment_item': {'shipment_id': 'shipment', 'item_id': 'item'},
    'transfer': {'from_location_id': 'location', 'to_location_id': 'location'},
    'transfer_item': {'transfer_id': 'transfer', 'item_id': 'item'},
}
STATUSES = {'order': ('order_status', {'Pending', 'Shipped'}),
            'shipment': ('shipment_status', {'Scheduled', 'Transit', 'Delivered'}),
            'transfer': ('transfer_status', {'Scheduled', 'Processed'})}
QUANTITIES = ('quantity_on_hand', 'quantity_expected', 'quantity_ordered', 'quantity_allocated')
POLICY_VERSION = 1


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def reject_non_json(value):
    raise ValueError('Non-finite numbers are not valid JSON')


def unique_fields(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f'Duplicate JSON field: {key}')
        result[key] = value
    return result


def primary(table):
    return ('item_id', 'location_id') if table == 'inventory' else ('id',)


def validate(source):
    tables, report = {}, {'files': {}, 'excluded_files': {
        'user.json': 'API-sleutels en rechten vallen onder CH-020; niet gelezen of geïmporteerd.'}}
    for table, entity in legacy.ENTITIES:
        path = source / (table + '.json')
        raw = path.read_bytes()
        rows = json.loads(raw, parse_float=Decimal, parse_constant=reject_non_json,
                          object_pairs_hook=unique_fields)
        if not isinstance(rows, list):
            raise ValueError(f'{table}.json: expected a JSON array')
        columns = legacy.entity_columns(entity)
        expected = {column for column, _ in columns}
        entries, transformations = [], Counter()
        for index, row in enumerate(rows, 1):
            errors, converted = [], {}
            key = None
            if not isinstance(row, dict):
                errors.append('record_is_not_an_object')
            else:
                if set(row) != expected:
                    errors.append('fields_do_not_match_model: missing=' + ','.join(sorted(expected-set(row)))
                                  + '; unknown=' + ','.join(sorted(set(row)-expected)))
                for column, kind in columns:
                    if column not in row:
                        continue
                    try:
                        converted[column] = legacy.convert_value(table, column, kind, row[column])
                        if kind == 'DateOnly' and converted[column] != row[column]:
                            transformations[column + ': UTC timestamp -> date'] += 1
                        if table == 'item' and column == 'commodity_code' and type(row[column]) is int:
                            transformations[column + ': integer -> text'] += 1
                    except (ValueError, TypeError, ArithmeticError) as exc:
                        errors.append(f'invalid_value:{column}: {exc}')
                keys = primary(table)
                if all(k in converted for k in keys):
                    key = tuple(converted[k] for k in keys)
                if not errors:
                    if table == 'inventory':
                        if any(int(converted[q]) < 0 for q in QUANTITIES):
                            errors.append('negative_inventory_quantity')
                        if int(converted['quantity_allocated']) > int(converted['quantity_on_hand']):
                            errors.append('allocation_exceeds_on_hand')
                    if table.endswith('_item'):
                        if int(converted['amount']) <= 0:
                            errors.append('amount_must_be_positive')
                        if table == 'order_item' and Decimal(converted['unit_price']) < 0:
                            errors.append('unit_price_must_be_nonnegative')
                    if table == 'transfer' and converted['from_location_id'] == converted['to_location_id']:
                        errors.append('source_equals_destination')
                    if table in STATUSES:
                        field, allowed = STATUSES[table]
                        if converted[field] not in allowed:
                            errors.append('unknown_status:' + field)
            entries.append({'row': index, 'key': key, 'values': converted, 'reasons': errors})
        # Reject ALL occurrences of a duplicate, rather than choosing an arbitrary winner.
        for fields, code in [(primary(table), 'duplicate_primary_key')] + (
                [(('transfer_id', 'item_id'), 'duplicate_transfer_item')] if table == 'transfer_item' else []):
            groups = defaultdict(list)
            for entry in entries:
                if all(f in entry['values'] for f in fields):
                    groups[tuple(entry['values'][f] for f in fields)].append(entry)
            for group in groups.values():
                if len(group) > 1:
                    for entry in group:
                        entry['reasons'].append(code)
        # ENTITIES is topologically ordered, so rejected parents cannot leave orphans.
        for entry in entries:
            for field, parent in FOREIGN_KEYS.get(table, {}).items():
                value = entry['values'].get(field)
                if value is not None and (value,) not in tables[parent]['accepted_keys']:
                    entry['reasons'].append('missing_or_rejected_reference:' + field + '->' + parent)
        accepted = [e for e in entries if not e['reasons']]
        tables[table] = {'columns': columns, 'accepted': accepted,
                         'accepted_keys': {e['key'] for e in accepted}}
        rejected = [{'row': e['row'], 'key': e['key'], 'reasons': sorted(set(e['reasons']))}
                    for e in entries if e['reasons']]
        report['files'][table] = {'source': table + '.json', 'sha256': digest(raw),
            'read': len(rows), 'accepted': len(accepted), 'imported': 0, 'rejected': len(rejected),
            'rejections': rejected, 'conversions_in_read_records': dict(transformations)}
    totals = {q: sum(int(e['values'][q]) for e in tables['inventory']['accepted']) for q in QUANTITIES}
    totals['quantity_available'] = totals['quantity_on_hand'] - totals['quantity_allocated']
    report['expected_inventory_totals'] = totals
    report['expected_counts'] = {table: len(data['accepted']) for table, data in tables.items()}
    return tables, report


def source_unchanged(source, report):
    return all(digest((source / f['source']).read_bytes()) == f['sha256'] for f in report['files'].values())


def sql_for(tables):
    q, literal, table_name = legacy.quoted, legacy.sql_string, legacy.table_name
    sql = [r'\set ON_ERROR_STOP on', r'\encoding UTF8', 'BEGIN;',
           "SET LOCAL TIME ZONE 'UTC';", "SET LOCAL standard_conforming_strings=on;",
           "SET LOCAL lock_timeout='10s';", 'DO $guard$ BEGIN',
           "IF current_database() !~ '^cargohub_import_test_[a-z0-9_]+$' THEN RAISE EXCEPTION 'Test database name required'; END IF;",
           'IF to_regclass(\'public."__EFMigrationsHistory"\') IS NULL THEN RAISE EXCEPTION \'Missing EF migrations\'; END IF;']
    for migration in legacy.MIGRATIONS:
        sql += [f'IF NOT EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId"={literal(migration)}) THEN RAISE EXCEPTION \'Missing required migration\'; END IF;']
    sql += ['END $guard$;', 'LOCK TABLE ' + ','.join(table_name(t) for t in tables) + ' IN SHARE ROW EXCLUSIVE MODE;', 'DO $empty$ BEGIN']
    for table in tables:
        sql += [f"IF EXISTS (SELECT 1 FROM {table_name(table)}) THEN RAISE EXCEPTION 'Target must be empty; use a new test database'; END IF;"]
    sql += ['END $empty$;']
    for table, data in tables.items():
        columns = data['columns']
        sql += [f'COPY {table_name(table)} (' + ','.join(q(c) for c, _ in columns) + ') FROM stdin;']
        sql += ['\t'.join(legacy.copy_value(entry['values'][c]) for c, _ in columns) for entry in data['accepted']]
        sql += [r'\.']
    sql += ['DO $counts$ BEGIN']
    for table, data in tables.items():
        sql += [f"IF (SELECT count(*) FROM {table_name(table)}) <> {len(data['accepted'])} THEN RAISE EXCEPTION 'Count mismatch'; END IF;"]
    for field in QUANTITIES:
        expected = sum(int(e['values'][field]) for e in tables['inventory']['accepted'])
        sql += [f"IF (SELECT coalesce(sum({q(field)}),0) FROM public.inventory) <> {expected} THEN RAISE EXCEPTION 'Inventory total mismatch'; END IF;"]
    sql += ['END $counts$;']
    for table, data in tables.items():
        if any(c == 'id' for c, _ in data['columns']):
            next_id = max([0] + [int(e['values']['id']) for e in data['accepted']]) + 1
            sql += [f'ALTER TABLE {table_name(table)} ALTER COLUMN id RESTART WITH {next_id};']
    # Return database measurements only if all checks and the commit succeeded.
    counts = ','.join(f"{literal(t)},(SELECT count(*) FROM {table_name(t)})" for t in tables)
    totals = ','.join(f"{literal(f)},(SELECT coalesce(sum({q(f)}),0) FROM public.inventory)" for f in QUANTITIES)
    sql += [f"SELECT json_build_object('counts',json_build_object({counts}),'inventory_totals',json_build_object({totals}));", 'COMMIT;']
    return '\n'.join(sql) + '\n'


def run(source, report_path, database=None, host='127.0.0.1', port='55432', user='cargohub_test', container=None):
    source, report_path = Path(source).resolve(), Path(report_path).resolve()
    if report_path.suffix.lower() != '.json':
        raise ValueError('Report filename must end in .json (a Markdown companion is also written)')
    if report_path.is_relative_to(source):
        raise ValueError('Report must be outside the source directory')
    if database is not None and not re.fullmatch(r'cargohub_import_test_[a-z0-9_]+', database):
        raise ValueError('Only a database named cargohub_import_test_<name> is allowed')
    if host not in ('127.0.0.1', 'localhost', '::1'):
        raise ValueError('Only a local test server is allowed')
    tables, report = validate(source)
    report.update({'policy_version': POLICY_VERSION, 'started_utc': datetime.now(timezone.utc).isoformat(),
                   'status': 'validated_only', 'database': database,
                   'restart_policy': 'Never upsert or reset; retry in a NEW empty test database.',
                   'model_sha256': {entity: digest((legacy.ROOT/'api-csharp'/'Domain'/(entity+'.cs')).read_bytes())
                                    for _, entity in legacy.ENTITIES}})
    code = 0
    try:
        if not source_unchanged(source, report):
            raise ValueError('Source files changed during validation')
        if database is not None:
            command = (['docker', 'exec', '-i', container, 'psql'] if container else ['psql', '-h', host, '-p', str(port)])
            command += ['-X', '-A', '-t', '-q', '-v', 'ON_ERROR_STOP=1', '-U', user, '-d', database]
            result = subprocess.run(command, input=sql_for(tables), text=True, capture_output=True)
            if result.returncode:
                # No source values, COPY payloads, connection secrets or stderr details in the report.
                report['status'] = 'not_imported' if 'ERROR:' in result.stderr else 'commit_not_confirmed'
                report['error'] = ('SQL import refused or rolled back; no records committed by this run.'
                    if report['status'] == 'not_imported' else
                    'Connection/process failed; commit is not confirmed. Inspect the test database before retrying.')
                if report['status'] == 'commit_not_confirmed':
                    for entry in report['files'].values():
                        entry['imported'] = None
                code = 2
            else:
                measurements = json.loads(next(line for line in reversed(result.stdout.splitlines()) if line.startswith('{')))
                actual = measurements['inventory_totals']
                actual['quantity_available'] = actual['quantity_on_hand'] - actual['quantity_allocated']
                report.update({'status': 'imported', 'database_counts': measurements['counts'],
                               'database_inventory_totals': actual})
                for entry in report['files'].values():
                    entry['imported'] = entry['accepted']
                if measurements['counts'] != report['expected_counts'] or actual != report['expected_inventory_totals']:
                    raise RuntimeError('Database measurements differ from accepted source')
    except (OSError, ValueError, RuntimeError) as exc:
        report['status'] = 'failed'
        report['error'] = type(exc).__name__ + ': ' + str(exc)
        code = 2
    report['source_hashes_unchanged'] = source_unchanged(source, report)
    if not report['source_hashes_unchanged']:
        report['status'] = 'source_changed_externally'
        code = 2
    report['summary'] = {name: (None if any(f[name] is None for f in report['files'].values()) else sum(f[name] for f in report['files'].values()))
                         for name in ('read', 'accepted', 'imported', 'rejected')}
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
    report_path.with_suffix('.md').write_text(markdown_report(report), encoding='utf-8')
    return code, report


def markdown_report(report):
    summary = report['summary']
    lines = ['# CH-013 — Importverslag', '',
             'Status: **' + report['status'] + '**. Tijd: ' + report['started_utc'], '',
             '| Bestand | Gelezen | Geaccepteerd | Geïmporteerd | Afgewezen |',
             '|---|---:|---:|---:|---:|']
    for table, entry in report['files'].items():
        lines.append('| ' + table + '.json | ' + ' | '.join(str(entry[k]) for k in ('read','accepted','imported','rejected')) + ' |')
    lines += ['', '**Totaal:** ' + ', '.join(k + '=' + str(v) for k,v in summary.items()), '',
              'Bronhashes ongewijzigd: **' + str(report['source_hashes_unchanged']) + '**.', '',
              '## Voorraadreconciliatie', '', '| Hoeveelheid | Geaccepteerde bron | Database |', '|---|---:|---:|']
    for field, expected in report['expected_inventory_totals'].items():
        actual = report.get('database_inventory_totals', {}).get(field, 'niet bevestigd')
        lines.append('| ' + field + ' | ' + str(expected) + ' | ' + str(actual) + ' |')
    lines += ['', '## Afwijzingen', '', 'Rijnummers beginnen bij 1 in de JSON-array; keys bevatten alleen technische IDs.']
    for table, entry in report['files'].items():
        for rejection in entry['rejections']:
            lines.append('- ' + table + '.json, rij ' + str(rejection['row']) + ', key ' + str(rejection['key']) + ': ' + '; '.join(rejection['reasons']))
    if not summary['rejected']:
        lines.append('Geen records afgewezen.')
    if 'error' in report:
        lines += ['', '## Uitvoeringsmelding', '', report['error']]
    lines += ['', 'Bij herhaling: gebruik een nieuwe lege testdatabase; er is geen automatische reset of upsert.',
              'Bronhashes, omzettingen en volledige details staan in het bijbehorende JSON-verslag.', '']
    return '\n'.join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=legacy.ROOT/'data')
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--database', help='Omit for validation only; otherwise cargohub_import_test_<name>')
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', default='55432')
    parser.add_argument('--user', default='cargohub_test')
    parser.add_argument('--container', help='Optional local Docker PostgreSQL container; uses docker exec')
    args = parser.parse_args()
    try:
        code, report = run(args.source, args.report, args.database, args.host, args.port, args.user, args.container)
    except (OSError, ValueError, TypeError) as exc:
        print('Preflight failed: ' + str(exc), file=sys.stderr)
        return 2
    print(json.dumps({'status': report['status'], **report['summary'], 'report': str(args.report)}, ensure_ascii=False))
    return code


if __name__ == '__main__':
    sys.exit(main())
