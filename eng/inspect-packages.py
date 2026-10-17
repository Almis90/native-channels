#!/usr/bin/env python3
"""Assert native payload and dependency packaging without relying on project outputs."""
from pathlib import Path
import argparse, io, plistlib, zipfile, xml.etree.ElementTree as ET
root = Path(__file__).resolve().parent.parent
packages = root / 'artifacts/packages'
parser = argparse.ArgumentParser(description=__doc__)
scope = parser.add_mutually_exclusive_group()
scope.add_argument('--android-only', action='store_true', help='Inspect an Android development feed')
scope.add_argument('--platform-only', choices=['ios', 'maccatalyst', 'macos', 'windows'], help='Inspect one framework development target')
scope.add_argument('--combined', action='store_true', help='Require Windows alongside mobile targets')
args = parser.parse_args()
version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
names = ['NativeChannels.Core', 'NativeChannels']
if args.platform_only != 'macos':
    names.append('NativeChannels.Maui')
platforms = [args.platform_only] if args.platform_only else (['android'] if args.android_only else ['android', 'ios', 'maccatalyst'] + (['windows'] if args.combined else []))
if 'android' in platforms:
    names.append('NativeChannels.Native.Android')
if set(platforms) & {'ios', 'maccatalyst', 'macos'}:
    names.append('NativeChannels.Native.Apple')
for name in names:
    path = packages / f'{name}.{version}.nupkg'
    with zipfile.ZipFile(path) as archive:
        files = archive.namelist()
        assert 'README.md' in files
        assert any(f.endswith(f'/{name}.dll') for f in files)
        if name in ('NativeChannels', 'NativeChannels.Maui'):
            groups = ET.fromstring(archive.read(name + '.nuspec')).findall('.//{*}dependencies/{*}group')
            assert len(groups) == len(platforms), 'Unexpected dependency groups'
            for platform in platforms:
                assert any(f.startswith(f'lib/net10.0-{platform}') and f.endswith(f'/{name}.dll') for f in files)
            for group in groups:
                if 'windows' in group.attrib['targetFramework']:
                    assert not any(d.attrib['id'].startswith('NativeChannels.Native.') for d in group)
        if name.endswith('Android'):
            aar = next(f for f in files if f.endswith('.aar'))
            with zipfile.ZipFile(io.BytesIO(archive.read(aar))) as native:
                assert 'classes.jar' in native.namelist()
                with zipfile.ZipFile(io.BytesIO(native.read('classes.jar'))) as classes:
                    for helper in ('BasicMessageChannel', 'EventChannel', 'EventSink', 'StringMessageCodec', 'BinaryMessageCodec', 'StandardMessageCodec', 'StandardMessageMap', 'NativeViewHost', 'NativeViewFactory', 'NativeViewInstance'):
                        assert f'dev/nativechannels/{helper}.class' in classes.namelist()
                assert any('proguard' in f for f in native.namelist())
            assert b'Xamarin.Kotlin.StdLib' in archive.read(name + '.nuspec')
        if name.endswith('Apple'):
            resources = [f for f in files if f.endswith('.resources.zip')]
            assert resources, 'Missing Apple native resources'
            for resource in resources:
                with zipfile.ZipFile(io.BytesIO(archive.read(resource))) as native:
                    assert any(f.endswith('/NativeChannelsNative') for f in native.namelist())
                    assert any(f.endswith('/Info.plist') for f in native.namelist())
                    headers = [f for f in native.namelist() if f.endswith('/NativeChannelsNative-Swift.h')]
                    assert len(headers) == (1 if args.platform_only == 'macos' else 3), 'Unexpected Apple slices'
                    if args.platform_only == 'macos':
                        manifest = next(f for f in native.namelist() if f.endswith('NativeChannelsNative.xcframework/Info.plist'))
                        libraries = plistlib.loads(native.read(manifest))['AvailableLibraries']
                        assert len(libraries) == 1
                        assert libraries[0]['SupportedPlatform'] == 'macos'
                        assert not libraries[0].get('SupportedPlatformVariant')
                        assert set(libraries[0]['SupportedArchitectures']) == {'arm64', 'x86_64'}
                    for header in headers:
                        for symbol in (b'MCNativeViewHost', b'MCNativeViewFactory', b'MCNativeViewInstance'):
                            assert symbol in native.read(header)
        print(f'PASS {path.name}: managed assembly, metadata, and required native packaging')
