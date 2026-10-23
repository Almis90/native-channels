#!/usr/bin/env python3
"""Prove native libraries come from this build's packages; allow the shared sample UI."""
from pathlib import Path
import hashlib, json, sys
from consumer_projects import verify_project_references
root = Path(__file__).resolve().parent.parent
assets_path = Path(sys.argv[1])
feed = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else root / 'artifacts/packages'
assets = json.loads(assets_path.read_text())
verify_project_references(assets, allow_avalonia_app=True)
count = 0
for identity, library in assets['libraries'].items():
    if library['type'] == 'project':
        continue
    if not identity.lower().startswith(('nativechannels', 'plugin.currency/', 'plugin.audiostreamer/', 'plugin.volumemonitor/', 'plugin.maui.pdfviewer/', 'plugin.avalonia.pdfviewer/', 'plugin.uno.pdfviewer/')):
        continue
    package_id, version = identity.split('/')
    package_dir = next(Path(folder) / library['path'] for folder in assets['packageFolders']
                       if (Path(folder) / library['path']).is_dir())
    metadata = json.loads((package_dir / '.nupkg.metadata').read_text())
    assert Path(metadata['source']).resolve() == feed, metadata
    cached = package_dir / f'{package_id.lower()}.{version}.nupkg'
    local = feed / f'{package_id}.{version}.nupkg'
    assert hashlib.sha256(cached.read_bytes()).digest() == hashlib.sha256(local.read_bytes()).digest(), identity
    count += 1
assert count > 0, 'No NativeChannels packages in consumer'
print(f'PASS package-only consumer: {count} local archives match local packages exactly')
