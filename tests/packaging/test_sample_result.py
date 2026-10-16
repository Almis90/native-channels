import importlib.util
import io
from contextlib import redirect_stdout
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / 'eng/wait-sample-result.py'
spec = importlib.util.spec_from_file_location('sample_result', SCRIPT)
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
SUCCESS = r'^PASS apple:.*view stress: 32 reconnects'


class SampleResultTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.paths = [self.root / 'library.txt', self.root / 'documents.txt']

    def test_empty_and_partial_files_are_pending_until_complete(self):
        for content in ('', 'PASS apple: methods passed; view stress: 3'):
            self.paths[0].write_text(content)
            self.assertIsNone(reader.read_result(self.paths, SUCCESS)[0])
        self.paths[0].write_text('PASS apple: methods passed; view stress: 32 reconnects')
        passed, result = reader.read_result(self.paths, SUCCESS)
        self.assertTrue(passed)
        self.assertIn('32 reconnects', result)

    def test_missing_first_location_does_not_hide_second_result(self):
        self.paths[1].write_text('PASS apple: view stress: 32 reconnects')
        self.assertTrue(reader.read_result(self.paths, SUCCESS)[0])

    def test_waiter_retries_until_writer_finishes(self):
        self.paths[0].write_text('')
        logs = [self.root / 'stdout.log', self.root / 'stderr.log']
        args = [str(SCRIPT), '--timeout', '3', '--success', SUCCESS,
                '--logs', *map(str, logs), *map(str, self.paths)]
        def finish_write(_):
            self.paths[0].write_text('PASS apple: view stress: 32 reconnects')
        output = io.StringIO()
        with mock.patch.object(sys, 'argv', args), \
                mock.patch.object(reader.time, 'sleep', side_effect=finish_write), redirect_stdout(output):
            self.assertEqual(reader.main(), 0)
        self.assertIn('32 reconnects', output.getvalue())

    def test_failure_is_not_hidden_by_pass_text_in_exception(self):
        self.paths[0].write_text('FAIL exception: expected PASS apple: view stress: 32 reconnects')
        self.assertFalse(reader.read_result(self.paths, SUCCESS)[0])

    def test_unrelated_pass_does_not_satisfy_framework_checks(self):
        self.paths[0].write_text('PASS native PCM playback')
        self.assertIsNone(reader.read_result(self.paths, SUCCESS)[0])

    def test_timeout_is_nonzero_and_includes_app_diagnostics(self):
        self.paths[0].write_text('')
        stdout = self.root / 'stdout.log'
        stderr = self.root / 'stderr.log'
        stdout.write_text('app started')
        stderr.write_text('native diagnostic')
        result = subprocess.run([
            sys.executable, str(SCRIPT), '--timeout', '0', '--success', SUCCESS,
            '--logs', str(stdout), str(stderr), *map(str, self.paths),
        ], text=True, capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('No complete sample result', result.stderr)
        self.assertIn('app started', result.stderr)
        self.assertIn('native diagnostic', result.stderr)
