"""Package-only Uno desktop build/smoke runner with explicit acceptance gaps."""
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

from consumer_projects import verify_project_references
from package_audit import audit_package
from uno_candidate import validate_candidate

ROOT = Path(__file__).resolve().parents[1]
SAMPLES = ('Interop', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer')
PLATFORMS = ('windows', 'linux', 'macos', 'android', 'ios', 'browser')
VERSION = '0.3.0-preview.6'
PACKAGES = ('NativeChannels.Core', 'NativeChannels', 'NativeChannels.Uno',
            'Plugin.Currency', 'Plugin.AudioStreamer', 'Plugin.VolumeMonitor', 'Plugin.Uno.PdfViewer')
GAPS = {
    'windows': 'Native view and Windows PDF painting checks pass; physical input and broader visual/lifecycle review remain unverified.',
    'linux': 'Native plugin assets and Uno runtime/native views/PDF are not verified.',
    'macos': 'Native plugin runtime is not verified; pinned Uno lacks public arbitrary NSView embedding.',
    'android': 'Uno mobile package integration, native views and device execution are pending.',
    'ios': 'Uno mobile package integration, native views and simulator/device execution are pending.',
    'browser': 'Uno browser transport integration, native views and browser execution are pending.',
}


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def audit_feed(feed, commit):
    records = [audit_package(path, commit) for path in sorted(feed.glob('*.nupkg'))]
    if {record['id'] for record in records} != set(PACKAGES) or len(records) != len(PACKAGES):
        raise ValueError('Uno desktop feed must contain exactly the seven expected packages')
    for record in records:
        if record['version'] != VERSION or any(not name.startswith('lib/net10.0/') for name in record['assemblies']):
            raise ValueError(f"Unexpected Uno desktop package version/framework: {record['id']}")
        with zipfile.ZipFile(feed / record['file']) as archive:
            spec = ET.fromstring(archive.read(record['id'] + '.nuspec'))
            dependencies = {node.get('id') for node in spec.iter() if node.tag.rsplit('}', 1)[-1] == 'dependency'}
            if any('.Maui' in name or '.Avalonia' in name for name in dependencies):
                raise ValueError('Uno packages must not depend on MAUI/Avalonia hosts')
            if record['id'] in ('Plugin.Currency', 'Plugin.AudioStreamer', 'Plugin.VolumeMonitor'):
                if any('Uno' in name for name in dependencies):
                    raise ValueError('Shared messaging plugins must remain independent of Uno')
            if record['id'] == 'Plugin.Uno.PdfViewer':
                required = {'buildTransitive/Plugin.Uno.PdfViewer.targets',
                            'buildTransitive/windows/Microsoft.Windows.SDK.NET.dll',
                            'buildTransitive/windows/WinRT.Runtime.dll'}
                if not required <= set(archive.namelist()):
                    raise ValueError('Uno PDF package is missing its Windows projection/runtime assets')
    return records


def audit_pdf_runtime(feed, output):
    """Verify raw private references really came from the audited PDF archive."""
    hashes = {}
    with zipfile.ZipFile(feed / f'Plugin.Uno.PdfViewer.{VERSION}.nupkg') as archive:
        for name in ('Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll'):
            expected = hashlib.sha256(archive.read('buildTransitive/windows/' + name)).hexdigest()
            if not (output / name).is_file() or sha256(output / name) != expected:
                raise ValueError(f'Windows PDF runtime asset differs from its package: {name}')
            hashes[name] = expected
    return hashes


def audit_consumer(assets, feed, sample):
    verify_project_references(assets)
    expected = {'NativeChannels.Core', 'NativeChannels', 'NativeChannels.Uno'}
    if sample != 'Interop':
        expected.add('Plugin.' + ('Uno.PdfViewer' if sample == 'PdfViewer' else sample))
    found = set()
    for identity, library in assets['libraries'].items():
        name, version = identity.split('/')
        if not name.lower().startswith(('nativechannels', 'plugin.')):
            if name.lower().startswith(('microsoft.maui', 'avalonia')):
                raise ValueError(f'Unexpected framework dependency: {name}')
            continue
        found.add(name)
        local = feed / f'{name}.{version}.nupkg'
        cached = next((Path(folder) / library['path'] for folder in assets['packageFolders']
                       if (Path(folder) / library['path']).is_dir()), None)
        if cached is None or not local.is_file():
            raise ValueError(f'Missing local package: {identity}')
        metadata = json.loads((cached / '.nupkg.metadata').read_text(encoding='utf-8-sig'))
        if Path(metadata['source']).resolve() != feed.resolve():
            raise ValueError(f'Package did not come from this run: {identity}')
        if sha256(cached / f'{name.lower()}.{version}.nupkg') != sha256(local):
            raise ValueError(f'Cached package differs from this feed: {identity}')
    if found != expected:
        raise ValueError(f'{sample} dependencies: expected {sorted(expected)}, found {sorted(found)}')
    return sorted(found)


def validate_result(result, sample):
    if result.get('sample') != sample or result.get('checks') != 'passed':
        raise ValueError(f'{sample}: invalid or failed smoke result: {result}')
    if not isinstance(result.get('limitation'), str) or not result['limitation'].strip():
        raise ValueError('A protocol smoke result must disclose its acceptance limitations')


def run(args, source_identity):
    folder = ROOT / 'artifacts/local-samples/uno' / args.platform / (time.strftime('%Y%m%d-%H%M%S-') + uuid.uuid4().hex[:8])
    folder.mkdir(parents=True)
    print(f'Evidence: {folder}', flush=True)
    summary = dict(schemaVersion=1, framework='uno', platform=args.platform, host=platform.platform(),
                   source=source_identity(), acceptance='blocked', acceptanceGaps=GAPS,
                   mode='build' if args.build_only else 'smoke', results=[])
    selected = args.sample or SAMPLES
    summary['results'] = [dict(sample=sample, build='not-run', smoke='not-run', acceptance='blocked') for sample in selected]
    try:
        if args.manifest or args.packages_only or args.device or args.virtual_audio or args.arch:
            raise ValueError('Uno does not yet support manifest reuse, packages-only, devices, virtual audio or architecture overrides')
        if args.require_clean and summary['source']['status']:
            raise ValueError('A clean source tree is required')
        host = {'Windows': 'windows', 'Linux': 'linux', 'Darwin': 'macos'}.get(platform.system())
        if args.platform not in ('windows', 'linux', 'macos'):
            summary['blockedReason'] = GAPS[args.platform]
            return 2
        if args.platform != host:
            raise ValueError('Uno desktop builds must run on the requested operating system')
        if not args.build_only and args.platform != 'windows':
            summary['blockedReason'] = GAPS[args.platform] + ' Use --build-only for compilation evidence.'
            return 2
        dotnet = Path(args.dotnet).resolve() if args.dotnet else ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
        if not dotnet.is_file():
            raise ValueError('Provide --dotnet pointing to SDK 10.0.401')

        def command(arguments, log, cwd=ROOT, env=None, timeout=900):
            with (folder / log).open('w', encoding='utf-8') as stream:
                subprocess.run([str(arg) for arg in arguments], cwd=cwd, env=env, stdout=stream,
                               stderr=subprocess.STDOUT, check=True, timeout=timeout)

        command([dotnet, '--info'], 'dotnet.log', ROOT / 'samples/Uno')
        feed, cache = folder / 'packages', folder / 'cache'
        if args.framework_packages:
            feed = Path(args.framework_packages).resolve()
            summary['candidate'] = str(feed)
            validate_candidate(feed, summary['source']['commit'])
        else:
            feed.mkdir()
        cache.mkdir()
        if args.reuse_external_cache:
            def link_or_copy(source, destination):
                try:
                    os.link(source, destination)
                except OSError:
                    shutil.copy2(source, destination)
                return destination
            for package in args.reuse_external_cache.resolve().iterdir():
                if package.is_dir() and not package.name.lower().startswith(('nativechannels', 'plugin.')):
                    shutil.copytree(package, cache / package.name, copy_function=link_or_copy)
        summary['cache'] = dict(localPackages='fresh', externalReuse=str(args.reuse_external_cache or ''))
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
        bootstrap.write_text('<configuration><packageSources><clear/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><clear/></packageSourceMapping></configuration>', encoding='utf-8')
        common = [f'-p:RestorePackagesPath={cache}', f'-p:Version={VERSION}',
                  f'-p:NativeChannelsPackageVersion={VERSION}', '-p:NativeChannelsDesktopOnly=true']
        for name in (() if args.framework_packages else PACKAGES):
            path = ROOT / ('plugins/' + name + '/src/' + name if name.startswith('Plugin.') else 'src/' + name)
            settings = [*common, f'-p:RestoreConfigFile={bootstrap if name == "NativeChannels.Core" else config}']
            if name != 'NativeChannels.Core':
                settings.append('-p:NativeChannelsUseCorePackage=true')
            command([dotnet, 'pack', path, '-c', 'Release', '-o', feed, *settings], f'pack-{name}.log')
        summary['packages'] = (validate_candidate(feed, summary['source']['commit']) if args.framework_packages
                               else audit_feed(feed, summary['source']['commit']))
        write_json(folder / 'packages.json', summary['packages'])
        for record in summary['results']:
            sample = record['sample']
            try:
                project = ROOT / f'samples/Uno/{sample}/{sample}.csproj'
                command([dotnet, 'build', project, '-c', 'Release', '-p:TargetFrameworks=net10.0-desktop',
                         f'-p:RestorePackagesPath={cache}', '--configfile', config], f'{sample}-build.log', ROOT / 'samples/Uno')
                assets_path = project.parent / 'obj/project.assets.json'
                assets = json.loads(assets_path.read_text(encoding='utf-8-sig'))
                record['packages'] = audit_consumer(assets, feed, sample)
                shutil.copy2(assets_path, folder / f'{sample}-assets.json')
                if sample == 'PdfViewer' and args.platform == 'windows':
                    record['windowsProjections'] = audit_pdf_runtime(feed, project.parent / 'bin/Release/net10.0-desktop')
                record['build'] = 'passed'
                if not args.build_only:
                    binary = project.parent / f'bin/Release/net10.0-desktop/{sample}.exe'
                    result_path = folder / f'{sample}.json'
                    environment = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), NATIVECHANNELS_UNO_SAMPLE_RESULT=str(result_path))
                    startup = subprocess.STARTUPINFO()
                    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                    startup.wShowWindow = 0
                    with (folder / f'{sample}-runtime.log').open('w', encoding='utf-8') as log:
                        process = subprocess.Popen([str(binary)], cwd=binary.parent, env=environment,
                                                   stdout=log, stderr=subprocess.STDOUT, startupinfo=startup)
                        try:
                            code = process.wait(timeout=args.timeout)
                            if code != 0:
                                raise RuntimeError(f'{sample} exited with {code}')
                        finally:
                            if process.poll() is None:
                                process.kill()
                                process.wait(timeout=10)
                    result = json.loads(result_path.read_text(encoding='utf-8-sig'))
                    validate_result(result, sample)
                    record.update(smoke='passed', limitation=result['limitation'])
            except Exception as error:
                record['error'] = str(error)
                record['smoke' if record['build'] == 'passed' else 'build'] = 'failed'
            print(f"{sample}: build={record['build']}, smoke={record['smoke']}, acceptance=blocked", flush=True)
            write_json(folder / 'summary.json', summary)
        if source_identity() != summary['source']:
            raise ValueError('Source changed during the run; results cannot be accepted')
        passed = all(record['build'] == 'passed' and (args.build_only or record['smoke'] == 'passed') for record in summary['results'])
        return 0 if passed and not args.require_acceptance else 1
    except Exception as error:
        summary['error'] = str(error)
        print(str(error), flush=True)
        return 1
    finally:
        write_json(folder / 'summary.json', summary)
