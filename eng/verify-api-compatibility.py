"""Compare candidate framework packages with hash-pinned, published NuGet baselines."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path, default=ROOT / 'artifacts/packages')
    parser.add_argument('--scope', choices=('core', 'mobile', 'combined'), default='combined')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/api-compatibility/result.json')
    args = parser.parse_args()
    manifest = json.loads((ROOT / 'eng/api-baseline.json').read_text())
    version = ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version')
    result = {'candidateVersion': version, 'baselineVersion': manifest['version'], 'scope': args.scope,
              'success': False, 'packages': []}
    try:
        for package_id, sha256 in manifest['packages'].items():
            if args.scope == 'core' and package_id != 'NativeChannels.Core':
                continue
            old = baseline(ROOT / 'artifacts/api-compatibility/baseline', package_id, manifest['version'], sha256)
            candidate = args.packages / f'{package_id}.{version}.nupkg'
            validate_targets(candidate, old, package_id, version, manifest['version'], args.scope)
            command = ['bash', str(ROOT / 'eng/dotnet.sh'), 'apicompat', 'package', str(candidate),
                       '--baseline-package', str(old), '--run-api-compat', '--enable-rule-cannot-change-parameter-name']
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
