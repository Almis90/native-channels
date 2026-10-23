"""Exercise the package targets using MSBuild, including item metadata."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
TARGETS = ROOT / 'plugins/Plugin.Uno.PdfViewer/src/Plugin.Uno.PdfViewer/buildTransitive/Plugin.Uno.PdfViewer.targets'
DOTNET = Path(os.environ.get('DOTNET_HOST_PATH',
                            ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')))


@unittest.skipUnless(DOTNET.is_file(), 'Requires the pinned SDK or DOTNET_HOST_PATH')
class MobilePdfAssetsTests(unittest.TestCase):
    def check_assets(self, platform, runtime, expected):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / 'verify.proj'
            project.write_text(f'''<Project DefaultTargets="Verify">
  <PropertyGroup>
    <TargetFramework>net10.0-{platform}</TargetFramework>
    <UnoUIRuntimeIdentifier>{runtime}</UnoUIRuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <ResolvedCompileFileDefinitions Include="mobile.dll" NuGetPackageId="Plugin.Uno.PdfViewer" Marker="original" />
    <RuntimeCopyLocalItems Include="mobile.dll" NuGetPackageId="Plugin.Uno.PdfViewer" DestinationSubPath="Plugin.Uno.PdfViewer.dll" />
  </ItemGroup>
  <Import Project="{escape(str(TARGETS))}" />
  <Target Name="ReplaceUnoRuntime">
    <ItemGroup>
      <ResolvedCompileFileDefinitions Remove="@(ResolvedCompileFileDefinitions)" />
      <RuntimeCopyLocalItems Remove="@(RuntimeCopyLocalItems)" />
      <ResolvedCompileFileDefinitions Include="desktop.dll" NuGetPackageId="Plugin.Uno.PdfViewer" />
      <RuntimeCopyLocalItems Include="desktop.dll" NuGetPackageId="Plugin.Uno.PdfViewer" />
      <ResolvedCompileFileDefinitions Include="uno-skia.dll" NuGetPackageId="Uno.WinUI" />
      <RuntimeCopyLocalItems Include="uno-skia.dll" NuGetPackageId="Uno.WinUI" />
    </ItemGroup>
  </Target>
  <Target Name="Verify" DependsOnTargets="ReplaceUnoRuntime">
    <ItemGroup>
      <PdfCompile Include="@(ResolvedCompileFileDefinitions)" Condition="'%(NuGetPackageId)' == 'Plugin.Uno.PdfViewer'" />
      <PdfRuntime Include="@(RuntimeCopyLocalItems)" Condition="'%(NuGetPackageId)' == 'Plugin.Uno.PdfViewer'" />
      <UnoRuntime Include="@(RuntimeCopyLocalItems)" Condition="'%(NuGetPackageId)' == 'Uno.WinUI'" />
    </ItemGroup>
    <Error Condition="'@(PdfCompile)' != '{expected}.dll'" Text="Wrong PDF compile asset: @(PdfCompile)" />
    <Error Condition="'@(PdfRuntime)' != '{expected}.dll'" Text="Wrong PDF runtime asset: @(PdfRuntime)" />
    <Error Condition="'@(UnoRuntime)' != 'uno-skia.dll'" Text="Uno runtime replacement was lost" />
    <Error Condition="'{expected}' == 'mobile' And '%(PdfCompile.Marker)' != 'original'" Text="Compile metadata lost" />
    <Error Condition="'{expected}' == 'mobile' And '%(PdfRuntime.DestinationSubPath)' != 'Plugin.Uno.PdfViewer.dll'" Text="Runtime metadata lost" />
  </Target>
</Project>''')
            result = subprocess.run([str(DOTNET), 'msbuild', str(project), '-nologo'],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_ios_preserves_mobile_assets(self):
        self.check_assets('ios', 'Skia', 'mobile')

    def test_android_preserves_mobile_assets(self):
        self.check_assets('android', 'Skia', 'mobile')

    def test_desktop_is_unchanged(self):
        self.check_assets('desktop', 'Skia', 'desktop')

    def test_native_mobile_runtime_is_unchanged(self):
        self.check_assets('ios', 'Native', 'desktop')


if __name__ == '__main__':
    unittest.main()
