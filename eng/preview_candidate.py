"""Local 0.2 framework candidate assembly. Does not publish or invoke workflows."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from package_audit import audit_package, audit_android_scope, audit_platform_scope

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('preview_merge', ROOT / 'eng/combine-packages.py')
merge = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(merge)
PLATFORMS = {'android', 'ios', 'maccatalyst', 'macos', 'windows', 'linux', 'browser'}
TARGETS = {
    'NativeChannels.Core': {'portable'},
    'NativeChannels.Avalonia': {'portable'},
    'NativeChannels': PLATFORMS,
    'NativeChannels.Maui': {'android', 'ios', 'maccatalyst', 'windows'},
    'NativeChannels.Native.Android': {'android'},
    'NativeChannels.Native.Apple': {'ios', 'maccatalyst', 'macos'},
}
MATRIX = {('avalonia', p) for p in PLATFORMS - {'maccatalyst'}} | {
    ('maui', p) for p in TARGETS['NativeChannels.Maui']}


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def target(tfm, package_id):
    if tfm == 'net10.0':
        return 'portable' if package_id in {'NativeChannels.Core', 'NativeChannels.Avalonia'} else 'linux'
    return next((p for p in PLATFORMS if re.fullmatch(r'net10\.0-' + p + r'(?:\d+(?:\.\d+)*)?', tfm)), None)


def inspect(path, package_id, version, source, expected):
    entries = merge.read_archive(path)
    spec, _ = merge.xml(entries[f'{package_id}.nuspec'])
    metadata = spec.find('metadata')
    repo = metadata.find('repository')
    if (metadata.findtext('id') != package_id or metadata.findtext('version') != version
            or repo is None or repo.get('commit') != source
            or repo.get('url') != 'https://github.com/Almis90/native-channels'):
        raise ValueError(f'{path.name}: identity or source mismatch')
    symbols = path.suffix == '.snupkg'
    extension = 'pdb' if symbols else 'dll'
    tfms = {name.split('/')[1] for name in entries
            if name.startswith('lib/') and name.endswith(f'/{package_id}.{extension}')}
    if len(tfms) != len(expected) or {target(tfm, package_id) for tfm in tfms} != expected:
        raise ValueError(f'{path.name}: missing or unexpected target frameworks: {sorted(tfms)}')
    groups = metadata.findall('dependencies/group')
    if len(groups) != len(tfms) or {g.get('targetFramework') for g in groups} != tfms:
        raise ValueError(f'{path.name}: dependency groups do not match payloads')
    for group in groups:
        platform = target(group.get('targetFramework'), package_id)
        for dep in group:
            name = dep.get('id', '')
            if name.startswith('NativeChannels.Native.') and name != {
                'android': 'NativeChannels.Native.Android', 'ios': 'NativeChannels.Native.Apple',
                'macos': 'NativeChannels.Native.Apple', 'maccatalyst': 'NativeChannels.Native.Apple',
            }.get(platform):
                raise ValueError(f'{path.name}: binding dependency on the wrong platform')
            if package_id in {'NativeChannels.Core', 'NativeChannels.Avalonia', 'NativeChannels'} and (
                    name == 'NativeChannels.Maui' or name.startswith('Microsoft.Maui')):
                raise ValueError(f'{path.name}: MAUI dependency in framework-neutral package')
    if symbols:
        if any(data[:4] != b'BSJB' for name, data in entries.items() if name.endswith('.pdb')):
            raise ValueError(f'{path.name}: invalid Portable PDB')
    else:
        audit_package(path, source)
        if package_id == 'NativeChannels' and 'browser' in expected:
            if any(not entries.get(name) for name in ('browser/nativechannels.mjs', 'browser/json-codec.mjs')):
                raise ValueError('Missing browser bridge assets')
    return entries


def merge_archives(inputs, output):
    merged = None
    for path in inputs:
        contents = merge.read_archive(path)
        if merged is None:
            merged = contents
            continue
        spec = next(n for n in merged if n.endswith('.nuspec'))
        merged[spec] = merge.merge_manifest(merged[spec], contents[spec])
        merged['[Content_Types].xml'] = merge.merge_content_types(merged['[Content_Types].xml'], contents['[Content_Types].xml'])
        for name, content in contents.items():
            if name in (spec, '[Content_Types].xml', '_rels/.rels') or name.startswith('package/services/metadata/'):
                continue
            if name in merged and merged[name] != content:
                if name.endswith(('.md', '.targets')) and merged[name].replace(b'\r\n', b'\n') == content.replace(b'\r\n', b'\n'):
                    continue
                raise ValueError(f'{output.name}: conflicting shared payload {name}')
            merged[name] = content
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, content in sorted(merged.items()):
            entry = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, content)


def assemble(manifests, output, source, version):
    if not re.fullmatch('[0-9a-f]{40}', source):
        raise ValueError('A full source commit is required')
    if output.exists() and any(output.iterdir()):
        raise ValueError('Output must be empty')
    builds = {}
    provenance = []
    for path in manifests:
        data = json.loads(path.read_text(encoding='utf-8'))
        key = (data['framework'], data['platform'])
        if key not in MATRIX or key in builds:
            raise ValueError(f'Duplicate or unexpected build: {key}')
        identity = data.get('source', {})
        if identity.get('commit') != source or identity.get('status') != '' or not identity.get('sourceSha256'):
            raise ValueError(f'{path}: build must record clean source at the candidate commit')
        builds[key] = (path.parent / 'packages', data.get('packageFiles', {}))
        provenance.append(dict(framework=key[0], platform=key[1], source=identity,
                               buildManifestSha256=digest(path)))
    if set(builds) != MATRIX:
        raise ValueError(f'Missing builds: {sorted(MATRIX - set(builds))}')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        stage = Path(temporary)
        records = []
        for package_id, platforms in sorted(TARGETS.items()):
            inputs = []
            # Portable packages are taken once from the Windows Avalonia build.
            # MAUI assemblies come from MAUI builds; platform assemblies from Avalonia,
            # except Catalyst, which has no Avalonia head.
            for platform in sorted(platforms):
                key = ('avalonia', 'windows') if platform == 'portable' else (
                    'maui' if package_id == 'NativeChannels.Maui' or platform == 'maccatalyst' else 'avalonia', platform)
                feed, hashes = builds[key]
                binary = feed / f'{package_id}.{version}.nupkg'
                for suffix in ('nupkg', 'snupkg'):
                    path = binary.with_suffix('.' + suffix)
                    if path.name not in hashes or digest(path) != hashes[path.name]:
                        raise ValueError(f'{path}: missing or changed build archive hash')
                    inspect(path, package_id, version, source, {platform})
                record = audit_package(binary, source)
                if key[1] == 'android':
                    audit_android_scope(binary, record)
                else:
                    audit_platform_scope(binary, record, key[1])
                inputs.append(binary)
            for suffix in ('nupkg', 'snupkg'):
                destination = stage / f'{package_id}.{version}.{suffix}'
                merge_archives([p.with_suffix('.' + suffix) for p in inputs], destination)
                inspect(destination, package_id, version, source, platforms)
                records.append(dict(file=destination.name, id=package_id, sha256=digest(destination)))
        result = dict(schemaVersion=1, version=version, sourceCommit=source, files=records,
                      builds=provenance, status='assembled; API compatibility and consumer acceptance still required')
        (stage / 'preview-manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
        output.mkdir(exist_ok=True)
        for path in stage.iterdir():
            path.replace(output / path.name)
    return verify_candidate(output, source)


def verify_candidate(directory, source):
    result = json.loads((directory / 'preview-manifest.json').read_text(encoding='utf-8'))
    if result.get('schemaVersion') != 1 or result.get('sourceCommit') != source:
        raise ValueError('Candidate belongs to another source commit')
    version = result['version']
    if not re.fullmatch(r'\d+\.\d+\.\d+-preview\.\d+', version):
        raise ValueError('Expected an exact preview package version')
    expected = {f'{name}.{version}.{suffix}': name for name in TARGETS for suffix in ('nupkg', 'snupkg')}
    files = result['files']
    if len(files) != len(expected) or {r['file'] for r in files} != set(expected):
        raise ValueError('Candidate must contain all six framework packages and symbols')
    if {p.name for p in directory.iterdir()} != set(expected) | {'preview-manifest.json'}:
        raise ValueError('Unexpected candidate files')
    for record in files:
        name = expected[record['file']]
        path = directory / record['file']
        if path.is_symlink() or record.get('id') != name or digest(path) != record['sha256']:
            raise ValueError(f'{path.name}: candidate bytes changed')
        inspect(path, name, version, source, TARGETS[name])
    return result


def accept(directory, summaries, api_report, source):
    candidate = verify_candidate(directory, source)
    api = json.loads(api_report.read_text(encoding='utf-8'))
    expected_api = set(TARGETS) - {'NativeChannels.Avalonia'}  # New package: no published API baseline.
    api_rows = api.get('packages', [])
    if (api.get('success') is not True or api.get('scope') != 'combined'
            or api.get('candidateVersion') != candidate['version']
            or len(api_rows) != len(expected_api) or {r['id'] for r in api_rows} != expected_api):
        raise ValueError('Complete combined API compatibility report required')
    for row in api_rows:
        if row.get('compatible') is not True or row.get('candidateSha256') != digest(
                directory / f'{row["id"]}.{candidate["version"]}.nupkg'):
            raise ValueError('API report refers to other package bytes')
    seen = set()
    evidence = []
    for path in summaries:
        report = json.loads(path.read_text(encoding='utf-8'))
        key = (report.get('framework'), report.get('platform'))
        if key not in MATRIX or key in seen:
            raise ValueError(f'Duplicate or unexpected consumer suite: {key}')
        seen.add(key)
        for field in ('buildSource', 'currentSource'):
            identity = report.get(field) or {}
            if identity.get('commit') != source or identity.get('status') != '':
                raise ValueError('Consumer must build and execute from the clean candidate commit')
        if report.get('frameworkCandidate') != candidate:
            raise ValueError('Consumer did not use these exact combined candidate archives')
        expected = {'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer', 'Interop'}
        if key[1] == 'linux':
            expected = expected - {'PdfViewer'} | {'PdfViewerQt', 'PdfViewerGtk'}
        rows = report.get('results', [])
        if len(rows) != len(expected) or {r['name'] for r in rows} != expected or any(r.get('status') != 'PASS' for r in rows):
            raise ValueError(f'{key}: all five samples (both Linux PDF heads) must pass')
        evidence.append(dict(framework=key[0], platform=key[1], summarySha256=digest(path)))
    if seen != MATRIX:
        raise ValueError(f'Missing consumer suites: {sorted(MATRIX - seen)}')
    return dict(sourceCommit=source, version=candidate['version'], success=True,
                candidateManifestSha256=digest(directory / 'preview-manifest.json'),
                apiReportSha256=digest(api_report), consumers=evidence,
                scope='Local preview package acceptance; no publication or workflow acceptance')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, action='append',
                        help='One clean build manifest per Avalonia/MAUI target; repeat for all ten')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--accept', type=Path, help='Existing candidate to accept against exact-package API and runtime results')
    parser.add_argument('--summary', type=Path, action='append')
    parser.add_argument('--api-report', type=Path)
    args = parser.parse_args()
    if args.accept:
        if args.manifest or not args.summary or not args.api_report:
            parser.error('--accept requires --summary and --api-report, without --manifest')
        if args.output.exists():
            parser.error('Acceptance output already exists')
        result = accept(args.accept, args.summary, args.api_report, args.source_commit)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
        print('PASS local preview candidate acceptance; publication remains a separate action')
        return
    if not args.manifest or args.summary or args.api_report:
        parser.error('Assembly requires --manifest; acceptance requires --accept')
    version = ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version')
    result = assemble(args.manifest, args.output, args.source_commit, version)
    print(f'PASS assembled {len(result["files"])} archives; API and runtime acceptance remain pending')


if __name__ == '__main__':
    main()
