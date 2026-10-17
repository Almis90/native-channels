#!/usr/bin/env python3
"""Prove MAUI and Avalonia consume identical local packages and execute the same scenarios."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

SHARED = {'nativechannels.core', 'nativechannels', 'plugin.maui.currency'}
MARKERS = {'maui': 'CURRENCY_SAMPLE', 'avalonia': 'CURRENCY_AVALONIA'}
PLATFORMS = {
    'android': {'binding': 'nativechannels.native.android', 'host': 'avalonia.android',
                'project': 'Currency.Avalonia.Android', 'runtime': 'android-x64',
                'output': 'shared-hosting', 'result': 'android'},
    'ios': {'binding': 'nativechannels.native.apple', 'host': 'avalonia.ios',
            'project': 'Currency.Avalonia.iOS', 'runtime': 'iossimulator-arm64',
            'output': 'shared-hosting-ios', 'result': 'apple'},
    'windows': {'binding': None, 'host': 'avalonia.win32',
                'project': 'Currency.Avalonia.Windows', 'runtime': 'win-x64',
                'output': 'shared-hosting-windows', 'result': 'windows'},
}


def package_proof(assets_by_host, feed, platform='android'):
    target = PLATFORMS[platform]
    shared = SHARED | ({target['binding']} if target['binding'] else set())
    records, caches = {}, {}
    for host in MARKERS:
        assets = assets_by_host[host]
        libraries = assets['libraries']
        ids = {identity.split('/')[0].lower() for identity in libraries}
        if platform == 'windows' and ids & {'nativechannels.native.android', 'nativechannels.native.apple'}:
            raise ValueError(f'{host}: mobile binding entered the Windows consumer')
        if host == 'avalonia':
            if any(name == 'nativechannels.maui' or name.startswith('microsoft.maui') for name in ids):
                raise ValueError('Avalonia resolved a MAUI dependency')
            if target['host'] not in ids:
                raise ValueError(f'Avalonia {platform} package is missing')
            if platform == 'windows' and any(name.startswith('microsoft.windowsappsdk') for name in ids):
                raise ValueError('Avalonia resolved a Windows App SDK dependency')
        elif 'nativechannels.maui' not in ids:
            raise ValueError('MAUI host package is missing')
        if not shared <= ids:
            raise ValueError(f'{host}: missing shared packages: {shared - ids}')
        caches[host] = {Path(folder).resolve() for folder in assets['packageFolders']}
        records[host] = {}
        for identity, library in libraries.items():
            if library['type'] != 'package':
                raise ValueError(f'{host}: project reference: {identity}')
            name, version = identity.split('/')
            if name.lower() not in shared:
                continue
            candidates = [folder / library['path'] for folder in caches[host]
                          if (folder / library['path']).is_dir()]
            if len(candidates) != 1:
                raise ValueError(f'{host}: ambiguous package cache: {identity}')
            directory = candidates[0]
            source = json.loads((directory / '.nupkg.metadata').read_text(encoding='utf-8-sig'))['source']
            if Path(source).resolve() != feed.resolve():
                raise ValueError(f'{host}: package did not come from this feed: {identity}')
            digest = hashlib.sha256((directory / f'{name.lower()}.{version}.nupkg').read_bytes()).hexdigest()
            local = feed / f'{name}.{version}.nupkg'
            if digest != hashlib.sha256(local.read_bytes()).hexdigest():
                raise ValueError(f'{host}: package bytes differ from feed: {identity}')
            records[host][identity] = digest
    if caches['maui'] & caches['avalonia']:
        raise ValueError('Consumers must restore into separate fresh package caches')
    if records['maui'] != records['avalonia']:
        raise ValueError('Hosts resolved different package identities or bytes')
    return records


def runtime_proof(logs, platform='android'):
    expected = PLATFORMS[platform]['result']
    results = {}
    for host, marker in MARKERS.items():
        lines = [line.split(marker + ' ', 1)[1].strip() for line in logs[host].splitlines()
                 if marker + ' ' in line]
        if len(lines) != 1 or not lines[0].startswith(f'PASS {expected}:'):
            raise ValueError(f'{host}: missing, duplicate or failed runtime result')
        if not lines[0].endswith('; normalization, errors, concurrency, cancellation'):
            raise ValueError(f'{host}: incomplete Currency scenarios')
        results[host] = lines[0]
    if results['maui'] != results['avalonia']:
        raise ValueError('Hosts returned different native Currency results')
    return results


def runtime_identifier(platform, requested):
    runtime = requested or PLATFORMS[platform]['runtime']
    allowed = {'win-x64', 'win-arm64'} if platform == 'windows' else {PLATFORMS[platform]['runtime']}
    if runtime not in allowed:
        raise ValueError(f'Unsupported runtime for {platform}: {runtime}')
    return runtime


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', choices=('packages', 'results'))
    parser.add_argument('--platform', choices=PLATFORMS, default='android')
    parser.add_argument('--runtime')
    parser.add_argument('--feed', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    target = PLATFORMS[args.platform]
    root = Path(__file__).resolve().parents[1]
    runtime = runtime_identifier(args.platform, args.runtime)
    output = args.output or root / 'artifacts' / target['output']
    output.mkdir(parents=True, exist_ok=True)
    sha = os.environ.get('GITHUB_SHA') or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    if args.stage == 'packages':
        for name in ('verification.json', 'packages.json', 'maui-result.txt', 'avalonia-result.txt'):
            (output / name).unlink(missing_ok=True)
        projects = {'maui': 'Currency.Sample', 'avalonia': target['project']}
        assets = {host: json.loads((root / 'samples' / project / 'obj/project.assets.json').read_text(encoding='utf-8-sig'))
                  for host, project in projects.items()}
        record = {'sourceCommit': sha, 'platform': args.platform, 'runtime': runtime,
                  'sourceDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=root, text=True).strip()),
                  'configuration': 'Release',
                  'packages': package_proof(assets, args.feed or root / 'artifacts/packages', args.platform)}
        (output / 'packages.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
        print('PASS: both hosts resolved identical shared packages; Avalonia has no MAUI dependency')
    else:
        record = json.loads((output / 'packages.json').read_text(encoding='utf-8-sig'))
        if record['sourceCommit'] != sha:
            raise ValueError('Package evidence belongs to another commit')
        if record.get('platform') != args.platform or record.get('runtime') != runtime:
            raise ValueError('Package evidence belongs to another platform or runtime')
        record['results'] = runtime_proof(
            {host: (output / f'{host}-result.txt').read_text(encoding='utf-8-sig') for host in MARKERS}, args.platform)
        record['success'] = True
        (output / 'verification.json').write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
        summary = os.environ.get('GITHUB_STEP_SUMMARY')
        if summary:
            with open(summary, 'a', encoding='utf-8') as stream:
                stream.write(f'### Shared Currency package — {args.platform} runtime proof\n\n')
                for host, result in record['results'].items():
                    stream.write(f'- **{host}**: {result}\n')
                stream.write('\nBoth fresh caches match the same package hashes. Avalonia contains no MAUI dependency.\n')
        print('PASS: the same Currency package executed successfully in MAUI and Avalonia')


if __name__ == '__main__':
    main()
