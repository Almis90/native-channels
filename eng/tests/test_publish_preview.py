"""Local publication must retain exact accepted bytes and fail before side effects."""
import importlib.util
import io
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).parents[1]))
import test_preview_candidate as fixtures

VERSION = fixtures.VERSION

SPEC = importlib.util.spec_from_file_location('publish_preview', Path(__file__).parents[1] / 'publish-preview.py')
publisher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publisher)


class PublishPreviewTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.PreviewCandidateTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root
        self.summaries, self.api = self.fixture.evidence()
        self.receipt = self.root / 'acceptance.json'
        result = publisher.candidate.accept(self.fixture.output, self.summaries, self.api, 'a' * 40)
        publisher.write(self.receipt, result)
        self.output = self.root / 'release'

    def prepare(self):
        return publisher.prepare(self.fixture.output, self.receipt, self.api, self.summaries, self.output)

    def test_portable_release_replays_acceptance_and_preserves_all_twelve_archives(self):
        manifest = self.prepare()
        self.assertEqual(manifest, publisher.verify(self.output))
        self.assertEqual(12, len(list((self.output / 'candidate').glob('*nupkg'))))
        for path in self.fixture.output.iterdir():
            self.assertEqual(path.read_bytes(), (self.output / 'candidate' / path.name).read_bytes())
        moved = self.root / 'relocated-release'
        self.output.rename(moved)
        self.assertEqual(manifest, publisher.verify(moved))

    def test_mismatched_receipt_api_or_suite_fails_before_preparing(self):
        for path in [self.receipt, self.api, self.summaries[0]]:
            original = path.read_bytes()
            data = json.loads(original)
            if path == self.receipt:
                data['candidateManifestSha256'] = 'f' * 64
            elif path == self.api:
                data['success'] = False
            else:
                data['results'][0]['status'] = 'FAIL'
            publisher.write(path, data)
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.prepare()
            self.assertFalse(self.output.exists())
            path.write_bytes(original)

    def test_missing_avalonia_symbols_cannot_publish(self):
        (self.fixture.output / f'NativeChannels.Avalonia.{VERSION}.snupkg').unlink()
        with self.assertRaises(ValueError):
            self.prepare()

    def test_release_rejects_changed_or_added_files(self):
        self.prepare()
        for relative in ['evidence/api.json', f'candidate/NativeChannels.Core.{VERSION}.nupkg', 'extra.txt']:
            path = self.output / relative
            original = path.read_bytes() if path.exists() else None
            path.write_bytes(b'changed')
            with self.subTest(path=path), self.assertRaises(ValueError):
                publisher.verify(self.output)
            if original is None:
                path.unlink()
            else:
                path.write_bytes(original)

    def test_acceptance_is_replayed_even_if_manifest_hash_is_replaced(self):
        manifest = self.prepare()
        path = self.output / 'evidence/avalonia-linux.json'
        data = publisher.read(path)
        data['results'][0]['status'] = 'FAIL'
        publisher.write(path, data)
        manifest['files'] = publisher.file_hashes(self.output)
        publisher.write(self.output / 'release-manifest.json', manifest)
        with self.assertRaisesRegex(ValueError, 'must pass'):
            publisher.verify(self.output)

    def test_preflight_includes_avalonia_and_stops_on_conflicting_version(self):
        manifest = self.prepare()
        conflict = io.BytesIO()
        with zipfile.ZipFile(conflict, 'w') as archive:
            archive.writestr('wrong.txt', 'other build')
        with patch.object(publisher.remote, 'fetch_package', side_effect=[None] * 5 + [conflict.getvalue()]) as fetch:
            with self.assertRaisesRegex(ValueError, 'NativeChannels.Avalonia.*differs'):
                publisher.preflight(self.output, manifest)
            self.assertEqual(fetch.call_count, 6)

    def test_identical_indexed_packages_allow_nuget_repository_signature(self):
        manifest = self.prepare()
        def fetch(package_id, version):
            data = io.BytesIO((self.output / 'candidate' / f'{package_id}.{version}.nupkg').read_bytes())
            with zipfile.ZipFile(data, 'a') as archive:
                archive.writestr('.signature.p7s', b'NuGet repository signature')
            return data.getvalue()
        with patch.object(publisher.remote, 'fetch_package', side_effect=fetch):
            self.assertEqual([], publisher.preflight(self.output, manifest, require_indexed=True))

    def test_publish_needs_exact_version_and_merged_source_before_credentials(self):
        self.prepare()
        with patch.object(publisher, 'read_api_key') as key, patch.object(publisher, 'require_merged') as merged:
            with self.assertRaisesRegex(ValueError, 'match'):
                publisher.publish(self.output, '0.2.0-preview.2', self.root / 'attempt.json')
            merged.assert_not_called()
            key.assert_not_called()
            merged.side_effect = ValueError('not merged')
            with self.assertRaisesRegex(ValueError, 'not merged'):
                publisher.publish(self.output, VERSION, self.root / 'attempt.json')
            key.assert_not_called()

    def test_collision_is_checked_before_credentials_or_upload(self):
        self.prepare()
        with patch.object(publisher, 'require_merged', return_value='b' * 40), \
                patch.object(publisher, 'preflight', side_effect=ValueError('conflict')), \
                patch.object(publisher, 'read_api_key') as key, patch.object(publisher, 'push') as push:
            with self.assertRaisesRegex(ValueError, 'conflict'):
                publisher.publish(self.output, VERSION, self.root / 'attempt.json')
            key.assert_not_called()
            push.assert_not_called()

    def test_publication_orders_dependencies_and_records_partial_failure_without_secret(self):
        self.prepare()
        report = self.root / 'attempt.json'
        with patch.object(publisher, 'require_merged', return_value='b' * 40), \
                patch.object(publisher.remote, 'fetch_package', return_value=None), \
                patch.object(publisher, 'read_api_key', return_value='secret-key'), \
                patch.object(publisher, 'push', side_effect=[None, ValueError('upload failed')]) as push:
            with self.assertRaisesRegex(ValueError, 'upload failed'):
                publisher.publish(self.output, VERSION, report)
            self.assertEqual('NativeChannels.Core.' + VERSION + '.nupkg', push.call_args_list[0].args[0].name)
            data = publisher.read(report)
            self.assertFalse(data['uploadsCompleted'])
            self.assertEqual('attempting', data['submissions'][1]['status'])
            self.assertNotIn('secret-key', report.read_text())

    def test_success_submits_all_six_packages_then_all_six_symbol_archives(self):
        self.prepare()
        with patch.object(publisher, 'require_merged', return_value='b' * 40), \
                patch.object(publisher.remote, 'fetch_package', return_value=None), \
                patch.object(publisher, 'read_api_key', return_value='secret-key'), \
                patch.object(publisher, 'push') as push, patch('builtins.print'):
            report = publisher.publish(self.output, VERSION, self.root / 'attempt.json')
            expected = [f'{package_id}.{VERSION}.{suffix}' for suffix in ('nupkg', 'snupkg')
                        for package_id in publisher.PACKAGE_IDS]
            self.assertEqual(expected, [call.args[0].name for call in push.call_args_list])
            self.assertTrue(report['uploadsCompleted'])
            self.assertTrue(all(row['status'] == 'submitted; indexing not verified' for row in report['submissions']))

    def test_changed_release_during_credential_entry_cannot_upload(self):
        self.prepare()
        def enter_key(gui):
            (self.output / 'evidence/api.json').write_text('{}')
            return 'secret-key'
        with patch.object(publisher, 'require_merged', return_value='b' * 40), \
                patch.object(publisher.remote, 'fetch_package', return_value=None), \
                patch.object(publisher, 'read_api_key', side_effect=enter_key), patch.object(publisher, 'push') as push:
            with self.assertRaises(ValueError):
                publisher.publish(self.output, VERSION, self.root / 'attempt.json')
            push.assert_not_called()

    def test_missing_public_package_is_not_successful_publication_verification(self):
        manifest = self.prepare()
        with patch.object(publisher.remote, 'fetch_package', return_value=None):
            with self.assertRaisesRegex(ValueError, 'Not indexed'):
                publisher.preflight(self.output, manifest, require_indexed=True)

    def test_http_upload_preserves_package_bytes_and_does_not_expose_key_on_error(self):
        self.prepare()
        path = self.output / 'candidate' / f'NativeChannels.Core.{VERSION}.nupkg'
        with patch.object(publisher.urllib.request, 'build_opener') as opener:
            opener.return_value.open.side_effect = urllib.error.HTTPError('url', 403, 'secret-key', {}, None)
            with self.assertRaises(ValueError) as error:
                publisher.push(path, 'secret-key')
            self.assertNotIn('secret-key', str(error.exception))
            request = opener.return_value.open.call_args.args[0]
            self.assertEqual(request.method, 'PUT')
            self.assertIn(path.read_bytes(), request.data)
            self.assertEqual('secret-key', request.get_header('X-nuget-apikey'))
            self.assertIsNone(publisher.NoRedirect().redirect_request(request, None, 302, '', {}, 'https://example.com'))

    def test_symbol_conflict_is_pending_indexing_not_success(self):
        path = self.root / 'test.snupkg'
        path.write_bytes(b'fixture')
        with patch.object(publisher.urllib.request, 'build_opener') as opener:
            opener.return_value.open.side_effect = urllib.error.HTTPError('url', 409, 'pending', {}, None)
            with self.assertRaisesRegex(ValueError, '409'):
                publisher.push(path, 'secret-key')


if __name__ == '__main__':
    unittest.main()
