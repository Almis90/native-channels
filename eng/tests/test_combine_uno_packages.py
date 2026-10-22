import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ENG = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ENG))
spec = importlib.util.spec_from_file_location('combine_uno', ENG / 'combine-uno-packages.py')
combine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(combine)
COMMIT = 'a' * 40


def package(folder, name, role, symbols=False, commit=COMMIT):
    tfm = {'android': 'net10.0-android36.0', 'ios': 'net10.0-ios26.0',
           'browser': 'net10.0-browserwasm1.0'}.get(role, 'net10.0')
    if name in ('NativeChannels.Core', 'NativeChannels.Uno'):
        tfm = 'net10.0'
    manifest = f'''<package><metadata><id>{name}</id><version>0.3.0-test</version>
<license type="expression">MIT</license><readme>README.md</readme>
<repository type="git" url="https://example.com/repo" commit="{commit}"/>
<dependencies><group targetFramework="{tfm}"/></dependencies></metadata></package>'''
    contents = {name + '.nuspec': manifest.encode(), 'README.md': b'readme',
                '[Content_Types].xml': b'<Types/>',
                f'lib/{tfm}/{name}.{"pdb" if symbols else "dll"}': role.encode(),
                f'runtimes/{role}/native/example.bin': role.encode()}
    path = folder / f'{name}.0.3.0-test.{"snupkg" if symbols else "nupkg"}'
    combine.write_archive(path, contents)
    return path


class CombineUnoTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.inputs = {}
        for role in combine.PLATFORMS:
            folder = self.root / role
            folder.mkdir()
            self.inputs[role] = folder
            names = combine.PACKAGES | ({'NativeChannels.Native.Android'} if role == 'android'
                                        else {'NativeChannels.Native.Apple'} if role == 'ios' else set())
            for name in names:
                for symbols in (False, True):
                    package(folder, name, role, symbols)

    def test_preserves_platform_payloads_and_canonical_desktop_symbols(self):
        output = self.root / 'combined'
        report = combine.assemble(self.inputs, output, COMMIT)
        self.assertEqual(9, len(report['packages']))
        self.assertEqual('pending', report['runtimeAcceptance'])
        for extension, payload in (('nupkg', 'dll'), ('snupkg', 'pdb')):
            with zipfile.ZipFile(output / f'Plugin.Uno.PdfViewer.0.3.0-test.{extension}') as archive:
                self.assertEqual(b'windows', archive.read(f'lib/net10.0/Plugin.Uno.PdfViewer.{payload}'))
                for role in combine.PLATFORMS:
                    self.assertEqual(role.encode(), archive.read(f'runtimes/{role}/native/example.bin'))
                self.assertIn(f'lib/net10.0-ios26.0/Plugin.Uno.PdfViewer.{payload}', archive.namelist())

    def test_rejects_different_source_commit_before_writing_output(self):
        package(self.inputs['ios'], 'Plugin.Currency', 'ios', commit='b' * 40)
        with self.assertRaises(ValueError):
            combine.assemble(self.inputs, self.root / 'combined', COMMIT)
        self.assertFalse((self.root / 'combined').exists())

    def test_rejects_symbols_from_other_source(self):
        package(self.inputs['macos'], 'Plugin.Currency', 'macos', symbols=True, commit='b' * 40)
        with self.assertRaises(ValueError):
            combine.assemble(self.inputs, self.root / 'combined', COMMIT)
        self.assertFalse((self.root / 'combined').exists())

    def test_requires_every_platform(self):
        del self.inputs['linux']
        with self.assertRaises(ValueError):
            combine.assemble(self.inputs, self.root / 'combined', COMMIT)

    def test_never_overwrites_output(self):
        output = self.root / 'combined'
        output.mkdir()
        with self.assertRaises(ValueError):
            combine.assemble(self.inputs, output, COMMIT)

    def test_conflicting_native_payload_is_rejected(self):
        left = combine.read_archive(self.inputs['windows'] / 'Plugin.Currency.0.3.0-test.nupkg')
        right = combine.read_archive(self.inputs['macos'] / 'Plugin.Currency.0.3.0-test.nupkg')
        right['runtimes/windows/native/example.bin'] = b'conflict'
        with self.assertRaisesRegex(ValueError, 'conflicting payload'):
            combine.merge_payload(left, right, 'Plugin.Currency')


if __name__ == '__main__':
    unittest.main()
