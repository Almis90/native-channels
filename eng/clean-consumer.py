#!/usr/bin/env python3
"""Clear one consumer's generated outputs and previous isolated package cache."""
from pathlib import Path
import shutil
import sys

CONSUMERS = {
    'samples/NativeChannels.Sample': 'sample',
    'tests/PackageConsumer': 'consumer',
    'samples/Currency.Sample': 'currency',
    'samples/Currency.Avalonia.Android': 'currency-avalonia',
    'samples/Currency.Avalonia.iOS': 'currency-avalonia-ios',
    'samples/Currency.Avalonia.Windows': 'currency-avalonia-windows',
    'samples/AudioStreamer.Sample': 'audio',
    'samples/VolumeMonitor.Sample': 'volume',
    'samples/PdfViewer.Sample': 'pdf',
}


def clean(root, relative):
    if relative not in CONSUMERS:
        raise ValueError('Unknown package consumer')
    root = root.resolve()
    artifacts = root / 'artifacts'
    marker = artifacts / (CONSUMERS[relative] + '-cache-path.txt')
    previous = None
    if marker.exists():
        previous = Path(marker.read_text().strip())
        if (previous.is_symlink() or previous.parent.resolve() != artifacts.resolve()
                or not previous.name.startswith('consumer-packages.')):
            raise ValueError('Refusing to remove a package cache outside artifacts/consumer-packages.*')
    outputs = [root / relative / name for name in ('bin', 'obj')]
    if any(output.is_symlink() or not output.resolve().is_relative_to(root) for output in outputs):
        raise ValueError('Refusing to remove symlinked or external consumer outputs')
    # Another consumer may explicitly share this cache; preserve that reference.
    shared = previous is not None and any(
        other != marker and Path(other.read_text().strip()).resolve() == previous.resolve()
        for other in artifacts.glob('*cache-path.txt')
    )
    for output in outputs:
        if output.exists():
            shutil.rmtree(output)
    if previous is not None and previous.exists() and not shared:
        shutil.rmtree(previous)
    marker.unlink(missing_ok=True)


if __name__ == '__main__':
    clean(Path(__file__).resolve().parent.parent, sys.argv[1])
