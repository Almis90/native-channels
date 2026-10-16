"""Startup checks must preserve failures and exercise every requested launch."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('prepare_simulator', ROOT / 'eng/prepare-ios-simulator.py')
simulator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(simulator)


class SimulatorBootTests(unittest.TestCase):
    def test_boot_failure_propagates_and_preserves_cleanup_target(self):
        inventory = {'devices': {'ios': [{'name': 'iPhone test', 'udid': 'test-id', 'isAvailable': True}]}}
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'output'
            with patch.dict(os.environ, {'GITHUB_OUTPUT': str(output)}), \
                 patch.object(simulator.subprocess, 'check_output', return_value=json.dumps(inventory)), \
                 patch.object(simulator.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, 'bootstatus')):
                with self.assertRaises(subprocess.CalledProcessError):
                    simulator.prepare()
            self.assertEqual('device=test-id\n', output.read_text())

    def test_unavailable_iphone_is_not_selected(self):
        inventory = {'devices': {'ios': [{'name': 'iPhone unavailable', 'udid': 'missing', 'isAvailable': False}]}}
        with patch.object(simulator.subprocess, 'check_output', return_value=json.dumps(inventory)):
            with self.assertRaisesRegex(RuntimeError, 'No available iPhone'):
                simulator.prepare()


class AudioLaunchTests(unittest.TestCase):
    def run_script(self, runs, fail_at='0'):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'eng').mkdir()
            shutil.copy(ROOT / 'eng/verify-android-audio.sh', root / 'eng/verify-android-audio.sh')
            sdk = root / 'sdk'
            (sdk / 'platform-tools').mkdir(parents=True)
            adb = sdk / 'platform-tools/adb'
            adb.write_text('''#!/usr/bin/env python3
import os, pathlib, sys
args = sys.argv[3:]
state = pathlib.Path(os.environ['FAKE_ADB_STATE'])
count = int(state.read_text()) if state.exists() else 0
if args[:3] == ['shell', 'am', 'start']:
    state.write_text(str(count + 1))
elif args[:4] == ['shell', 'cmd', 'package', 'resolve-activity']:
    print('dev.nativechannels.audio.sample/MainActivity')
elif args[:2] == ['logcat', '-d']:
    if count == int(os.environ['FAKE_FAIL_AT']):
        print('AUDIO_SAMPLE FAIL audio_focus_denied')
    else:
        print('AUDIO_SAMPLE PASS native PCM playback')
''')
            adb.chmod(0o755)
            state = root / 'launches'
            env = {**os.environ, 'ANDROID_HOME': str(sdk), 'AUDIO_VERIFICATION_RUNS': str(runs),
                   'FAKE_ADB_STATE': str(state), 'FAKE_FAIL_AT': fail_at}
            result = subprocess.run(['bash', str(root / 'eng/verify-android-audio.sh'), 'emulator-test', 'Release', 'android-x64'],
                                    env=env, text=True, capture_output=True, timeout=10)
            return result, int(state.read_text()) if state.exists() else 0

    def test_all_cold_launches_are_required(self):
        result, launches = self.run_script(5)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(5, launches)

    def test_failed_launch_is_not_retried_or_hidden_by_previous_pass(self):
        result, launches = self.run_script(5, fail_at='2')
        self.assertEqual(1, result.returncode)
        self.assertEqual(2, launches)
        self.assertIn('2/5 failed', result.stderr)

    def test_invalid_launch_count_is_rejected_before_installation(self):
        for count in ('0', '21', 'five'):
            result, launches = self.run_script(count)
            self.assertEqual(2, result.returncode)
            self.assertEqual(0, launches)


if __name__ == '__main__':
    unittest.main()
