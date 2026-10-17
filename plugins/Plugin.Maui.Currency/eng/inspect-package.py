#!/usr/bin/env python3
"""Check the reusable plugin package, including both native adapters and transitive imports."""
from pathlib import Path
import io
import plistlib
import xml.etree.ElementTree as ET
import zipfile
import argparse

root = Path(__file__).resolve().parents[3]
parser = argparse.ArgumentParser(description=__doc__)
scope = parser.add_mutually_exclusive_group()
scope.add_argument("--android-only", action="store_true")
scope.add_argument('--platform-only', choices=('ios', 'maccatalyst', 'macos'), help='Inspect an Apple development package')
scope.add_argument('--combined', action='store_true', help='Require the Windows target as well')
args = parser.parse_args()
versions = ET.parse(root / 'Directory.Packages.props')
version = versions.findtext('.//CurrencyPackageVersion').strip()
framework_version = versions.findtext('.//NativeChannelsPackageVersion').strip()
package = root / f"artifacts/packages/Plugin.Maui.Currency.{version}.nupkg"
with zipfile.ZipFile(package) as archive:
    files = archive.namelist()
    assert archive.read("README.md").startswith(b"# Plugin.Maui.Currency")
    platforms = ([args.platform_only] if args.platform_only else ['android'] if args.android_only
                 else ['android', 'ios', 'maccatalyst'] + (['windows'] if args.combined else []))
    assemblies = [f for f in files if f.startswith('lib/') and f.endswith('/Plugin.Maui.Currency.dll')]
    assert len(assemblies) == len(platforms), assemblies
    for platform in platforms:
        assert any(f.startswith(f"lib/net10.0-{platform}") and f.endswith("/Plugin.Maui.Currency.dll") for f in files)
    metadata = ET.fromstring(archive.read("Plugin.Maui.Currency.nuspec"))
    assert metadata.find(".//{*}version").text == version
    dependencies = metadata.findall(".//{*}dependency")
    assert len(dependencies) == len(platforms) and all(d.attrib["id"] == "NativeChannels" for d in dependencies)
    assert all(d.attrib["version"] == framework_version for d in dependencies)
    targets = archive.read("buildTransitive/Plugin.Maui.Currency.targets").decode()
    assert '../native/android/currency.aar' in targets and 'Bind="false"' in targets
    assert '../native/apple/MauiCurrencyNative.xcframework' in targets
    assert '<SmartLink>false</SmartLink>' in targets
    if args.platform_only:
        assert not any(f.startswith(('native/android/', 'native/windows/')) for f in files)
    else:
        with zipfile.ZipFile(io.BytesIO(archive.read("native/android/currency.aar"))) as aar:
            assert b"dev.nativechannels.currency.CurrencyModule" in aar.read("proguard.txt")
            with zipfile.ZipFile(io.BytesIO(aar.read("classes.jar"))) as classes:
                assert "dev/nativechannels/currency/CurrencyModule.class" in classes.namelist()
                assert not any(f.startswith("dev/nativechannels/NativeBridge") for f in classes.namelist())
    if not args.android_only:
        prefix = "native/apple/MauiCurrencyNative.xcframework/"
        manifest = plistlib.loads(archive.read(prefix + "Info.plist"))
        expected = {("ios", ""): {"arm64"}, ("ios", "simulator"): {"arm64", "x86_64"}, ("ios", "maccatalyst"): {"arm64", "x86_64"}}
        if args.platform_only == "macos":
            expected = {("macos", ""): {"arm64", "x86_64"}}
        actual = {}
        for library in manifest["AvailableLibraries"]:
            key = (library["SupportedPlatform"], library.get("SupportedPlatformVariant", ""))
            actual[key] = set(library["SupportedArchitectures"])
            framework = prefix + library["LibraryIdentifier"] + "/" + library["LibraryPath"]
            assert framework + "/MauiCurrencyNative" in files
            assert b"MCCurrencyModule" in archive.read(framework + "/Headers/MauiCurrencyNative-Swift.h")
        assert actual == expected, actual
scope_name = args.platform_only or ('Android' if args.android_only else 'combined' if args.combined else 'mobile')
print(f"PASS Plugin.Maui.Currency: assemblies, dependencies, transitive imports and native assets ({scope_name} feed)")
