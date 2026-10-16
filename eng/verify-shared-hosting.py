#!/usr/bin/env python3
"""Prove both Android hosts consume identical local packages and execute the same scenarios."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

SHARED = {'nativechannels.core', 'nativechannels', 'nativechannels.native.android', 'plugin.maui.currency'}
MARKERS = {'maui': 'CURRENCY_SAMPLE', 'avalonia': 'CURRENCY_AVALONIA'}


def package_proof(assets_by_host, feed):
    records, caches = {}, {}
    for host in MARKERS:
        assets = assets_by_host[host]
        libraries = assets['libraries']
        ids = {identity.split('/')[0].lower() for identity in libraries}
        if host == 'avalonia':
            if any(name == 'nativechannels.maui' or name.startswith('microsoft.maui') for name in ids):
                raise ValueError('Avalonia resolved a MAUI dependency')
            if 'avalonia.android' not in ids:
                raise ValueError('Avalonia Android package is missing')
        elif 'nativechannels.maui' not in ids:
            raise ValueError('MAUI host package is missing')
        if not SHARED <= ids:
            raise ValueError(f'{host}: missing shared packages: {SHARED - ids}')
        caches[host] = {Path(folder).resolve() for folder in assets['packageFolders']}
        records[host] = {}
        for identity, library in libraries.items():
            if library['type'] != 'package':
                raise ValueError(f'{host}: project reference: {identity}')
            name, version = identity.split('/')
            if name.lower() not in SHARED:
                continue
            candidates = [folder / library['path'] for folder in caches[host]
                          if (folder / library['path']).is_dir()]
            if len(candidates) != 1:
                raise ValueError(f'{host}: ambiguous package cache: {identity}')
            directory = candidates[0]
            source = json.loads((directory / '.nupkg.metadata').read_text())['source']
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


def runtime_proof(logs):
    results = {}
    for host, marker in MARKERS.items():
        lines = [line.split(marker + ' ', 1)[1].strip() for line in logs[host].splitlines()
                 if marker + ' ' in line]
        if len(lines) != 1 or not lines[0].startswith('PASS android:'):
            raise ValueError(f'{host}: missing, duplicate or failed runtime result')
        if not lines[0].endswith('; normalization, errors, concurrency, cancellation'):
            raise ValueError(f'{host}: incomplete Currency scenarios')
        results[host] = lines[0]
    if results['maui'] != results['avalonia']:
        raise ValueError('Hosts returned different native Currency results')
    return results


def main():
    root = Path(__file__).resolve().parents[1]
    output = root / 'artifacts/shared-hosting'
    output.mkdir(parents=True, exist_ok=True)
    sha = os.environ.get('GITHUB_SHA') or subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    if sys.argv[1:] == ['packages']:
        for name in ('verification.json', 'packages.json', 'maui-result.txt', 'avalonia-result.txt'):
            (output / name).unlink(missing_ok=True)
        projects = {'maui': 'Currency.Sample', 'avalonia': 'Currency.Avalonia.Android'}
        assets = {host: json.loads((root / 'samples' / project / 'obj/project.assets.json').read_text())
                  for host, project in projects.items()}
        record = {'sourceCommit': sha, 'packages': package_proof(assets, root / 'artifacts/packages')}
        (output / 'packages.json').write_text(json.dumps(record, indent=2) + '\n')
        print('PASS: both hosts resolved identical shared packages; Avalonia has no MAUI dependency')
    elif sys.argv[1:] == ['results']:
        record = json.loads((output / 'packages.json').read_text())
        if record['sourceCommit'] != sha:
            raise ValueError('Package evidence belongs to another commit')
        record['results'] = runtime_proof({host: (output / f'{host}-result.txt').read_text() for host in MARKERS})
        record.update(success=True, platform='android', runtime='android-x64', configuration='Release')
        (output / 'verification.json').write_text(json.dumps(record, indent=2) + '\n')
        summary = os.environ.get('GITHUB_STEP_SUMMARY')
        if summary:
            with open(summary, 'a') as stream:
                stream.write('### Shared Currency package — Android runtime proof\n\n')
                for host, result in record['results'].items():
                    stream.write(f'- **{host}**: {result}\n')
                stream.write('\nBoth fresh caches match the same package hashes. Avalonia contains no MAUI dependency.\n')
        print('PASS: the same Currency package executed successfully in MAUI and Avalonia')
    else:
        raise SystemExit('Usage: verify-shared-hosting.py packages|results')


if __name__ == '__main__':
    main()
