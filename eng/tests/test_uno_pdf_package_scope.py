"""Ensure desktop PDF builds share one managed implementation before package assembly."""
import json
import os
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
DOTNET = ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
PROJECT = ROOT / 'plugins/Plugin.Uno.PdfViewer/src/Plugin.Uno.PdfViewer/Plugin.Uno.PdfViewer.csproj'


@unittest.skipUnless(DOTNET.is_file(), 'Requires the pinned SDK')
class UnoPdfPackageScopeTests(unittest.TestCase):
    def evaluate(self, *properties):
        result = subprocess.run([str(DOTNET), 'msbuild', str(PROJECT),
                                 *['-p:' + value for value in properties],
                                 '-getProperty:TargetFramework,DefineConstants', '-getItem:Compile,PackageReference'],
                                cwd=ROOT, check=True, capture_output=True, text=True)
        return json.loads(result.stdout)

    def test_desktop_platform_builds_compile_the_same_backends_and_dependencies(self):
        builds = [self.evaluate('NativeChannelsDesktopOnly=true', *flags) for flags in (
            (), ('NativeChannelsLinuxOnly=true',), ('NativeChannelsMacOSOnly=true',))]
        def managed_inputs(build):
            return (build['Properties'],
                    sorted(item['Identity'] for item in build['Items']['Compile']),
                    sorted(item['Identity'] for item in build['Items']['PackageReference']))
        for build in builds:
            self.assertEqual(managed_inputs(build), managed_inputs(builds[0]))
        files = {Path(item['Identity']).name for item in builds[0]['Items']['Compile']}
        self.assertTrue({'WindowsPdfView.cs', 'UnoLinuxNativeView.cs', 'UnoMacOSNativeView.cs'} <= files)

    def test_mobile_and_browser_do_not_compile_desktop_backends(self):
        for scope in ('NativeChannelsAppleTarget=ios', 'NativeChannelsAndroidOnly=true',
                      'NativeChannelsBrowserOnly=true'):
            with self.subTest(scope=scope):
                build = self.evaluate(scope)
                files = {Path(item['Identity']).name for item in build['Items']['Compile']}
                self.assertFalse({'WindowsPdfView.cs', 'WindowsPdfImports.cs',
                                  'UnoLinuxNativeView.cs', 'UnoMacOSNativeView.cs'} & files)
                dependencies = {item['Identity'] for item in build['Items']['PackageReference']}
                self.assertNotIn('System.Drawing.Common', dependencies)


if __name__ == '__main__':
    unittest.main()
