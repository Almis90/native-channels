import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('compatibility', ROOT / 'eng/verify-api-compatibility.py')
compatibility = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compatibility)


class CompatibilityTests(unittest.TestCase):
    def test_baseline_pins_all_framework_packages_and_core_version(self):
        manifest = json.loads((ROOT / 'eng/api-baseline.json').read_text())
        self.assertEqual({'NativeChannels.Core', 'NativeChannels', 'NativeChannels.Maui',
                          'NativeChannels.Native.Apple', 'NativeChannels.Native.Android'}, set(manifest['packages']))
        for digest in manifest['packages'].values():
            self.assertRegex(digest, r'^[0-9a-f]{64}$')
        project = ET.parse(ROOT / 'src/NativeChannels.Core/NativeChannels.Core.csproj')
        self.assertEqual(manifest['version'], project.findtext('.//PackageValidationBaselineVersion'))

    def test_modified_cached_baseline_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'nativechannels.core.0.1.0-preview.1.nupkg'
            path.write_bytes(b'expected')
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            self.assertEqual(path, compatibility.baseline(Path(folder), 'NativeChannels.Core', '0.1.0-preview.1', digest))
            path.write_bytes(b'replaced')
            with self.assertRaisesRegex(ValueError, 'hash mismatch'):
                compatibility.baseline(Path(folder), 'NativeChannels.Core', '0.1.0-preview.1', digest)

    def test_mobile_feed_cannot_pass_combined_gate(self):
        with tempfile.TemporaryDirectory() as folder:
            def package(filename, tfms):
                path = Path(folder) / filename
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr('NativeChannels.nuspec', '<package><metadata><id>NativeChannels</id><version>1.0.0</version></metadata></package>')
                    for tfm in tfms:
                        archive.writestr(f'lib/{tfm}/NativeChannels.dll', b'fixture')
                return path
            mobile = ['net10.0-android36.0', 'net10.0-ios26.0', 'net10.0-maccatalyst26.0']
            baseline = package('baseline.nupkg', mobile + ['net10.0-windows10.0.19041'])
            candidate = package('candidate.nupkg', mobile)
            compatibility.validate_targets(candidate, baseline, 'NativeChannels', '1.0.0', '1.0.0', 'mobile')
            with self.assertRaises(ValueError):
                compatibility.validate_targets(candidate, baseline, 'NativeChannels', '1.0.0', '1.0.0', 'combined')
            incomplete = package('missing-ios.nupkg', mobile[:1])
            with self.assertRaises(ValueError):
                compatibility.validate_targets(incomplete, baseline, 'NativeChannels', '1.0.0', '1.0.0', 'mobile')

    def test_release_gate_checks_final_combined_packages(self):
        workflow = (ROOT / '.github/workflows/release-packages.yml').read_text()
        check = workflow.index('python3 eng/verify-api-compatibility.py --scope combined')
        self.assertLess(workflow.index('mv artifacts/combined-packages artifacts/packages'), check)
        self.assertLess(check, workflow.index('tar -czf artifacts/package-feed.tar.gz'))
        self.assertNotIn('--noWarn', (ROOT / 'eng/verify-api-compatibility.py').read_text())
