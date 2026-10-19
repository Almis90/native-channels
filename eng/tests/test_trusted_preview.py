"""Trusted publication transports existing evidence; never rebuilds or runs sample tests."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).parents[1]))
import test_publish_preview as fixtures

SPEC = importlib.util.spec_from_file_location('trusted_preview', Path(__file__).parents[1] / 'trusted-preview.py')
trusted = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(trusted)
VERSION = fixtures.VERSION
ENV = dict(GITHUB_REPOSITORY=trusted.REPOSITORY, GITHUB_REF='refs/heads/main',
           GITHUB_EVENT_NAME='workflow_dispatch',
           GITHUB_WORKFLOW_REF=trusted.REPOSITORY + '/.github/workflows/publish-nuget.yml@refs/heads/main',
           PUBLISH_NUGET='true', NUGET_API_KEY='secret-fixture', GITHUB_RUN_ID='123')


class TrustedPreviewTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.PublishPreviewTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root
        self.manifest = self.fixture.prepare()
        self.directory = self.fixture.output
        self.archive = self.root / 'accepted.zip'
        self.record = self.root / 'record.json'
        trusted.publisher.write(self.record, dict(version=VERSION, sourceCommit='a' * 40,
            candidate=trusted.publisher.read(self.directory / 'candidate/preview-manifest.json'),
            acceptance=trusted.publisher.read(self.directory / 'evidence/acceptance.json')))

    def test_bundle_roundtrip_keeps_every_accepted_byte(self):
        sha = trusted.bundle(self.directory, self.archive)
        output = self.root / 'downloaded'
        self.assertEqual(self.manifest, trusted.unpack(self.archive, sha, output))
        self.assertEqual(trusted.publisher.file_hashes(self.directory), trusted.publisher.file_hashes(output))

    def test_unreviewed_hash_and_modified_archive_are_rejected_before_extraction(self):
        sha = trusted.bundle(self.directory, self.archive)
        for value in ['f' * 64, '../not-a-hash']:
            with self.assertRaisesRegex(ValueError, 'SHA-256'):
                trusted.unpack(self.archive, value, self.root / 'output')
        with self.archive.open('ab') as stream:
            stream.write(b'changed')
        with self.assertRaisesRegex(ValueError, 'SHA-256'):
            trusted.unpack(self.archive, sha, self.root / 'output')
        self.assertFalse((self.root / 'output').exists())

    def test_unsafe_bundle_paths_are_rejected(self):
        for name in ['../escape', '/absolute', 'C:/absolute', 'bad\\path', 'candidate/./a']:
            with zipfile.ZipFile(self.archive, 'w') as archive:
                entry = zipfile.ZipInfo()
                entry.filename = entry.orig_filename = name
                archive.writestr(entry, b'unsafe')
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'Unsafe'):
                trusted.unpack(self.archive, trusted.publisher.candidate.digest(self.archive), self.root / 'output')

    def test_committed_acceptance_and_main_ancestry_are_required(self):
        with patch.object(trusted, 'RECORD', self.record), \
                patch.object(trusted.publisher, 'require_merged', return_value='b' * 40) as merged:
            self.assertEqual(self.manifest, trusted.verify(self.directory, VERSION))
            merged.assert_called_once_with('a' * 40)
            record = trusted.publisher.read(self.record)
            record['acceptance']['success'] = False
            trusted.publisher.write(self.record, record)
            with self.assertRaisesRegex(ValueError, 'committed'):
                trusted.verify(self.directory, VERSION)

    def test_wrong_workflow_branch_repository_or_disabled_publication_is_rejected(self):
        for key, value in [('GITHUB_REF', 'refs/heads/develop'), ('GITHUB_EVENT_NAME', 'push'),
                           ('GITHUB_REPOSITORY', 'other/repo'), ('GITHUB_WORKFLOW_REF', 'other-workflow'),
                           ('PUBLISH_NUGET', 'false')]:
            with self.subTest(key=key), patch.dict(os.environ, {**ENV, key: value}, clear=True):
                with self.assertRaises(ValueError):
                    trusted.require_workflow(publish=True)

    def test_release_asset_hash_must_match_before_download(self):
        data = dict(tag_name='v' + VERSION, draft=True, assets=[dict(
            name=f'NativeChannels.{VERSION}.accepted.zip', state='uploaded', digest='sha256:' + 'b' * 64)])
        with patch.dict(os.environ, ENV, clear=True), \
                patch.object(trusted.subprocess, 'check_output', return_value=json.dumps(data)), \
                patch.object(trusted.subprocess, 'run') as download:
            with self.assertRaisesRegex(ValueError, 'reviewed hash'):
                trusted.prepare('123', 'a' * 64, VERSION, self.root / 'downloaded')
            download.assert_not_called()

    def test_temporary_key_publishes_twelve_archives_and_is_not_recorded(self):
        with patch.dict(os.environ, ENV, clear=True), patch.object(trusted, 'RECORD', self.record), \
                patch.object(trusted.publisher, 'require_merged', return_value='b' * 40), \
                patch.object(trusted.publisher.remote, 'fetch_package', return_value=None), \
                patch.object(trusted.publisher, 'push') as push, patch('builtins.print'):
            path = self.root / 'submission.json'
            trusted.publish(self.directory, VERSION, path)
            self.assertEqual(push.call_count, 12)
            self.assertTrue(trusted.publisher.read(path)['uploadsCompleted'])
            self.assertNotIn('secret-fixture', path.read_text())
            self.assertNotIn('NUGET_API_KEY', os.environ)

    def test_collision_stops_before_any_submission(self):
        with patch.dict(os.environ, ENV, clear=True), patch.object(trusted, 'RECORD', self.record), \
                patch.object(trusted.publisher, 'require_merged', return_value='b' * 40), \
                patch.object(trusted.publisher, 'preflight', side_effect=ValueError('collision')), \
                patch.object(trusted.publisher, 'push') as push:
            with self.assertRaisesRegex(ValueError, 'collision'):
                trusted.publish(self.directory, VERSION, self.root / 'submission.json')
            push.assert_not_called()


if __name__ == '__main__':
    unittest.main()
