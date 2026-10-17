#!/usr/bin/env python3
"""Build one package-only Release sample, then measure its real platform adapter."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import shutil
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / 'samples/Maui/ChannelExplorer'
PREFIX = 'NATIVECHANNELS_PROFILE '
TIMINGS = {'method-int32', 'method-reentrant', 'binary-reentrant-4096'}
METRICS = {'android': {'processPss', 'nativeHeapAllocated', 'jvmUsed'},
           'ios': {'physicalFootprint', 'resident'}, 'catalyst': {'physicalFootprint', 'resident'},
           'windows': {'workingSet', 'privateBytes'}}
PLATFORMS = {'android': 'Android', 'ios': 'iOS', 'catalyst': 'MacCatalyst', 'windows': 'WinUI'}


def records(text):
    result = []
    for line in text.splitlines():
        if PREFIX in line:
            line = line.split(PREFIX, 1)[1]
        elif not line.startswith('{'):
            continue
        try:
            result.append(json.loads(line))
        except json.JSONDecodeError as error:
            raise ValueError('Truncated or malformed profile record') from error
    return result


def validate(rows, run_id, seconds, target):
    if not rows or rows[0].get('kind') != 'start' or rows[-1].get('kind') != 'complete':
        raise ValueError('Profile did not complete successfully')
    for index, row in enumerate(rows):
        if row.get('runId') != run_id or row.get('sequence') != index:
            raise ValueError('Stale, missing, duplicated or out-of-order profile records')
        if row.get('kind') not in {'start', 'timing', 'memory', 'complete'}:
            raise ValueError('Profile reported an error or unknown record')
    start, end = rows[0], rows[-1]
    if (sum(row['kind'] == 'start' for row in rows) != 1
            or sum(row['kind'] == 'complete' for row in rows) != 1
            or start.get('durationSeconds') != seconds or start.get('platform') != PLATFORMS[target]
            or not start.get('frameworkVersion') or not start.get('runtime')):
        raise ValueError('Profile configuration or runtime identity mismatch')
    if (not isinstance(end.get('elapsedSeconds'), (int, float))
            or not seconds <= end['elapsedSeconds'] <= seconds + 120
            or end.get('cycles', 0) <= 0 or end.get('activeStreams') != 0):
        raise ValueError('Incomplete soak or native subscriptions remain active')
    timings = [row for row in rows if row['kind'] == 'timing']
    if len(timings) != 3 or {row.get('name') for row in timings} != TIMINGS:
        raise ValueError('Missing native round-trip measurements')
    for row in timings:
        values = [row.get(key, -1) for key in ('minMs', 'p50Ms', 'p95Ms', 'maxMs')]
        if (row.get('samples') != 256 or row.get('warmup') != 32
                or not all(isinstance(value, (int, float)) and math.isfinite(value) and value >= 0 for value in values)
                or values != sorted(values) or not values[0] <= row.get('meanMs', -1) <= values[-1]):
            raise ValueError('Invalid timing statistics')
    memory = [row for row in rows if row['kind'] == 'memory']
    if len(memory) < 3 or memory[-1]['elapsedSeconds'] < seconds:
        raise ValueError('Missing start, running or idle memory samples')
    previous = -1
    for row in memory:
        if (not math.isfinite(row['elapsedSeconds']) or row['elapsedSeconds'] < previous
                or set(row.get('nativeBytes', {})) != METRICS[target]
                or any(not isinstance(value, int) or value < 0 for value in
                       [row.get('managedBytes', -1), row.get('allocatedBytes', -1), *row['nativeBytes'].values()])):
            raise ValueError('Invalid or out-of-order memory measurements')
        previous = row['elapsedSeconds']
    series = {key: [row['nativeBytes'][key] for row in memory] for key in METRICS[target]}
    series['managedBytes'] = [row['managedBytes'] for row in memory]
    return {'start': start, 'complete': end, 'timings': timings, 'memorySamples': len(memory),
            'memoryBytes': {key: {'first': values[0], 'last': values[-1], 'peak': max(values),
                                  'delta': values[-1] - values[0]} for key, values in series.items()},
            'interpretation': 'Observed process/heap retention, not proof of a leak or its absence. No forced GC.'}


def command(*args, timeout=60, capture=False, **kwargs):
    return subprocess.run([str(arg) for arg in args], cwd=ROOT, check=True, timeout=timeout,
                          text=True, capture_output=capture, **kwargs)


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(chunk)
    return value.hexdigest()


def run(args):
    if not 1 <= args.seconds <= 1800:
        raise ValueError('Duration must be 1–1800 seconds; use 300 or longer for a soak')
    if args.target in {'android', 'ios'} and not args.device:
        raise ValueError('Select one already-running device with --device; this script never starts an emulator')
    run_id = str(uuid.uuid4())
    output = ROOT / 'artifacts/native-performance' / run_id
    output.mkdir(parents=True)
    arch = 'arm64' if platform.machine().lower() in {'arm64', 'aarch64'} else 'x64'
    tfm, runtime = {
        'android': ('net10.0-android', f'android-{arch}'),
        'ios': ('net10.0-ios', f'iossimulator-{arch}'),
        'catalyst': ('net10.0-maccatalyst', f'maccatalyst-{arch}'),
        'windows': ('net10.0-windows10.0.19041.0', f'win-{arch}'),
    }[args.target]
    runtime = args.runtime or runtime
    allowed = {'android': {'android-arm64', 'android-x64'}, 'ios': {'iossimulator-arm64', 'iossimulator-x64'},
               'catalyst': {f'maccatalyst-{arch}'}, 'windows': {f'win-{arch}'}}
    if runtime not in allowed[args.target]:
        raise ValueError('Runtime must match the selected execution target/host architecture')
    local_dotnet = ROOT / '.tools/dotnet/dotnet.exe'
    dotnet = ([str(local_dotnet) if local_dotnet.exists() else 'dotnet']
              if os.name == 'nt' else ['bash', 'eng/dotnet.sh'])
    properties = [f'-p:TargetFrameworks={tfm}', f'-p:NativeChannelsProfileSeconds={args.seconds}',
                  f'-p:NativeChannelsProfileRunId={run_id}']
    with (output / 'build.log').open('w') as log:
        print(f'Restoring package-only {args.target} consumer; log: {output / "build.log"}', flush=True)
        if os.name == 'nt':
            command('python', 'eng/clean-consumer.py', 'samples/Maui/ChannelExplorer')
            cache = ROOT / '.tools/np' / run_id[:8]
            cache.mkdir(parents=True)
            command('python', 'eng/consumer-config.py', cache / 'NuGet.Config')
            command(*dotnet, 'restore', PROJECT, *properties, '-r', runtime, '--packages', cache,
                    '--configfile', cache / 'NuGet.Config', '--force', '--no-http-cache', timeout=600, stdout=log, stderr=subprocess.STDOUT)
            command('python', 'eng/verify-consumer-assets.py', PROJECT / 'obj/project.assets.json', stdout=log)
        else:
            command('bash', 'eng/restore-sample.sh', *properties, '-r', runtime,
                    timeout=600, stdout=log, stderr=subprocess.STDOUT)
        print('Compiling Release profile consumer…', flush=True)
        command(*dotnet, 'build', PROJECT, '-c', 'Release', '-f', tfm, '-r', runtime, *properties,
                '--no-restore', '--disable-build-servers', timeout=1200, stdout=log, stderr=subprocess.STDOUT)
    version = ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version')
    assets = json.loads((PROJECT / 'obj/project.assets.json').read_text())
    package_paths = [ROOT / 'artifacts/packages' / f'{identity.replace("/", ".")}.nupkg'
                     for identity in assets['libraries'] if identity.startswith('NativeChannels')]
    evidence = {'source': command('git', 'rev-parse', 'HEAD', capture=True).stdout.strip(),
                'startedUtc': datetime.now(timezone.utc).isoformat(),
                'dirty': bool(command('git', 'status', '--porcelain', capture=True).stdout.strip()),
                'runId': run_id, 'runtimeIdentifier': runtime, 'configuration': 'Release', 'version': version,
                'packageRun': os.environ.get('PACKAGE_RUN'), 'workflowRun': os.environ.get('GITHUB_RUN_ID'),
                'packages': {path.name: digest(path) for path in package_paths}}
    adapters = ([ROOT / 'native/android/sample/build/outputs/aar/sample-release.aar'] if args.target == 'android'
                else list((ROOT / 'artifacts/native/NativeChannelsSample.xcframework').rglob('NativeChannelsSample'))
                if args.target in {'ios', 'catalyst'} else list((ROOT / 'native/windows/sample').glob('*.cs')))
    evidence['adapterInputs'] = {str(path.relative_to(ROOT)): digest(path) for path in adapters if path.is_file()}
    evidence['profileInputs'] = {str(path.relative_to(ROOT)): digest(path) for path in
                               [PROJECT / 'NativeProfile.cs', PROJECT / 'ViewVerification.cs',
                                PROJECT / 'MainPage.cs', PROJECT / 'ChannelExplorer.csproj', Path(__file__).resolve()]}
    (output / 'inputs.json').write_text(json.dumps(evidence, indent=2) + '\n')
    build = PROJECT / 'bin/Release' / tfm / runtime
    raw = output / 'native-performance.jsonl'
    log_path = output / 'process.log'
    process = None
    adb = shutil.which('adb') or str(Path(os.environ.get('ANDROID_HOME', Path.home() / 'Library/Android/sdk')) / 'platform-tools/adb')
    stop = None
    deadline = time.monotonic() + args.seconds + 240
    with log_path.open('w') as log:
        try:
            if args.target == 'android':
                apk, = build.glob('*-Signed.apk')
                command(adb, '-s', args.device, 'install', '-r', '--no-incremental', apk, timeout=120, stdout=log)
                stop = [adb, '-s', args.device, 'shell', 'am', 'force-stop', 'dev.nativechannels.sample']
                command(*stop)
                activity = command(adb, '-s', args.device, 'shell', 'cmd', 'package', 'resolve-activity', '--brief', 'dev.nativechannels.sample', capture=True).stdout.strip().splitlines()[-1]
                command(adb, '-s', args.device, 'shell', 'am', 'start', '-W', '-n', activity, stdout=log)
                pid, = command(adb, '-s', args.device, 'shell', 'pidof', 'dev.nativechannels.sample', capture=True).stdout.split()
                process = subprocess.Popen([adb, '-s', args.device, 'logcat', '--pid', pid, '-v', 'brief'], stdout=log, stderr=subprocess.STDOUT)
            elif args.target == 'ios':
                app, = build.glob('*.app')
                command('xcrun', 'simctl', 'install', args.device, app)
                data = Path(command('xcrun', 'simctl', 'get_app_container', args.device, 'dev.nativechannels.sample', 'data', capture=True).stdout.strip())
                raw = data / 'Library/native-performance.jsonl'
                raw.unlink(missing_ok=True)
                command('xcrun', 'simctl', 'launch', '--terminate-running-process', f'--stdout={log_path}', f'--stderr={output / "stderr.log"}', args.device, 'dev.nativechannels.sample')
                stop = ['xcrun', 'simctl', 'terminate', args.device, 'dev.nativechannels.sample']
            else:
                if args.target == 'catalyst':
                    app, = build.glob('*.app')
                    binary = app / 'Contents/MacOS/ChannelExplorer'
                else:
                    binary = build / 'ChannelExplorer.exe'
                process = subprocess.Popen([str(binary)], stdout=log, stderr=subprocess.STDOUT,
                                           env={**os.environ, 'NATIVECHANNELS_PROFILE_REPORT': str(raw)})
            print(f'Running {args.seconds}s native profile ({run_id})…', flush=True)
            rows = []
            next_progress = 0
            while time.monotonic() < deadline:
                text = log_path.read_text(errors='replace')
                if 'NATIVECHANNELS_SAMPLE FAIL' in text:
                    raise ValueError('Functional native checks failed; see process.log')
                # Append-only files may have an incomplete last line while being written.
                data = raw.read_text() if raw.exists() else text
                complete_lines = data[:data.rfind('\n') + 1]
                rows = records(complete_lines)
                if rows and rows[-1].get('kind') in {'complete', 'error'}:
                    break
                if process is not None and process.poll() is not None:
                    raise ValueError('App/log collector exited before completing the profile')
                if time.monotonic() >= next_progress:
                    memory = [row for row in rows if row.get('kind') == 'memory']
                    print(f'Profile progress: {memory[-1]["cycles"] if memory else 0} cycles; {len(memory)} memory samples', flush=True)
                    next_progress = time.monotonic() + 30
                time.sleep(1)
            normalized = output / 'records.jsonl'
            normalized.write_text(''.join(json.dumps(row) + '\n' for row in rows))
            result = validate(rows, run_id, args.seconds, args.target)
            if result['start']['frameworkVersion'].split('+')[0] != version:
                raise ValueError('Measured framework version differs from the selected package version')
            (output / 'summary.json').write_text(json.dumps({**evidence, **result}, indent=2) + '\n')
            print(f'PASS: {result["complete"]["cycles"]} cycles; report: {output / "summary.json"}')
        finally:
            try:
                if stop:
                    subprocess.run(stop, stdout=log, stderr=subprocess.STDOUT, timeout=30, check=False)
            finally:
                if process is not None and process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=10)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('target', choices=METRICS)
    parser.add_argument('--seconds', type=int, default=300)
    parser.add_argument('--device', help='Existing Android serial or iOS simulator UDID')
    parser.add_argument('--runtime', help='Override Android/simulator architecture, e.g. android-x64')
    try:
        run(parser.parse_args())
    except (ValueError, subprocess.SubprocessError, OSError) as error:
        raise SystemExit(f'FAIL native profile: {error}') from error
