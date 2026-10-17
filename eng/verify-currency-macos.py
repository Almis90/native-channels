#!/usr/bin/env python3
"""Execute the package-only Avalonia AppKit application and retain native macOS evidence."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import platform
import plistlib
import subprocess

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('shared_hosting', ROOT / 'eng/verify-shared-hosting.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


def check_log(log, returncode):
    if returncode != 0:
        raise ValueError(f'macOS application exited with code {returncode}')
    result = proof.runtime_proof({'avalonia': log}, 'macos')
    if log.splitlines().count('CURRENCY_AVALONIA_CLOSED') != 1:
        raise ValueError('Missing orderly desktop shutdown')
    return result


def check_binary(path, architecture):
    arches = subprocess.check_output(['xcrun', 'lipo', '-archs', str(path)], text=True).split()
    if architecture not in arches:
        raise ValueError(f'{path.name}: missing {architecture} slice')
    build = subprocess.check_output(['xcrun', 'vtool', '-arch', architecture, '-show-build', str(path)], text=True)
    if 'platform MACOS\n' not in build or 'MACCATALYST' in build or 'IOS' in build:
        raise ValueError(f'{path.name}: expected native macOS Mach-O platform')
    linked = subprocess.check_output(['xcrun', 'otool', '-L', str(path)], text=True)
    if 'UIKit' in linked or '/iOSSupport/' in linked:
        raise ValueError(f'{path.name}: unexpected UIKit or Catalyst dependency')
    return {'architectures': arches, 'build': build, 'dependencies': linked}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', choices=('osx-arm64', 'osx-x64'), default='osx-arm64')
    args = parser.parse_args()
    architecture = 'arm64' if args.runtime == 'osx-arm64' else 'x86_64'
    if platform.system() != 'Darwin' or platform.machine() != architecture:
        parser.error('Execute on a Mac matching the requested architecture')
    output = ROOT / 'artifacts/shared-hosting-macos' / args.runtime
    output.mkdir(parents=True, exist_ok=True)
    for name in ('verification.json', 'avalonia-result.txt'):
        (output / name).unlink(missing_ok=True)
    project = ROOT / 'samples/Avalonia/Currency/Currency.Desktop'
    assets = json.loads((project / 'obj/project.assets.json').read_text())
    record = {'platform': 'macos', 'runtime': args.runtime, 'configuration': 'Release',
              'sourceCommit': os.environ.get('GITHUB_SHA') or subprocess.check_output(
                  ['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'workingTreeDirty': bool(subprocess.check_output(
                  ['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()),
              'packages': proof.package_proof({'avalonia': assets}, ROOT / 'artifacts/packages', 'macos')}
    apps = list((project / f'bin/Release/net10.0-macos/{args.runtime}').glob('*.app'))
    if len(apps) != 1:
        raise ValueError('Expected exactly one macOS app bundle; restore into a fresh cache and rebuild')
    app = apps[0]
    manifest = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
    if manifest['CFBundleIdentifier'] != 'dev.nativechannels.currency.avalonia.macos':
        raise ValueError('Unexpected application bundle identifier')
    executable = app / 'Contents/MacOS' / manifest['CFBundleExecutable']
    record['native'] = {'application': check_binary(executable, architecture)}
    for name in ('NativeChannelsNative', 'MauiCurrencyNative'):
        record['native'][name] = check_binary(app / f'Contents/Frameworks/{name}.framework/{name}', architecture)
    with (output / 'avalonia-result.txt').open('w') as log:
        process = subprocess.Popen([str(executable), '--verify'], cwd=app.parent, stdout=log, stderr=subprocess.STDOUT)
        try:
            process.wait(timeout=90)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise ValueError('macOS verification timed out') from None
    record['results'] = check_log((output / 'avalonia-result.txt').read_text(), process.returncode)
    record['success'] = True
    (output / 'verification.json').write_text(json.dumps(record, indent=2) + '\n')
    print('PASS: package-only Avalonia Currency on native macOS; AppKit binary, native calls and shutdown verified')


if __name__ == '__main__':
    main()
