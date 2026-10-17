"""Check the public presentation of a built framework NuGet archive."""
import argparse
from pathlib import Path
import struct
import xml.etree.ElementTree as ET
import zipfile


def validate(archive):
    manifests = [name for name in archive.namelist() if name.endswith('.nuspec')]
    if len(manifests) != 1:
        raise ValueError('Expected one package manifest')
    metadata = ET.fromstring(archive.read(manifests[0])).find('{*}metadata')
    if metadata is None:
        raise ValueError('Missing package metadata')
    if metadata.findtext('{*}projectUrl') != 'https://almis90.github.io/native-channels/':
        raise ValueError('Project website must point to the documentation')
    description = metadata.findtext('{*}description', '').strip()
    if len(description) < 80 or description == 'Native platform channels for .NET applications.':
        raise ValueError('Expected a package-specific description')
    if 'https://almis90.github.io/native-channels/changelog' not in metadata.findtext('{*}releaseNotes', ''):
        raise ValueError('Expected release notes and changelog link')
    if metadata.findtext('{*}icon') != 'icon.png':
        raise ValueError('Expected an embedded package icon')
    image = archive.read('icon.png')
    if (len(image) < 24 or len(image) > 1024 * 1024 or image[:8] != b'\x89PNG\r\n\x1a\n'
            or image[12:16] != b'IHDR' or struct.unpack('>II', image[16:24]) != (256, 256)):
        raise ValueError('Expected the 256x256 PNG icon, at most 1 MiB')
    if metadata.findtext('{*}readme') != 'README.md' or not archive.read('README.md').strip():
        raise ValueError('Expected a packaged README')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('packages', nargs='+', type=Path)
    for path in parser.parse_args().packages:
        with zipfile.ZipFile(path) as archive:
            validate(archive)
        print(f'PASS package presentation: {path.name}')
