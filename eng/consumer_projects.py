"""Allow the template's shared application project while keeping libraries package-only."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AVALONIA_APP = ROOT / 'samples/Avalonia/Currency/Currency/Currency.csproj'
AVALONIA_HEADS = {
    ROOT / f'samples/Avalonia/Currency/Currency.{head}/Currency.{head}.csproj'
    for head in ('Android', 'iOS', 'Desktop', 'Browser')
}


def verify_project_references(assets, allow_avalonia_app=False):
    project = assets.get('project', {}).get('restore', {}).get('projectPath', '')
    consumer = Path(project) if project else None
    for identity, library in assets['libraries'].items():
        if library['type'] == 'package':
            continue
        if (library['type'] != 'project' or not allow_avalonia_app or consumer is None
                or not consumer.is_absolute() or consumer.resolve() not in AVALONIA_HEADS):
            raise ValueError(f'Unexpected project reference: {identity}')
        paths = [library.get(key) for key in ('path', 'msbuildProject')]
        if not all(path and (consumer.parent / path).resolve() == AVALONIA_APP for path in paths):
            raise ValueError(f'Unexpected project reference: {identity}')
