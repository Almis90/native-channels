import copy
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('profile_native', ROOT / 'eng/profile-native.py')
profile = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profile)


def completed_run():
    rows = [{'kind': 'start', 'durationSeconds': 300, 'platform': 'MacCatalyst',
             'frameworkVersion': '0.1.0-preview.2+source', 'runtime': '.NET 10 / Arm64'}]
    for name in sorted(profile.TIMINGS):
        rows.append({'kind': 'timing', 'name': name, 'samples': 256, 'warmup': 32,
                     'minMs': 0.1, 'meanMs': 0.25, 'p50Ms': 0.2, 'p95Ms': 0.3, 'maxMs': 0.5})
    for seconds in (0, 150, 305):
        rows.append({'kind': 'memory', 'elapsedSeconds': seconds, 'managedBytes': 1000 + seconds,
                     'allocatedBytes': 5000 + seconds, 'nativeBytes': {'physicalFootprint': 4000 + seconds, 'resident': 8000}})
    rows.append({'kind': 'complete', 'elapsedSeconds': 305, 'cycles': 1000, 'activeStreams': 0})
    for index, row in enumerate(rows):
        row.update(runId='test-run', sequence=index)
    return rows


class NativeProfileTests(unittest.TestCase):
    def test_preserves_measured_memory_delta_and_units(self):
        result = profile.validate(completed_run(), 'test-run', 300, 'catalyst')
        self.assertEqual(result['memoryBytes']['physicalFootprint'],
                         {'first': 4000, 'last': 4305, 'peak': 4305, 'delta': 305})
        self.assertEqual(result['memorySamples'], 3)

    def test_does_not_accept_partial_stale_or_failed_run(self):
        original = completed_run()
        for rows in (original[:-1], original[1:], original + [original[-1]],
                     [dict(row, runId='previous-run') for row in original],
                     [dict(row, kind='error') if row['kind'] == 'complete' else row for row in original]):
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                profile.validate(rows, 'test-run', 300, 'catalyst')

    def test_rejects_wrong_target_duration_leaked_subscription_or_invalid_metrics(self):
        for index, changes in [(0, {'platform': 'Android'}), (0, {'durationSeconds': 60}),
                               (-1, {'elapsedSeconds': 299}), (-1, {'activeStreams': 1}),
                               (-1, {'cycles': 0}), (1, {'samples': 0}), (1, {'p95Ms': float('nan')}),
                               (4, {'nativeBytes': {'resident': 100}}), (4, {'managedBytes': -1})]:
            rows = copy.deepcopy(completed_run())
            rows[index].update(changes)
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                profile.validate(rows, 'test-run', 300, 'catalyst')

    def test_log_collector_handles_prefix_but_rejects_truncated_json(self):
        self.assertEqual(profile.records('noise\nI/monodroid: NATIVECHANNELS_PROFILE {"kind":"start"}\n'), [{'kind': 'start'}])
        with self.assertRaises(ValueError):
            profile.records('I/monodroid: NATIVECHANNELS_PROFILE {"kind":\n')

    def test_opt_in_workflow_reuses_strict_distribution_feed(self):
        workflow = (ROOT / '.github/workflows/native-performance.yml').read_text()
        self.assertIn('workflow_dispatch:', workflow)
        self.assertNotIn('pull_request:', workflow)
        self.assertNotIn('schedule:', workflow)
        self.assertIn('uses: ./.github/actions/consumer-feed', workflow)
        self.assertIn('source: Distribution', workflow)
        self.assertIn('--source-commit "$GITHUB_SHA"', workflow)
        self.assertNotIn('bash eng/pack', workflow)
        self.assertIn('retention-days: 7', workflow)


if __name__ == '__main__':
    unittest.main()
