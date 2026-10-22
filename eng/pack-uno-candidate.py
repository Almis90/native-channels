#!/usr/bin/env python3
"""Pack one candidate input from built native assets; never publish packages."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET

from uno_package_matrix import PLATFORMS, COMPATIBILITY_PLATFORMS, expected_packages

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', choices=PLATFORMS + COMPATIBILITY_PLATFORMS, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True, help='New input directory (packages, cache and logs)')
    parser.add_argument('--reuse-external-cache', type=Path)
    parser.add_argument('--include-existing-hosts', action='store_true')
    parser.add_argument('--version', default='0.3.0-preview.6')
    parser.add_argument('--android-sdk', type=Path)
    parser.add_argument('--java-sdk', type=Path)
    args = parser.parse_args()
    if args.platform in COMPATIBILITY_PLATFORMS and not args.include_existing_hosts:
        parser.error('Compatibility inputs require --include-existing-hosts')
    status = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True)
    if status:
        parser.error('Commit source changes before packing a candidate')
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    output = args.output.resolve()
    if output.exists():
        parser.error('Output must be a new directory')
    feed, cache = output / 'packages', output / 'cache'
    feed.mkdir(parents=True)
    cache.mkdir()
    if args.reuse_external_cache:
        for source in args.reuse_external_cache.resolve().iterdir():
            if source.is_dir() and not source.name.lower().startswith(('nativechannels', 'plugin.')):
                shutil.copytree(source, cache / source.name, copy_function=os.link)
    config = output / 'NuGet.Config'
    xml = ET.Element('configuration')
    sources = ET.SubElement(xml, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local', value=str(feed))
    ET.SubElement(sources, 'add', key='nuget', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(xml, 'packageSourceMapping')
    ET.SubElement(mapping, 'clear')
    local = ET.SubElement(mapping, 'packageSource', key='local')
    for pattern in ('NativeChannels*', 'Plugin.*'):
        ET.SubElement(local, 'package', pattern=pattern)
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget'), 'package', pattern='*')
    ET.ElementTree(xml).write(config, encoding='utf-8')
    bootstrap = output / 'Bootstrap.Config'
    bootstrap.write_text('<configuration><packageSources><clear/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/></packageSourceMapping></configuration>')
    flags = {
        'windows': ['NativeChannelsDesktopOnly=true'],
        'linux': ['NativeChannelsLinuxOnly=true'],
        'macos': ['NativeChannelsDesktopOnly=true', 'NativeChannelsMacOSOnly=true', 'NativeChannelsIncludeAppleDesktopAssets=true'],
        'android': ['NativeChannelsAndroidOnly=true'],
        'ios': ['NativeChannelsAppleTarget=ios'],
        'browser': ['NativeChannelsBrowserOnly=true'],
        'windows-native': ['NativeChannelsWindowsOnly=true'],
        'maccatalyst': ['NativeChannelsAppleTarget=maccatalyst'],
        'macos-native': ['NativeChannelsAppleTarget=macos'],
    }[args.platform]
    flags += [f'Version={args.version}', f'NativeChannelsPackageVersion={args.version}', f'RestorePackagesPath={cache}']
    for property_name, path in (('AndroidSdkDirectory', args.android_sdk), ('JavaSdkDirectory', args.java_sdk)):
        if path:
            flags.append(f'{property_name}={path.resolve()}')
    # Dependencies must be available in the local feed before their consumers.
    order = ['NativeChannels.Core', 'NativeChannels.Native.Android', 'NativeChannels.Native.Apple',
             'NativeChannels', 'NativeChannels.Maui', 'NativeChannels.Avalonia', 'NativeChannels.Uno',
             'Plugin.Currency', 'Plugin.AudioStreamer', 'Plugin.VolumeMonitor',
             'Plugin.Maui.PdfViewer', 'Plugin.Avalonia.PdfViewer', 'Plugin.Uno.PdfViewer']
    names = expected_packages(args.platform, args.include_existing_hosts)
    for name in order:
        if name not in names:
            continue
        project = ROOT / (f'plugins/{name}/src/{name}' if name.startswith('Plugin.') else f'src/{name}')
        print(args.platform, name, flush=True)
        with (output / (name + '.log')).open('w') as log:
            subprocess.run([str(args.dotnet.resolve()), 'pack', str(project), '-c', 'Release', '-o', str(feed),
                            *[f'-p:{flag}' for flag in flags],
                            f'-p:RestoreConfigFile={bootstrap if name == "NativeChannels.Core" else config}',
                            f'-p:NativeChannelsUseCorePackage={str(name != "NativeChannels.Core").lower()}'],
                           cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
    after = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True)
    if after or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != commit:
        raise RuntimeError('Source changed while packing')
    (output / 'source.json').write_text(json.dumps({'commit': commit, 'platform': args.platform, 'status': after}, indent=2) + '\n')
    print('PASS', feed, flush=True)


if __name__ == '__main__':
    main()
