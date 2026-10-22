"""Validate an assembled feed before a sample restores from it."""
import json
from pathlib import Path

from package_audit import audit_package
from uno_package_matrix import PLATFORMS, COMPATIBILITY_PLATFORMS, validate_release_targets


def validate_candidate(folder, source_commit):
    folder = Path(folder).resolve()
    manifest = json.loads((folder / 'package-audit.json').read_text())
    if manifest.get('sourceCommit') != source_commit:
        raise ValueError('Candidate and sample source commits differ')
    expected = set(PLATFORMS + (COMPATIBILITY_PLATFORMS if manifest.get('includesExistingHosts') else ()))
    if set(manifest.get('inputs', {})) != expected:
        raise ValueError('Candidate must include all required platform inputs')
    actual = [audit_package(p, source_commit) for p in sorted(folder.glob('*.nupkg'))]
    if actual != manifest.get('packages'):
        raise ValueError('Candidate packages differ from their assembly audit')
    if manifest.get('includesExistingHosts'):
        validate_release_targets(actual)
    return actual
