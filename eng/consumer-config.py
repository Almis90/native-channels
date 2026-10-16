#!/usr/bin/env python3
"""Map NativeChannels packages exclusively to this checkout's local package feed."""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parent.parent
configuration = ET.Element('configuration')
sources = ET.SubElement(configuration, 'packageSources')
ET.SubElement(sources, 'clear')
for key, value in [('local', root / 'artifacts/packages'), ('dependencies', root / '.tools/dependency-feed'), ('nuget', 'https://api.nuget.org/v3/index.json')]:
    ET.SubElement(sources, 'add', key=key, value=str(value))
mapping = ET.SubElement(configuration, 'packageSourceMapping')
ET.SubElement(mapping, 'clear')
for key, pattern in [('local', 'NativeChannels*'), ('dependencies', '*'), ('nuget', '*')]:
    source = ET.SubElement(mapping, 'packageSource', key=key)
    ET.SubElement(source, 'package', pattern=pattern)
    if key == 'local':
        ET.SubElement(source, 'package', pattern='Plugin.Maui.Currency')
        ET.SubElement(source, 'package', pattern='Plugin.Maui.AudioStreamer')
        ET.SubElement(source, 'package', pattern='Plugin.Maui.VolumeMonitor')
        ET.SubElement(source, 'package', pattern='Plugin.Maui.PdfViewer')
ET.ElementTree(configuration).write(sys.argv[1], encoding='utf-8', xml_declaration=True)
