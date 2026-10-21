#!/usr/bin/env python3
"""Validate package-only Uno samples on a Linux x64 X11 session."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET

from consumer_projects import verify_project_references

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.3.0-preview.6'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', required=True, type=Path)
    parser.add_argument('--display-kind', choices=('desktop', 'xvfb'), default='desktop')
    parser.add_argument('--pdf-backend', choices=('Qt', 'Gtk'), default='Qt')
    parser.add_argument('--sample', choices=('Interop', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer'), default='Currency')
    parser.add_argument('--reuse-external-cache', type=Path)
    args = parser.parse_args()
    if platform.system() != 'Linux' or platform.machine() != 'x86_64' or not os.environ.get('DISPLAY'):
        parser.error('Run on Linux x64 with an X11 display (desktop or xvfb-run).')
    dotnet = args.dotnet.resolve()
    sample = args.sample
    folder = ROOT / 'artifacts/uno-linux' / uuid.uuid4().hex
    feed, cache = folder / 'packages', folder / 'cache'
    feed.mkdir(parents=True)
    cache.mkdir()
    report = {'sample': sample, 'platform': 'linux', 'rid': 'linux-x64',
              'backend': 'X11', 'displayKind': args.display_kind, 'pdfBackend': args.pdf_backend if sample == 'PdfViewer' else None, 'build': 'pending', 'runtime': 'pending',
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
                if package.is_dir() and any(package.glob('*/*.nupkg.metadata')) and not package.name.lower().startswith(('nativechannels', 'plugin.')):
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
        if sample == 'Interop':
            command(['bash', 'eng/build-linux-native.sh', 'sample'], 'native-sample')
        common = [f'-p:Version={VERSION}', f'-p:NativeChannelsPackageVersion={VERSION}',
                  '-p:NativeChannelsLinuxOnly=true', '-p:RestoreNoHttpCache=true', f'-p:RestorePackagesPath={cache}']
        packages = ['NativeChannels.Core', 'NativeChannels', 'NativeChannels.Uno']
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
        command([dotnet, 'build', project, '-c', 'Debug', '-p:TargetFrameworks=net10.0-desktop', '-p:NativeChannelsLinuxOnly=true', f'-p:UnoPdfBackend={args.pdf_backend}',
                 '-p:RuntimeIdentifier=linux-x64', '-p:RestoreNoHttpCache=true', '-p:UnoDisableHotDesign=true',
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
        app = project.parent / f'bin/Debug/net10.0-desktop/linux-x64/{assembly}.dll'
        result = folder / 'sample-result.json'
        launch_env = dict(env, NATIVECHANNELS_UNO_SAMPLE_RESULT=str(result))
        command([dotnet, app], 'runtime', extra_env=launch_env, timeout=75)
        payload = json.loads(result.read_text())
        report['result'] = payload
        if payload.get('checks') != 'passed' or payload.get('sample') != sample:
            raise RuntimeError(f'{sample} runtime verification failed')
        logs = (folder / 'runtime.log').read_text(errors='replace')
        if sample == 'Interop' and not all(marker in logs for marker in ('typed calls, DTOs', 'view stress: 32 reconnects')):
            raise RuntimeError('Interop did not complete both shared native verifiers')
        if sample == 'PdfViewer' and 'document, navigation, zoom, native callback, isolation, errors and detach/reconnect' not in logs:
            raise RuntimeError('PdfViewer did not complete the shared PDF verifier')
        report['runtime'] = 'passed'
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
