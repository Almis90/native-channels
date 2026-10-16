"""A partial, mismatched or cross-platform SDK cache must not reach the build."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('mobile_toolchain', ROOT / '.github/scripts/mobile-toolchain.py')
toolchain = importlib.util.module_from_spec(spec)
spec.loader.exec_module(toolchain)


class MobileToolchainTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'dotnet').touch()
        (self.root / 'packs' / 'Microsoft.Maui.Sdk').mkdir(parents=True)
        (self.root / 'packs' / 'Microsoft.iOS.Sdk.net10.0_26.0').mkdir()
        self.metadata = self.root / 'metadata/workloads/10.0.400'
        self.registration = self.metadata / 'InstalledWorkloads/maui-ios'
        self.registration.parent.mkdir(parents=True)
        self.registration.touch()
        self.responses = {
            ('--version',): '10.0.401',
            ('workload', '--version'): '10.0.401',
            ('workload', 'list', '--machine-readable'): json.dumps({'installed': ['maui-ios']}),
        }
        self.which = patch.object(toolchain.shutil, 'which', return_value=str(self.root / 'dotnet')).start()
        patcher = patch.object(toolchain.subprocess, 'check_output', side_effect=lambda command, **_: self.responses[tuple(command[1:])])
        patcher.start()
        self.addCleanup(patch.stopall)

    def verify(self):
        return toolchain.verify(self.root, 'ios', '10.0.401', '10.0.401', '10.0.400')

    def test_exact_restored_toolchain_is_accepted(self):
        with patch.dict(os.environ, {'TOOLCHAIN_CACHE_HIT': 'true'}):
            result = self.verify()
        self.assertTrue(result['cacheHit'])
        self.assertEqual(['maui-ios'], result['installed'])
        self.assertEqual(0, result['foreignPlatformPacks'])

    def test_system_sdk_on_path_is_rejected(self):
        self.which.return_value = str(self.root / 'system/dotnet')
        with self.assertRaisesRegex(ValueError, 'PATH resolves'):
            self.verify()

    def test_sdk_and_workload_version_mismatches_are_rejected(self):
        for command in (('--version',), ('workload', '--version')):
            with self.subTest(command=command):
                self.responses[command] = '10.0.400'
                with self.assertRaisesRegex(ValueError, 'version differs'):
                    self.verify()
                self.responses[command] = '10.0.401'

    def test_missing_or_extra_workloads_are_rejected(self):
        for installed in ([], ['maui-ios', 'maui-android'], ['maui-maccatalyst']):
            with self.subTest(installed=installed):
                self.responses[('workload', 'list', '--machine-readable')] = json.dumps({'installed': installed})
                with self.assertRaisesRegex(ValueError, 'Unexpected installed workloads'):
                    self.verify()

    def test_missing_registration_is_rejected(self):
        self.registration.unlink()
        with self.assertRaisesRegex(ValueError, 'Missing file-based'):
            self.verify()

    def test_wrong_installer_registration_is_rejected(self):
        marker = self.metadata / 'installertype/msi'
        marker.parent.mkdir()
        marker.touch()
        with self.assertRaisesRegex(ValueError, 'MSI'):
            self.verify()

    def test_unrelated_runtime_sdk_and_web_packs_are_rejected(self):
        for name in ('Microsoft.Android.Sdk.Linux', 'Microsoft.MacCatalyst.Ref.net10.0_26.0',
                     'Microsoft.NETCore.App.Runtime.Mono.android-arm64',
                     'Microsoft.NETCore.App.Runtime.AOT.osx-arm64.Cross.maccatalyst-arm64',
                     'Microsoft.NETCore.App.Runtime.Mono.browser-wasm', 'Microsoft.NET.Workload.Emscripten.net10',
                     'Microsoft.Maui.Graphics.Win2D.WinUI.Desktop'):
            with self.subTest(name=name):
                directory = self.root / 'packs' / name
                directory.mkdir()
                with self.assertRaisesRegex(ValueError, 'unrelated workload packs'):
                    self.verify()
                directory.rmdir()

    def test_own_platform_runtime_packs_are_allowed(self):
        for target in toolchain.TARGETS:
            with self.subTest(target=target), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                for name in (f'Microsoft.{target}.Sdk.net10', f'Microsoft.NETCore.App.Runtime.Mono.{target}-arm64',
                             f'Microsoft.NETCore.App.Runtime.AOT.osx-arm64.Cross.{target}-arm64', 'Microsoft.Maui.Sdk'):
                    (root / 'packs' / name).mkdir(parents=True)
                self.assertEqual([], toolchain.foreign_packs(root, target))

    def test_install_refuses_partial_directory_without_downloading(self):
        with patch.object(toolchain.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'empty private SDK'):
                toolchain.install(self.root, 'ios', '10.0.401', '10.0.401', 'arm64')
            run.assert_not_called()

    def test_host_and_feature_band_are_selected_independently_of_target_architecture(self):
        (self.root / 'global.json').write_text(json.dumps({
            'sdk': {'version': '10.0.401'}, 'workload': {'version': '10.0.401'}}))
        self.assertEqual(('10.0.401', '10.0.401', '10.0.400'), toolchain.versions(self.root))
        with patch.object(toolchain.platform, 'system', return_value='Darwin'), \
                patch.object(toolchain.platform, 'machine', return_value='arm64'):
            self.assertEqual('arm64', toolchain.host('ios'))
        with patch.object(toolchain.platform, 'system', return_value='Linux'), \
                patch.object(toolchain.platform, 'machine', return_value='x86_64'):
            self.assertEqual('x64', toolchain.host('android'))
            with self.assertRaisesRegex(ValueError, 'require macOS'):
                toolchain.host('ios')

    def test_prepare_exports_the_private_sdk_and_workload_for_later_steps(self):
        paths = {key: str(self.root / key) for key in ('GITHUB_OUTPUT', 'GITHUB_ENV', 'GITHUB_PATH')}
        self.responses[('-version',)] = 'Xcode 26.6\nBuild version example'
        with patch.dict(os.environ, paths):
            toolchain.prepare(self.root, 'ios', '10.0.401', '10.0.401', 'arm64')
        self.assertIn('TOOLCHAIN_TARGET=ios\n', Path(paths['GITHUB_ENV']).read_text())
        self.assertIn(f'DOTNET_ROOT_ARM64={self.root}\n', Path(paths['GITHUB_ENV']).read_text())
        self.assertEqual(str(self.root), Path(paths['GITHUB_PATH']).read_text().strip())
        outputs = dict(line.split('=', 1) for line in Path(paths['GITHUB_OUTPUT']).read_text().splitlines())
        self.assertEqual('ios', outputs['target'])
        self.assertNotEqual('none', outputs['xcode'])


if __name__ == '__main__':
    unittest.main()
