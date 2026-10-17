#!/usr/bin/env python3
"""Verify fresh browser package inputs and fingerprint the published static app."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import platform
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('shared_hosting', ROOT / 'eng/verify-shared-hosting.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


def check_published_modules(published, feed):
    modules = {}
    for package_id in ('NativeChannels', 'Plugin.Maui.Currency'):
        candidates = list(feed.glob(package_id + '.[0-9]*.nupkg'))
        if len(candidates) != 1:
            raise ValueError(f'Expected one {package_id} archive')
        with zipfile.ZipFile(candidates[0]) as archive:
            names = [name for name in archive.namelist() if name.startswith('browser/') and name.endswith('.mjs')]
            if not names:
                raise ValueError(f'Missing packaged JavaScript: {package_id}')
            for name in names:
                target = f'_content/{package_id}/{Path(name).name}'
                data = archive.read(name)
                if (published / target).read_bytes() != data:
                    raise ValueError(f'Published module differs from package: {target}')
                modules[target] = hashlib.sha256(data).hexdigest()
    return modules


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--published', type=Path, default=ROOT / 'samples/Avalonia/Currency/Currency.Browser/bin/Release/net10.0-browser/publish/wwwroot')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/shared-hosting-browser')
    args = parser.parse_args()
    assets = json.loads((ROOT / 'samples/Avalonia/Currency/Currency.Browser/obj/project.assets.json').read_text(encoding='utf-8-sig'))
    if not (args.published / 'index.html').is_file() or not list((args.published / '_framework').glob('Currency.Browser*.wasm')):
        raise ValueError('Expected a published browser app with WebCIL assemblies')
    feed = ROOT / 'artifacts/packages'
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    sources = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
               for name in sorted(set(names)) if name and (ROOT / name).is_file()}
    files = {file.relative_to(args.published).as_posix(): hashlib.sha256(file.read_bytes()).hexdigest()
             for file in sorted(args.published.rglob('*')) if file.is_file()}
    record = {
        'schemaVersion': 1, 'platform': 'browser', 'runtime': 'browser-wasm', 'configuration': 'Release',
        'buildOS': platform.platform(),
        'sdk': subprocess.check_output(['bash', 'eng/dotnet.sh', '--version'], cwd=ROOT, text=True).strip(),
        'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'workingTreeDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()),
        'sourceTreeSha256': hashlib.sha256(json.dumps(sources, sort_keys=True).encode()).hexdigest(),
        'packages': proof.package_proof({'avalonia': assets}, feed, 'browser'),
        'packagedModules': check_published_modules(args.published, feed),
        'publishedFiles': files,
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'inputs.json').write_text(json.dumps(record, indent=2) + '\n')
    (args.output / 'source-files.json').write_text(json.dumps(sources, indent=2) + '\n')
    print(f'PASS browser inputs: 3 local packages, {len(record["packagedModules"])} identical packaged modules, {len(files)} published files')


if __name__ == '__main__':
    main()
