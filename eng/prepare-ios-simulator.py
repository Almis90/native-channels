#!/usr/bin/env python3
"""Prepare one iPhone simulator, with a separate workflow deadline for boot."""
import json
import os
from pathlib import Path
import subprocess


def prepare():
    inventory = json.loads(subprocess.check_output(
        ['xcrun', 'simctl', 'list', 'devices', 'available', '-j'], text=True))
    device = next((device for devices in inventory['devices'].values() for device in devices
                   if 'iPhone' in device['name'] and device.get('isAvailable', True)), None)
    if device is None:
        raise RuntimeError('No available iPhone simulator. Install an iOS simulator runtime for the selected Xcode.')
    # Emit before boot so a failed or timed-out boot still has a cleanup target.
    with Path(os.environ['GITHUB_OUTPUT']).open('a') as output:
        output.write(f'device={device["udid"]}\n')
    print(f'Preparing {device["name"]} ({device["udid"]})', flush=True)
    # -b boots if needed; unlike `boot || true`, genuine failures propagate.
    subprocess.run(['xcrun', 'simctl', 'bootstatus', device['udid'], '-b', '-d'], check=True)


if __name__ == '__main__':
    prepare()
