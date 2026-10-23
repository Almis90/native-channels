"""Transport accepted local previews to the NuGet Trusted Publishing workflow."""
import argparse
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('local_publisher', ROOT / 'eng/publish-preview.py')
publisher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publisher)
REPOSITORY = 'Almis90/native-channels'
RECORD = ROOT / 'docs/public/verification/preview-02-local.json'


def bundle(directory, output):
    publisher.verify(directory)
    if output.exists():
        raise ValueError('Bundle already exists; never replace reviewed release bytes')
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(directory.rglob('*')):
            if path.is_file():
                entry = zipfile.ZipInfo(path.relative_to(directory).as_posix(), (1980, 1, 1, 0, 0, 0))
                entry.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(entry, path.read_bytes())
    return publisher.candidate.digest(output)


def unpack(path, expected_hash, output):
    if not re.fullmatch('[a-f0-9]{64}', expected_hash) or publisher.candidate.digest(path) != expected_hash:
        raise ValueError('Release bundle SHA-256 does not match the reviewed input')
    if output.exists():
        raise ValueError('Extraction output already exists')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        stage = Path(temporary) / 'release'
        stage.mkdir()
        with zipfile.ZipFile(path) as archive:
            entries = archive.infolist()
            names = [entry.orig_filename for entry in entries]
            if len(names) != len(set(names)) or len(names) > 100 or sum(e.file_size for e in entries) > 256 * 1024 * 1024:
                raise ValueError('Duplicate or oversized release bundle')
            for entry in entries:
                name = entry.orig_filename
                parts = PurePosixPath(name).parts
                if (not name or name != entry.filename or any(ord(c) < 32 for c in name)
                        or '\\' in name or ':' in name or name.startswith('/') or '..' in parts
                        or name != PurePosixPath(name).as_posix()
                        or entry.is_dir() or stat.S_ISLNK(entry.external_attr >> 16)):
                    raise ValueError('Unsafe release bundle entry')
                destination = stage.joinpath(*parts)
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(archive.read(entry))
        manifest = publisher.verify(stage)
        stage.rename(output)
    return manifest


def verify(directory, version):
    manifest = publisher.verify(directory)
    framework_release = manifest.get('kind') == 'framework-preview-release'
    record = publisher.read(ROOT / 'docs/public/verification/preview-03-local.json' if framework_release else RECORD)
    audit = 'package-audit.json' if framework_release else 'preview-manifest.json'
    if (manifest['version'] != version or record.get('version') != version
            or record.get('sourceCommit') != manifest['sourceCommit']
            or record.get('acceptance') != publisher.read(directory / 'evidence/acceptance.json')
            or record.get('candidate') != publisher.read(directory / 'candidate' / audit)):
        raise ValueError('Release differs from the accepted package evidence committed in this repository')
    publisher.require_merged(manifest['sourceCommit'])
    return manifest


def require_workflow(publish=False):
    if (os.environ.get('GITHUB_REPOSITORY') != REPOSITORY or os.environ.get('GITHUB_REF') != 'refs/heads/main'
            or os.environ.get('GITHUB_EVENT_NAME') != 'workflow_dispatch'
            or os.environ.get('GITHUB_WORKFLOW_REF') != REPOSITORY + '/.github/workflows/publish-nuget.yml@refs/heads/main'):
        raise ValueError('Requires the manually dispatched publishing workflow on main in this repository')
    if publish and os.environ.get('PUBLISH_NUGET') != 'true':
        raise ValueError('Publication must be explicitly enabled')


def prepare(release_id, expected_hash, version, output):
    require_workflow()
    if not re.fullmatch('[1-9][0-9]*', release_id) or not re.fullmatch('[a-f0-9]{64}', expected_hash):
        raise ValueError('A numeric draft release ID and exact bundle SHA-256 are required')
    api = f'repos/{REPOSITORY}/releases/{release_id}'
    release = json.loads(subprocess.check_output(['gh', 'api', api], text=True))
    if release.get('tag_name') != 'v' + version or not (release.get('draft') or release.get('prerelease')):
        raise ValueError('Expected the draft/prerelease staging release for this exact version')
    name = f'NativeChannels.{version}.accepted.zip'
    assets = [a for a in release.get('assets', []) if a.get('name') == name and a.get('state') == 'uploaded']
    if len(assets) != 1 or assets[0].get('digest') != 'sha256:' + expected_hash:
        raise ValueError('Expected exactly one uploaded release bundle matching the reviewed hash')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        archive = Path(temporary) / name
        with archive.open('wb') as stream:
            subprocess.run(['gh', 'api', f"repos/{REPOSITORY}/releases/assets/{assets[0]['id']}",
                            '-H', 'Accept: application/octet-stream'], stdout=stream, check=True)
        unpack(archive, expected_hash, output)
    return verify(output, version)


def publish(directory, version, report_path):
    require_workflow(publish=True)
    manifest = verify(directory, version)
    if report_path.exists() or directory.resolve() in report_path.resolve().parents:
        raise ValueError('Use a new report outside the immutable release directory')
    key = os.environ.pop('NUGET_API_KEY', None)
    if not key:
        raise ValueError('NuGet Trusted Publishing did not supply a temporary key')
    try:
        pending = publisher.preflight(directory, manifest)
        report = dict(sourceCommit=manifest['sourceCommit'], version=version,
                      releaseManifestSha256=publisher.candidate.digest(directory / 'release-manifest.json'),
                      workflowRun=os.environ.get('GITHUB_RUN_ID'), uploadsCompleted=False, submissions=[])
        report_path.parent.mkdir(parents=True, exist_ok=True)
        publisher.write(report_path, report)
        for path in pending + [directory / 'candidate' / f'{p}.{version}.snupkg' for p in manifest['packageIds']]:
            row = dict(file=path.name, sha256=publisher.candidate.digest(path), status='attempting')
            report['submissions'].append(row)
            publisher.write(report_path, report)
            publisher.push(path, key)
            row['status'] = 'submitted; indexing not verified'
            publisher.write(report_path, report)
            print(f'Submitted {path.name}', flush=True)
        report['uploadsCompleted'] = True
        publisher.write(report_path, report)
    finally:
        key = None
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('bundle', 'prepare', 'verify', 'publish'))
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--release-id')
    parser.add_argument('--bundle-sha256')
    parser.add_argument('--version')
    parser.add_argument('--submission-report', type=Path)
    args = parser.parse_args()
    if args.mode == 'bundle':
        if not args.output:
            parser.error('bundle requires --output')
        print(bundle(args.directory, args.output))
        return
    if not args.version:
        parser.error('--version is required')
    if args.mode == 'prepare':
        if not args.release_id or not args.bundle_sha256:
            parser.error('prepare requires --release-id and --bundle-sha256')
        manifest = prepare(args.release_id, args.bundle_sha256, args.version, args.directory)
    elif args.mode == 'publish':
        if not args.submission_report:
            parser.error('publish requires --submission-report')
        manifest = publish(args.directory, args.version, args.submission_report)
    else:
        manifest = verify(args.directory, args.version)
        publisher.preflight(args.directory, manifest)
    if summary := os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(summary, 'a', encoding='utf-8') as stream:
            stream.write(f"## {args.mode}: {manifest['version']}\n\n"
                         f"Tested package source: `{manifest['sourceCommit']}`. "
                         f"{len(manifest['packageIds'])} packages and symbol archives.\n\n"
                         'Tests were performed locally; this workflow only verifies the recorded evidence and publishes.\n')
    print(f"PASS {args.mode}: {manifest['version']} from {manifest['sourceCommit']}")


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))
