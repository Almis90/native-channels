"""Validate an assembled feed before a sample restores from it."""
import json
from pathlib import Path

from package_audit import audit_package


def validate_candidate(folder, source_commit):
    folder = Path(folder).resolve()
    manifest = json.loads((folder / 'package-audit.json').read_text())
    if manifest.get('sourceCommit') != source_commit:
        raise ValueError('Candidate and sample source commits differ')
    if set(manifest.get('inputs', {})) != {'windows', 'linux', 'macos', 'android', 'ios', 'browser'}:
        raise ValueError('Candidate must include all six platform inputs')
    actual = [audit_package(p, source_commit) for p in sorted(folder.glob('*.nupkg'))]
    if actual != manifest.get('packages'):
        raise ValueError('Candidate packages differ from their assembly audit')
    return actual
