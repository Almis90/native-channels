#!/usr/bin/env python3
"""Prove a consumer resolves only packages and uses this build's NativeChannels archives."""
from pathlib import Path
import hashlib, json, sys
root = Path(__file__).resolve().parent.parent
assets_path = Path(sys.argv[1])
assets = json.loads(assets_path.read_text())
count = 0
for identity, library in assets['libraries'].items():
    assert library['type'] == 'package', f'Project reference found: {identity}'
    if not identity.lower().startswith(('nativechannels', 'plugin.maui.currency/', 'plugin.maui.audiostreamer/', 'plugin.maui.volumemonitor/', 'plugin.maui.pdfviewer/')):
        continue
    package_id, version = identity.split('/')
    package_dir = next(Path(folder) / library['path'] for folder in assets['packageFolders']
                       if (Path(folder) / library['path']).is_dir())
    metadata = json.loads((package_dir / '.nupkg.metadata').read_text())
    assert Path(metadata['source']).resolve() == root / 'artifacts/packages', metadata
    cached = package_dir / f'{package_id.lower()}.{version}.nupkg'
    local = root / 'artifacts/packages' / f'{package_id}.{version}.nupkg'
    assert hashlib.sha256(cached.read_bytes()).digest() == hashlib.sha256(local.read_bytes()).digest(), identity
    count += 1
assert count > 0, 'No NativeChannels packages in consumer'
print(f'PASS package-only consumer: {count} local archives match local packages exactly')
