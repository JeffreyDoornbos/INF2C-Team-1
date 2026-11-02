"""Meaningful CH-013 validation cases; standard library, no source writes."""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'database/postgresql'))
import import_test_json as importer


def fixture():
    data = {}
    for table, entity in importer.legacy.ENTITIES:
        row = {}
        for column, kind in importer.legacy.entity_columns(entity):
            row[column] = (1 if kind.rstrip('?') == 'int' else 1.5 if kind == 'decimal' else
                           '2026-01-15T12:00:00Z' if kind == 'DateTimeOffset' else
                           '2026-01-15' if kind == 'DateOnly' else None if kind.endswith('?') else 'test')
        if table in importer.STATUSES:
            column, allowed = importer.STATUSES[table]
            row[column] = {'order':'Pending', 'shipment':'Scheduled', 'transfer':'Scheduled'}[table]
        data[table] = [row]
    data['location'].append({**data['location'][0], 'id':2})
    data['transfer'][0]['to_location_id'] = 2
    data['inventory'][0]['quantity_on_hand'] = 10
    return data


def write_fixture(path, data):
    for table, rows in data.items():
        (path/(table+'.json')).write_text(json.dumps(rows), encoding='utf-8')


class ValidationTests(unittest.TestCase):
    def validate(self, edit=None):
        data = fixture()
        if edit:
            edit(data)
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder)
            write_fixture(source, data)
            return importer.validate(source)[1]

    def reasons(self, report, table):
        return ' '.join(reason for row in report['files'][table]['rejections'] for reason in row['reasons'])

    def test_valid_relational_fixture(self):
        r = self.validate()
        self.assertEqual(sum(f['rejected'] for f in r['files'].values()), 0)
        self.assertEqual(r['expected_inventory_totals']['quantity_available'],9)

    def test_all_duplicate_primary_keys_rejected_and_children_reported(self):
        r=self.validate(lambda d:d['warehouse'].append(copy.deepcopy(d['warehouse'][0])))
        self.assertEqual(r['files']['warehouse']['rejected'],2)
        self.assertIn('missing_or_rejected_reference:warehouse_id',self.reasons(r,'location'))
        self.assertIn('missing_or_rejected_reference:from_location_id',self.reasons(r,'transfer'))

    def test_duplicate_composite_inventory_key(self):
        r=self.validate(lambda d:d['inventory'].append(copy.deepcopy(d['inventory'][0])))
        self.assertEqual(r['files']['inventory']['rejected'],2)
        self.assertEqual(r['expected_inventory_totals']['quantity_on_hand'],0)

    def test_duplicate_transfer_item_with_different_id(self):
        r=self.validate(lambda d:d['transfer_item'].append({**d['transfer_item'][0],'id':2}))
        self.assertEqual(r['files']['transfer_item']['rejected'],2)
        self.assertIn('duplicate_transfer_item',self.reasons(r,'transfer_item'))

    def test_orphan_reference(self):
        r=self.validate(lambda d:d['transfer_item'][0].update(item_id=999))
        self.assertIn('missing_or_rejected_reference:item_id',self.reasons(r,'transfer_item'))

    def test_zero_and_negative_amount(self):
        for amount in (0,-2):
            with self.subTest(amount=amount):
                r=self.validate(lambda d:d['transfer_item'][0].update(amount=amount))
                self.assertIn('amount_must_be_positive',self.reasons(r,'transfer_item'))

    def test_inventory_rules(self):
        for change,reason in [({'quantity_expected':-1},'negative_inventory'),
                              ({'quantity_allocated':11},'allocation_exceeds')]:
            with self.subTest(change=change):
                r=self.validate(lambda d:d['inventory'][0].update(change))
                self.assertIn(reason,self.reasons(r,'inventory'))

    def test_invalid_status_and_locations(self):
        r=self.validate(lambda d:d['transfer'][0].update(transfer_status='Unknown',to_location_id=1))
        self.assertIn('unknown_status',self.reasons(r,'transfer'))
        self.assertIn('source_equals_destination',self.reasons(r,'transfer'))

    def test_unknown_missing_and_null_fields(self):
        for edit in (lambda d:d['item'][0].update(unknown=1),
                     lambda d:d['item'][0].pop('code'),
                     lambda d:d['item'][0].update(code=None)):
            r=self.validate(edit)
            self.assertEqual(r['files']['item']['rejected'],1)

    def test_numeric_rounding_is_rejected(self):
        r=self.validate(lambda d:d['order_item'][0].update(unit_price=1.12345))
        self.assertIn('rounded',self.reasons(r,'order_item'))

    def test_timestamps_need_explicit_timezone(self):
        r=self.validate(lambda d:d['warehouse'][0].update(created_at='2026-01-15T12:00:00'))
        self.assertIn('no timezone',self.reasons(r,'warehouse'))

    def test_utc_date_conversion_and_numeric_commodity(self):
        r=self.validate(lambda d:(d['order'][0].update(order_date='2026-01-15T23:30:00-02:00'),
                                  d['item'][0].update(commodity_code=123)))
        self.assertEqual(r['files']['order']['conversions_in_read_records']['order_date: UTC timestamp -> date'],1)
        self.assertEqual(r['files']['item']['rejected'],0)

    def test_database_and_report_safety_before_any_connection(self):
        with tempfile.TemporaryDirectory() as folder:
            source=Path(folder)/'data';source.mkdir();write_fixture(source,fixture())
            for db in ('cargohub','postgres','cargohub_import_test_x; DROP DATABASE postgres'):
                with self.assertRaises(ValueError):
                    importer.run(source,Path(folder)/'report.json',db)
            with self.assertRaises(ValueError):
                importer.run(source,Path(folder)/'report.json','cargohub_import_test_x',host='production.example')
            with self.assertRaises(ValueError):
                importer.run(source,source/'report.json')

    def test_validation_report_and_sources_unchanged(self):
        with tempfile.TemporaryDirectory() as folder:
            source=Path(folder)/'data';source.mkdir();write_fixture(source,fixture())
            before={p.name:p.read_bytes() for p in source.iterdir()}
            code,r=importer.run(source,Path(folder)/'report.json')
            self.assertEqual(code,0);self.assertEqual(r['status'],'validated_only')
            self.assertEqual(r['summary']['imported'],0)
            self.assertTrue(r['source_hashes_unchanged'])
            self.assertEqual(before,{p.name:p.read_bytes() for p in source.iterdir()})

    def test_invalid_json_stops_before_database(self):
        with tempfile.TemporaryDirectory() as folder:
            source=Path(folder);write_fixture(source,fixture())
            for raw in ('{broken', '[{"id":1,"id":2}]','[NaN]'):
                (source/'warehouse.json').write_text(raw)
                with self.assertRaises(ValueError):importer.validate(source)


if __name__=='__main__':
    unittest.main(verbosity=2)
