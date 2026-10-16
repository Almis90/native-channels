"""CI routing and evidence must fail closed without native toolchains."""
import importlib.util
from pathlib import Path
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
            'docs/building.md', 'plugins/Plugin.Maui.Currency/native/Foo.kt',
            'samples/Currency.Sample/MainPage.cs', 'benchmarks/Benchmarks.cs']))

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
