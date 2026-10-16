"""Distribution verification must fail closed on partial, foreign or mixed-feed evidence."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / '.github/scripts' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


source = load('consumer-source')
results = load('distribution-result')


class ConsumerSourceTests(unittest.TestCase):
    def setUp(self):
        self.sha = 'a' * 40
        self.run = {'path': '.github/workflows/verify-distribution.yml', 'head_sha': self.sha,
                    'head_repository': {'full_name': 'owner/repo'}, 'event': 'workflow_dispatch',
                    'status': 'in_progress'}

    def resolve(self, kind='Distribution', run_id='123', metadata=None):
        return source.resolve(kind, run_id, '123', self.run if metadata is None else metadata, self.sha, 'owner/repo')

    def test_own_feed_is_only_for_active_distribution_coordinator(self):
        self.assertEqual('true', self.resolve()['current'])
        for field, value in [('path', '.github/workflows/verify-android.yml'), ('head_sha', 'b' * 40),
                             ('head_repository', {'full_name': 'other/repo'}), ('event', 'pull_request'),
                             ('status', 'completed')]:
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.resolve(metadata={**self.run, field: value})
        for kind in ('Example', 'Release'):
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                self.resolve(kind=kind)

    def test_external_feeds_still_require_existing_verified_artifact_guard(self):
        for kind, workflow in source.WORKFLOWS.items():
            choice = self.resolve(kind, '456', {})
            self.assertEqual('false', choice['current'])
            self.assertEqual(workflow, choice['workflow'])
        for kind, run_id in [('Unknown', '123'), ('Release', '-1'), ('Release', 'abc')]:
            with self.subTest(kind=kind, run_id=run_id), self.assertRaises(ValueError):
                self.resolve(kind, run_id)


class DistributionEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.sha = 'a' * 40
        self.needs = {name: {'result': 'success'} for name in ['packages', *results.CONFIGURATIONS]}
        self.evidence = [{'configuration': name, 'sourceCommit': self.sha, 'packageRun': '123',
                          'packageSource': 'Distribution', 'sample': 'Framework', 'feedSha256': 'b' * 64,
                          'compiled': True, 'executed': executes} for name, executes in results.CONFIGURATIONS.items()]

    def summarize(self, evidence=None):
        return results.summarize(self.evidence if evidence is None else evidence, self.needs, self.sha, '123')

    def test_complete_seven_consumer_evidence_passes(self):
        self.assertTrue(self.summarize()['success'])
        self.assertEqual(7, len(self.summarize()['results']))

    def test_missing_or_duplicate_consumer_is_rejected(self):
        self.assertFalse(self.summarize(self.evidence[:-1])['success'])
        with self.assertRaises(ValueError):
            self.summarize(self.evidence + [self.evidence[0]])

    def test_foreign_source_feed_or_example_is_rejected(self):
        for key, value in [('sourceCommit', 'c' * 40), ('packageRun', '456'),
                           ('packageSource', 'Example'), ('sample', 'Currency')]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                self.summarize([{**self.evidence[0], key: value}, *self.evidence[1:]])

    def test_mixed_or_missing_archives_fail(self):
        for digest in ('c' * 64, None):
            record = self.summarize([{**self.evidence[0], 'feedSha256': digest}, *self.evidence[1:]])
            self.assertFalse(record['success'])
            self.assertFalse(record['sameFeed'])

    def test_failed_runtime_preserves_compilation(self):
        self.evidence[0]['executed'] = False
        self.needs['android-x64']['result'] = 'failure'
        record = self.summarize()
        self.assertFalse(record['success'])
        self.assertTrue(record['results'][0]['compiled'])
        self.assertFalse(record['results'][0]['executed'])

    def test_compile_only_targets_cannot_claim_execution(self):
        self.evidence[1]['executed'] = True
        self.assertFalse(self.summarize()['success'])

    def test_missing_package_job_or_skipped_consumer_is_failure(self):
        for name in ('packages', 'ios-device'):
            with self.subTest(name=name):
                self.needs[name]['result'] = 'skipped'
                self.assertFalse(self.summarize()['success'])
                self.needs[name]['result'] = 'success'

    def test_record_uses_actual_step_outcomes_and_archive_hash(self):
        with tempfile.TemporaryDirectory() as temp:
            archive = Path(temp) / 'feed.tar.gz'
            archive.write_bytes(b'feed')
            env = {'GITHUB_SHA': self.sha, 'PACKAGE_RUN': '123', 'PACKAGE_SOURCE': 'Distribution',
                   'SAMPLE': 'Framework', 'COMPILE_OUTCOME': 'success', 'EXECUTION_OUTCOME': 'failure'}
            record = results.record('ios-simulator', env, archive)
            self.assertTrue(record['compiled'])
            self.assertFalse(record['executed'])
            self.assertEqual(64, len(record['feedSha256']))
            env['COMPILE_OUTCOME'] = 'skipped'
            self.assertFalse(results.record('ios-device', env, archive)['compiled'])


if __name__ == '__main__':
    unittest.main()
