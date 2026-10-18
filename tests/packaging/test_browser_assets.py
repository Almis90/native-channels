import importlib.util
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('browser_proof', ROOT / 'eng/prepare-browser-proof.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


class BrowserAssetTests(unittest.TestCase):
    def test_published_javascript_must_match_both_packages(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            feed, published = root / 'feed', root / 'wwwroot'
            feed.mkdir()
            for name in ('NativeChannels', 'Plugin.Maui.Currency'):
                with ZipFile(feed / f'{name}.1.0.0.nupkg', 'w') as archive:
                    archive.writestr('browser/module.mjs', b'export const packaged = true;')
                target = published / '_content' / name / 'module.mjs'
                target.parent.mkdir(parents=True)
                target.write_bytes(b'export const packaged = true;')
            self.assertEqual(2, len(proof.check_published_modules(published, feed)))
            target.write_bytes(b'checkout substitute')
            with self.assertRaisesRegex(ValueError, 'differs from package'):
                proof.check_published_modules(published, feed)

    def test_empty_javascript_payload_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with ZipFile(root / 'NativeChannels.1.0.0.nupkg', 'w') as archive:
                archive.writestr('README.md', 'no modules')
            with self.assertRaisesRegex(ValueError, 'Missing packaged JavaScript'):
                proof.check_published_modules(root, root)
