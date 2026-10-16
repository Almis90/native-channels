"""Publish an approved candidate; reject conflicting versions before uploading anything."""
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import sys
import urllib.error
import urllib.request
import zipfile

spec = importlib.util.spec_from_file_location('nuget_release', Path(__file__).with_name('nuget-release.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
SOURCE = 'https://api.nuget.org/v3/index.json'


def content_hashes(data):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate entries in published package')
        # NuGet adds its repository signature after upload. All other bytes must match.
        return {entry.filename: hashlib.sha256(archive.read(entry)).hexdigest()
                for entry in archive.infolist() if not entry.is_dir() and entry.filename != '.signature.p7s'}


def fetch_package(package_id, version):
    name, version = package_id.lower(), version.lower()
    url = f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg'
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def pending_packages(directory, manifest):
    pending = []
    for package_id in release.PACKAGE_IDS:
        path = directory / f"{package_id}.{manifest['version']}.nupkg"
        remote = fetch_package(package_id, manifest['version'])
        if remote is None:
            pending.append(path)
        elif content_hashes(remote) != content_hashes(path.read_bytes()):
            raise ValueError(f'{package_id}: published version differs from the verified candidate; stopping')
        else:
            print(f'Already published with identical contents: {path.name}')
    return pending


def push(path, api_key, symbols=False):
    args = ['dotnet', 'nuget', 'push', str(path), '--api-key', api_key, '--source', SOURCE,
            '--timeout', '120']
    args += ['--skip-duplicate'] if symbols else ['--no-symbols']
    # Do not print argv or propagate CalledProcessError: either would expose the temporary key.
    if subprocess.run(args, check=False).returncode:
        raise SystemExit(f'NuGet upload failed for {path.name}. Keep the candidate and inspect partial publication before retrying.')


def main():
    if (os.environ.get('PUBLISH_NUGET') != 'true' or os.environ.get('GITHUB_REF') != 'refs/heads/main'
            or os.environ.get('GITHUB_EVENT_NAME') != 'workflow_dispatch'):
        sys.exit('Publication requires the explicitly enabled manual workflow on main')
    api_key = os.environ.get('NUGET_API_KEY')
    if not api_key:
        sys.exit('NuGet Trusted Publishing did not supply a temporary API key')
    directory = Path(sys.argv[1])
    manifest = release.verify(directory, os.environ['GITHUB_SHA'], os.environ['DISTRIBUTION_RUN'], os.environ['VERSION'])
    # Inspect all existing versions before the first push, so conflicts fail without partial upload.
    pending = pending_packages(directory, manifest)
    for path in pending:
        push(path, api_key)
    for package_id in release.PACKAGE_IDS:
        push(directory / f"{package_id}.{manifest['version']}.snupkg", api_key, symbols=True)
    with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as summary:
        summary.write('## NuGet upload commands completed\n\nFive framework packages and their symbols were submitted or already present. '
                      'Wait for NuGet validation/indexing, then verify fresh NuGet.org consumer restores. '
                      'No example packages, tags or GitHub releases were created.\n')


if __name__ == '__main__':
    main()
