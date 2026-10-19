"""Candidate gates must reject partial, stale and modified evidence."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).parents[1]))
import preview_candidate as candidate

SOURCE = 'a' * 40
VERSION = '0.2.0-preview.1'


class PreviewCandidateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.output = self.root / 'combined'
        self.manifests = []
        self.identity = dict(commit=SOURCE, status='', sourceSha256='b' * 64)
        for framework, platform in sorted(candidate.MATRIX):
            folder = self.root / framework / platform
            feed = folder / 'packages'
            feed.mkdir(parents=True)
            ids = {'NativeChannels.Core', 'NativeChannels', 'NativeChannels.' + ('Maui' if framework == 'maui' else 'Avalonia')}
            if platform == 'android':
                ids.add('NativeChannels.Native.Android')
            elif platform in {'ios', 'macos', 'maccatalyst'}:
                ids.add('NativeChannels.Native.Apple')
            for package in ids:
                tfm = 'net10.0' if package in {'NativeChannels.Core', 'NativeChannels.Avalonia'} or platform == 'linux' else 'net10.0-' + platform
                spec = f'''<package><metadata><id>{package}</id><version>{VERSION}</version>
<license type="expression">MIT</license><readme>README.md</readme>
<repository type="git" url="https://github.com/Almis90/native-channels" commit="{SOURCE}" />
<dependencies><group targetFramework="{tfm}" /></dependencies></metadata></package>'''
                for suffix, extension in (('nupkg', 'dll'), ('snupkg', 'pdb')):
                    with zipfile.ZipFile(feed / f'{package}.{VERSION}.{suffix}', 'w') as archive:
                        archive.writestr(f'{package}.nuspec', spec)
                        archive.writestr('[Content_Types].xml', f'<Types><Default Extension="{extension}" ContentType="application/octet" /></Types>')
                        archive.writestr(f'lib/{tfm}/{package}.{extension}', b'BSJB' + f'{package}:{tfm}:{suffix}'.encode())
                        if suffix == 'nupkg':
                            archive.writestr('README.md', 'same documentation\n')
                            if package == 'NativeChannels' and platform == 'browser':
                                for module in ('nativechannels', 'json-codec'):
                                    archive.writestr(f'browser/{module}.mjs', b'export {}')
            path = folder / 'manifest.json'
            path.write_text(json.dumps(dict(framework=framework, platform=platform, source=self.identity,
                                            packageFiles={p.name: candidate.digest(p) for p in feed.iterdir()})))
            self.manifests.append(path)

    def assemble(self):
        return candidate.assemble(self.manifests, self.output, SOURCE, VERSION)

    def test_preserves_seven_platforms_and_six_packages_with_symbols(self):
        result = self.assemble()
        self.assertEqual(len(result['files']), 12)
        with zipfile.ZipFile(self.output / f'NativeChannels.{VERSION}.nupkg') as archive:
            self.assertEqual(len([p for p in archive.namelist() if p.endswith('.dll')]), 7)
            self.assertEqual(archive.read('browser/nativechannels.mjs'), b'export {}')
        for record in result['files']:
            self.assertEqual(candidate.digest(self.output / record['file']), record['sha256'])

    def test_missing_platform_is_not_a_candidate(self):
        self.manifests.pop()
        with self.assertRaisesRegex(ValueError, 'Missing builds'):
            self.assemble()
        self.assertFalse(self.output.exists())

    def test_dirty_or_wrong_commit_cannot_claim_candidate_provenance(self):
        path = self.manifests[0]
        original = json.loads(path.read_text())
        for change in ({'status': ' M source.cs'}, {'commit': 'c' * 40}):
            data = {**original, 'source': {**self.identity, **change}}
            path.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, 'clean source'):
                self.assemble()

    def test_changed_build_bytes_are_rejected_before_assembly(self):
        path = self.root / 'avalonia/windows/packages' / f'NativeChannels.Core.{VERSION}.nupkg'
        path.write_bytes(path.read_bytes() + b'changed')
        with self.assertRaisesRegex(ValueError, 'archive hash'):
            self.assemble()
        self.assertFalse(self.output.exists())

    def test_tampered_final_package_is_rejected(self):
        result = self.assemble()
        path = self.output / result['files'][0]['file']
        path.write_bytes(path.read_bytes() + b'changed')
        with self.assertRaisesRegex(ValueError, 'bytes changed'):
            candidate.verify_candidate(self.output, SOURCE)

    def test_duplicate_build_is_rejected(self):
        self.manifests.append(self.manifests[0])
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            self.assemble()

    def evidence(self):
        manifest = self.assemble()
        api = self.root / 'api.json'
        rows = [dict(id=name, compatible=True, candidateSha256=candidate.digest(self.output / f'{name}.{VERSION}.nupkg'))
                for name in candidate.TARGETS if name != 'NativeChannels.Avalonia']
        api.write_text(json.dumps(dict(scope='combined', success=True, candidateVersion=VERSION, packages=rows)))
        summaries = []
        for framework, platform in sorted(candidate.MATRIX):
            samples = ['Currency', 'AudioStreamer', 'VolumeMonitor', 'Interop']
            samples += ['PdfViewerQt', 'PdfViewerGtk'] if platform == 'linux' else ['PdfViewer']
            path = self.root / framework / platform / 'summary.json'
            path.write_text(json.dumps(dict(framework=framework, platform=platform, buildSource=self.identity,
                                            currentSource=self.identity, frameworkCandidate=manifest,
                                            results=[dict(name=n, status='PASS') for n in samples])))
            summaries.append(path)
        return summaries, api

    def test_acceptance_requires_all_ten_suites_and_exact_candidate(self):
        summaries, api = self.evidence()
        self.assertTrue(candidate.accept(self.output, summaries, api, SOURCE)['success'])
        with self.assertRaisesRegex(ValueError, 'Missing consumer'):
            candidate.accept(self.output, summaries[:-1], api, SOURCE)
        path = summaries[0]
        data = json.loads(path.read_text())
        data['frameworkCandidate'] = None
        path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'exact combined'):
            candidate.accept(self.output, summaries, api, SOURCE)

    def test_api_report_for_other_archives_is_rejected(self):
        summaries, api = self.evidence()
        data = json.loads(api.read_text())
        data['packages'][0]['candidateSha256'] = 'f' * 64
        api.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'other package bytes'):
            candidate.accept(self.output, summaries, api, SOURCE)

    def test_failed_suite_cannot_pass_acceptance(self):
        summaries, api = self.evidence()
        data = json.loads(summaries[0].read_text())
        data['results'][0]['status'] = 'FAIL'
        summaries[0].write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'must pass'):
            candidate.accept(self.output, summaries, api, SOURCE)


if __name__ == '__main__':
    unittest.main()
