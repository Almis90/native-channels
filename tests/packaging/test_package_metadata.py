import importlib.util
import io
from pathlib import Path
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('metadata', ROOT / 'eng/package_metadata.py')
metadata = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metadata)


class PackageMetadataTests(unittest.TestCase):
    def archive(self, field=None, replacement='', omit=None):
        fields = {
            'projectUrl': 'https://almis90.github.io/native-channels/',
            'description': 'NativeChannels portable channel contracts, codecs, typed serialization, and opt-in operation diagnostics for .NET.',
            'releaseNotes': 'Operation diagnostics. https://almis90.github.io/native-channels/changelog',
            'icon': 'icon.png', 'readme': 'README.md',
        }
        if field:
            fields[field] = replacement
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as archive:
            archive.writestr('NativeChannels.Core.nuspec', '<package><metadata>' +
                             ''.join(f'<{key}>{value}</{key}>' for key, value in fields.items()) +
                             '</metadata></package>')
            if omit != 'icon':
                archive.writestr('icon.png', (ROOT / 'assets/package-icon.png').read_bytes())
            if omit != 'readme':
                archive.writestr('README.md', 'Instructions')
        return zipfile.ZipFile(stream)

    def test_complete_presentation_passes(self):
        with self.archive() as archive:
            metadata.validate(archive)

    def test_regressions_are_rejected(self):
        for field, value in [('projectUrl', 'https://github.com/Almis90/native-channels'),
                             ('description', 'Generic description'), ('releaseNotes', ''),
                             ('icon', ''), ('readme', '')]:
            with self.subTest(field=field), self.archive(field, value) as archive:
                with self.assertRaises(ValueError):
                    metadata.validate(archive)
        for omitted in ('icon', 'readme'):
            with self.subTest(omitted=omitted), self.archive(omit=omitted) as archive:
                with self.assertRaises(KeyError):
                    metadata.validate(archive)
