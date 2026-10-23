"""Compare candidate framework packages with hash-pinned, published NuGet baselines."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def baseline(directory, package_id, version, sha256):
    name = f'{package_id.lower()}.{version}.nupkg'
    path = directory / name
    if not path.exists():
        directory.mkdir(parents=True, exist_ok=True)
        url = f'https://api.nuget.org/v3-flatcontainer/{package_id.lower()}/{version}/{name}'
        data = urllib.request.urlopen(url, timeout=60).read()
        if hashlib.sha256(data).hexdigest() != sha256:
            raise ValueError(f'{package_id}: published baseline hash mismatch')
        path.write_bytes(data)
    if digest(path) != sha256:
        raise ValueError(f'{package_id}: cached baseline hash mismatch')
    return path


def frameworks(path, package_id, version):
    with zipfile.ZipFile(path) as archive:
        metadata = ET.fromstring(archive.read(f'{package_id}.nuspec')).find('{*}metadata')
        if metadata.findtext('{*}id') != package_id or metadata.findtext('{*}version') != version:
            raise ValueError('Unexpected API comparison package identity')
        return {name.split('/')[1] for name in archive.namelist()
                if name.startswith('lib/') and name.endswith(f'/{package_id}.dll')}


def validate_targets(candidate, published, package_id, version, baseline_version, scope):
    expected = frameworks(published, package_id, baseline_version)
    if scope == 'mobile':
        expected = {tfm for tfm in expected if not tfm.startswith('net10.0-windows')}
    actual = frameworks(candidate, package_id, version)
    # New target frameworks are additive. Every baseline target in this scope must remain.
    if not expected or not expected.issubset(actual):
        raise ValueError(f'{package_id}: expected {sorted(expected)}, got {sorted(actual)}; use a complete feed for this scope')


def extract_references(packages, destination):
    """Load dependencies from the same package set, never from a developer's cache."""
    result = {}
    for package_id, path in packages.items():
        assets = result[package_id] = {}
        with zipfile.ZipFile(path) as archive:
            for name in archive.namelist():
                parts = name.split('/')
                if len(parts) == 3 and parts[0] == 'lib' and parts[2] == package_id + '.dll':
                    moniker(parts[1])  # Validate before constructing an extraction path.
                    output = destination / package_id / parts[1] / parts[2]
                    output.parent.mkdir(parents=True, exist_ok=True)
                    output.write_bytes(archive.read(name))
                    assets[parts[1]] = output
    return result


def moniker(tfm):
    # ApiCompat's parser takes canonical MSBuild monikers, not NuGet folder TFMs.
    match = re.fullmatch(r'net(\d+\.\d+)(?:-([a-z]+)([\d.]+)?)?', tfm)
    if not match:
        raise ValueError(f'Unsupported API reference target: {tfm}')
    result = '.NETCoreApp,Version=v' + match[1]
    if match[2]:
        result += '+' + match[2] + ',Version=' + (match[3] or '0.0')
    return result


def reference_arguments(option, package_id, assets, system_references=()):
    arguments = []
    for tfm in sorted(assets[package_id]):
        paths = list(system_references)
        for dependency, frameworks in assets.items():
            if dependency != package_id:
                selected = frameworks.get(tfm, frameworks.get(tfm.split('-')[0]))
                if selected:
                    paths.append(selected)
        if paths:
            arguments += [option, moniker(tfm) + '|' + ','.join(str(path) for path in paths)]
    return arguments


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path, default=ROOT / 'artifacts/packages')
    parser.add_argument('--scope', choices=('core', 'mobile', 'combined'), default='combined')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/api-compatibility/result.json')
    parser.add_argument('--dotnet', type=Path, help='Pinned SDK executable (including dotnet.exe on Windows)')
    parser.add_argument('--version', help='Candidate package version (defaults to Directory.Build.props)')
    args = parser.parse_args()
    manifest = json.loads((ROOT / 'eng/api-baseline.json').read_text())
    version = args.version or ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version')
    result = {'candidateVersion': version, 'baselineVersion': manifest['version'], 'scope': args.scope,
              'success': False, 'packages': []}
    try:
        selected = {name: sha for name, sha in manifest['packages'].items()
                    if args.scope != 'core' or name == 'NativeChannels.Core'}
        published = {name: baseline(ROOT / 'artifacts/api-compatibility/baseline', name, manifest['version'], sha)
                     for name, sha in selected.items()}
        candidates = {name: args.packages / f'{name}.{version}.nupkg' for name in selected}
        for name in selected:
            validate_targets(candidates[name], published[name], name, version, manifest['version'], args.scope)
        dotnet = args.dotnet or ROOT / '.tools/dotnet' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
        system_refs = sorted((dotnet.resolve().parent / 'packs/Microsoft.NETCore.App.Ref').glob('*/ref/net10.0'),
                             key=lambda path: tuple(int(n) for n in path.parents[1].name.split('.')))
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='api-references-', dir=args.output.parent) as temporary:
            old_assets = extract_references(published, Path(temporary) / 'baseline')
            new_assets = extract_references(candidates, Path(temporary) / 'candidate')
            for package_id, sha256 in selected.items():
                candidate, old = candidates[package_id], published[package_id]
                command = [str(dotnet), 'apicompat', 'package', str(candidate),
                           '--baseline-package', str(old), '--run-api-compat', '--enable-rule-cannot-change-parameter-name']
                command += reference_arguments('--package-assembly-references', package_id, new_assets, system_refs[-1:])
                command += reference_arguments('--baseline-package-assembly-references', package_id, old_assets, system_refs[-1:])
                if args.scope == 'mobile':
                    command += ['--baseline-package-frameworks-to-ignore', 'net10.0-windows*']
                subprocess.run(command, cwd=ROOT, check=True, timeout=120)
                result['packages'].append({'id': package_id, 'candidateSha256': digest(candidate),
                                           'baselineSha256': sha256, 'compatible': True})
        result['success'] = True
    except (ValueError, OSError, subprocess.SubprocessError, zipfile.BadZipFile, KeyError) as error:
        result['error'] = str(error)
        raise
    finally:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + '\n')
    print(f'PASS API compatibility ({args.scope}): {len(result["packages"])} packages against published {manifest["version"]}')


if __name__ == '__main__':
    main()
