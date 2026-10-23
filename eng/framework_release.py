"""Exact-archive acceptance for the seven-package 0.3 framework preview.

Interop exercises channels and native views on each supported framework/platform.
Reference plugins are neither shipped nor claimed as part of this acceptance.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import tempfile

from uno_candidate import validate_candidate

ROOT = Path(__file__).resolve().parents[1]
KIND = 'framework-preview-release'
PACKAGE_IDS = ('NativeChannels.Core', 'NativeChannels.Native.Android', 'NativeChannels.Native.Apple',
               'NativeChannels', 'NativeChannels.Maui', 'NativeChannels.Avalonia', 'NativeChannels.Uno')
MATRIX = ({('maui', p) for p in ('windows', 'android', 'ios', 'maccatalyst')}
          | {(f, p) for f in ('avalonia', 'uno')
             for p in ('windows', 'android', 'ios', 'macos', 'linux', 'browser')})


def read(path):
    return json.loads(path.read_text(encoding='utf-8'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def hashes(directory):
    paths = list(directory.rglob('*'))
    if any(p.is_symlink() for p in paths):
        raise ValueError('Release must not contain symbolic links')
    return {p.relative_to(directory).as_posix(): digest(p) for p in sorted(paths)
            if p.is_file() and p != directory / 'release-manifest.json'}


def inspect_archives(packages, version, source):
    spec = importlib.util.spec_from_file_location('release_archive', ROOT / '.github/scripts/nuget-release.py')
    archive = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(archive)
    for name in PACKAGE_IDS:
        for suffix in ('nupkg', 'snupkg'):
            archive.inspect_archive(packages / f'{name}.{version}.{suffix}', name, version, source)


def accept(packages, api_path, reports):
    audit = read(packages / 'package-audit.json')
    source = audit.get('sourceCommit', '')
    if not re.fullmatch('[a-f0-9]{40}', source):
        raise ValueError('Full source commit required')
    if audit.get('frameworksOnly') is not True or audit.get('includesExistingHosts') is not True:
        raise ValueError('Complete framework-only candidate required')
    rows = validate_candidate(packages, source)
    if {r['id'] for r in rows} != set(PACKAGE_IDS) or len(rows) != len(PACKAGE_IDS):
        raise ValueError('Exactly seven framework packages required')
    versions = {r['version'] for r in rows}
    if len(versions) != 1 or not re.fullmatch(r'0\.3\.0-preview\.[1-9][0-9]*', next(iter(versions))):
        raise ValueError('Expected a single 0.3 preview version')
    version = versions.pop()
    inspect_archives(packages, version, source)
    package_hashes = {r['file']: digest(packages / r['file']) for r in rows}
    baseline = read(ROOT / 'eng/api-baseline-02.json')
    api = read(api_path)
    api_rows = api.get('packages', [])
    if (api.get('success') is not True or api.get('scope') != 'combined'
            or api.get('candidateVersion') != version or api.get('baselineVersion') != baseline['version']
            or len(api_rows) != len(baseline['packages'])
            or {r['id'] for r in api_rows} != set(baseline['packages'])):
        raise ValueError('Complete API comparison against 0.2 is required')
    for row in api_rows:
        if (row.get('compatible') is not True
                or row.get('baselineSha256') != baseline['packages'][row['id']]
                or row.get('candidateSha256') != package_hashes[f"{row['id']}.{version}.nupkg"]):
            raise ValueError('API evidence does not match accepted archives')
    consumers = {}
    for path in reports:
        report = read(path)
        key = (report.get('framework'), report.get('platform'))
        if key not in MATRIX or key in consumers:
            raise ValueError('Unexpected or duplicate Interop consumer')
        result = report.get('result', '')
        if (report.get('success') is not True or report.get('scenario') != 'Interop'
                or report.get('sourceCommit') != source or report.get('packages') != package_hashes
                or not re.fullmatch('[a-f0-9]{64}', report.get('assetsSha256', ''))
                or not result.startswith('PASS ')
                or not all(marker in result for marker in ('typed calls, DTOs', 'view stress: 32 reconnects'))):
            raise ValueError('Interop evidence must pass both shared verifiers against these exact archives')
        consumers[key] = dict(framework=key[0], platform=key[1], reportSha256=digest(path))
    if set(consumers) != MATRIX:
        raise ValueError(f'Missing Interop consumers: {sorted(MATRIX - set(consumers))}')
    return dict(sourceCommit=source, version=version, success=True,
                archives={f'{p}.{version}.{s}': digest(packages / f'{p}.{version}.{s}')
                          for p in PACKAGE_IDS for s in ('nupkg', 'snupkg')},
                candidateManifestSha256=digest(packages / 'package-audit.json'), apiReportSha256=digest(api_path),
                consumers=[consumers[k] for k in sorted(consumers)],
                scope='Seven framework packages; existing Interop channel and native-view suites on 16 targets. '
                      'Reference plugin samples are excluded.')


def verify(directory):
    manifest = read(directory / 'release-manifest.json')
    version = manifest.get('version', '')
    if not re.fullmatch(r'0\.3\.0-preview\.[1-9][0-9]*', version):
        raise ValueError('Invalid framework preview version')
    expected = {'candidate/package-audit.json', 'evidence/acceptance.json', 'evidence/api.json'}
    expected.update(f'evidence/{f}-{p}.json' for f, p in MATRIX)
    expected.update(f'candidate/{p}.{version}.{s}' for p in PACKAGE_IDS for s in ('nupkg', 'snupkg'))
    if (manifest.get('schemaVersion') != 1 or manifest.get('kind') != KIND
            or manifest.get('packageIds') != list(PACKAGE_IDS)
            or set(manifest.get('files', {})) != expected or hashes(directory) != manifest['files']):
        raise ValueError('Incomplete or changed framework release files')
    receipt = accept(directory / 'candidate', directory / 'evidence/api.json',
                     [directory / f'evidence/{f}-{p}.json' for f, p in sorted(MATRIX)])
    if (receipt != read(directory / 'evidence/acceptance.json')
            or receipt['version'] != version or receipt['sourceCommit'] != manifest.get('sourceCommit')):
        raise ValueError('Release identity or acceptance differs')
    return manifest


def prepare(packages, api, reports, output):
    receipt = accept(packages, api, reports)
    if output.exists():
        raise ValueError('Use a new immutable release directory')
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
        stage = Path(temporary) / 'release'
        shutil.copytree(packages, stage / 'candidate')
        evidence = stage / 'evidence'
        evidence.mkdir()
        shutil.copyfile(api, evidence / 'api.json')
        write(evidence / 'acceptance.json', receipt)
        for path in reports:
            report = read(path)
            shutil.copyfile(path, evidence / f"{report['framework']}-{report['platform']}.json")
        manifest = dict(schemaVersion=1, kind=KIND, version=receipt['version'],
                        sourceCommit=receipt['sourceCommit'], packageIds=list(PACKAGE_IDS), files=hashes(stage))
        write(stage / 'release-manifest.json', manifest)
        verify(stage)
        stage.rename(output)
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--api', type=Path, required=True)
    parser.add_argument('--report', type=Path, action='append', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    prepare(args.packages, args.api, args.report, args.output)
    print('PASS prepared seven framework packages and symbols')
