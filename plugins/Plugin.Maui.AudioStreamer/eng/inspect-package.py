#!/usr/bin/env python3
"""Check the reusable plugin package, including both native adapters and transitive imports."""
from pathlib import Path
import io
import argparse
import plistlib
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
scope = parser.add_mutually_exclusive_group()
scope.add_argument("--android-only", action="store_true")
scope.add_argument("--combined", action="store_true")
args = parser.parse_args()
platforms = ["android"] if args.android_only else ["android", "ios", "maccatalyst"] + (["windows"] if args.combined else [])

root = Path(__file__).resolve().parents[3]
package = root / "artifacts/packages/Plugin.Maui.AudioStreamer.0.1.0-alpha.1.nupkg"
with zipfile.ZipFile(package) as archive:
    files = archive.namelist()
    assert archive.read("README.md").startswith(b"# Plugin.Maui.AudioStreamer")
    for platform in platforms:
        assert any(f.startswith(f"lib/net10.0-{platform}") and f.endswith("/Plugin.Maui.AudioStreamer.dll") for f in files)
    metadata = ET.fromstring(archive.read("Plugin.Maui.AudioStreamer.nuspec"))
    assert metadata.find(".//{*}version").text == "0.1.0-alpha.1"
    dependencies = metadata.findall(".//{*}dependency")
    assert len(dependencies) == len(platforms) and all(d.attrib["id"] == "NativeChannels.Maui" for d in dependencies)
    assert all(d.attrib["version"] == ET.parse(root / "Directory.Packages.props").findtext(".//NativeChannelsPackageVersion").strip() for d in dependencies)
    targets = archive.read("buildTransitive/Plugin.Maui.AudioStreamer.targets").decode()
    assert '../native/android/audio.aar' in targets and 'Bind="false"' in targets
    assert '../native/apple/MauiAudioStreamerNative.xcframework' in targets
    assert '<SmartLink>false</SmartLink>' in targets
    with zipfile.ZipFile(io.BytesIO(archive.read("native/android/audio.aar"))) as aar:
        assert b"dev.nativechannels.audio.AudioStreamerModule" in aar.read("proguard.txt")
        assert b"AudioStreamerInitializer" in aar.read("AndroidManifest.xml")
        with zipfile.ZipFile(io.BytesIO(aar.read("classes.jar"))) as classes:
            assert "dev/nativechannels/audio/AudioStreamerModule.class" in classes.namelist()
            assert not any(f.startswith("dev/nativechannels/NativeBridge") for f in classes.namelist())
    if not args.android_only:
        prefix = "native/apple/MauiAudioStreamerNative.xcframework/"
        manifest = plistlib.loads(archive.read(prefix + "Info.plist"))
        expected = {("ios", ""): {"arm64"}, ("ios", "simulator"): {"arm64", "x86_64"}, ("ios", "maccatalyst"): {"arm64", "x86_64"}}
        actual = {}
        for library in manifest["AvailableLibraries"]:
            key = (library["SupportedPlatform"], library.get("SupportedPlatformVariant", ""))
            actual[key] = set(library["SupportedArchitectures"])
            framework = prefix + library["LibraryIdentifier"] + "/" + library["LibraryPath"]
            assert framework + "/MauiAudioStreamerNative" in files
            assert b"MCAudioStreamerModule" in archive.read(framework + "/Headers/MauiAudioStreamerNative-Swift.h")
        assert actual == expected, actual
print("PASS Plugin.Maui.AudioStreamer: assemblies, dependencies, transitive imports, AAR" + (" (Android development feed)" if args.android_only else ", and all XCFramework slices"))
