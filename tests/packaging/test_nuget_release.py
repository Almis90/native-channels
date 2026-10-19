"""Publication consumes verified bytes; tests never authenticate or upload to NuGet."""
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / '.github/scripts' / f'{name}.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release = load('nuget-release')
publisher = load('push-nuget')
SHA, RUN, VERSION = 'a' * 40, '123', '0.1.0-preview.1'


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.output = self.root / 'candidate'
        self.feed = self.root / 'feed.tar.gz'
        self.evidence_path = self.root / 'evidence.json'
        self.members = {}
        records = []
        for package_id in release.PACKAGE_IDS:
            for extension in ('nupkg', 'snupkg'):
                path = self.root / f'{package_id}.{VERSION}.{extension}'
                nuspec = f'''<package><metadata><id>{package_id}</id><version>{VERSION}</version>
                    <license type="expression">MIT</license><readme>README.md</readme>
                    <repository type="git" url="{release.REPOSITORY}" commit="{SHA}" />
                    </metadata></package>'''
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr(f'{package_id}.nuspec', nuspec)
                    archive.writestr('_rels/.rels', '<Relationships />')
                    archive.writestr('[Content_Types].xml', '<Types />')
                    if extension == 'nupkg':
                        archive.writestr('README.md', 'Package documentation')
                        archive.writestr(f'lib/net10.0/{package_id}.dll', b'assembly fixture')
                    else:
                        archive.writestr(f'lib/net10.0/{package_id}.pdb', b'BSJB portable PDB fixture')
                self.members[f'artifacts/packages/{path.name}'] = path.read_bytes()
                if extension == 'nupkg':
                    records.append(release.audit.audit_package(path, SHA))
        self.members['artifacts/package-audit.json'] = json.dumps({'packages': records}).encode()
        self.write_feed()
        self.evidence = {'sourceCommit': SHA, 'runId': RUN, 'success': True, 'sameFeed': True,
                         'failedJobs': [], 'unverifiedConsumers': [], 'results': [
                             {'configuration': name, 'sourceCommit': SHA, 'packageRun': RUN,
                              'packageSource': 'Distribution', 'sample': 'Framework',
                              'compiled': True, 'executed': executes, 'feedSha256': release.digest(self.feed)}
                             for name, executes in release.CONFIGURATIONS.items()]}

    def write_feed(self):
        with tarfile.open(self.feed, 'w:gz') as archive:
            for name, data in self.members.items():
                member = tarfile.TarInfo(name)
                member.size = len(data)
                archive.addfile(member, io.BytesIO(data))

    def prepare(self):
        self.evidence_path.write_text(json.dumps(self.evidence))
        return release.prepare(self.feed, self.evidence_path, self.output, SHA, RUN, VERSION)

    def test_exact_candidate_passes_and_rechecks(self):
        result = self.prepare()
        self.assertEqual(10, len(result['files']))
        self.assertEqual(result, release.verify(self.output, SHA, RUN, VERSION))

    def test_partial_failed_foreign_or_mixed_evidence_rejected(self):
        original = copy.deepcopy(self.evidence)
        for field, value in [('sourceCommit', 'b' * 40), ('runId', '456'), ('success', False),
                             ('sameFeed', False), ('failedJobs', ['android-x64']),
                             ('unverifiedConsumers', ['ios-device']), ('results', original['results'][:-1])]:
            self.evidence = {**original, field: value}
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.prepare()
        for field, value in [('sourceCommit', 'b' * 40), ('packageRun', '456'), ('sample', 'Currency'),
                             ('feedSha256', 'c' * 64), ('compiled', False), ('executed', False)]:
            self.evidence = copy.deepcopy(original)
            self.evidence['results'][0][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                self.prepare()

    def test_same_feed_cannot_claim_device_execution(self):
        self.evidence['results'][1]['executed'] = True
        with self.assertRaises(ValueError):
            self.prepare()

    def test_extra_or_missing_package_rejected_even_with_updated_feed_hash(self):
        self.members['artifacts/packages/Plugin.Currency.1.0.0.nupkg'] = b'not a framework package'
        self.write_feed()
        for row in self.evidence['results']:
            row['feedSha256'] = release.digest(self.feed)
        with self.assertRaisesRegex(ValueError, 'exactly five'):
            self.prepare()

    def test_audit_must_match_actual_package_bytes(self):
        key = 'artifacts/package-audit.json'
        audit = json.loads(self.members[key])
        audit['packages'][0]['sha256'] = 'b' * 64
        self.members[key] = json.dumps(audit).encode()
        self.write_feed()
        for row in self.evidence['results']:
            row['feedSha256'] = release.digest(self.feed)
        with self.assertRaisesRegex(ValueError, 'distribution audit'):
            self.prepare()

    def test_tar_link_cannot_replace_a_package(self):
        with tarfile.open(self.feed, 'w:gz') as archive:
            for index, (name, data) in enumerate(self.members.items()):
                member = tarfile.TarInfo(name)
                if index == 0:
                    member.type = tarfile.SYMTYPE
                    member.linkname = '/tmp/foreign.nupkg'
                    archive.addfile(member)
                else:
                    member.size = len(data)
                    archive.addfile(member, io.BytesIO(data))
        for row in self.evidence['results']:
            row['feedSha256'] = release.digest(self.feed)
        with self.assertRaisesRegex(ValueError, 'regular files'):
            self.prepare()

    def test_approval_handoff_rejects_modified_archives(self):
        self.prepare()
        path = self.output / f'NativeChannels.Core.{VERSION}.nupkg'
        with zipfile.ZipFile(path, 'a') as archive:
            archive.writestr('extra.txt', 'modified after validation')
        with self.assertRaisesRegex(ValueError, 'changed after validation'):
            release.verify(self.output, SHA, RUN, VERSION)

    def test_symbol_source_and_format_are_checked(self):
        path = self.root / f'NativeChannels.Core.{VERSION}.snupkg'
        with self.assertRaisesRegex(ValueError, 'source'):
            release.inspect_archive(path, 'NativeChannels.Core', VERSION, 'b' * 40)
        with zipfile.ZipFile(path, 'a') as archive:
            archive.writestr('lib/net10.0/native.pdb', b'Windows native PDB')
        with self.assertRaisesRegex(ValueError, 'Portable PDBs'):
            release.inspect_archive(path, 'NativeChannels.Core', VERSION, SHA)

    def test_candidate_refuses_version_or_run_changes(self):
        self.prepare()
        for source, run, version in [(SHA, '456', VERSION), ('b' * 40, RUN, VERSION), (SHA, RUN, '1.0.0')]:
            with self.subTest(source=source, run=run), self.assertRaises(ValueError):
                release.verify(self.output, source, run, version)

    def test_retries_skip_identical_packages_despite_repository_signature(self):
        result = self.prepare()
        def signed(package_id, version):
            data = io.BytesIO((self.output / f'{package_id}.{version}.nupkg').read_bytes())
            with zipfile.ZipFile(data, 'a') as archive:
                archive.writestr('.signature.p7s', b'NuGet signature fixture')
            return data.getvalue()
        with patch.object(publisher, 'fetch_package', side_effect=signed):
            self.assertEqual([], publisher.pending_packages(self.output, result))

    def test_remote_collision_prevents_uploads(self):
        self.prepare()
        conflict = io.BytesIO()
        with zipfile.ZipFile(conflict, 'w') as archive:
            archive.writestr('different.txt', 'foreign package')
        env = {'PUBLISH_NUGET': 'true', 'GITHUB_REF': 'refs/heads/main', 'GITHUB_EVENT_NAME': 'workflow_dispatch',
               'NUGET_API_KEY': 'test-only', 'GITHUB_SHA': SHA, 'DISTRIBUTION_RUN': RUN, 'VERSION': VERSION}
        with patch.dict(os.environ, env, clear=True), patch('sys.argv', ['publish', str(self.output)]), \
                patch.object(publisher, 'fetch_package', side_effect=[None, None, conflict.getvalue()]), \
                patch.object(publisher, 'push') as upload:
            with self.assertRaisesRegex(ValueError, 'differs'):
                publisher.main()
            upload.assert_not_called()

    def test_publication_needs_explicit_manual_main_dispatch(self):
        for env in ({}, {'PUBLISH_NUGET': 'false'}, {'PUBLISH_NUGET': 'true', 'GITHUB_REF': 'refs/heads/feature'}):
            with patch.dict(os.environ, env, clear=True), patch.object(publisher, 'push') as upload:
                with self.assertRaises(SystemExit):
                    publisher.main()
                upload.assert_not_called()

    def test_upload_error_does_not_echo_temporary_api_key(self):
        with patch.object(publisher.subprocess, 'run') as command:
            command.return_value.returncode = 1
            with self.assertRaises(SystemExit) as error:
                publisher.push(Path('package.nupkg'), 'do-not-expose')
            self.assertNotIn('do-not-expose', str(error.exception))

    def test_workflow_defaults_to_credential_free_validation(self):
        workflow = (ROOT / '.github/workflows/publish-nuget.yml').read_text()
        validation, publication = workflow.split('\n  publish:\n', 1)
        self.assertIn('default: false', validation)
        self.assertNotIn('NuGet/login', validation)
        self.assertNotIn('id-token: write', validation)
        self.assertIn("if: ${{ inputs.publish && github.ref == 'refs/heads/main' }}", publication)
        self.assertIn('environment: nuget', publication)
        self.assertIn('eng/trusted-preview.py prepare', validation)
        self.assertIn('bundle_sha256:', validation)
        self.assertNotIn('dotnet test', workflow)
        self.assertNotIn('test-samples.py', workflow)
        self.assertNotIn('pull_request:', workflow)
        self.assertNotIn('\n  push:', workflow)


if __name__ == '__main__':
    unittest.main()
