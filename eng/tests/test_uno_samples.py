"""Acceptance and package-origin guards for Uno; no UI or device required."""
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
import zipfile
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
import uno_samples as uno


class UnoSamplesTests(unittest.TestCase):
    def test_pdf_private_runtime_references_match_packaged_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / 'output'
            output.mkdir()
            with zipfile.ZipFile(root / f'Plugin.Uno.PdfViewer.{uno.VERSION}.nupkg', 'w') as archive:
                for name in ('Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll'):
                    archive.writestr('buildTransitive/windows/' + name, name.encode())
                    (output / name).write_bytes(name.encode())
            self.assertEqual(len(uno.audit_pdf_runtime(root, output)), 2)
            (output / 'WinRT.Runtime.dll').write_bytes(b'stale projection')
            with self.assertRaisesRegex(ValueError, 'runtime asset differs'):
                uno.audit_pdf_runtime(root, output)

    def test_protocol_result_cannot_claim_missing_identity_or_hide_limitations(self):
        good = dict(sample='PdfViewer', checks='passed', limitation='Protocol only')
        uno.validate_result(good, 'PdfViewer')
        for bad in ({**good, 'sample': 'Currency'}, {**good, 'checks': 'failed'},
                    {**good, 'limitation': ''}, {'checks': 'passed'}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                uno.validate_result(bad, 'PdfViewer')

    def consumer(self, root):
        feed = root / 'feed'
        cache = root / 'cache'
        feed.mkdir()
        assets = dict(libraries={}, packageFolders={str(cache): {}})
        for name in ('NativeChannels', 'NativeChannels.Core', 'NativeChannels.Uno'):
            relative = name.lower() + '/0.3.0-preview.6'
            location = cache / relative
            location.mkdir(parents=True)
            (location / '.nupkg.metadata').write_text(json.dumps({'source': str(feed)}))
            (location / (name.lower() + '.0.3.0-preview.6.nupkg')).write_bytes(name.encode())
            (feed / (name + '.0.3.0-preview.6.nupkg')).write_bytes(name.encode())
            assets['libraries'][name + '/0.3.0-preview.6'] = dict(type='package', path=relative)
        return assets, feed, cache

    def test_cached_bytes_must_match_the_run_feed(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets, feed, cache = self.consumer(Path(temporary))
            self.assertEqual(len(uno.audit_consumer(assets, feed, 'Interop')), 3)
            next(cache.rglob('*.nupkg')).write_bytes(b'stale package')
            with self.assertRaisesRegex(ValueError, 'differs'):
                uno.audit_consumer(assets, feed, 'Interop')

    def test_matching_bytes_from_a_different_source_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets, feed, cache = self.consumer(Path(temporary))
            next(cache.rglob('.nupkg.metadata')).write_text(json.dumps({'source': str(feed.parent / 'old-feed')}))
            with self.assertRaisesRegex(ValueError, 'this run'):
                uno.audit_consumer(assets, feed, 'Interop')

    def test_missing_plugin_and_project_references_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets, feed, _ = self.consumer(Path(temporary))
            with self.assertRaisesRegex(ValueError, 'dependencies'):
                uno.audit_consumer(assets, feed, 'Currency')
            assets['libraries']['Injected/1.0'] = dict(type='project')
            with self.assertRaisesRegex(ValueError, 'project reference'):
                uno.audit_consumer(assets, feed, 'Interop')

    def test_unavailable_platforms_produce_blocked_evidence_and_nonzero_exit(self):
        for target in ('android', 'ios', 'browser'):
            with self.subTest(target=target), tempfile.TemporaryDirectory() as temporary:
                args = SimpleNamespace(platform=target, build_only=False, sample=None, manifest=None,
                    framework_packages=None, packages_only=False, device=None, virtual_audio=False,
                    arch=None, require_clean=False)
                with patch.object(uno, 'ROOT', Path(temporary)):
                    self.assertEqual(uno.run(args, lambda: {'commit': 'a' * 40, 'status': ''}), 2)
                result = json.loads(next(Path(temporary).rglob('summary.json')).read_text())
                self.assertEqual(result['acceptance'], 'blocked')
                self.assertEqual(len(result['results']), 5)
                self.assertTrue(all(item['smoke'] == 'not-run' for item in result['results']))

    def test_all_six_platforms_are_explicitly_accounted_for(self):
        self.assertEqual(set(uno.PLATFORMS), set(uno.GAPS))
        self.assertTrue(all(uno.GAPS.values()))


if __name__ == '__main__':
    unittest.main()
