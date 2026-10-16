import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('shared_hosting', Path(__file__).resolve().parents[2] / 'eng/verify-shared-hosting.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


class SharedHostingTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.feed = self.root / 'feed'
        self.feed.mkdir()
        self.assets = {}
        self.names = ['NativeChannels.Core', 'NativeChannels', 'NativeChannels.Native.Android', 'Plugin.Maui.Currency']
        for host in ('maui', 'avalonia'):
            cache = self.root / host
            self.assets[host] = {'packageFolders': {str(cache): {}}, 'libraries': {}}
            for name in self.names:
                relative = name.lower() + '/1.0.0'
                directory = cache / relative
                directory.mkdir(parents=True)
                (directory / '.nupkg.metadata').write_text(json.dumps({'source': str(self.feed)}))
                (directory / f'{name.lower()}.1.0.0.nupkg').write_bytes(name.encode())
                (self.feed / f'{name}.1.0.0.nupkg').write_bytes(name.encode())
                self.assets[host]['libraries'][name + '/1.0.0'] = {'type': 'package', 'path': relative}
            host_package = 'NativeChannels.Maui' if host == 'maui' else 'Avalonia.Android'
            self.assets[host]['libraries'][host_package + '/1.0.0'] = {'type': 'package'}

    def test_identical_archives_from_separate_caches_pass(self):
        records = proof.package_proof(self.assets, self.feed)
        self.assertEqual(records['maui'], records['avalonia'])
        self.assertEqual(4, len(records['avalonia']))

    def test_maui_transitive_dependencies_are_rejected(self):
        for name in ('NativeChannels.Maui', 'Microsoft.Maui.Controls', 'Microsoft.Maui.Essentials'):
            assets = copy.deepcopy(self.assets)
            assets['avalonia']['libraries'][name + '/1.0'] = {'type': 'package'}
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'MAUI dependency'):
                proof.package_proof(assets, self.feed)

    def test_wrong_package_bytes_and_sources_are_rejected(self):
        directory = self.root / 'avalonia/plugin.maui.currency/1.0.0'
        archive = directory / 'plugin.maui.currency.1.0.0.nupkg'
        original = archive.read_bytes()
        archive.write_bytes(b'stale package')
        with self.assertRaisesRegex(ValueError, 'bytes differ'):
            proof.package_proof(self.assets, self.feed)
        archive.write_bytes(original)
        (directory / '.nupkg.metadata').write_text(json.dumps({'source': 'https://api.nuget.org/v3/index.json'}))
        with self.assertRaisesRegex(ValueError, 'this feed'):
            proof.package_proof(self.assets, self.feed)

    def test_missing_package_and_project_reference_are_rejected(self):
        assets = copy.deepcopy(self.assets)
        del assets['avalonia']['libraries']['Plugin.Maui.Currency/1.0.0']
        with self.assertRaisesRegex(ValueError, 'missing shared packages'):
            proof.package_proof(assets, self.feed)
        self.assets['avalonia']['libraries']['SourceProject/1.0'] = {'type': 'project'}
        with self.assertRaisesRegex(ValueError, 'project reference'):
            proof.package_proof(self.assets, self.feed)

    def test_shared_cache_is_rejected(self):
        self.assets['avalonia']['packageFolders'] = self.assets['maui']['packageFolders']
        with self.assertRaisesRegex(ValueError, 'separate fresh'):
            proof.package_proof(self.assets, self.feed)

    def test_success_requires_matching_complete_runtime_results(self):
        result = 'PASS android: USD=$, EUR=€; normalization, errors, concurrency, cancellation'
        logs = {host: f'I/DOTNET: {marker} {result}' for host, marker in proof.MARKERS.items()}
        self.assertEqual({'maui': result, 'avalonia': result}, proof.runtime_proof(logs))
        for invalid in ('', 'CURRENCY_AVALONIA FAIL crash', logs['avalonia'] * 2,
                        'CURRENCY_AVALONIA PASS android: USD=$', logs['avalonia'].replace('USD=$', 'USD=US$')):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                proof.runtime_proof({**logs, 'avalonia': invalid})


if __name__ == '__main__':
    unittest.main()
