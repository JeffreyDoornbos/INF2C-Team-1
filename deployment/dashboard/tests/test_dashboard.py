"""Regression tests for the admin boundary; no real services are stopped."""
import http.client
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[1] / 'server.py'
spec = importlib.util.spec_from_file_location('dashboard', MODULE)
dashboard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dashboard)


class DashboardTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.config = {'username': 'groep1', 'salt': 'ab' * 16,
                      'password_hash': dashboard.password_hash('test-password', 'ab' * 16),
                      'origin': 'https://145.24.237.22', 'acme_root': cls.temporary.name}
        cls.server = dashboard.ThreadingHTTPServer(('127.0.0.1', 0), dashboard.Handler)
        cls.server.state = dashboard.State(cls.config, start_collector=False)
        threading.Thread(target=cls.server.serve_forever, daemon=True).start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.temporary.cleanup()

    def setUp(self):
        self.server.state.sessions.clear()
        self.server.state.attempts.clear()

    def request(self, method, path, body=None, cookie=None, csrf=None, origin=True):
        headers = {}
        if method == 'POST':
            headers['Content-Type'] = 'application/json'
            if origin:
                headers['Origin'] = self.config['origin']
        if cookie:
            headers['Cookie'] = cookie
        if csrf:
            headers['X-CSRF-Token'] = csrf
        connection = http.client.HTTPConnection('127.0.0.1', self.server.server_port, timeout=5)
        connection.request(method, path, json.dumps(body) if body is not None else None, headers)
        response = connection.getresponse()
        value = response.read()
        result = response.status, dict(response.getheaders()), value
        connection.close()
        return result

    def login(self):
        status, headers, _ = self.request('POST', '/beheer/api/login',
                                         {'username': 'groep1', 'password': 'test-password'})
        self.assertEqual(200, status)
        cookie = headers['Set-Cookie'].split(';')[0]
        _, _, body = self.request('GET', '/beheer/api/status', cookie=cookie)
        return cookie, json.loads(body)['csrf']

    def test_status_requires_login(self):
        self.assertEqual(401, self.request('GET', '/beheer/api/status')[0])

    def test_static_page_contains_no_live_server_data(self):
        code, headers, body = self.request('GET', '/beheer/')
        self.assertEqual(200, code)
        self.assertIn(b'login-form', body)
        self.assertNotIn(b'password_hash', body)
        self.assertIn("frame-ancestors 'none'", headers['Content-Security-Policy'])

    def test_wrong_password_is_rejected(self):
        self.assertEqual(401, self.request('POST', '/beheer/api/login',
            {'username': 'groep1', 'password': 'wrong'})[0])

    def test_correct_password_wrong_user_is_rejected(self):
        self.assertEqual(401, self.request('POST', '/beheer/api/login',
            {'username': 'wrong', 'password': 'test-password'})[0])

    def test_login_requires_matching_origin(self):
        self.assertEqual(403, self.request('POST', '/beheer/api/login',
            {'username': 'groep1', 'password': 'test-password'}, origin=False)[0])

    def test_login_rate_is_limited(self):
        for _ in range(6):
            self.request('POST', '/beheer/api/login', {'username': 'wrong', 'password': 'wrong'})
        self.assertEqual(429, self.request('POST', '/beheer/api/login',
            {'username': 'groep1', 'password': 'test-password'})[0])

    def test_cookie_is_secure_and_not_readable_by_javascript(self):
        _, headers, _ = self.request('POST', '/beheer/api/login',
            {'username': 'groep1', 'password': 'test-password'})
        for value in ('Secure', 'HttpOnly', 'SameSite=Strict', 'Path=/beheer'):
            self.assertIn(value, headers['Set-Cookie'])

    def test_action_requires_login(self):
        self.assertEqual(401, self.request('POST', '/beheer/api/action',
            {'service': 'v2', 'action': 'stop'})[0])

    def test_action_requires_csrf_token(self):
        cookie, _ = self.login()
        self.assertEqual(403, self.request('POST', '/beheer/api/action',
            {'service': 'v2', 'action': 'stop'}, cookie=cookie)[0])

    def test_database_and_arbitrary_commands_cannot_be_requested(self):
        cookie, csrf = self.login()
        with patch.object(dashboard, 'helper') as helper:
            for service, action in [('db', 'stop'), ('v2', 'rm -rf /'), ('proxy', 'restart')]:
                self.assertEqual(400, self.request('POST', '/beheer/api/action',
                    {'service': service, 'action': action}, cookie=cookie, csrf=csrf)[0])
            helper.assert_not_called()

    def test_allowlisted_action_calls_fixed_broker_arguments(self):
        cookie, csrf = self.login()
        with patch.object(dashboard, 'helper', return_value={'ok': True}) as helper:
            self.assertEqual(200, self.request('POST', '/beheer/api/action',
                {'service': 'v2', 'action': 'start'}, cookie=cookie, csrf=csrf)[0])
            helper.assert_called_once_with('action', 'v2', 'start')

    def test_logout_invalidates_session(self):
        cookie, csrf = self.login()
        self.assertEqual(200, self.request('POST', '/beheer/api/logout', {}, cookie, csrf)[0])
        self.assertEqual(401, self.request('GET', '/beheer/api/status', cookie=cookie)[0])

    def test_expired_session_is_rejected(self):
        cookie, _ = self.login()
        for value in self.server.state.sessions.values():
            value['expires'] = 0
        self.assertEqual(401, self.request('GET', '/beheer/api/status', cookie=cookie)[0])

    def test_acme_token_is_public_but_traversal_is_rejected(self):
        target = Path(self.temporary.name) / '.well-known/acme-challenge'
        target.mkdir(parents=True, exist_ok=True)
        (target / 'safe-token').write_text('challenge-proof')
        self.assertEqual(b'challenge-proof', self.request('GET', '/.well-known/acme-challenge/safe-token')[2])
        self.assertEqual(404, self.request('GET', '/.well-known/acme-challenge/../../config.json')[0])

    def test_unknown_files_are_not_served(self):
        for path in ('/beheer/server.py', '/beheer/../server.py', '/beheer/config.json'):
            self.assertEqual(404, self.request('GET', path)[0])


if __name__ == '__main__':
    unittest.main()
