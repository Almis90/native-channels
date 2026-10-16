"""Physical-device evidence must never be inferred from compilation or emulator results."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('android_device', ROOT / 'eng/verify-android-device.py')
device = importlib.util.module_from_spec(spec)
spec.loader.exec_module(device)


class PhysicalDeviceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.apk = self.root / 'framework.apk'
        with zipfile.ZipFile(self.apk, 'w') as archive:
            archive.writestr('lib/arm64-v8a/libmonodroid.so', b'fixture')
        self.adb = self.root / 'sdk/platform-tools/adb'
        self.adb.parent.mkdir(parents=True)
        self.adb.touch()

    def verify(self):
        return device.verify('physical-test', self.apk, 'a' * 40, self.adb, self.root / 'evidence')

    def test_rejects_emulator_before_install(self):
        with patch.object(device.subprocess, 'check_output', side_effect=['device', '1']), \
                patch.object(device.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'emulator'):
                self.verify()
            run.assert_not_called()

    def test_rejects_non_arm64_hardware_before_install(self):
        with patch.object(device.subprocess, 'check_output', side_effect=['device', '0', 'x86_64']), \
                patch.object(device.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'ARM64'):
                self.verify()
            run.assert_not_called()

    def test_records_actual_result_and_apk_hash(self):
        for code in (0, 1):
            with self.subTest(code=code), \
                    patch.object(device.subprocess, 'check_output', side_effect=['device', '0', 'arm64-v8a', 'Test phone', '15', '35']), \
                    patch.object(device.subprocess, 'run', return_value=subprocess.CompletedProcess([], code)) as run:
                self.assertEqual(code == 0, self.verify())
                self.assertEqual('physical-test', run.call_args.args[0][2])
                self.assertEqual(str(self.apk.resolve()), run.call_args.args[0][-1])
        records = [json.loads(p.read_text()) for p in (self.root / 'evidence').glob('*/verification.json')]
        self.assertEqual({True, False}, {r['executed'] for r in records})
        for record in records:
            self.assertEqual('physical-device', record['target'])
            self.assertEqual(64, len(record['apkSha256']))
            self.assertNotIn('physical-test', json.dumps(record))

    def test_timeout_retains_failure_record(self):
        with patch.object(device.subprocess, 'check_output', side_effect=['device', '0', 'arm64-v8a', 'Test phone', '15', '35']), \
                patch.object(device.subprocess, 'run', side_effect=subprocess.TimeoutExpired('verify', 150)):
            self.assertFalse(self.verify())
        record = json.loads(next((self.root / 'evidence').glob('*/verification.json')).read_text())
        self.assertFalse(record['executed'])
        self.assertIn('150 seconds', record['error'])


if __name__ == '__main__':
    unittest.main()
