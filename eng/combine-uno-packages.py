#!/usr/bin/env python3
"""Assemble same-commit Uno platform feeds; never publish or overwrite inputs."""
import argparse
import importlib.util
import json
from pathlib import Path
import tempfile
import zipfile

# Reuse the existing strict ZIP/XML merger without changing the mobile/Windows tool.
_shared_spec = importlib.util.spec_from_file_location('combine_packages', Path(__file__).with_name('combine-packages.py'))
_shared = importlib.util.module_from_spec(_shared_spec)
_shared_spec.loader.exec_module(_shared)
merge_content_types = _shared.merge_content_types
merge_manifest = _shared.merge_manifest
read_archive = _shared.read_archive
serialize = _shared.serialize
xml = _shared.xml
from package_audit import audit_package
from uno_package_matrix import PLATFORMS, COMPATIBILITY_PLATFORMS, PACKAGES, expected_packages, validate_release_targets


def merge_payload(left, right, package_id):
    """The Windows feed owns portable net10.0 binaries and their dependency groups.

    Other desktop feeds contribute native assets. Mobile/browser feeds contribute
    distinct target frameworks. All resulting desktop apps must test the canonical
    DLL; independent platform smoke results are not candidate acceptance.
    """
    spec = package_id + '.nuspec'
    incoming, namespace = xml(right[spec])
    original, original_namespace = xml(left[spec])
    content_files = {}
    for manifest in (original, incoming):
        metadata = manifest.find('metadata')
        content = metadata.find('contentFiles')
        if content is not None:
            for entry in content:
                key = (entry.get('include'), entry.get('exclude'))
                if key in content_files and _shared.signature(content_files[key]) != _shared.signature(entry):
                    raise ValueError(f'{package_id}: conflicting contentFiles entry {key}')
                content_files[key] = entry
            metadata.remove(content)
    for section in ('dependencies', 'frameworkReferences'):
        groups = incoming.find('metadata/' + section)
        if groups is not None:
            for group in list(groups):
                if group.get('targetFramework') == 'net10.0':
                    groups.remove(group)
    result = dict(left)
    merged_manifest, merged_namespace = xml(merge_manifest(
        serialize(original, original_namespace), serialize(incoming, namespace)))
    if content_files:
        import xml.etree.ElementTree as ET
        content = ET.SubElement(merged_manifest.find('metadata'), 'contentFiles')
        content.extend(content_files.values())
    result[spec] = serialize(merged_manifest, merged_namespace)
    result['[Content_Types].xml'] = merge_content_types(left['[Content_Types].xml'], right['[Content_Types].xml'])
    for name, data in right.items():
        if (name in (spec, '[Content_Types].xml', '_rels/.rels')
                or name.startswith(('package/services/metadata/', 'lib/net10.0/'))):
            continue
        if name in result and result[name] != data:
            if name.endswith(('.md', '.targets', '.props')) and result[name].replace(b'\r\n', b'\n') == data.replace(b'\r\n', b'\n'):
                continue
            raise ValueError(f'{package_id}: conflicting payload {name}')
        result[name] = data
    return result


def write_archive(path, entries):
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(entries):
            info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, entries[name])


def assemble(inputs, output, commit, include_existing_hosts=False, frameworks_only=False):
    platforms = PLATFORMS + (COMPATIBILITY_PLATFORMS if include_existing_hosts else ())
    if set(inputs) != set(platforms):
        raise ValueError(f'Provide all named platform feeds: {platforms}')
    if output.exists():
        raise ValueError('Output must be a new directory')
    records = {}
    for platform, folder in inputs.items():
        audited = [audit_package(p, commit) for p in sorted(folder.glob('*.nupkg'))]
        records[platform] = {p['id']: p for p in audited}
        expected = expected_packages(platform, include_existing_hosts, frameworks_only)
        if set(records[platform]) != expected or len(audited) != len(expected):
            raise ValueError(f'{platform}: missing, duplicate or unexpected packages')
    versions = {p['version'] for feed in records.values() for p in feed.values()}
    if len(versions) != 1:
        raise ValueError('All inputs must use the same version')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        stage = Path(temporary) / 'feed'
        stage.mkdir()
        for package_id in sorted(set().union(*(set(feed) for feed in records.values()))):
            sources = [(inputs[platform] / records[platform][package_id]['file'])
                       for platform in platforms if package_id in records[platform]]
            for symbols in (False, True):
                archives = [p.with_suffix('.snupkg') if symbols else p for p in sources]
                merged = read_archive(archives[0])
                for archive in archives:
                    entries = read_archive(archive)
                    metadata, _ = xml(entries[package_id + '.nuspec'])
                    repository = metadata.find('metadata/repository')
                    if (metadata.findtext('metadata/id') != package_id
                            or metadata.findtext('metadata/version') not in versions
                            or repository is None or repository.get('commit') != commit):
                        raise ValueError(f'{archive}: symbol/binary identity mismatch')
                for archive in archives[1:]:
                    merged = merge_payload(merged, read_archive(archive), package_id)
                write_archive(stage / archives[0].name, merged)
        audited = [audit_package(p, commit) for p in sorted(stage.glob('*.nupkg'))]
        if include_existing_hosts:
            validate_release_targets(audited, frameworks_only)
        report = {'sourceCommit': commit, 'canonicalDesktop': 'windows', 'inputs': records,
                  'packages': audited, 'runtimeAcceptance': 'pending',
                  'includesExistingHosts': include_existing_hosts}
        if frameworks_only:
            report['frameworksOnly'] = True
        (stage / 'package-audit.json').write_text(json.dumps(report, indent=2) + '\n')
        stage.rename(output)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for platform in PLATFORMS:
        parser.add_argument('--' + platform, required=True, type=Path)
    for platform in COMPATIBILITY_PLATFORMS:
        parser.add_argument('--' + platform, type=Path)
    parser.add_argument('--include-existing-hosts', action='store_true',
                        help='Require MAUI/Avalonia packages and their existing target frameworks')
    parser.add_argument('--frameworks-only', action='store_true', help='Exclude reference plugin packages')
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--source-commit', required=True)
    args = parser.parse_args()
    platforms = PLATFORMS + COMPATIBILITY_PLATFORMS
    report = assemble({name: getattr(args, name.replace('-', '_')).resolve() for name in platforms
                       if getattr(args, name.replace('-', '_')) is not None},
                      args.output.resolve(), args.source_commit, args.include_existing_hosts, args.frameworks_only)
    print(f"Assembled {len(report['packages'])} packages; runtime acceptance remains pending.")


if __name__ == '__main__':
    main()
