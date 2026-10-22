#!/usr/bin/env python3
"""Validate package-only Uno samples in a browser."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET

from consumer_projects import verify_project_references
from uno_candidate import validate_candidate

ROOT = Path(__file__).resolve().parents[1]
VERSION = '0.3.0-preview.6'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', required=True, type=Path)
    parser.add_argument('--sample', choices=('Interop', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer'), default='Currency')
    parser.add_argument('--reuse-external-cache', type=Path)
    parser.add_argument('--framework-packages', type=Path)
    args = parser.parse_args()
    dotnet = args.dotnet.resolve()
    sample = args.sample
    folder = ROOT / 'artifacts/uno-browser' / uuid.uuid4().hex
    feed, cache = folder / 'packages', folder / 'cache'
    folder.mkdir(parents=True)
    if args.framework_packages:
        feed = args.framework_packages.resolve()
        validate_candidate(feed, subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip())
    else:
        feed.mkdir()
    cache.mkdir()
    report = {'sample': sample, 'platform': 'browser', 'build': 'pending', 'runtime': 'pending',
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
            previous = args.reuse_external_cache.resolve().parent / 'used-packages.json'
            used = set(json.loads(previous.read_text())) if previous.exists() else None
            for package in args.reuse_external_cache.resolve().iterdir():
                if used is not None and package.name.lower() not in used:
                    continue
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
        common = [f'-p:Version={VERSION}', f'-p:NativeChannelsPackageVersion={VERSION}',
                  '-p:NativeChannelsBrowserOnly=true', '-p:RestoreNoHttpCache=true', f'-p:RestorePackagesPath={cache}']
        packages = ['NativeChannels.Core', 'NativeChannels', 'NativeChannels.Uno']
        if sample != 'Interop':
            packages.append('Plugin.Uno.PdfViewer' if sample == 'PdfViewer' else f'Plugin.{sample}')
        for name in ([] if args.framework_packages else packages):
            project = ROOT / (f'plugins/{name}/src/{name}' if name.startswith('Plugin.') else f'src/{name}')
            settings = [f'-p:RestoreConfigFile={bootstrap if name == "NativeChannels.Core" else config}']
            if name in ('NativeChannels', 'NativeChannels.Uno'):
                settings.append('-p:NativeChannelsUseCorePackage=true')
            command([dotnet, 'pack', project, '-c', 'Release', '-o', feed, *common, *settings], 'pack-' + name)
        report['packages'] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in feed.glob('*.nupkg')}
        project = ROOT / f'samples/Uno/{sample}/{sample}.csproj'
        command([dotnet, 'build', project, '-c', 'Debug', '-p:TargetFrameworks=net10.0-browserwasm',
                 '-p:NativeChannelsBrowserOnly=true', '-p:RestoreNoHttpCache=true', '-p:UnoDisableHotDesign=true', '-p:WasmEnableHotReload=false',
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
        index = list((project.parent / 'bin/Debug/net10.0-browserwasm').rglob('index.html'))
        if len(index) != 1:
            raise RuntimeError(f'Expected one browser app entry point: {index}')
        command(['node', ROOT / 'eng/test-uno-browser.mjs', index[0].parent, sample, folder], 'browser', timeout=150)
        report['result'] = (folder / 'result.txt').read_text()
        report['runtime'] = 'passed'
        return 0
    except Exception as error:
        report['runtime' if report['build'] == 'passed' else 'build'] = 'failed'
        report['error'] = str(error)
        print(str(error), flush=True)
        return 1
    finally:
        used = set()
        for asset in [ROOT / f'samples/Uno/{sample}/obj/project.assets.json', *ROOT.glob('src/*/obj/project.assets.json'), *ROOT.glob('plugins/*/src/*/obj/project.assets.json')]:
            if asset.exists():
                used.update(name.split('/')[0].lower() for name in json.loads(asset.read_text())['libraries'])
        (folder / 'used-packages.json').write_text(json.dumps(sorted(used)))
        (folder / 'summary.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
