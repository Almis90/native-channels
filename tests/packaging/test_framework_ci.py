"""CI routing and evidence must fail closed without native toolchains."""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / '.github/scripts' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


selection = load('select-platforms')
results = load('verification-result')
release = load('release-evidence')


class PlatformSelectionTests(unittest.TestCase):
    INTEROP_PATHS = [
        '.github/workflows/windows-interop.yml',
        'eng/verify-windows-interop.ps1',
        'native/windows/interop/NativeDllModule.cs',
        'native/windows/interop/cpp/probe.cpp',
        'native/windows/interop/native_channels_probe.h',
        'native/windows/interop/rust/Cargo.toml',
        'native/windows/interop/rust/Cargo.lock',
        'native/windows/interop/rust/src/lib.rs',
        'tests/NativeChannels.Windows.Interop/NativeChannels.Windows.Interop.csproj',
        'tests/NativeChannels.Windows.Interop/Program.cs',
    ]

    def test_windows_interop_paths_include_project_workflow_and_verifier(self):
        for path in self.INTEROP_PATHS:
            with self.subTest(path=path):
                self.assertEqual(({'windows'}, True), selection.classify([path]))

    def test_selector_changes_run_core_tests_without_forcing_platform_builds(self):
        paths = ['.github/scripts/select-platforms.py', 'tests/packaging/test_framework_ci.py']
        self.assertEqual((set(), True), selection.classify(paths))
        self.assertEqual(({'windows'}, True), selection.classify(paths + self.INTEROP_PATHS))

    def test_windows_toolchain_workflows_are_scoped_without_hiding_shared_config(self):
        paths = [
            '.github/actions/windows-toolchain/action.yml',
            '.github/scripts/windows-toolchain.ps1',
            '.github/workflows/framework-windows.yml',
            '.github/workflows/windows.yml',
        ]
        for path in paths:
            with self.subTest(path=path):
                self.assertEqual(({'windows'}, True), selection.classify([path]))
        self.assertEqual(({'windows'}, True), selection.classify(paths + self.INTEROP_PATHS))
        for shared in ('global.json', '.github/workflows/build-examples.yml',
                       '.github/actions/windows-toolchain-shared/action.yml'):
            with self.subTest(path=shared):
                self.assertEqual((selection.ALL, True), selection.classify(paths + [shared]))

    def test_interop_exception_does_not_hide_shared_or_unknown_changes(self):
        for path in (
            'Directory.Build.props',
            'src/NativeChannels/NativeChannels.csproj',
            '.github/workflows/verify-framework.yml',
            '.github/actions/shared-core/action.yml',
            '.github/scripts/new-helper.py',
            'eng/verify-windows-interop-extra.ps1',
            'tests/NativeChannels.Windows.Interop.Other/Program.cs',
        ):
            with self.subTest(path=path):
                self.assertEqual((selection.ALL, True), selection.classify(self.INTEROP_PATHS + [path]))

    def test_mobile_toolchain_changes_select_the_affected_platforms(self):
        cases = {
            '.github/actions/mobile-toolchain/action.yml': {'android', 'ios', 'catalyst'},
            '.github/scripts/mobile-toolchain.py': {'android', 'ios', 'catalyst'},
            '.github/workflows/framework-android.yml': {'android'},
            '.github/workflows/verify-android.yml': {'android'},
            '.github/workflows/framework-apple.yml': {'ios', 'catalyst'},
            '.github/workflows/verify-ios.yml': {'ios'},
            '.github/workflows/verify-ios-device.yml': {'ios'},
            '.github/workflows/verify-catalyst.yml': {'catalyst'},
        }
        for path, expected in cases.items():
            with self.subTest(path=path):
                self.assertEqual((expected, True), selection.classify([path]))
        self.assertEqual((selection.ALL, True), selection.classify(list(cases) + self.INTEROP_PATHS))
        self.assertEqual((selection.ALL, True), selection.classify(list(cases) + ['global.json']))

    def test_mixed_platform_changes_are_combined(self):
        self.assertEqual(({'windows', 'android'}, True), selection.classify(
            self.INTEROP_PATHS + ['native/android/library/src/Bridge.kt']))
        self.assertEqual(({'windows', 'ios', 'catalyst'}, True), selection.classify(
            self.INTEROP_PATHS + ['native/apple/Bridge.swift']))

    def test_pr_outputs_disable_mobile_jobs_for_complete_interop_diff(self):
        paths = self.INTEROP_PATHS + [
            'native/windows/interop/README.md', 'docs/windows-integration.md',
            '.github/scripts/select-platforms.py', 'tests/packaging/test_framework_ci.py',
        ]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            payload = root / 'event.json'
            payload.write_text(json.dumps({'pull_request': {'base': {'sha': 'a' * 40}}}))
            env = {'GITHUB_EVENT_NAME': 'pull_request', 'GITHUB_EVENT_PATH': str(payload),
                   'GITHUB_SHA': 'b' * 40, 'GITHUB_OUTPUT': str(root / 'output'),
                   'GITHUB_STEP_SUMMARY': str(root / 'summary')}
            previous = Path.cwd()
            try:
                os.chdir(root)
                with patch.dict(os.environ, env), patch.object(selection, 'changed_paths', return_value=paths) as diff:
                    selection.main()
                diff.assert_called_once_with('a' * 40)
            finally:
                os.chdir(previous)
            outputs = dict(line.split('=', 1) for line in (root / 'output').read_text().splitlines())
            self.assertEqual({'android': 'false', 'ios': 'false', 'catalyst': 'false',
                              'windows': 'true', 'core': 'true', 'apple': 'false',
                              'depth': 'Compile'}, outputs)
            self.assertEqual(['windows'], json.loads((root / 'artifacts/selection.json').read_text())['platforms'])

    def test_platform_source_is_independent(self):
        for path, platforms in (
            ('native/android/library/src/Bridge.kt', {'android'}),
            ('src/NativeChannels/Platforms/Android/Transport.cs', {'android'}),
            ('native/apple/Bridge.swift', {'ios', 'catalyst'}),
            ('native/windows/sample/Bridge.cs', {'windows'}),
            ('samples/NativeChannels.Sample/Platforms/iOS/AppDelegate.cs', {'ios'}),
        ):
            with self.subTest(path=path):
                self.assertEqual((platforms, True), selection.classify([path]))

    def test_shared_and_unknown_changes_widen_selection(self):
        for path in ('src/NativeChannels.Core/Messenger.cs', 'Directory.Packages.props',
                     '.github/scripts/shared-core.py', 'eng/new-tool.sh'):
            self.assertEqual((selection.ALL, True), selection.classify([path]))

    def test_docs_examples_and_benchmarks_do_not_build_framework(self):
        self.assertEqual((set(), False), selection.classify([
            'docs/building.md', 'plugins/Plugin.Maui.AudioStreamer/native/Foo.kt',
            'samples/AudioStreamer.Sample/MainPage.cs', 'benchmarks/Benchmarks.cs']))

    def test_shared_currency_proof_selects_android_without_other_platform_builds(self):
        for path in ('samples/Currency.Avalonia.Android/Currency.Avalonia.Android.csproj',
                     'samples/Currency.Shared/PluginVerification.cs', 'samples/Currency.Sample/MainPage.cs',
                     'plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/CurrencyService.cs',
                     'plugins/Plugin.Maui.Currency/native/android/CurrencyModule.kt',
                     '.github/workflows/shared-hosting-android.yml', 'eng/verify-shared-hosting.py',
                     'eng/pack-currency-android.sh', 'eng/restore-currency-avalonia.sh'):
            with self.subTest(path=path):
                self.assertEqual(({'android'}, True), selection.classify([path]))
        self.assertEqual((selection.ALL, True), selection.classify([
            'samples/Currency.Avalonia.Android/App.cs', 'src/NativeChannels/Hosting/Registration.cs']))

    def test_currency_docs_and_non_android_adapters_do_not_trigger_android(self):
        self.assertEqual((set(), False), selection.classify([
            'samples/Currency.Avalonia.Android/README.md',
            'plugins/Plugin.Maui.Currency/native/windows/WindowsCurrencyModule.cs',
            'plugins/Plugin.Maui.Currency/native/apple/Sources/CurrencyModule.swift',
            'samples/Currency.Sample/Platforms/iOS/AppDelegate.cs']))

    def test_failed_comparison_falls_back_to_all(self):
        with patch.object(selection, 'changed_paths', side_effect=ValueError('missing base')):
            platforms, core, _ = selection.choose('Auto', '')
        self.assertEqual(selection.ALL, platforms)
        self.assertTrue(core)

    def test_manual_override_does_not_need_comparison(self):
        with patch.object(selection, 'changed_paths', side_effect=AssertionError('unexpected diff')):
            platforms, core, _ = selection.choose('Android', '')
        self.assertEqual({'android'}, platforms)
        self.assertTrue(core)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.sha = 'a' * 40
        self.selection = {'sourceCommit': self.sha, 'platforms': ['android'], 'core': True, 'depth': 'Runtime'}
        self.needs = {name: {'result': 'success'} for name in ('select', 'core', 'android')}
        self.compile = {'sourceCommit': self.sha, 'platform': 'android', 'stage': 'compile',
                        'runtime': 'android-x64', 'outcome': 'success'}

        self.arm64 = {**self.compile, 'runtime': 'android-arm64'}

    def test_runtime_failure_preserves_compile_evidence(self):
        self.needs['android']['result'] = 'failure'
        runtime = {**self.compile, 'stage': 'runtime', 'outcome': 'failure'}
        record = results.summarize(self.selection, self.needs, [self.compile, self.arm64, runtime], self.sha)
        self.assertFalse(record['success'])
        self.assertTrue(record['results'][0]['compiled'])
        self.assertFalse(record['results'][0]['executed'])

    def test_green_parent_without_evidence_is_insufficient(self):
        record = results.summarize(self.selection, self.needs, [], self.sha)
        self.assertFalse(record['success'])

    def test_selected_shared_hosting_failure_blocks_framework_result(self):
        chosen = {**self.selection, 'depth': 'Compile', 'sharedHosting': True}
        needs = {**self.needs, 'shared-hosting': {'result': 'failure'}}
        record = results.summarize(chosen, needs, [self.compile, self.arm64], self.sha)
        self.assertFalse(record['success'])
        self.assertEqual(['shared-hosting'], record['failedJobs'])
        needs['shared-hosting']['result'] = 'success'
        self.assertTrue(results.summarize(chosen, needs, [self.compile, self.arm64], self.sha)['success'])

    def test_wrong_source_and_duplicate_evidence_rejected(self):
        for evidence in ([{**self.compile, 'sourceCommit': 'b' * 40}], [self.compile, self.compile]):
            with self.assertRaises(ValueError):
                results.summarize(self.selection, self.needs, evidence, self.sha)

    def test_windows_requires_both_architectures_but_no_runtime(self):
        chosen = {**self.selection, 'platforms': ['windows']}
        needs = {**self.needs, 'windows': {'result': 'success'}}
        evidence = [{**self.compile, 'platform': 'windows', 'runtime': rid} for rid in ('win-x64', 'win-arm64')]
        record = results.summarize(chosen, needs, evidence, self.sha)
        self.assertTrue(record['success'])
        self.assertFalse(record['results'][3]['executed'])
        self.assertFalse(results.summarize(chosen, needs, evidence[:1], self.sha)['success'])

    def test_release_rejects_partial_or_compile_only_verification(self):
        record = results.summarize({**self.selection, 'depth': 'Compile'}, self.needs, [self.compile, self.arm64], self.sha)
        self.assertTrue(record['success'])
        with self.assertRaises(ValueError):
            release.validate(record, self.sha)

    def test_android_requires_arm64_compile_but_only_x64_execution(self):
        runtime = {**self.compile, 'stage': 'runtime'}
        partial = results.summarize(self.selection, self.needs, [self.compile, runtime], self.sha)
        self.assertFalse(partial['success'])
        record = results.summarize(self.selection, self.needs, [self.compile, self.arm64, runtime], self.sha)
        self.assertTrue(record['success'])
        self.assertEqual(['android-arm64', 'android-x64'], record['results'][0]['compiledRuntimes'])
        self.assertEqual(['android-x64'], record['results'][0]['executedRuntimes'])

    def test_full_runtime_evidence_allows_packaging(self):
        chosen = {**self.selection, 'platforms': sorted(selection.ALL)}
        needs = {name: {'result': 'success'} for name in ('select', 'core', 'apple-native', *selection.ALL)}
        evidence = []
        for platform, runtimes in results.RUNTIMES.items():
            for rid in runtimes:
                evidence.append({**self.compile, 'platform': platform, 'runtime': rid})
                if rid in results.EXECUTION_RUNTIMES[platform]:
                    evidence.append({**self.compile, 'platform': platform, 'runtime': rid, 'stage': 'runtime'})
        record = results.summarize(chosen, needs, evidence, self.sha)
        release.validate(record, self.sha)
        record['results'][0]['compiledRuntimes'] = ['android-x64']
        with self.assertRaises(ValueError):
            release.validate(record, self.sha)


if __name__ == '__main__':
    unittest.main()
