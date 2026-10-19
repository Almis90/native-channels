#!/usr/bin/env python3
"""Build and exercise the five packaged Avalonia or MAUI consumers locally. Never invokes CI."""
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import platform
import plistlib
import shutil
import signal
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET

from preview_candidate import verify_candidate

ROOT = Path(__file__).resolve().parents[1]
SAMPLES = ('Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer', 'Interop')
HEADS = dict(windows='Windows', linux='Linux', macos='macOS', ios='iOS', android='Android', browser='Browser', maccatalyst='MacCatalyst')


def command(args, **kwargs):
    return subprocess.run([str(a) for a in args], cwd=ROOT, check=True, **kwargs)


def capture(args):
    return command(args, capture_output=True, text=True).stdout.strip()


def project(sample, target, backend='', framework='avalonia'):
    if framework == 'maui':
        name = 'ChannelExplorer' if sample == 'Interop' else sample
        return ROOT / 'samples/Maui' / name / (name + ('' if sample == 'Interop' else '.Sample') + '.csproj')
    base = ROOT / 'samples/Avalonia' / sample
    if sample == 'Currency':
        head = HEADS[target] if target in ('android', 'ios', 'browser') else 'Desktop'
        return base / f'Currency.{head}/Currency.{head}.csproj'
    if sample == 'AudioStreamer' and target == 'windows':
        return base / 'AudioStreamer.Windows.csproj'
    head = HEADS[target] + backend
    return base / head / f'{sample}.{head}.csproj'


def source_identity():
    digest = hashlib.sha256()
    names = capture(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z']).split('\0')
    for name in sorted(set(names)):
        path = ROOT / name
        if name and path.is_file():
            digest.update(name.encode('utf-8') + b'\0')
            digest.update(path.read_bytes())
    return dict(commit=capture(['git', 'rev-parse', 'HEAD']),
                status=capture(['git', 'status', '--porcelain']),
                sourceSha256=digest.hexdigest(),
                diffSha256=hashlib.sha256(command(['git', 'diff', 'HEAD'], capture_output=True).stdout).hexdigest())


def file_hash(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def build(args, destination):
    initial_source = source_identity()
    candidate = getattr(args, 'framework_packages', None)
    require_clean = getattr(args, 'require_clean', False) or candidate is not None
    if require_clean and initial_source['status']:
        raise ValueError('Candidate builds require a clean committed working tree')
    target = args.platform
    maui = args.framework == 'maui'
    tfm = 'net10.0-' + ('windows10.0.19041.0' if target == 'windows' else target)
    dotnet = args.dotnet or str(ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet'))
    if not Path(dotnet).is_file():
        dotnet = args.dotnet or 'dotnet'
    command([dotnet, '--version'])  # Honors the repository's pinned SDK.
    feed, cache = destination / 'packages', destination / 'cache'
    # WinUI's XAML compiler still hits MAX_PATH for long NuGet reference paths.
    if maui and target == 'windows':
        cache = ROOT / '.tools/sample-cache' / uuid.uuid4().hex[:8]
    feed.mkdir(parents=True)
    cache.mkdir(parents=True)
    if args.reuse_external_cache:
        for package in args.reuse_external_cache.resolve().iterdir():
            if package.is_dir() and not package.name.lower().startswith(('nativechannels', 'plugin.maui.', 'plugin.avalonia.')):
                def link_or_copy(source, destination):
                    try:
                        os.link(source, destination)
                    except OSError:
                        shutil.copy2(source, destination)
                    return destination
                shutil.copytree(package, cache / package.name, copy_function=link_or_copy)
    config = destination / 'NuGet.Config'
    xml = ET.Element('configuration')
    sources = ET.SubElement(xml, 'packageSources')
    ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local', value=str(feed))
    ET.SubElement(sources, 'add', key='nuget', value='https://api.nuget.org/v3/index.json')
    mapping = ET.SubElement(xml, 'packageSourceMapping')
    local = ET.SubElement(mapping, 'packageSource', key='local')
    for pattern in ('NativeChannels*', 'Plugin.Maui.*', 'Plugin.Avalonia.*'):
        ET.SubElement(local, 'package', pattern=pattern)
    ET.SubElement(ET.SubElement(mapping, 'packageSource', key='nuget'), 'package', pattern='*')
    ET.ElementTree(xml).write(config, encoding='utf-8', xml_declaration=True)
    flags = dict(windows='NativeChannelsWindowsOnly=true', linux='NativeChannelsLinuxOnly=true',
                 browser='NativeChannelsBrowserOnly=true', android='NativeChannelsAndroidOnly=true',
                 macos='NativeChannelsAppleTarget=macos', ios='NativeChannelsAppleTarget=ios',
                 maccatalyst='NativeChannelsAppleTarget=maccatalyst')
    common = [f'-p:RestoreConfigFile={config}', f'-p:RestorePackagesPath={cache}', '-p:EnableWindowsTargeting=true']
    if target == 'android':
        gradle = ROOT / 'native/android' / ('gradlew.bat' if os.name == 'nt' else 'gradlew')
        command([gradle, '-p', ROOT / 'native/android', '--no-daemon',
                 *[f':{name}:assembleRelease' for name in ('library', 'sample', 'currency', 'audio', 'volume', 'pdf')]])
    elif target in ('macos', 'ios', 'maccatalyst'):
        apple = ['--macos-only'] if target == 'macos' else []
        if not candidate:
            command([sys.executable, 'eng/build-apple.py', *apple])
        for name in SAMPLES[:4]:
            command([sys.executable, f'plugins/Plugin.Maui.{name}/eng/build-apple.py', *apple])
    elif target == 'browser':
        command(['npm.cmd' if os.name == 'nt' else 'npm', '--prefix', 'plugins/Plugin.Avalonia.PdfViewer',
                 'ci', '--ignore-scripts', '--no-audit', '--no-fund'])

    def pack(path, *extra):
        command([dotnet, 'pack', path, '-c', 'Release', '-o', feed, '--disable-build-servers', *common, *extra])
    # Core validates against its published API baseline. Only this bootstrap pack
    # may restore NativeChannels from nuget.org; consumers stay mapped to this feed.
    bootstrap = destination / 'Bootstrap.Config'
    bootstrap.write_text('<configuration><packageSources><clear/><add key="nuget" '
                         'value="https://api.nuget.org/v3/index.json"/></packageSources>'
                         '<packageSourceMapping><clear/></packageSourceMapping></configuration>', encoding='utf-8')
    if candidate:
        accepted = verify_candidate(candidate, initial_source['commit'])
        for record in accepted['files']:
            if record['file'].endswith('.nupkg'):
                shutil.copy2(candidate / record['file'], feed / record['file'])
    else:
        pack('src/NativeChannels.Core', f'-p:RestoreConfigFile={bootstrap}')
        if target == 'android':
            pack('src/NativeChannels.Native.Android')
        if target in ('ios', 'macos', 'maccatalyst'):
            pack('src/NativeChannels.Native.Apple', '-p:' + flags[target])
        pack('src/NativeChannels', '-p:' + flags[target], '-p:NativeChannelsUseCorePackage=true')
        if maui:
            pack('src/NativeChannels.Maui', '-p:' + flags[target], '-p:NativeChannelsUseCorePackage=true')
        else:
            pack('src/NativeChannels.Avalonia', '-p:NativeChannelsUseCorePackage=true')
    for name in ('Plugin.Maui.Currency', 'Plugin.Maui.AudioStreamer', 'Plugin.Maui.VolumeMonitor',
                 'Plugin.Maui.PdfViewer' if maui else 'Plugin.Avalonia.PdfViewer'):
        extra = [f'-p:TargetFrameworks={tfm}'] if name == 'Plugin.Maui.PdfViewer' else []
        pack(f'plugins/{name}/src/{name}', '-p:' + flags[target], *extra)
    audit = ['--android-only'] if target == 'android' else ['--platform-only', target]
    archives = sorted(feed.glob('Plugin.*.nupkg' if candidate else '*.nupkg'))
    command([sys.executable, 'eng/package_audit.py', *archives, *audit,
             *(['--source-commit', initial_source['commit']] if require_clean else []),
             '--output', destination / 'audit.json'])
    architecture = args.arch or ('arm64' if platform.machine().lower() in ('arm64', 'aarch64') else 'x64')
    rid = dict(windows='win-', linux='linux-', macos='osx-', ios='iossimulator-', maccatalyst='maccatalyst-').get(target)
    entries = []
    for sample in (() if getattr(args, 'packages_only', False) else SAMPLES):
        for backend in (('Qt', 'Gtk') if target == 'linux' and sample == 'PdfViewer' else ('',)):
            name = sample + backend
            csproj = project(sample, target, backend, args.framework)
            output = destination / 'apps' / name
            extra = [f'-p:CurrencyDesktopTarget={target}'] if sample == 'Currency' and not maui else []
            if maui:
                extra += ['-f', tfm, f'-p:TargetFrameworks={tfm}', '-p:' + flags[target], '-p:NativeChannelsProfileSeconds=0']
            if rid:
                extra += ['-r', rid + architecture]
            if target in ('windows', 'linux'):
                extra += ['--self-contained', 'false' if args.framework_dependent else 'true']
            # Apple publish emits distribution archives in -o, leaving the runnable
            # .app in bin. Build places the app in our isolated evidence directory
            # and also supports iOS simulator RIDs (which cannot be published).
            verb = 'build' if target in ('android', 'macos', 'ios', 'maccatalyst') else 'publish'
            command([dotnet, verb, csproj, '-c', 'Release', '-o', output, '--disable-build-servers', *common, *extra])
            command([sys.executable, 'eng/verify-consumer-assets.py', csproj.parent / 'obj/project.assets.json', feed])
            item = dict(name=name, sample=sample, directory=str(output), framework=args.framework)
            if maui:
                item['executable'] = ET.parse(csproj).findtext('.//AssemblyName') or csproj.stem
            if target == 'android':
                item['bundleId'] = ET.parse(csproj).findtext('.//ApplicationId')
            entries.append(item)
    final_source = source_identity()
    if require_clean and final_source != initial_source:
        raise ValueError('Source changed during candidate build; discard these outputs and rebuild')
    if candidate:
        verify_candidate(candidate, initial_source['commit'])
    manifest = dict(platform=target, framework=args.framework, packageCache=str(cache), source=final_source, apps=entries,
                    packageFiles={p.name: file_hash(p) for p in sorted(feed.glob('*.*pkg'))})
    if candidate:
        manifest['frameworkCandidate'] = accepted
    path = destination / 'manifest.json'
    path.write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return path


def validate_result(sample, text, stdout, code=0, framework='avalonia'):
    if code != 0 or not text.startswith('PASS '):
        raise RuntimeError(f'exit={code}: {text or "No result"}')
    if framework == 'maui':
        if stdout.count('NATIVECHANNELS_MAUI_CLOSED') != 1:
            raise RuntimeError('Expected one completed MAUI cleanup marker')
        return
    if sample in ('Currency', 'AudioStreamer'):
        marker = 'CURRENCY_AVALONIA_CLOSED' if sample == 'Currency' else 'AUDIO_AVALONIA_CLOSED'
        # Cross-platform AudioStreamer uses the shared protocol instead.
        if sample == 'Currency' or 'NATIVECHANNELS_LOCAL_RESULT ' not in stdout:
            if stdout.count(marker) != 1:
                raise RuntimeError(f'Expected one completed cleanup marker: {marker}')


def desktop(entry, target, folder, timeout):
    directory = Path(entry['directory'])
    sample = entry['sample']
    if target in ('macos', 'maccatalyst'):
        apps = list(directory.rglob('*.app'))
        if len(apps) != 1:
            raise RuntimeError(f'Expected one .app in {directory}')
        info = plistlib.loads((apps[0] / 'Contents/Info.plist').read_bytes())
        exe = apps[0] / 'Contents/MacOS' / info['CFBundleExecutable']
    else:
        name = ('Currency.Desktop' if sample == 'Currency' else 'NativeChannels.Interop.Avalonia'
                if sample == 'Interop' else 'AudioStreamer.Windows' if sample == 'AudioStreamer' and target == 'windows'
                else entry['name'] + '.' + ('Linux' if target == 'linux' else 'Windows'))
        if entry['name'] in ('PdfViewerQt', 'PdfViewerGtk'):
            name = 'PdfViewer.Linux' + entry['name'].removeprefix('PdfViewer')
        name = entry.get('executable', name)
        exe = directory / (name + ('.exe' if target == 'windows' else ''))
    result = folder / 'result.txt'
    env = os.environ.copy()
    for variable in ('CURRENCY_AVALONIA_RESULT', 'AUDIO_AVALONIA_RESULT', 'NATIVECHANNELS_SAMPLE_RESULT'):
        env[variable] = str(result)
    startup = None
    if os.name == 'nt':
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
    with (folder / 'stdout.log').open('w', encoding='utf-8') as out, (folder / 'stderr.log').open('w', encoding='utf-8') as err:
        process = subprocess.Popen([str(exe), '--verify'], cwd=exe.parent, env=env, stdout=out, stderr=err, startupinfo=startup)
        try:
            code = process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)
    text = result.read_text(encoding='utf-8-sig') if result.exists() else ''
    # Apple MAUI routes Console.WriteLine through NSLog to stderr. Keep both
    # streams as evidence and require exactly one cleanup marker across them.
    logs = '\n'.join((folder / name).read_text(encoding='utf-8', errors='replace')
                     for name in ('stdout.log', 'stderr.log'))
    validate_result(sample, text, logs, code, entry.get('framework', 'avalonia'))
    return text


def mobile(entry, args, folder):
    target, sample = args.platform, entry['sample']
    directory = Path(entry['directory'])
    prefix = 'CURRENCY_AVALONIA ' if sample == 'Currency' else 'NATIVECHANNELS_LOCAL_RESULT '
    maui = entry.get('framework') == 'maui'
    if maui:
        prefix = 'NATIVECHANNELS_MAUI_RESULT '
    if target == 'android':
        adb = [args.adb] + (['-s', args.device] if args.device else [])
        apks = list(directory.glob('*-Signed.apk'))
        if len(apks) != 1:
            raise RuntimeError(f'Expected one signed APK in {directory}')
        bundle = entry['bundleId']
        command([*adb, 'install', '-r', apks[0]])
        command([*adb, 'shell', 'am', 'force-stop', bundle])
        activity = capture([*adb, 'shell', 'cmd', 'package', 'resolve-activity', '--brief', bundle]).splitlines()[-1]
        since = capture([*adb, 'shell', "date '+%m-%d %H:%M:%S.000'"])
        command([*adb, 'shell', 'am', 'start', '-W', '-n', activity, '--ez', 'verify', 'true'])
        pid = capture([*adb, 'shell', 'pidof', '-s', bundle])
        if not pid.isdigit():
            raise RuntimeError('Launched Android sample has no live process')
        # Select only this launch's PID and time interval; never clear device logs.
        stream = [*adb, 'logcat', '-T', since, '--pid', pid, '-v', 'brief']
        launch = None
        stop = [*adb, 'shell', 'am', 'force-stop', bundle]
    else:
        device = args.device or 'booted'
        apps = list(directory.rglob('*.app'))
        if len(apps) != 1:
            raise RuntimeError(f'Expected one simulator .app in {directory}')
        bundle = plistlib.loads((apps[0] / 'Info.plist').read_bytes())['CFBundleIdentifier']
        command(['xcrun', 'simctl', 'install', device, apps[0]])
        stream = ['xcrun', 'simctl', 'launch', '--console', '--terminate-running-process', device, bundle, '--verify']
        launch = None
        stop = ['xcrun', 'simctl', 'terminate', device, bundle]
    log = folder / 'stdout.log'
    with log.open('w', encoding='utf-8') as output:
        process = subprocess.Popen(stream, stdout=output, stderr=subprocess.STDOUT)
        try:
            if launch:
                command(launch)
            end = time.monotonic() + args.timeout
            while time.monotonic() < end:
                contents = log.read_text(encoding='utf-8', errors='replace')
                lines = [line.split(prefix, 1)[1] for line in contents.splitlines() if prefix in line]
                if lines and (maui or sample != 'Currency' or 'CURRENCY_AVALONIA_CLOSED' in contents):
                    validate_result(sample, lines[-1], contents, framework=entry.get('framework', 'avalonia'))
                    return lines[-1]
                if process.poll() is not None:
                    raise RuntimeError('Device logging/launch ended without a complete result')
                time.sleep(0.25)
            raise TimeoutError('Mobile verification or cleanup timed out')
        finally:
            subprocess.run(stop, check=False, capture_output=True)
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)


def browser_test(entry, folder, timeout):
    process = subprocess.Popen(['node', str(ROOT / 'eng/test-samples-browser.mjs'),
                                entry['directory'], entry['sample'], str(folder)], cwd=ROOT,
                               start_new_session=os.name != 'nt')
    try:
        code = process.wait(timeout=timeout)
        if code:
            raise RuntimeError(f'Browser verification exited with {code}; see stdout.log and stderr.log')
    finally:
        if process.poll() is None:
            if os.name == 'nt':
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True, check=False)
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=10)
    result = (folder / 'result.txt').read_text(encoding='utf-8')
    if not result.startswith('PASS '):
        raise RuntimeError(result)
    return result


@contextmanager
def audio_environment(args):
    if not args.virtual_audio:
        yield
        return
    if args.platform != 'linux':
        raise ValueError('--virtual-audio requires Linux with PulseAudio')
    name = 'nativechannels_test_' + uuid.uuid4().hex
    module = capture(['pactl', 'load-module', 'module-null-sink', 'sink_name=' + name])
    previous = os.environ.get('PULSE_SINK')
    os.environ['PULSE_SINK'] = name
    try:
        yield
    finally:
        if previous is None:
            os.environ.pop('PULSE_SINK', None)
        else:
            os.environ['PULSE_SINK'] = previous
        command(['pactl', 'unload-module', module])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--framework', choices=('avalonia', 'maui'), default='avalonia')
    parser.add_argument('--platform', choices=HEADS, required=True)
    parser.add_argument('--manifest', type=Path, help='Reuse a completed build instead of rebuilding')
    parser.add_argument('--build-only', action='store_true')
    parser.add_argument('--packages-only', action='store_true', help='Prepare hashed platform packages for candidate assembly, without building consumers')
    parser.add_argument('--dotnet', help='Path to the pinned SDK dotnet executable')
    parser.add_argument('--require-clean', action='store_true', help='Require unchanged clean source for candidate builds')
    parser.add_argument('--framework-packages', type=Path, help='Build consumers against an audited combined preview candidate, without repacking framework packages')
    parser.add_argument('--arch', choices=('x64', 'arm64'))
    parser.add_argument('--framework-dependent', action='store_true', help='Use an installed .NET runtime instead of copying it into each desktop app')
    parser.add_argument('--reuse-external-cache', type=Path, help='Hardlink external NuGet dependencies; local framework/plugin packages are always fresh')
    parser.add_argument('--device', help='adb serial or booted iOS simulator UDID')
    parser.add_argument('--adb', default='adb')
    parser.add_argument('--virtual-audio', action='store_true', help='Linux: use a temporary PulseAudio null sink for PCM checks; does not test audible output')
    parser.add_argument('--timeout', type=float, default=120)
    parser.add_argument('--sample', choices=SAMPLES, action='append', help='Run only selected samples (default: all five)')
    args = parser.parse_args()
    if args.manifest and (args.framework_packages or args.require_clean):
        parser.error('--manifest cannot be combined with candidate build options')
    if args.packages_only and (args.manifest or args.framework_packages):
        parser.error('--packages-only cannot reuse a manifest or combined candidate')
    if args.framework_packages:
        args.framework_packages = args.framework_packages.resolve()
    supported = ('windows', 'android', 'ios', 'maccatalyst') if args.framework == 'maui' else tuple(p for p in HEADS if p != 'maccatalyst')
    if args.platform not in supported:
        parser.error(f'{args.framework} supports: {", ".join(supported)}')
    folder = ROOT / 'artifacts/local-samples' / ('maui' if args.framework == 'maui' else '') / args.platform / (time.strftime('%Y%m%d-%H%M%S-') + uuid.uuid4().hex[:8])
    folder.mkdir(parents=True)
    print(f'Evidence: {folder}', flush=True)
    manifest_path = args.manifest.resolve() if args.manifest else build(args, folder / 'build')
    if args.build_only or args.packages_only:
        print(f'Manifest: {manifest_path}')
        return 0
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    if manifest.get('framework', 'avalonia') != args.framework:
        raise ValueError('Manifest framework does not match --framework')
    if manifest['platform'] != args.platform:
        raise ValueError('Manifest platform does not match --platform')
    selected = [entry for entry in manifest['apps'] if not args.sample or entry['sample'] in args.sample]
    expected = set(args.sample or SAMPLES)
    if {entry['sample'] for entry in selected} != expected:
        raise ValueError('Manifest does not contain all requested samples')
    if args.platform == 'linux' and 'PdfViewer' in expected:
        if not {'PdfViewerQt', 'PdfViewerGtk'} <= {entry['name'] for entry in selected}:
            raise ValueError('Linux PDF verification requires both Qt and GTK heads')
    with audio_environment(args):
        return run_selected(args, folder, manifest_path, manifest, selected)


def run_selected(args, folder, manifest_path, manifest, selected):
    results = []
    for entry in selected:
        run = folder / entry['name']
        run.mkdir()
        record = dict(name=entry['name'], status='FAIL')
        started = time.monotonic()
        try:
            files = sorted(path for path in Path(entry['directory']).rglob('*') if path.is_file())
            (run / 'inputs.json').write_text(json.dumps({str(p.relative_to(entry['directory'])):
                file_hash(p) for p in files}, indent=2), encoding='utf-8')
            if args.platform in ('windows', 'linux', 'macos', 'maccatalyst'):
                record['result'] = desktop(entry, args.platform, run, args.timeout)
            elif args.platform in ('android', 'ios'):
                record['result'] = mobile(entry, args, run)
            else:
                record['result'] = browser_test(entry, run, args.timeout)
            record['status'] = 'PASS'
        except Exception as error:
            record['error'] = str(error)
        record['seconds'] = round(time.monotonic() - started, 3)
        results.append(record)
        detail = record.get('error', record.get('result', '')).removeprefix('PASS ')
        print(f"{record['name']}: {record['status']} {detail}", flush=True)
        (folder / 'summary.json').write_text(json.dumps(dict(platform=args.platform, framework=args.framework, host=platform.platform(),
            buildManifest=str(manifest_path), buildSource=manifest.get('source'), currentSource=source_identity(),
            frameworkCandidate=manifest.get('frameworkCandidate'),
            runtimeEnvironment={key: os.environ.get(key) for key in ('DOTNET_ROOT', 'DOTNET_TieredCompilation', 'DISPLAY', 'PULSE_SERVER', 'PULSE_SINK', 'BROWSER_CHROMIUM_CHANNEL')},
            results=results), indent=2), encoding='utf-8')
    return 0 if results and all(item['status'] == 'PASS' for item in results) else 1


if __name__ == '__main__':
    sys.exit(main())
