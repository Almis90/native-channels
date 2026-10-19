#!/usr/bin/env python3
"""Build and execute the package-only Uno macOS transport gate on a Mac with Xcode."""
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    if sys.platform != 'darwin':
        raise SystemExit('Run this gate on macOS with Xcode installed.')
    dotnet = os.environ.get('DOTNET_HOST_PATH') or shutil.which('dotnet')
    if not dotnet:
        raise SystemExit('Put the repository-pinned .NET SDK on PATH.')
    run = ROOT / 'artifacts/uno-feasibility/runs' / uuid.uuid4().hex
    run.mkdir(parents=True)
    feed = ROOT / 'artifacts/uno-feasibility/packages'
    version = '0.3.0-preview.6'
    project = 'samples/Uno/Feasibility/UnoFeasibility'
    rid = 'osx-arm64' if platform.machine() == 'arm64' else 'osx-x64'

    def execute(*args):
        subprocess.run(args, cwd=ROOT, check=True)

    for arch in ('arm64', 'x86_64'):
        execute(sys.executable, 'eng/build-apple-desktop.py', '--arch', arch, '--plugins', '--sample')
    packages_to_build = ['src/NativeChannels.Core', 'src/NativeChannels', 'src/NativeChannels.Uno', 'tests/NativeChannels.Desktop.ProbeAssets']
    packages_to_build += [f'plugins/Plugin.{name}/src/Plugin.{name}' for name in ('Currency', 'AudioStreamer', 'VolumeMonitor', 'Uno.PdfViewer')]
    for package in packages_to_build:
        execute(dotnet, 'pack', package, '-c', 'Release', '-p:NativeChannelsDesktopOnly=true',
                '-p:NativeChannelsIncludeAppleDesktopAssets=true', f'-p:Version={version}',
                f'-p:NativeChannelsPackageVersion={version}',
                f'-p:RestoreConfigFile={ROOT / "samples/Uno/Feasibility/NuGet.Config"}', '-o', str(feed))
    with zipfile.ZipFile(feed / f'NativeChannels.{version}.nupkg') as archive:
        for entry in ('lib/net10.0/NativeChannels.dll',
                      'runtimes/osx-arm64/native/libNativeChannelsNative.dylib',
                      'runtimes/osx-x64/native/libNativeChannelsNative.dylib'):
            if entry not in archive.namelist():
                raise RuntimeError(f'Missing portable package asset: {entry}')
    properties = ['-p:TargetFrameworks=net10.0-desktop', '-p:NativeChannelsAppleDesktopProbe=true',
                  '-p:Configuration=Release', '-r', rid]
    execute(dotnet, 'restore', project, *properties, '--configfile',
            'samples/Uno/Feasibility/NuGet.Config', '--packages', str(run / 'cache'))
    execute(dotnet, 'build', project, *properties, '--no-restore')
    result = subprocess.run([dotnet, str(ROOT / project / 'bin/Release/net10.0-desktop' / rid /
                                        'UnoFeasibility.dll'), '--probe-dependencies'],
                            cwd=ROOT, check=True, capture_output=True, text=True, timeout=60)
    (run / 'dependencies.json').write_text(result.stdout)
    (run / 'contracts.log').write_text(result.stderr)
    packages = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                for p in feed.glob(f'*.{version}.nupkg')}
    (run / 'packages.json').write_text(json.dumps(packages, indent=2))
    report = json.loads(result.stdout)
    print(result.stdout)
    print(f'Evidence: {run}')
    if report.get('nativeMethod') != 'passed' or report.get('desktopContract') != 'passed':
        raise SystemExit('Uno macOS transport acceptance failed; inspect the retained evidence.')


if __name__ == '__main__':
    main()
