#!/usr/bin/env python3
"""Validate package-only Uno samples on an ARM64 iOS simulator."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

from consumer_projects import verify_project_references

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.3.0-preview.6'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', required=True, type=Path)
    parser.add_argument('--device', required=True)
    parser.add_argument('--sample', choices=('Interop', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer'), default='Currency')
    parser.add_argument('--reuse-external-cache', type=Path)
    args = parser.parse_args()
    if platform.system() != 'Darwin' or platform.machine() != 'arm64':
        parser.error('Run on an ARM64 Mac with Xcode and the iOS workload.')
    dotnet = args.dotnet.resolve()
    sample = args.sample
    folder = ROOT / 'artifacts/uno-ios' / uuid.uuid4().hex
    feed, cache = folder / 'packages', folder / 'cache'
    feed.mkdir(parents=True)
    cache.mkdir()
    report = {'sample': sample, 'platform': 'ios', 'rid': 'iossimulator-arm64',
              'device': args.device, 'build': 'pending', 'runtime': 'pending',
              'commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'sourceStatus': subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True)}
    print(f'Evidence: {folder}', flush=True)
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), PATH=str(dotnet.parent) + os.pathsep + os.environ['PATH'])

    def command(argv, name, cwd=ROOT, extra_env=None, timeout=900):
        print(name, flush=True)
        with (folder / (name + '.log')).open('w') as log:
            subprocess.run([str(x) for x in argv], cwd=cwd, env=extra_env or env,
                           stdout=log, stderr=subprocess.STDOUT, check=True, timeout=timeout)

    try:
        if args.reuse_external_cache:
            def copy(source, destination):
                try:
                    os.link(source, destination)
                except OSError:
                    shutil.copy2(source, destination)
                return destination
            for package in args.reuse_external_cache.resolve().iterdir():
                if package.is_dir() and not package.name.lower().startswith(('nativechannels', 'plugin.')):
                    shutil.copytree(package, cache / package.name, copy_function=copy)
        config = folder / 'NuGet.Config'
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
        ET.ElementTree(xml).write(config, encoding='utf-8', xml_declaration=True)
        bootstrap = folder / 'Bootstrap.Config'
        bootstrap.write_text('<configuration><packageSources><clear/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/></packageSourceMapping></configuration>')
        command(['python3', 'eng/build-apple.py'], 'native-bridge')
        if sample != 'Interop':
            native_plugin = 'Maui.PdfViewer' if sample == 'PdfViewer' else sample
            command(['python3', f'plugins/Plugin.{native_plugin}/eng/build-apple.py'], 'native-plugin')
        common = [f'-p:Version={VERSION}', f'-p:NativeChannelsPackageVersion={VERSION}',
                  '-p:NativeChannelsAppleTarget=ios', f'-p:RestorePackagesPath={cache}']
        packages = ['NativeChannels.Core', 'NativeChannels.Native.Apple', 'NativeChannels', 'NativeChannels.Uno']
        if sample != 'Interop':
            packages.append('Plugin.Uno.PdfViewer' if sample == 'PdfViewer' else f'Plugin.{sample}')
        for name in packages:
            project = ROOT / (f'plugins/{name}/src/{name}' if name.startswith('Plugin.') else f'src/{name}')
            settings = [f'-p:RestoreConfigFile={bootstrap if name == "NativeChannels.Core" else config}']
            if name in ('NativeChannels', 'NativeChannels.Uno'):
                settings.append('-p:NativeChannelsUseCorePackage=true')
            command([dotnet, 'pack', project, '-c', 'Release', '-o', feed, *common, *settings], 'pack-' + name)
        report['packages'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in feed.glob('*.nupkg')}
        project = ROOT / f'samples/Uno/{sample}/{sample}.csproj'
        command([dotnet, 'build', project, '-c', 'Debug', '-p:TargetFrameworks=net10.0-ios',
                 '-p:RuntimeIdentifier=iossimulator-arm64',
                 f'-p:RestorePackagesPath={cache}', f'-p:RestoreConfigFile={config}'],
                'sample-build', ROOT / 'samples/Uno')
        assets_path = project.parent / 'obj/project.assets.json'
        assets = json.loads(assets_path.read_text())
        verify_project_references(assets)
        shutil.copy2(assets_path, folder / 'sample-assets.json')
        for identity, library in assets['libraries'].items():
            name, version = identity.split('/')
            if not name.lower().startswith(('nativechannels', 'plugin.')):
                continue
            archive = feed / f'{name}.{version}.nupkg'
            restored = cache / library['path']
            metadata = json.loads((restored / '.nupkg.metadata').read_text())
            if Path(metadata['source']).resolve() != feed.resolve() or hashlib.sha256(archive.read_bytes()).digest() != hashlib.sha256((restored / f'{name.lower()}.{version}.nupkg').read_bytes()).digest():
                raise RuntimeError(f'Consumer package does not match this run: {identity}')
        report['build'] = 'passed'
        assembly = 'NativeChannels.Interop.Uno' if sample == 'Interop' else sample
        app = project.parent / f'bin/Debug/net10.0-ios/iossimulator-arm64/{assembly}.app'
        bundle = plistlib.loads((app / 'Info.plist').read_bytes())['CFBundleIdentifier']
        command(['xcrun', 'simctl', 'bootstatus', args.device, '-b', '-d'], 'simulator-boot')
        command(['xcrun', 'simctl', 'install', args.device, app], 'simulator-install')
        container = Path(subprocess.check_output(['xcrun', 'simctl', 'get_app_container', args.device, bundle, 'data'], text=True).strip())
        result = container / 'Documents' / ('uno-' + sample + '-' + folder.name + '.json')
        launch_env = dict(env, SIMCTL_CHILD_NATIVECHANNELS_UNO_SAMPLE_RESULT=str(result))
        try:
            command(['xcrun', 'simctl', 'launch', '--terminate-running-process',
                     '--stdout=' + str(folder / 'stdout.log'), '--stderr=' + str(folder / 'stderr.log'),
                     args.device, bundle], 'simulator-launch', extra_env=launch_env)
            deadline = time.monotonic() + 60
            while not result.exists() and time.monotonic() < deadline:
                time.sleep(0.25)
            if not result.exists():
                raise TimeoutError(f'{sample} produced no result within 60 seconds; inspect simulator logs and crash reports.')
            payload = json.loads(result.read_text())
            report['result'] = payload
            shutil.copy2(result, folder / 'sample-result.json')
            if payload.get('checks') != 'passed' or payload.get('sample') != sample:
                raise RuntimeError(f'{sample} runtime verification failed')
            if sample == 'Interop':
                logs = '\n'.join((folder / name).read_text(errors='replace') for name in ('stdout.log', 'stderr.log'))
                if not all(marker in logs for marker in ('typed calls, DTOs', 'view stress: 32 reconnects')):
                    raise RuntimeError('Interop did not complete both shared native verifiers')
            if sample == 'PdfViewer':
                logs = '\n'.join((folder / name).read_text(errors='replace') for name in ('stdout.log', 'stderr.log'))
                if 'document, navigation, zoom, native callback, isolation, errors and detach/reconnect' not in logs:
                    raise RuntimeError('PdfViewer did not complete the shared PDF verifier')
            report['runtime'] = 'passed'
        finally:
            subprocess.run(['xcrun', 'simctl', 'terminate', args.device, bundle], capture_output=True, timeout=15)
        return 0
    except Exception as error:
        report['runtime' if report['build'] == 'passed' else 'build'] = 'failed'
        report['error'] = str(error)
        print(str(error), flush=True)
        return 1
    finally:
        (folder / 'summary.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
