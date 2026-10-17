#!/usr/bin/env python3
"""Run the framework ARM64 APK on one physical Android device and retain evidence."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def verify(serial, apk, source_commit, adb, output_root):
    if not re.fullmatch(r'[0-9a-fA-F]{40}', source_commit):
        raise ValueError('Supply the full source SHA of the APK build, not the current checkout by assumption.')
    apk = apk.resolve(strict=True)
    with zipfile.ZipFile(apk) as archive:
        if not any(name.startswith('lib/arm64-v8a/') for name in archive.namelist()):
            raise ValueError('The APK has no ARM64 native libraries.')

    def query(*args):
        return subprocess.check_output([str(adb), '-s', serial, *args], text=True, timeout=15).strip()

    if query('get-state') != 'device':
        raise ValueError('The selected device is not connected and authorized.')
    qemu = query('shell', 'getprop', 'ro.kernel.qemu')
    if qemu not in ('', '0') or serial.startswith('emulator-'):
        raise ValueError('Physical-device verification cannot use an emulator.')
    abis = query('shell', 'getprop', 'ro.product.cpu.abilist').split(',')
    if 'arm64-v8a' not in abis:
        raise ValueError('The selected device does not support ARM64.')
    output_root.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix='device-', dir=output_root))
    record = {
        'sourceCommit': source_commit,
        'sourceProvenance': 'Caller-supplied APK build SHA; use the originating Actions run headSha.',
        'apkSha256': hashlib.sha256(apk.read_bytes()).hexdigest(),
        'runtime': 'android-arm64', 'target': 'physical-device',
        'model': query('shell', 'getprop', 'ro.product.model'),
        'androidVersion': query('shell', 'getprop', 'ro.build.version.release'),
        'apiLevel': query('shell', 'getprop', 'ro.build.version.sdk'),
        'startedAt': datetime.now(timezone.utc).isoformat(), 'executed': False,
    }
    # Keep device serials out of the saved report. The explicit serial is used for every ADB call.
    env = {**os.environ, 'ANDROID_HOME': str(adb.resolve().parent.parent)}
    try:
        with (directory / 'verification.log').open('w') as log:
            result = subprocess.run(
                ['bash', str(ROOT / 'eng/verify-android-sample.sh'), serial, 'Release', 'android-arm64', str(apk)],
                env=env, stdout=log, stderr=subprocess.STDOUT, timeout=150)
        record['executed'] = result.returncode == 0
        record['exitCode'] = result.returncode
    except subprocess.TimeoutExpired:
        record['error'] = 'Physical-device verification exceeded 150 seconds.'
    finally:
        record['completedAt'] = datetime.now(timezone.utc).isoformat()
        (directory / 'verification.json').write_text(json.dumps(record, indent=2) + '\n')
    print(f"{'PASS' if record['executed'] else 'FAIL'} physical Android verification: {directory}")
    return record['executed']


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('serial', help='Explicit authorized ADB device serial or wireless address')
    parser.add_argument('--apk', type=Path, default=ROOT / 'samples/Maui/ChannelExplorer/bin/Release/net10.0-android/android-arm64/dev.nativechannels.sample-Signed.apk')
    parser.add_argument('--source-commit', required=True, help='Full source SHA from the APK build run')
    args = parser.parse_args()
    sdk = Path(os.environ.get('ANDROID_HOME') or os.environ.get('ANDROID_SDK_ROOT') or Path.home() / 'Library/Android/sdk')
    raise SystemExit(0 if verify(args.serial, args.apk, args.source_commit, sdk / 'platform-tools/adb',
                                ROOT / 'artifacts/android-devices') else 1)
