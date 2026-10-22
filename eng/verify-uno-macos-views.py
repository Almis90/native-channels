#!/usr/bin/env python3
"""Run the existing Interop and PDF parity suites on released Uno macOS."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reuse-cache', type=Path, help='Hard-link external packages from an existing cache')
    args = parser.parse_args()
    if sys.platform != 'darwin':
        raise SystemExit('Run on macOS with Xcode and the pinned .NET SDK.')
    dotnet = os.environ.get('DOTNET_HOST_PATH') or shutil.which('dotnet')
    if not dotnet:
        raise SystemExit('Set DOTNET_HOST_PATH to the pinned SDK.')
    run = ROOT / 'artifacts/uno-macos-views' / uuid.uuid4().hex
    feed, cache = run / 'packages', run / 'cache'
    feed.mkdir(parents=True)
    cache.mkdir()
    if args.reuse_cache:
        for package in args.reuse_cache.resolve().iterdir():
            # Always restore our packages fresh; only reuse external dependencies.
            if package.is_dir() and not package.name.startswith(('nativechannels', 'plugin.')):
                shutil.copytree(package, cache / package.name, copy_function=os.link)
    config = run / 'NuGet.Config'
    config.write_text(f'<configuration><packageSources><clear/>'
                      f'<add key="local" value="{feed}"/>'
                      '<add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>'
                      '</packageSources></configuration>')
    version = '0.3.0-preview.6'
    properties = ['-p:NativeChannelsDesktopOnly=true', '-p:NativeChannelsMacOSOnly=true',
                  '-p:NativeChannelsIncludeAppleDesktopAssets=true', f'-p:Version={version}',
                  f'-p:NativeChannelsPackageVersion={version}',
                  f'-p:RestoreConfigFile={config}', f'-p:RestorePackagesPath={cache}']
    rid = 'osx-arm64' if platform.machine() == 'arm64' else 'osx-x64'
    print(f'Evidence: {run}', flush=True)
    with (run / 'build.log').open('w') as log:
        def execute(*command):
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        for arch in ('arm64', 'x86_64'):
            execute(sys.executable, 'eng/build-apple-desktop.py', '--arch', arch, '--sample', '--views')
        for project in ('src/NativeChannels.Core', 'src/NativeChannels', 'src/NativeChannels.Uno',
                        'plugins/Plugin.Uno.PdfViewer/src/Plugin.Uno.PdfViewer'):
            execute(dotnet, 'pack', project, '-c', 'Release', *properties, '-o', str(feed))
        for name in ('Interop', 'PdfViewer'):
            execute(dotnet, 'build', f'samples/Uno/{name}', '-c', 'Release',
                    '-p:TargetFrameworks=net10.0-desktop', '-r', rid, *properties)
    reports = {}
    for name, assembly in (('Interop', 'NativeChannels.Interop.Uno'), ('PdfViewer', 'PdfViewer')):
        output = ROOT / f'samples/Uno/{name}/bin/Release/net10.0-desktop' / rid
        result = run / f'{name}.json'
        env = {**os.environ, 'DOTNET_ROOT': str(Path(dotnet).resolve().parent),
               'NATIVECHANNELS_UNO_SAMPLE_RESULT': str(result)}
        with (run / f'{name}.log').open('w') as log:
            subprocess.run([dotnet, str(output / f'{assembly}.dll')], cwd=ROOT, env=env,
                           stdout=log, stderr=subprocess.STDOUT, timeout=75, check=True)
        reports[name] = json.loads(result.read_text())
        if reports[name]['checks'] != 'passed':
            raise RuntimeError(reports[name])
        # Prove the runtime came from the unmodified pinned NuGet package.
        runtime = cache / 'uno.winui.runtime.skia.macos/6.7.135'
        for relative in ('lib/net10.0/Uno.UI.Runtime.Skia.MacOS.dll',
                         'runtimes/osx/native/libUnoNativeMac.dylib'):
            original = runtime / relative
            deployed = output / original.name
            if hashlib.sha256(original.read_bytes()).digest() != hashlib.sha256(deployed.read_bytes()).digest():
                raise RuntimeError(f'Uno runtime differs from package: {deployed}')
    (run / 'summary.json').write_text(json.dumps({
        'results': reports, 'uno': '6.7.135', 'runtime': rid,
        'externalCacheReused': args.reuse_cache is not None,
        'packages': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in feed.glob('*.nupkg')},
    }, indent=2))
    print('PASS Interop and PDF shared parity checks; released Uno runtime verified.')


if __name__ == '__main__':
    main()
