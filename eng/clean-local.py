#!/usr/bin/env python3
"""Preview or remove unused consumer caches and optional generated build outputs."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess


def candidates(root, projects, builds=False):
    root = root.resolve()
    artifacts = root / 'artifacts'
    references = {Path(marker.read_text().strip()).resolve()
                  for marker in artifacts.glob('*cache-path.txt')}
    outputs = []
    for project in projects:
        directory = root / Path(project).parent
        assets = directory / 'obj/project.assets.json'
        if assets.exists():
            # Malformed restore state aborts planning before anything is removed.
            references.update(Path(folder).resolve() for folder in
                              json.loads(assets.read_text()).get('packageFolders', {}))
        if builds:
            for name in ('bin', 'obj'):
                output = directory / name
                if output.is_dir() and not output.is_symlink() and output.resolve().is_relative_to(root):
                    outputs.append(output)
    caches = [cache for cache in artifacts.glob('consumer-packages.*')
              if cache.is_dir() and not cache.is_symlink() and cache.resolve() not in references]
    return sorted(set(caches + outputs))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true', help='Remove the listed generated directories')
    parser.add_argument('--builds', action='store_true', help='Also remove bin/obj for tracked projects')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    projects = subprocess.check_output(['git', 'ls-files', '--', '*.csproj'], cwd=root, text=True).splitlines()
    paths = candidates(root, projects, args.builds)
    for path in paths:
        print(('Remove ' if args.apply else 'Would remove ') + str(path.relative_to(root)))
        if args.apply:
            shutil.rmtree(path)
    print(f'{len(paths)} generated directories ' + ('removed.' if args.apply else 'selected; use --apply to remove.'))
    print('Retained current consumer caches, .tools, emulator data, packages, native artifacts, and verification logs.')


if __name__ == '__main__':
    main()
