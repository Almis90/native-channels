#!/usr/bin/env python3
"""Clear one consumer's generated outputs and previous isolated package cache."""
from pathlib import Path
import shutil
import sys

CONSUMERS = {
    'samples/NativeChannels.Sample': 'sample',
    'tests/PackageConsumer': 'consumer',
    'samples/Currency.Sample': 'currency',
    'samples/Avalonia/Currency/Currency.Android': 'currency-avalonia',
    'samples/Avalonia/Currency/Currency.iOS': 'currency-avalonia-ios',
    'samples/Avalonia/Currency/Currency.Desktop': 'currency-avalonia-macos',
    'samples/Avalonia/Currency/Currency.Browser': 'currency-avalonia-browser',
    'samples/AudioStreamer.Sample': 'audio',
    'samples/Avalonia/AudioStreamer': 'audio-avalonia-windows',
    'samples/VolumeMonitor.Sample': 'volume',
    'samples/PdfViewer.Sample': 'pdf',
}


def clean(root, relative, cache_marker=None):
    if relative not in CONSUMERS:
        raise ValueError('Unknown package consumer')
    if cache_marker is not None and (relative != 'samples/Avalonia/Currency/Currency.Desktop'
                                     or cache_marker not in {'currency-avalonia-macos', 'currency-avalonia-linux'}):
        raise ValueError('Unknown desktop cache marker')
    root = root.resolve()
    artifacts = root / 'artifacts'
    marker = artifacts / ((cache_marker or CONSUMERS[relative]) + '-cache-path.txt')
    previous = None
    if marker.exists():
        previous = Path(marker.read_text().strip())
        if (previous.is_symlink() or previous.parent.resolve() != artifacts.resolve()
                or not previous.name.startswith('consumer-packages.')):
            raise ValueError('Refusing to remove a package cache outside artifacts/consumer-packages.*')
    outputs = [root / relative / name for name in ('bin', 'obj')]
    if relative.startswith('samples/Avalonia/Currency/'):
        outputs += [root / 'samples/Avalonia/Currency/Currency' / name for name in ('bin', 'obj')]
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
    clean(Path(__file__).resolve().parent.parent, sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
