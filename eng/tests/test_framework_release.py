"""Seven-package evidence gates; package inspection itself has separate archive tests."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import importlib.util

sys.path.insert(0, str(Path(__file__).parents[1]))
import framework_release as release

SPEC = importlib.util.spec_from_file_location('framework_trusted', release.ROOT / 'eng/trusted-preview.py')
trusted = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(trusted)


class FrameworkReleaseTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.packages = self.root / 'packages'
        self.packages.mkdir()
        self.output = self.root / 'accepted'
        self.version = '0.3.0-preview.1'
        self.source = 'a' * 40
        self.rows = []
        for name in release.PACKAGE_IDS:
            for suffix in ('nupkg', 'snupkg'):
                (self.packages / f'{name}.{self.version}.{suffix}').write_bytes(name.encode())
            self.rows.append(dict(id=name, version=self.version, file=f'{name}.{self.version}.nupkg'))
        release.write(self.packages / 'package-audit.json', dict(sourceCommit=self.source,
                      frameworksOnly=True, includesExistingHosts=True))
        baseline = release.read(release.ROOT / 'eng/api-baseline-02.json')
        self.api = self.root / 'api.json'
        release.write(self.api, dict(success=True, scope='combined', candidateVersion=self.version,
            baselineVersion=baseline['version'], packages=[dict(id=name, compatible=True,
                baselineSha256=sha, candidateSha256=release.digest(self.packages / f'{name}.{self.version}.nupkg'))
                for name, sha in baseline['packages'].items()]))
        self.reports = []
        for f, p in sorted(release.MATRIX):
            path = self.root / f'{f}-{p}.json'
            release.write(path, dict(framework=f, platform=p, sourceCommit=self.source, success=True,
                scenario='Interop', assetsSha256='b' * 64,
                packages={r['file']: release.digest(self.packages / r['file']) for r in self.rows},
                result='PASS typed calls, DTOs; view stress: 32 reconnects'))
            self.reports.append(path)
        for target, kwargs in [('validate_candidate', dict(return_value=self.rows)),
                               ('inspect_archives', {})]:
            mock = patch.object(release, target, **kwargs)
            mock.start()
            self.addCleanup(mock.stop)

    def prepare(self):
        return release.prepare(self.packages, self.api, self.reports, self.output)

    def change(self, path, **values):
        row = release.read(path)
        row.update(values)
        release.write(path, row)

    def test_complete_matrix_round_trip(self):
        manifest = self.prepare()
        self.assertEqual(manifest, release.verify(self.output))
        self.assertEqual(len(manifest['packageIds']), 7)
        self.assertEqual(len(release.read(self.output / 'evidence/acceptance.json')['consumers']), 16)

    def test_missing_or_duplicate_target_fails(self):
        for reports in (self.reports[:-1], self.reports + self.reports[:1]):
            with self.assertRaises(ValueError):
                release.accept(self.packages, self.api, reports)

    def test_failed_stale_partial_or_other_scenario_fails(self):
        path = self.reports[0]
        original = release.read(path)
        for values in (dict(success=False), dict(sourceCommit='c' * 40), dict(packages={}),
                       dict(scenario='Currency'), dict(result='PASS compile only'), dict(assetsSha256='')):
            release.write(path, dict(original, **values))
            with self.assertRaises(ValueError):
                release.accept(self.packages, self.api, self.reports)

    def test_api_must_cover_all_previous_packages_and_exact_bytes(self):
        original = release.read(self.api)
        for values in (dict(success=False), dict(baselineVersion='0.1.0-preview.1'),
                       dict(packages=original['packages'][:-1])):
            release.write(self.api, dict(original, **values))
            with self.assertRaises(ValueError):
                release.accept(self.packages, self.api, self.reports)
        original['packages'][0]['candidateSha256'] = 'f' * 64
        release.write(self.api, original)
        with self.assertRaises(ValueError):
            release.accept(self.packages, self.api, self.reports)

    def test_changed_symbols_and_extra_files_fail(self):
        self.prepare()
        symbol = next((self.output / 'candidate').glob('*.snupkg'))
        original = symbol.read_bytes()
        symbol.write_bytes(b'changed')
        with self.assertRaises(ValueError):
            release.verify(self.output)
        symbol.write_bytes(original)
        (self.output / 'extra.txt').write_text('unreviewed')
        with self.assertRaises(ValueError):
            release.verify(self.output)

    def test_rehashed_failed_report_still_fails(self):
        self.prepare()
        path = self.output / 'evidence/uno-windows.json'
        self.change(path, success=False)
        self.change(self.output / 'release-manifest.json', files=release.hashes(self.output))
        with self.assertRaises(ValueError):
            release.verify(self.output)

    def test_rehashed_symbols_still_require_new_acceptance(self):
        self.prepare()
        next((self.output / 'candidate').glob('*.snupkg')).write_bytes(b'other symbols')
        self.change(self.output / 'release-manifest.json', files=release.hashes(self.output))
        with self.assertRaisesRegex(ValueError, 'acceptance differs'):
            release.verify(self.output)

    def test_unsafe_version_and_package_set_fail(self):
        self.prepare()
        manifest = self.output / 'release-manifest.json'
        original = release.read(manifest)
        for values in (dict(version='../escape'), dict(packageIds=['Other']), dict(sourceCommit='c' * 40)):
            release.write(manifest, dict(original, **values))
            with self.assertRaises(ValueError):
                release.verify(self.output)

    def test_trusted_bundle_routes_seven_package_validation(self):
        manifest = self.prepare()
        archive = self.root / 'accepted.zip'
        sha = trusted.bundle(self.output, archive)
        self.assertEqual(manifest, trusted.unpack(archive, sha, self.root / 'unpacked'))

    def test_preflight_checks_uno_before_any_publication(self):
        manifest = self.prepare()
        with patch.object(trusted.publisher.remote, 'fetch_package', return_value=None) as fetch:
            pending = trusted.publisher.preflight(self.output, manifest)
        self.assertEqual(len(pending), 7)
        self.assertIn('NativeChannels.Uno', [call.args[0] for call in fetch.call_args_list])

    def test_trusted_push_includes_all_seven_symbol_archives(self):
        manifest = self.prepare()
        with patch.object(trusted, 'require_workflow'), patch.object(trusted, 'verify', return_value=manifest), \
                patch.object(trusted.publisher.remote, 'fetch_package', return_value=None), \
                patch.object(trusted.publisher, 'push') as push, \
                patch.dict('os.environ', {'NUGET_API_KEY': 'test-secret'}):
            trusted.publish(self.output, self.version, self.root / 'submission.json')
        self.assertEqual(len(push.call_args_list), 14)
        self.assertIn(f'NativeChannels.Uno.{self.version}.snupkg', [c.args[0].name for c in push.call_args_list])


if __name__ == '__main__':
    unittest.main()
