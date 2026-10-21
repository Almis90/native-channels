#!/usr/bin/env python3
"""Validate package-only Uno samples on an Android device or emulator."""
import argparse
import hashlib
import json
import os
from pathlib import Path
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
    parser.add_argument('--android-sdk', required=True, type=Path)
    parser.add_argument('--java-sdk', required=True, type=Path)
    parser.add_argument('--sample', choices=('Interop', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer'), default='Currency')
    parser.add_argument('--reuse-external-cache', type=Path)
    args = parser.parse_args()
    dotnet = args.dotnet.resolve()
    sdk, java = args.android_sdk.resolve(), args.java_sdk.resolve()
    adb = sdk / 'platform-tools' / ('adb.exe' if os.name == 'nt' else 'adb')
    def query(*argv):
        return subprocess.check_output([str(adb), '-s', args.device, *argv], text=True, timeout=30).strip()
    abi = query('shell', 'getprop', 'ro.product.cpu.abi')
    rid = {'x86_64': 'android-x64', 'arm64-v8a': 'android-arm64'}[abi]
    sample = args.sample
    folder = ROOT / 'artifacts/uno-android' / uuid.uuid4().hex
    feed, cache = folder / 'packages', folder / 'cache'
    feed.mkdir(parents=True)
    cache.mkdir()
    report = {'sample': sample, 'platform': 'android', 'rid': rid, 'api': query('shell', 'getprop', 'ro.build.version.sdk'),
              'target': 'emulator' if query('shell', 'getprop', 'ro.kernel.qemu') == '1' else 'device',
              'build': 'pending', 'runtime': 'pending',
              'commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'sourceStatus': subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True)}
    print(f'Evidence: {folder}', flush=True)
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), JAVA_HOME=str(java), ANDROID_HOME=str(sdk), PATH=str(dotnet.parent) + os.pathsep + os.environ['PATH'])

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
        gradle = ROOT / 'native/android' / ('gradlew.bat' if os.name == 'nt' else 'gradlew')
        command([gradle, '--no-daemon', ':library:assembleRelease', ':sample:assembleRelease',
                 ':currency:assembleRelease', ':audio:assembleRelease', ':volume:assembleRelease', ':pdf:assembleRelease'],
                'native-build', ROOT / 'native/android')
        common = [f'-p:Version={VERSION}', f'-p:NativeChannelsPackageVersion={VERSION}',
                  '-p:NativeChannelsAndroidOnly=true', f'-p:RestorePackagesPath={cache}', f'-p:AndroidSdkDirectory={sdk}', f'-p:JavaSdkDirectory={java}']
        packages = ['NativeChannels.Core', 'NativeChannels.Native.Android', 'NativeChannels', 'NativeChannels.Uno']
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
        bundle = 'dev.nativechannels.uno.' + sample.lower()
        environment = folder / 'environment.txt'
        environment.write_text(f'NATIVECHANNELS_UNO_SAMPLE_RESULT=/data/data/{bundle}/files/uno-result.json\n')
        command([dotnet, 'build', project, '-c', 'Debug', '-p:TargetFrameworks=net10.0-android',
                 f'-p:RuntimeIdentifier={rid}', '-p:EmbedAssembliesIntoApk=true',
                 f'-p:NativeChannelsVerificationEnvironment={environment}',
                 f'-p:AndroidSdkDirectory={sdk}', f'-p:JavaSdkDirectory={java}',
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
        apk = project.parent / f'bin/Debug/net10.0-android/{rid}/{bundle}-Signed.apk'
        report['apkSha256'] = hashlib.sha256(apk.read_bytes()).hexdigest()
        command([adb, '-s', args.device, 'install', '-r', apk], 'device-install')
        query('shell', 'am', 'force-stop', bundle)
        query('shell', 'run-as', bundle, 'rm', '-f', 'files/uno-result.json')
        activity = query('shell', 'cmd', 'package', 'resolve-activity', '--brief', bundle).splitlines()[-1]
        try:
            command([adb, '-s', args.device, 'shell', 'am', 'start', '-W', '-n', activity], 'device-launch')
            pid = query('shell', 'pidof', bundle).split()[0]
            report['processId'] = pid
            deadline = time.monotonic() + 60
            payload = None
            while time.monotonic() < deadline:
                probe = subprocess.run([str(adb), '-s', args.device, 'shell', 'run-as', bundle, 'cat', 'files/uno-result.json'], capture_output=True, text=True, timeout=15)
                if probe.returncode == 0 and probe.stdout.strip():
                    try:
                        payload = json.loads(probe.stdout)
                        break
                    except json.JSONDecodeError:
                        pass  # The app may still be writing the result file.
                time.sleep(.5)
            logs = query('logcat', '-d', '-v', 'brief', '--pid=' + pid)
            (folder / 'runtime.log').write_text(logs, encoding='utf-8')
            if payload is None:
                raise TimeoutError(f'{sample} produced no result within 60 seconds')
            report['result'] = payload
            (folder / 'sample-result.json').write_text(json.dumps(payload, indent=2))
            if payload.get('checks') != 'passed' or payload.get('sample') != sample:
                raise RuntimeError(f'{sample} runtime verification failed')
            if sample == 'Interop' and not all(marker in logs for marker in ('typed calls, DTOs', 'view stress: 32 reconnects')):
                raise RuntimeError('Interop did not complete both shared native verifiers')
            if sample == 'PdfViewer' and 'document, navigation, zoom, native callback, isolation, errors and detach/reconnect' not in logs:
                raise RuntimeError('PdfViewer did not complete the shared PDF verifier')
            report['runtime'] = 'passed'
        finally:
            subprocess.run([str(adb), '-s', args.device, 'shell', 'am', 'force-stop', bundle], capture_output=True, timeout=15)
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
