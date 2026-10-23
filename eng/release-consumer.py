"""Run the existing Interop sample against immutable combined framework archives."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile

from uno_candidate import validate_candidate


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_public_assets(assets_path, feed):
    """NuGet adds a repository signature; all other accepted payload bytes must match."""
    from consumer_projects import verify_project_references
    assets = json.loads(assets_path.read_text(encoding='utf-8-sig'))
    verify_project_references(assets, allow_avalonia_app=True)
    count = 0
    def payload(path):
        with zipfile.ZipFile(path) as archive:
            if len(archive.namelist()) != len(set(archive.namelist())):
                raise ValueError('Duplicate package entries')
            return {e.filename: hashlib.sha256(archive.read(e)).hexdigest() for e in archive.infolist()
                    if not e.is_dir() and e.filename != '.signature.p7s'}
    for identity, library in assets['libraries'].items():
        if not identity.lower().startswith('nativechannels'):
            continue
        name, version = identity.split('/')
        folder = next(Path(p) / library['path'] for p in assets['packageFolders']
                      if (Path(p) / library['path']).is_dir())
        if json.loads((folder / '.nupkg.metadata').read_text())['source'] != 'https://api.nuget.org/v3/index.json':
            raise ValueError('Expected a fresh public NuGet restore')
        if payload(folder / f'{name.lower()}.{version}.nupkg') != payload(feed / f'{name}.{version}.nupkg'):
            raise ValueError('Public package payload differs from accepted archive')
        count += 1
    if not count:
        raise ValueError('No framework packages restored')
    return f'PASS {count} public NuGet package payloads match the accepted archives'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--framework', choices=('maui', 'avalonia', 'uno'), required=True)
    parser.add_argument('--platform', choices=('windows', 'linux', 'macos', 'ios', 'maccatalyst', 'android', 'browser'), required=True)
    parser.add_argument('--device')
    parser.add_argument('--adb', default='adb')
    parser.add_argument('--reuse-cache', type=Path)
    parser.add_argument('--public-feed', action='store_true', help='Post-publication fresh NuGet.org consumer')
    args = parser.parse_args()
    root, feed, output, dotnet = [p.resolve() for p in (args.root, args.packages, args.output, args.dotnet)]
    if output.exists():
        raise ValueError('Use a new evidence directory')
    output.mkdir(parents=True)
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    def clean():
        return not subprocess.check_output(['git', 'status', '--porcelain'], cwd=root, text=True).strip()
    if not clean():
        raise ValueError('Consumer source must be clean')
    records = validate_candidate(feed, source)
    versions = {r['version'] for r in records}
    if len(versions) != 1:
        raise ValueError('Mixed candidate versions')
    version = versions.pop()
    package_hashes = {r['file']: digest(feed / r['file']) for r in records}
    spec = importlib.util.spec_from_file_location('samples', Path(__file__).with_name('test-samples.py'))
    samples = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(samples)
    samples.ROOT = root
    report = dict(framework=args.framework, platform=args.platform, sourceCommit=source,
                  packages=package_hashes, scenario='Interop', success=False,
                  packageSource='nuget.org' if args.public_feed else 'candidate')
    cache = output / 'cache'
    cache.mkdir()
    if args.reuse_cache:
        for package in args.reuse_cache.resolve().iterdir():
            if package.is_dir() and not package.name.lower().startswith(('nativechannels', 'plugin.')):
                def copy(src, dst):
                    try:
                        os.link(src, dst)
                    except OSError:
                        shutil.copy2(src, dst)
                    return dst
                shutil.copytree(package, cache / package.name, copy_function=copy)
    config = output / 'NuGet.Config'
    xml = ET.Element('configuration')
    sources = ET.SubElement(xml, 'packageSources')
    ET.SubElement(sources, 'clear')
    if not args.public_feed:
        ET.SubElement(sources, 'add', key='candidate', value=str(feed))
    ET.SubElement(sources, 'add', key='nuget', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(xml, 'packageSourceMapping')
    ET.SubElement(mapping, 'clear')
    if not args.public_feed:
        ET.SubElement(ET.SubElement(mapping, 'packageSource', key='candidate'), 'package', pattern='NativeChannels*')
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget'), 'package', pattern='*')
    ET.ElementTree(xml).write(config, encoding='utf-8')
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), PATH=str(dotnet.parent) + os.pathsep + os.environ['PATH'])
    def run(argv, log, timeout=2100, environment=None):
        with (output / log).open('w') as stream:
            subprocess.run(list(map(str, argv)), cwd=root, env=environment or env,
                           stdout=stream, stderr=subprocess.STDOUT, check=True, timeout=timeout)
    try:
        uno = args.framework == 'uno'
        project = root / 'samples/Uno/Interop/Interop.csproj' if uno else samples.project('Interop', args.platform, framework=args.framework)
        common = [f'-p:RestorePackagesPath={cache}', f'-p:RestoreConfigFile={config}',
                  f'-p:NativeChannelsPackageVersion={version}', '-p:EnableWindowsTargeting=true']
        if args.platform == 'android':
            for variable, prop in [('JAVA_HOME', 'JavaSdkDirectory'), ('ANDROID_HOME', 'AndroidSdkDirectory')]:
                if os.environ.get(variable):
                    common += [f'-p:{prop}={os.environ[variable]}']
        tfm = {'windows': 'net10.0-windows10.0.19041.0', 'linux': 'net10.0',
               'macos': 'net10.0-macos', 'ios': 'net10.0-ios', 'maccatalyst': 'net10.0-maccatalyst',
               'android': 'net10.0-android', 'browser': 'net10.0-browser'}[args.platform]
        if uno:
            tfm = 'net10.0-browserwasm' if args.platform == 'browser' else tfm if args.platform in ('ios', 'android') else 'net10.0-desktop'
            # Preserve all existing Uno dependencies, overriding only released framework versions.
            props = ET.parse(root / 'samples/Uno/Directory.Packages.props')
            for item in props.findall('.//PackageVersion'):
                if item.get('Include') in {r['id'] for r in records}:
                    item.set('Version', version)
            central = output / 'Directory.Packages.props'
            props.write(central, encoding='utf-8')
            common += [f'-p:DirectoryPackagesPropsPath={central}', '-p:UnoDisableHotDesign=true', '-p:WasmEnableHotReload=false']
            if args.platform == 'android':
                bundle = ET.parse(project).findtext('.//ApplicationId')
                verification_env = output / 'environment.txt'
                verification_env.write_text(f'NATIVECHANNELS_UNO_SAMPLE_RESULT=/data/data/{bundle}/files/uno-result.json\n')
                common += [f'-p:NativeChannelsVerificationEnvironment={verification_env}', '-p:EmbedAssembliesIntoApk=true']
        if args.platform == 'linux': common += ['-p:NativeChannelsLinuxOnly=true']
        if args.platform == 'macos' and uno: common += ['-p:NativeChannelsMacOSOnly=true', '-p:NativeChannelsDesktopOnly=true']
        if args.platform == 'browser': common += ['-p:NativeChannelsBrowserOnly=true']
        if uno or args.framework == 'maui':
            common += [f'-p:TargetFrameworks={tfm}', '-f', tfm]
        arch = 'arm64' if args.platform in ('ios', 'macos', 'maccatalyst') else 'x64'
        rid = {'windows': 'win-', 'linux': 'linux-', 'macos': 'osx-', 'ios': 'iossimulator-', 'maccatalyst': 'maccatalyst-', 'android': 'android-'}.get(args.platform)
        if rid: common += ['-r', rid + arch]
        app = output / 'app'
        verb = 'publish' if args.platform in ('windows', 'linux', 'browser') and not uno else 'build'
        configuration = 'Debug' if uno and args.platform in ('ios', 'android', 'browser') else 'Release'
        run([dotnet, verb, project, '-c', configuration, '-o', app, *common], 'build.log')
        assets = project.parent / 'obj/project.assets.json'
        if args.public_feed:
            (output / 'package-origin.log').write_text(verify_public_assets(assets, feed) + '\n')
        else:
            run([sys.executable, root / 'eng/verify-consumer-assets.py', assets, feed], 'package-origin.log')
        shutil.copy2(assets, output / 'project.assets.json')
        report['assetsSha256'] = digest(assets)
        runtime = output / 'runtime'
        runtime.mkdir()
        entry = dict(name='Interop', sample='Interop', directory=str(app), framework=args.framework)
        if args.framework == 'maui': entry['executable'] = ET.parse(project).findtext('.//AssemblyName') or project.stem
        os.environ['DOTNET_ROOT'] = str(dotnet.parent)
        if not uno:
            if args.platform == 'browser':
                result = samples.browser_test(entry, runtime, 180)
            elif args.platform in ('ios', 'android'):
                entry['bundleId'] = ET.parse(project).findtext('.//ApplicationId')
                args.timeout = 180
                result = samples.mobile(entry, args, runtime)
            else:
                result = samples.desktop(entry, args.platform, runtime, 180)
        elif args.platform == 'browser':
            run(['node', root / 'eng/test-uno-browser.mjs', app / 'wwwroot', 'Interop', runtime], 'runtime.log', 180)
            result = (runtime / 'result.txt').read_text()
        else:
            result_path = runtime / 'sample-result.json'
            stop = None
            try:
                if args.platform == 'ios':
                    apps = list(app.rglob('*.app'))
                    if len(apps) != 1: raise ValueError('Expected one iOS app')
                    bundle = plistlib.loads((apps[0] / 'Info.plist').read_bytes())['CFBundleIdentifier']
                    run(['xcrun', 'simctl', 'install', args.device, apps[0]], 'install.log')
                    container = Path(subprocess.check_output(['xcrun', 'simctl', 'get_app_container', args.device, bundle, 'data'], text=True).strip())
                    device_result = container / 'Documents' / ('release-interop-' + output.name + '.json')
                    device_result.unlink(missing_ok=True)
                    launch_env = dict(env, SIMCTL_CHILD_NATIVECHANNELS_UNO_SAMPLE_RESULT=str(device_result))
                    stop = ['xcrun', 'simctl', 'terminate', args.device, bundle]
                    run(['xcrun', 'simctl', 'launch', '--terminate-running-process',
                         '--stdout=' + str(output / 'runtime.log'), '--stderr=' + str(output / 'runtime-stderr.log'),
                         args.device, bundle], 'launch.log', environment=launch_env)
                    deadline = time.monotonic() + 180
                    while not device_result.exists() and time.monotonic() < deadline: time.sleep(.25)
                    shutil.copy2(device_result, result_path)
                elif args.platform == 'android':
                    apks = list(app.rglob('*-Signed.apk'))
                    if len(apks) != 1: raise ValueError('Expected one signed APK')
                    bundle = ET.parse(project).findtext('.//ApplicationId')
                    adb = [args.adb, '-s', args.device]
                    run([*adb, 'install', '-r', apks[0]], 'install.log')
                    stop = [*adb, 'shell', 'am', 'force-stop', bundle]
                    subprocess.run(stop, check=True)
                    subprocess.run([*adb, 'shell', 'run-as', bundle, 'rm', '-f', 'files/uno-result.json'], check=True)
                    activity = subprocess.check_output([*adb, 'shell', 'cmd', 'package', 'resolve-activity', '--brief', bundle], text=True).strip().splitlines()[-1]
                    run([*adb, 'shell', 'am', 'start', '-W', '-n', activity], 'launch.log')
                    deadline = time.monotonic() + 180
                    while time.monotonic() < deadline:
                        probe = subprocess.run([*adb, 'shell', 'run-as', bundle, 'cat', 'files/uno-result.json'], capture_output=True, text=True)
                        if probe.returncode == 0:
                            try:
                                json.loads(probe.stdout)
                                result_path.write_text(probe.stdout)
                                break
                            except json.JSONDecodeError: pass
                        time.sleep(.5)
                    pid = subprocess.check_output([*adb, 'shell', 'pidof', bundle], text=True).strip().split()[0]
                    run([*adb, 'logcat', '-d', '-v', 'brief', '--pid=' + pid], 'runtime.log')
                else:
                    assembly = 'Interop' if args.platform == 'windows' else 'NativeChannels.Interop.Uno'
                    launch_env = dict(env, NATIVECHANNELS_UNO_SAMPLE_RESULT=str(result_path))
                    run([dotnet, app / (assembly + '.dll')], 'runtime.log', 180, launch_env)
                payload = json.loads(result_path.read_text())
                logs = (output / 'runtime.log').read_text(errors='replace')
                if (output / 'runtime-stderr.log').exists():
                    logs += '\n' + (output / 'runtime-stderr.log').read_text(errors='replace')
                if payload.get('checks') != 'passed' or payload.get('sample') != 'Interop':
                    raise ValueError('Uno Interop failed')
                if not all(marker in logs for marker in ('typed calls, DTOs', 'view stress: 32 reconnects')):
                    raise ValueError('Both shared native verifiers must complete')
                report['unoResult'] = payload
                result = 'PASS ' + logs
            finally:
                if stop: subprocess.run(stop, capture_output=True, timeout=20)
        if not str(result).startswith('PASS '):
            raise ValueError('Interop runtime did not pass')
        if args.platform == 'browser':
            result += '\n' + (runtime / 'stdout.log').read_text(errors='replace')
        if not all(marker in result for marker in ('typed calls, DTOs', 'view stress: 32 reconnects')):
            raise ValueError('Both shared Interop verifiers must pass')
        if not clean() or package_hashes != {r['file']: digest(feed / r['file']) for r in records}:
            raise ValueError('Source or packages changed during execution')
        report.update(success=True, result=result)
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        (output / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
    print('PASS', args.framework, args.platform, output, flush=True)


if __name__ == '__main__':
    main()
