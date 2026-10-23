"""Check NuGet RID selection, including the Android-to-Linux fallback regression."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
DOTNET = Path(os.environ.get('DOTNET_HOST_PATH', ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')))


@unittest.skipUnless(DOTNET.is_file(), 'Requires the pinned SDK or DOTNET_HOST_PATH')
class AndroidNativeAssetsTests(unittest.TestCase):
    def check_selection(self, rid, placeholder, expected):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            # Read the real packaging item rather than duplicating its path in the test.
            project = root / 'items.proj'
            project.write_text(f'''<Project><PropertyGroup><NativeChannelsAndroidOnly>true</NativeChannelsAndroidOnly></PropertyGroup>
<Import Project="{escape(str(ROOT / 'eng/AndroidNativeAssets.props'))}" /></Project>''')
            result = subprocess.run([str(DOTNET), 'msbuild', str(project), '-getItem:None'], capture_output=True, text=True, check=True)
            marker = json.loads(result.stdout)['Items']['None'][0]
            with zipfile.ZipFile(root / 'NativeSelection.1.0.0.nupkg', 'w') as archive:
                archive.writestr('NativeSelection.nuspec', '<package><metadata><id>NativeSelection</id><version>1.0.0</version><authors>test</authors><description>test</description></metadata></package>')
                archive.writestr('runtimes/linux-x64/native/libtest.so', b'linux')
                if placeholder:
                    archive.write(marker['Identity'], marker['PackagePath'] + '_._')
            project = root / 'consumer.csproj'
            project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><RuntimeIdentifier>{rid}</RuntimeIdentifier>
<SelfContained>false</SelfContained><RestorePackagesPath>{escape(str(root / 'cache'))}</RestorePackagesPath>
</PropertyGroup><ItemGroup><PackageReference Include="NativeSelection" Version="1.0.0" /></ItemGroup></Project>''')
            config = root / 'NuGet.Config'
            config.write_text(f'<configuration><packageSources><clear/><add key="local" value="{escape(str(root))}"/></packageSources><packageSourceMapping><clear/></packageSourceMapping></configuration>')
            result = subprocess.run([str(DOTNET), 'restore', str(project), '--configfile', str(config)], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            assets = json.loads((root / 'obj/project.assets.json').read_text())
            target = next(value for key, value in assets['targets'].items() if key.endswith('/' + rid))
            native = target['NativeSelection/1.0.0'].get('native', {})
            actual = [path for path in native if not path.endswith('/_._')]
            self.assertEqual(expected, actual)

    def test_without_marker_android_inherits_linux(self):
        self.check_selection('android-x64', False, ['runtimes/linux-x64/native/libtest.so'])

    def test_android_does_not_inherit_linux(self):
        for rid in ('android-x64', 'android-x86', 'android-arm', 'android-arm64'):
            with self.subTest(rid=rid):
                self.check_selection(rid, True, [])

    def test_linux_keeps_native_library(self):
        self.check_selection('linux-x64', True, ['runtimes/linux-x64/native/libtest.so'])


if __name__ == '__main__':
    unittest.main()
