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
        self.create_assets()

    def create_assets(self, platform='android'):
        self.assets = {}
        self.names = ['NativeChannels.Core', 'NativeChannels', 'Plugin.Maui.Currency']
        if platform != 'windows':
            self.names.append('NativeChannels.Native.Apple' if platform in {'ios', 'macos'} else 'NativeChannels.Native.Android')
        for host in (['avalonia'] if platform == 'macos' else ['maui', 'avalonia']):
            cache = self.root / host
            self.assets[host] = {'packageFolders': {str(cache): {}}, 'libraries': {}, 'targets': {f'net10.0-{platform}26.0': {}}}
            for name in self.names:
                relative = name.lower() + '/1.0.0'
                directory = cache / relative
                directory.mkdir(parents=True, exist_ok=True)
                (directory / '.nupkg.metadata').write_text(json.dumps({'source': str(self.feed)}))
                (directory / f'{name.lower()}.1.0.0.nupkg').write_bytes(name.encode())
                (self.feed / f'{name}.1.0.0.nupkg').write_bytes(name.encode())
                self.assets[host]['libraries'][name + '/1.0.0'] = {'type': 'package', 'path': relative}
            host_package = 'NativeChannels.Maui' if host == 'maui' else proof.PLATFORMS[platform]['host']
            self.assets[host]['libraries'][host_package + '/1.0.0'] = {'type': 'package'}

    def test_macos_proof_requires_appkit_target_and_desktop_packages(self):
        self.create_assets('macos')
        records = proof.package_proof(self.assets, self.feed, 'macos')
        self.assertEqual({'avalonia'}, set(records))
        self.assertEqual(4, len(records['avalonia']))
        for target in ('net10.0-maccatalyst26.0', 'net10.0', 'net10.0-ios26.0'):
            self.assets['avalonia']['targets'] = {target: {}}
            with self.subTest(target=target), self.assertRaisesRegex(ValueError, 'native macOS target'):
                proof.package_proof(self.assets, self.feed, 'macos')

    def test_macos_cannot_resolve_a_maui_dependency(self):
        self.create_assets('macos')
        self.assets['avalonia']['libraries']['Microsoft.Maui.Core/1.0'] = {'type': 'package'}
        with self.assertRaisesRegex(ValueError, 'MAUI dependency'):
            proof.package_proof(self.assets, self.feed, 'macos')

    def test_identical_archives_from_separate_caches_pass(self):
        records = proof.package_proof(self.assets, self.feed)
        self.assertEqual(records['maui'], records['avalonia'])
        self.assertEqual(4, len(records['avalonia']))

    def test_ios_proof_requires_apple_binding_and_ios_avalonia_host(self):
        self.create_assets('ios')
        records = proof.package_proof(self.assets, self.feed, 'ios')
        self.assertEqual(records['maui'], records['avalonia'])
        self.assertIn('NativeChannels.Native.Apple/1.0.0', records['avalonia'])
        with self.assertRaisesRegex(ValueError, 'missing shared packages'):
            proof.package_proof(self.assets, self.feed, 'android')
        del self.assets['avalonia']['libraries']['avalonia.ios/1.0.0']
        self.assets['avalonia']['libraries']['Avalonia.Android/1.0.0'] = {'type': 'package'}
        with self.assertRaisesRegex(ValueError, 'Avalonia ios package is missing'):
            proof.package_proof(self.assets, self.feed, 'ios')

    def test_android_package_graph_cannot_satisfy_ios_proof(self):
        with self.assertRaisesRegex(ValueError, 'missing shared packages'):
            proof.package_proof(self.assets, self.feed, 'ios')

    def test_windows_proof_uses_three_shared_archives_without_mobile_bindings(self):
        self.create_assets('windows')
        records = proof.package_proof(self.assets, self.feed, 'windows')
        self.assertEqual(records['maui'], records['avalonia'])
        self.assertEqual(3, len(records['avalonia']))
        for name in ('NativeChannels.Native.Apple', 'NativeChannels.Native.Android'):
            assets = copy.deepcopy(self.assets)
            assets['maui']['libraries'][name + '/1.0.0'] = {'type': 'package'}
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'mobile binding'):
                proof.package_proof(assets, self.feed, 'windows')

    def test_windows_avalonia_requires_win32_without_maui_or_winui(self):
        self.create_assets('windows')
        for name in ('Microsoft.Maui.Controls', 'NativeChannels.Maui', 'Microsoft.WindowsAppSDK'):
            assets = copy.deepcopy(self.assets)
            assets['avalonia']['libraries'][name + '/1.0.0'] = {'type': 'package'}
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'dependency'):
                proof.package_proof(assets, self.feed, 'windows')
        del self.assets['avalonia']['libraries']['avalonia.win32/1.0.0']
        with self.assertRaisesRegex(ValueError, 'Avalonia windows package is missing'):
            proof.package_proof(self.assets, self.feed, 'windows')

    def test_runtime_identifiers_cannot_cross_platforms(self):
        for runtime in ('win-x64', 'win-arm64'):
            self.assertEqual(runtime, proof.runtime_identifier('windows', runtime))
        for platform, runtime in (('windows', 'iossimulator-arm64'), ('ios', 'win-x64'), ('android', 'win-arm64')):
            with self.subTest(platform=platform), self.assertRaisesRegex(ValueError, 'Unsupported runtime'):
                proof.runtime_identifier(platform, runtime)

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

    def add_shared_app(self, host='avalonia', project_path='../Currency/Currency.csproj'):
        self.assets[host]['project'] = {'restore': {'projectPath': str(
            Path(__file__).resolve().parents[2] / 'samples/Avalonia/Currency/Currency.Android/Currency.Android.csproj')}}
        self.assets[host]['libraries']['Currency/1.0.0'] = {
            'type': 'project', 'path': project_path, 'msbuildProject': project_path}

    def test_template_shared_ui_project_is_allowed_for_avalonia(self):
        self.add_shared_app()
        self.assertEqual(4, len(proof.package_proof(self.assets, self.feed)['avalonia']))

    def test_windows_desktop_head_accepts_shared_ui_and_still_checks_three_archives(self):
        self.create_assets('windows')
        self.add_shared_app()
        self.assets['avalonia']['project']['restore']['projectPath'] = str(
            Path(__file__).resolve().parents[2]
            / 'samples/Avalonia/Currency/Currency.Desktop/Currency.Desktop.csproj')
        self.assertEqual(3, len(proof.package_proof(self.assets, self.feed, 'windows')['avalonia']))

    def test_shared_ui_allowance_cannot_bypass_native_package_checks(self):
        self.add_shared_app()
        for path in ('../../../src/NativeChannels/NativeChannels.csproj', '../Other/Currency.csproj'):
            self.add_shared_app(project_path=path)
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, 'project reference'):
                proof.package_proof(self.assets, self.feed)

    def test_maui_and_unknown_heads_cannot_use_shared_ui_allowance(self):
        self.add_shared_app(host='maui')
        with self.assertRaisesRegex(ValueError, 'project reference'):
            proof.package_proof(self.assets, self.feed)
        del self.assets['maui']['libraries']['Currency/1.0.0']
        self.add_shared_app()
        self.assets['avalonia']['project']['restore']['projectPath'] = '/tmp/Unknown.csproj'
        with self.assertRaisesRegex(ValueError, 'project reference'):
            proof.package_proof(self.assets, self.feed)

    def test_success_requires_matching_complete_runtime_results(self):
        result = 'PASS android: USD=$, EUR=€; normalization, errors, concurrency, cancellation'
        logs = {host: f'I/DOTNET: {marker} {result}' for host, marker in proof.MARKERS.items()}
        self.assertEqual({'maui': result, 'avalonia': result}, proof.runtime_proof(logs))
        for invalid in ('', 'CURRENCY_AVALONIA FAIL crash', logs['avalonia'] * 2,
                        'CURRENCY_AVALONIA PASS android: USD=$', logs['avalonia'].replace('USD=$', 'USD=US$')):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                proof.runtime_proof({**logs, 'avalonia': invalid})

    def test_ios_runtime_requires_matching_apple_results_in_both_hosts(self):
        result = 'PASS apple: USD=$, EUR=€, GBP=£, JPY=¥; normalization, errors, concurrency, cancellation'
        logs = {host: f'{marker} {result}' for host, marker in proof.MARKERS.items()}
        self.assertEqual({'maui': result, 'avalonia': result}, proof.runtime_proof(logs, 'ios'))
        for invalid in ('', logs['avalonia'].replace('apple:', 'android:'),
                        logs['avalonia'] + '\nCURRENCY_AVALONIA FAIL shutdown',
                        logs['avalonia'].replace('USD=$', 'USD=US$')):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                proof.runtime_proof({**logs, 'avalonia': invalid}, 'ios')
        with self.assertRaises(ValueError):
            proof.runtime_proof(logs, 'android')

    def test_windows_runtime_requires_matching_complete_results(self):
        result = 'PASS windows: USD=$, EUR=€, GBP=£, JPY=¥; normalization, errors, concurrency, cancellation'
        logs = {host: f'{marker} {result}' for host, marker in proof.MARKERS.items()}
        self.assertEqual({'maui': result, 'avalonia': result}, proof.runtime_proof(logs, 'windows'))
        for invalid in ('', logs['avalonia'].replace('windows:', 'apple:'),
                        logs['avalonia'].replace('JPY=¥', 'JPY=￥'),
                        logs['avalonia'] + '\nCURRENCY_AVALONIA FAIL shutdown'):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                proof.runtime_proof({**logs, 'avalonia': invalid}, 'windows')


if __name__ == '__main__':
    unittest.main()
