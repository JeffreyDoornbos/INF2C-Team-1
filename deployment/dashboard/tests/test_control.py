"""Check the privileged broker with mocked commands and locks, never real Docker."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import mock_open, patch

spec = importlib.util.spec_from_file_location('control', Path(__file__).resolve().parents[1] / 'control.py')
control = importlib.util.module_from_spec(spec)
spec.loader.exec_module(control)


class ControlTests(unittest.TestCase):
    def test_unknown_service_or_operation_is_rejected_before_opening_lock(self):
        with patch('builtins.open') as opened:
            for service, operation in [('db', 'stop'), ('proxy', 'restart'), ('v2', 'rm'), ('anything', 'start')]:
                with self.assertRaises(ValueError):
                    control.action(service, operation)
            opened.assert_not_called()

    def test_v2_uses_current_slot_and_reloads_proxy_after_start(self):
        for slot in ('blue', 'green'):
            with self.subTest(slot=slot), patch('builtins.open', mock_open()), \
                 patch.object(control.fcntl, 'flock'), patch.object(control, 'state', return_value={'active_slot': slot}), \
                 patch.object(control, 'run', return_value='') as run:
                control.action('v2', 'start')
                self.assertEqual(control.COMPOSE + ['start', 'api-' + slot], run.call_args_list[0].args[0])
                self.assertIn(control.COMPOSE + ['exec', '-T', 'proxy', 'nginx', '-s', 'reload'],
                              [call.args[0] for call in run.call_args_list])

    def test_python_actions_only_target_the_python_unit(self):
        with patch('builtins.open', mock_open()), patch.object(control.fcntl, 'flock'), \
             patch.object(control, 'run', return_value='') as run:
            control.action('v1', 'stop')
            self.assertEqual(['/usr/bin/systemctl', 'stop', 'cargohub-python.service'],
                             run.call_args_list[0].args[0])

    def test_busy_deploy_lock_prevents_commands(self):
        with patch('builtins.open', mock_open()), \
             patch.object(control.fcntl, 'flock', side_effect=BlockingIOError), \
             patch.object(control, 'run') as run:
            with self.assertRaises(ValueError):
                control.action('v2', 'restart')
            run.assert_not_called()

    def test_status_does_not_expose_container_environment(self):
        record = {'Id': 'container-id', 'Name': '/api-blue',
                  'Config': {'Env': ['SECRET=SHOULD_NOT_LEAK'],
                             'Labels': {'com.docker.compose.service': 'api-blue'}},
                  'State': {'Running': True, 'Status': 'running', 'StartedAt': '2026-11-18T01:00:00.000000000Z'}}
        def command(args, **kwargs):
            if 'inspect' in args:
                return json.dumps([record])
            if 'stats' in args:
                return json.dumps({'Name': 'api-blue', 'CPUPerc': '1%', 'MemUsage': '10MiB / 1GiB'})
            return 'container-id'
        with patch.object(control, 'state', return_value={'active_slot': 'blue'}), \
             patch.object(control, 'unit', return_value={'running': True}), \
             patch.object(control, 'run', side_effect=command):
            result = control.status()
            self.assertNotIn('SHOULD_NOT_LEAK', json.dumps(result))
            self.assertNotIn('Env', json.dumps(result))
            self.assertTrue(result['containers']['api-blue']['running'])

    def test_invalid_slot_in_deployment_file_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            Path(folder, 'deployment.json').write_text(json.dumps({'active_slot': '../../etc'}))
            with patch.object(control, 'BASE', Path(folder)), self.assertRaises(ValueError):
                control.state()


if __name__ == '__main__':
    unittest.main()
