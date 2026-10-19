"""Evaluate the PDF package's platform selection using the pinned SDK."""
import json
import os
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
DOTNET = ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
PROJECT = ROOT / 'plugins/Plugin.Maui.PdfViewer/src/Plugin.Maui.PdfViewer/Plugin.Maui.PdfViewer.csproj'


@unittest.skipUnless(DOTNET.is_file(), 'Requires the pinned local SDK')
class PdfPackageScopeTests(unittest.TestCase):
    def test_apple_scope_excludes_android_without_changing_android_or_combined_feed(self):
        for target, properties, framework, android in (
            ('ios', ['NativeChannelsAppleTarget=ios'], 'net10.0-ios', False),
            ('maccatalyst', ['NativeChannelsAppleTarget=maccatalyst'], 'net10.0-maccatalyst', False),
            ('android', ['NativeChannelsAndroidOnly=true'], 'net10.0-android', True),
            ('combined', [], None, True),
        ):
            with self.subTest(target=target):
                result = subprocess.run(
                    [str(DOTNET), 'msbuild', str(PROJECT),
                     *['-p:' + value for value in properties],
                     '-getProperty:TargetFrameworks', '-getItem:None'],
                    cwd=ROOT, check=True, capture_output=True, text=True)
                evaluated = json.loads(result.stdout)
                if framework:
                    self.assertEqual(evaluated['Properties']['TargetFrameworks'], framework)
                packaged = [item for item in evaluated['Items']['None'] if item.get('Pack') == 'true']
                self.assertEqual(any(item.get('PackagePath') == 'native/android/pdf.aar'
                                     for item in packaged), android)


if __name__ == '__main__':
    unittest.main()
