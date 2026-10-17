"""Check the sample inventory and the paths actually selected by CI."""
import os
from pathlib import Path
import subprocess
import tempfile
import textwrap
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / 'samples/Maui'
IDENTITIES = {
    'ChannelExplorer': ('ChannelExplorer', 'dev.nativechannels.sample'),
    'Currency': ('Currency.Sample', 'dev.nativechannels.currency.sample'),
    'AudioStreamer': ('AudioStreamer.Sample', 'dev.nativechannels.audio.sample'),
    'VolumeMonitor': ('VolumeMonitor.Sample', 'dev.nativechannels.volume.sample'),
    'PdfViewer': ('PdfViewer.Sample', 'dev.nativechannels.pdf.sample'),
}


class MauiSampleTests(unittest.TestCase):
    def test_every_maui_sample_is_a_single_project_package_consumer(self):
        projects = [path for path in (ROOT / 'samples').rglob('*.csproj')
                    if 'obj' not in path.parts and 'bin' not in path.parts
                    and ET.parse(path).findtext('.//UseMaui') == 'true']
        self.assertEqual({SAMPLES / folder / (name + '.csproj')
                          for folder, (name, _) in IDENTITIES.items()}, set(projects))
        for path in projects:
            with self.subTest(project=path):
                project = ET.parse(path)
                name, app_id = IDENTITIES[path.parent.name]
                self.assertEqual('true', project.findtext('.//SingleProject'))
                self.assertEqual(name, project.findtext('.//AssemblyName'))
                self.assertEqual(name, project.findtext('.//RootNamespace'))
                self.assertEqual(app_id, project.findtext('.//ApplicationId'))
                self.assertFalse(project.findall('.//ProjectReference'))
                self.assertTrue(project.findall('.//PackageReference'))
                frameworks = ';'.join(item.text for item in project.findall('.//TargetFrameworks'))
                for target, platform in [('android', 'Android'), ('ios', 'iOS'),
                                         ('maccatalyst', 'MacCatalyst'),
                                         ('windows10.0.19041.0', 'Windows')]:
                    self.assertIn('net10.0-' + target, frameworks)
                    self.assertTrue((path.parent / 'Platforms' / platform).is_dir())
                manifest = ET.parse(path.parent / 'Platforms/Windows/Package.appxmanifest')
                self.assertEqual(app_id, manifest.find('{*}Identity').get('Name'))
                for item in project.findall('.//MauiIcon') + project.findall('.//MauiSplashScreen'):
                    self.assertTrue((path.parent / item.get('Include')).is_file())

    def test_ci_sample_choices_resolve_to_real_projects_and_restore_scripts(self):
        action = (ROOT / '.github/actions/select-sample/action.yml').read_text()
        script = textwrap.dedent(action.split('      run: |\n', 1)[1])
        for choice in ('Framework', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer'):
            with self.subTest(choice=choice), tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary) / 'output'
                subprocess.run(['bash', '-eu', '-c', script], check=True, cwd=ROOT,
                               env={**os.environ, 'SAMPLE': choice, 'GITHUB_OUTPUT': str(output)})
                values = dict(line.split('=', 1) for line in output.read_text().splitlines())
                directory = ROOT / values['project']
                self.assertEqual(1, len(list(directory.glob('*.csproj'))))
                self.assertTrue(directory.is_relative_to(SAMPLES))
                restore = ROOT / 'eng' / ('restore-' + values['suffix'] + '.sh')
                self.assertIn(values['project'] + '/', restore.read_text())

    def test_shared_currency_checker_resolves_outside_the_maui_tree(self):
        path = SAMPLES / 'Currency/Currency.Sample.csproj'
        project = ET.parse(path)
        shared = ROOT / 'samples/Currency.Shared/PluginVerification.cs'
        includes = {(path.parent / item.get('Include')).resolve()
                    for item in project.findall('.//Compile')}
        self.assertIn(shared, includes)
        self.assertNotIn('Microsoft.Maui', shared.read_text())
        self.assertIn('namespace Currency.Verification;', shared.read_text())


if __name__ == '__main__':
    unittest.main()
