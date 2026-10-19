"""Prepare, verify and publish an accepted six-package preview locally (no workflows)."""
import argparse
from datetime import datetime, timezone
import getpass
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import uuid

import preview_candidate as candidate

ROOT = Path(__file__).resolve().parents[1]
PACKAGE_IDS = ('NativeChannels.Core', 'NativeChannels.Native.Android', 'NativeChannels.Native.Apple',
               'NativeChannels', 'NativeChannels.Maui', 'NativeChannels.Avalonia')
SPEC = importlib.util.spec_from_file_location('nuget_push', ROOT / '.github/scripts/push-nuget.py')
remote = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(remote)


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def summaries_in_order(paths, receipt):
    indexed = {}
    for path in paths:
        report = read(path)
        key = (report.get('framework'), report.get('platform'))
        if key not in candidate.MATRIX or key in indexed:
            raise ValueError('Unexpected or duplicate consumer summary')
        indexed[key] = path
    consumers = receipt.get('consumers', [])
    keys = [(row.get('framework'), row.get('platform')) for row in consumers]
    if len(keys) != len(candidate.MATRIX) or set(keys) != candidate.MATRIX or set(indexed) != candidate.MATRIX:
        raise ValueError('All ten accepted consumer summaries are required')
    return [indexed[key] for key in keys]


def recheck(directory, receipt_path, api_path, summaries):
    receipt = read(receipt_path)
    ordered = summaries_in_order(summaries, receipt)
    actual = candidate.accept(directory, ordered, api_path, receipt['sourceCommit'])
    if actual != receipt:
        raise ValueError('Acceptance receipt does not match these package, API and consumer bytes')
    # Include the publication-specific Portable PDB/allowed symbol-entry checks.
    for package_id in PACKAGE_IDS:
        for suffix in ('nupkg', 'snupkg'):
            path = directory / f"{package_id}.{receipt['version']}.{suffix}"
            remote.release.inspect_archive(path, package_id, receipt['version'], receipt['sourceCommit'])
    return receipt, ordered


def file_hashes(directory):
    paths = list(directory.rglob('*'))
    if any(path.is_symlink() for path in paths):
        raise ValueError('Release files and directories must not be symbolic links')
    return {p.relative_to(directory).as_posix(): candidate.digest(p) for p in sorted(paths)
            if p.is_file() and p != directory / 'release-manifest.json'}


def prepare(packages, acceptance, api, summaries, output):
    receipt, ordered = recheck(packages, acceptance, api, summaries)
    if output.exists():
        raise ValueError('Release output already exists; keep accepted releases immutable')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        stage = Path(temporary) / 'release'
        shutil.copytree(packages, stage / 'candidate')
        evidence = stage / 'evidence'
        evidence.mkdir()
        shutil.copyfile(acceptance, evidence / 'acceptance.json')
        shutil.copyfile(api, evidence / 'api.json')
        for path, row in zip(ordered, receipt['consumers']):
            shutil.copyfile(path, evidence / f"{row['framework']}-{row['platform']}.json")
        manifest = dict(schemaVersion=1, kind='local-preview-release', sourceCommit=receipt['sourceCommit'],
                        version=receipt['version'], packageIds=list(PACKAGE_IDS), files=file_hashes(stage))
        write(stage / 'release-manifest.json', manifest)
        verify(stage)
        stage.rename(output)
    return manifest


def verify(directory):
    manifest = read(directory / 'release-manifest.json')
    expected = {'evidence/acceptance.json', 'evidence/api.json', 'candidate/preview-manifest.json'}
    expected.update(f'evidence/{framework}-{platform}.json' for framework, platform in candidate.MATRIX)
    expected.update(f"candidate/{package_id}.{manifest['version']}.{suffix}"
                    for package_id in PACKAGE_IDS for suffix in ('nupkg', 'snupkg'))
    if (manifest.get('schemaVersion') != 1 or manifest.get('kind') != 'local-preview-release'
            or manifest.get('packageIds') != list(PACKAGE_IDS) or set(manifest.get('files', {})) != expected
            or file_hashes(directory) != manifest['files']):
        raise ValueError('Release files changed or the manifest is incomplete')
    receipt, _ = recheck(directory / 'candidate', directory / 'evidence/acceptance.json',
                         directory / 'evidence/api.json',
                         [directory / f'evidence/{f}-{p}.json' for f, p in candidate.MATRIX])
    if manifest['sourceCommit'] != receipt['sourceCommit'] or manifest['version'] != receipt['version']:
        raise ValueError('Release identity does not match acceptance')
    return manifest


def preflight(directory, manifest, require_indexed=False):
    """Check every ID before the first upload; repository signatures may be added by NuGet."""
    pending = []
    for package_id in PACKAGE_IDS:
        path = directory / 'candidate' / f"{package_id}.{manifest['version']}.nupkg"
        published = remote.fetch_package(package_id, manifest['version'])
        if published is None:
            pending.append(path)
        elif remote.content_hashes(published) != remote.content_hashes(path.read_bytes()):
            raise ValueError(f'{package_id}: published version differs; no upload is allowed')
    if require_indexed and pending:
        raise ValueError('Not indexed on NuGet.org yet: ' + ', '.join(p.name for p in pending))
    return pending


def require_merged(source):
    def git(*args):
        return subprocess.run(['git', *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()
    if git('status', '--porcelain'):
        raise ValueError('Commit release tooling and documentation before publication')
    # Fetch only this ref. Fetching is read-only on GitHub and starts no workflows.
    git('fetch', 'origin', 'main')
    try:
        git('merge-base', '--is-ancestor', source, 'FETCH_HEAD')
    except subprocess.CalledProcessError:
        raise ValueError('The tested source commit must be merged into origin/main before publication') from None
    return git('rev-parse', 'FETCH_HEAD')


def read_api_key(gui=False):
    if gui:
        import tkinter as tk
        from tkinter import simpledialog
        window = tk.Tk()
        window.withdraw()
        window.attributes('-topmost', True)
        try:
            key = simpledialog.askstring('Publish NativeChannels preview to NuGet.org',
                'Enter a NuGet API key scoped to the six NativeChannels package IDs.\n'
                'The key stays in memory and will not be printed or saved.\n'
                'Cancel to leave the release unpublished.', show='*', parent=window)
        finally:
            window.destroy()
    else:
        if not sys.stdin.isatty():
            raise ValueError('Use an interactive terminal or --key-dialog; never pass the API key in arguments')
        key = getpass.getpass('NuGet API key (hidden, held only in memory): ')
    if not key or not key.strip() or '\r' in key or '\n' in key:
        raise ValueError('No valid NuGet API key supplied; nothing uploaded')
    return key.strip()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # Never forward the API-key header to a redirected destination.
        return None


def push(path, api_key):
    # NuGet's documented multipart PUT protocol keeps the key out of process arguments.
    endpoint = 'https://www.nuget.org/api/v2/' + ('symbolpackage' if path.suffix == '.snupkg' else 'package')
    boundary = uuid.uuid4().hex
    body = (f'--{boundary}\r\nContent-Disposition: form-data; name="package"; filename="{path.name}"\r\n'
            'Content-Type: application/octet-stream\r\n\r\n').encode() + path.read_bytes()
    body += f'\r\n--{boundary}--\r\n'.encode()
    request = urllib.request.Request(endpoint, data=body, method='PUT', headers={
        'Content-Type': f'multipart/form-data; boundary={boundary}', 'X-NuGet-ApiKey': api_key,
        'User-Agent': 'NativeChannels-local-preview-publisher/1'})
    try:
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=120) as response:
            if response.status not in (200, 201, 202):
                raise ValueError(f'{path.name}: unexpected upload status {response.status}')
    except urllib.error.HTTPError as error:
        # Response bodies can echo headers. Report only the status, never credentials or server text.
        raise ValueError(f'{path.name}: NuGet returned HTTP {error.code}; retain the release and inspect '
                         'partial uploads/indexing before retrying') from None
    except (urllib.error.URLError, TimeoutError, OSError):
        raise ValueError(f'{path.name}: upload outcome is unknown; inspect NuGet before retrying') from None


def publish(directory, version, report_path, gui=False):
    manifest = verify(directory)
    if version != manifest['version']:
        raise ValueError('--publish-version must match the reviewed release exactly')
    if report_path.exists() or directory.resolve() in report_path.resolve().parents:
        raise ValueError('Use a new submission report path outside the immutable release directory')
    main = require_merged(manifest['sourceCommit'])
    pending = preflight(directory, manifest)
    key = read_api_key(gui)
    try:
        # Recheck after user entry: neither changed files nor intervening publication are accepted.
        if verify(directory) != manifest:
            raise ValueError('Release changed during credential entry')
        pending = preflight(directory, manifest)
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report = dict(version=version, sourceCommit=manifest['sourceCommit'], mergedMain=main,
                      releaseManifestSha256=candidate.digest(directory / 'release-manifest.json'),
                      startedUtc=datetime.now(timezone.utc).isoformat(), uploadsCompleted=False, submissions=[])
        write(report_path, report)
        paths = pending + [directory / 'candidate' / f'{p}.{version}.snupkg' for p in PACKAGE_IDS]
        for path in paths:
            row = dict(file=path.name, sha256=candidate.digest(path), status='attempting')
            report['submissions'].append(row)
            write(report_path, report)
            push(path, key)
            row['status'] = 'submitted; indexing not verified'
            write(report_path, report)
            print(f'Submitted {path.name}', flush=True)
        report['uploadsCompleted'] = True
        write(report_path, report)
        return report
    finally:
        key = None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'verify', 'preflight', 'publish', 'verify-public'))
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--acceptance', type=Path)
    parser.add_argument('--api-report', type=Path)
    parser.add_argument('--summary', type=Path, action='append')
    parser.add_argument('--publish-version', help='Explicitly authorize publishing this exact preview version')
    parser.add_argument('--submission-report', type=Path)
    parser.add_argument('--key-dialog', action='store_true', help='Use a local masked dialog instead of terminal input')
    args = parser.parse_args()
    if args.mode == 'prepare':
        if not all((args.candidate, args.acceptance, args.api_report, args.summary)):
            parser.error('prepare requires --candidate, --acceptance, --api-report and all ten --summary paths')
        result = prepare(args.candidate, args.acceptance, args.api_report, args.summary, args.directory)
    elif args.mode == 'publish':
        if not args.publish_version or not args.submission_report:
            parser.error('publish requires --publish-version and a new --submission-report path')
        result = publish(args.directory, args.publish_version, args.submission_report, args.key_dialog)
        print('Uploads completed; NuGet validation, symbol status and fresh public-feed consumers remain to check')
        return
    else:
        result = verify(args.directory)
        if args.mode in ('preflight', 'verify-public'):
            pending = preflight(args.directory, result, require_indexed=args.mode == 'verify-public')
            print(f'NuGet package preflight: {len(pending)} new package versions; {6 - len(pending)} identical indexed versions')
    print(f"PASS {args.mode}: six packages and symbols for {result['version']} from {result['sourceCommit']}")


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))
