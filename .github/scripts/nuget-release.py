"""Prepare and verify the exact five framework packages accepted by distribution CI."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tarfile
from pathlib import PurePosixPath
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PACKAGE_IDS = ('NativeChannels.Core', 'NativeChannels.Native.Android', 'NativeChannels.Native.Apple',
               'NativeChannels', 'NativeChannels.Maui')
CONFIGURATIONS = {'android-x64': True, 'android-arm64': False, 'ios-simulator': True,
                  'ios-device': False, 'catalyst': False, 'win-x64': False, 'win-arm64': False}
REPOSITORY = 'https://github.com/Almis90/native-channels'

spec = importlib.util.spec_from_file_location('package_audit', ROOT / 'eng/package_audit.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(chunk)
    return result.hexdigest()


def validate_inputs(source, run_id, version):
    if not re.fullmatch(r'[0-9a-f]{40}', source) or not re.fullmatch(r'[1-9][0-9]*', run_id):
        raise ValueError('A full source SHA and numeric distribution run ID are required')
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?', version):
        raise ValueError('Provide the exact package version, without paths or build metadata')


def validate_evidence(evidence, source, run_id, feed_hash):
    if (evidence.get('sourceCommit') != source or evidence.get('runId') != run_id
            or evidence.get('success') is not True or evidence.get('sameFeed') is not True
            or evidence.get('failedJobs') != [] or evidence.get('unverifiedConsumers') != []):
        raise ValueError('Distribution must have passed at this exact source and run')
    rows = evidence.get('results', [])
    if len(rows) != len(CONFIGURATIONS) or {row.get('configuration') for row in rows} != set(CONFIGURATIONS):
        raise ValueError('Expected exactly seven distinct distribution consumers')
    for row in rows:
        if (row.get('sourceCommit') != source or row.get('packageRun') != run_id
                or row.get('packageSource') != 'Distribution' or row.get('sample') != 'Framework'
                or row.get('feedSha256') != feed_hash or row.get('compiled') is not True
                or row.get('executed') is not CONFIGURATIONS[row['configuration']]):
            raise ValueError('Consumer evidence does not match the candidate feed or required outcomes')


def inspect_archive(path, package_id, version, source):
    with zipfile.ZipFile(path) as archive:
        names = [info.orig_filename for info in archive.infolist()]
        if len(names) != len(set(names)) or archive.testzip() is not None:
            raise ValueError('Invalid or duplicate package entries')
        if any('\\' in name or PurePosixPath(name).is_absolute() or '..' in PurePosixPath(name).parts for name in names):
            raise ValueError('Unsafe package entry')
        metadata = ET.fromstring(archive.read(f'{package_id}.nuspec'))
        for node in metadata.iter():
            node.tag = node.tag.rsplit('}', 1)[-1]
        metadata = metadata.find('metadata')
        if metadata is None:
            raise ValueError('Package metadata is missing')
        repository = metadata.find('repository')
        if (metadata.findtext('id') != package_id or metadata.findtext('version') != version
                or repository is None or repository.get('commit') != source
                or repository.get('url') != REPOSITORY):
            raise ValueError('Package identity, repository or source does not match the release')
        if path.suffix == '.snupkg':
            pdbs = [name for name in names if name.endswith('.pdb')]
            if not pdbs or any(archive.read(name)[:4] != b'BSJB' for name in pdbs):
                raise ValueError('Symbol archive must contain managed Portable PDBs')
            if any(not name.endswith(('.pdb', '.nuspec', '.xml', '.psmdcp', '.rels', '.p7s'))
                   for name in names if not name.endswith('/')):
                raise ValueError('Symbol archive contains files unsupported by NuGet.org')
    if path.suffix == '.nupkg':
        audit.audit_package(path, source)
    return {'file': path.name, 'id': package_id, 'version': version, 'sha256': digest(path)}


def prepare(feed, evidence_path, output, source, run_id, version):
    validate_inputs(source, run_id, version)
    feed_hash = digest(feed)
    evidence = json.loads(evidence_path.read_text())
    validate_evidence(evidence, source, run_id, feed_hash)
    if output.exists() and any(output.iterdir()):
        raise ValueError('Candidate output must be empty; do not mix release artifacts')
    wanted = {f'artifacts/packages/{name}.{version}.{extension}': (name, extension)
              for name in PACKAGE_IDS for extension in ('nupkg', 'snupkg')}
    with tarfile.open(feed) as archive:
        entries = archive.getmembers()
        packages = [entry for entry in entries if entry.name.endswith(('.nupkg', '.snupkg'))]
        if len(packages) != len(wanted) or {entry.name for entry in packages} != set(wanted):
            raise ValueError('Feed must contain exactly five framework packages and their symbols')
        if any(not entry.isfile() for entry in packages):
            raise ValueError('Package entries must be regular files')
        audits = [entry for entry in entries if entry.name == 'artifacts/package-audit.json']
        if len(audits) != 1 or not audits[0].isfile():
            raise ValueError('Missing or duplicate package audit')
        records = json.load(archive.extractfile(audits[0]))['packages']
        if len(records) != len(PACKAGE_IDS) or {p['id'] for p in records} != set(PACKAGE_IDS):
            raise ValueError('Package audit must describe exactly the five framework packages')
        records = {p['id']: p for p in records}
        output.mkdir(parents=True, exist_ok=True)
        manifest = []
        for name, (package_id, extension) in wanted.items():
            path = output / Path(name).name
            # Extract only whitelisted regular files, never paths or links from the archive.
            path.write_bytes(archive.extractfile(name).read())
            item = inspect_archive(path, package_id, version, source)
            if extension == 'nupkg':
                recorded = records[package_id]
                if (any(recorded.get(key) != item[key] for key in ('file', 'id', 'version', 'sha256'))
                        or recorded.get('sourceCommit') != source):
                    raise ValueError('Package bytes or metadata do not match the distribution audit')
            manifest.append(item)
    result = {'sourceCommit': source, 'distributionRun': run_id, 'version': version,
              'feedSha256': feed_hash, 'files': manifest}
    (output / 'release-manifest.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


def verify(directory, source, run_id, version):
    validate_inputs(source, run_id, version)
    result = json.loads((directory / 'release-manifest.json').read_text())
    if (result.get('sourceCommit') != source or result.get('distributionRun') != run_id
            or result.get('version') != version
            or not re.fullmatch(r'[0-9a-f]{64}', result.get('feedSha256', ''))):
        raise ValueError('Candidate manifest belongs to another source, run or version')
    expected = {f'{name}.{version}.{extension}': name for name in PACKAGE_IDS
                for extension in ('nupkg', 'snupkg')}
    files = result.get('files', [])
    if len(files) != len(expected) or {item['file'] for item in files} != set(expected):
        raise ValueError('Candidate must contain exactly the five packages and symbols')
    if {path.name for path in directory.iterdir()} != set(expected) | {'release-manifest.json'}:
        raise ValueError('Unexpected files in candidate directory')
    for item in files:
        path = directory / item['file']
        if path.is_symlink() or inspect_archive(path, expected[path.name], version, source) != item:
            raise ValueError('Candidate bytes or metadata changed after validation')
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'verify'))
    parser.add_argument('--feed', type=Path)
    parser.add_argument('--evidence', type=Path)
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--source', required=True)
    parser.add_argument('--run', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--summary', type=Path)
    args = parser.parse_args()
    pinned = ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version')
    if args.version != pinned:
        parser.error(f'Version must match the checked-out source: {pinned}')
    if args.mode == 'prepare':
        if args.feed is None or args.evidence is None:
            parser.error('prepare requires --feed and --evidence')
        result = prepare(args.feed, args.evidence, args.directory, args.source, args.run, args.version)
    else:
        result = verify(args.directory, args.source, args.run, args.version)
    if args.summary:
        with args.summary.open('a') as summary:
            summary.write(f"## NuGet candidate {args.version}\n\nSource: `{args.source}`\n\n"
                          f"Distribution run: {args.run}\n\nFeed SHA-256: `{result['feedSha256']}`\n\n"
                          '| Archive | SHA-256 |\n| --- | --- |\n')
            for item in result['files']:
                summary.write(f"| {item['file']} | `{item['sha256']}` |\n")
            summary.write('\nCandidate verified; this step does not publish or request NuGet credentials.\n')
    print(f'PASS: five framework packages and symbols verified at {args.source}')


if __name__ == '__main__':
    main()
