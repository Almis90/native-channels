#!/usr/bin/env python3
"""Run the packaged Linux AudioStreamer sample locally; never dispatch workflows."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import uuid
import zipfile

from package_audit import audit_package, audit_platform_scope

ROOT = Path(__file__).resolve().parents[1]


def check_result(log, result, exit_code):
    if (exit_code != 0 or not result.startswith('PASS ')
            or 'host recreation, controls and minimize/restart' not in result
            or log.splitlines().count('AUDIO_AVALONIA_CLOSED') != 1
            or 'AUDIO_AVALONIA FAIL' in log):
        raise ValueError(f'Audio verification or orderly shutdown failed (exit {exit_code}): {result}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--app-directory', type=Path, default=ROOT / 'artifacts/audio-avalonia/linux/app')
    args = parser.parse_args()
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        parser.error('This verifier currently targets Linux x64.')
    if not os.environ.get('DISPLAY'):
        parser.error('An X11 desktop or Xvfb display with a window manager is required.')
    app = args.app_directory.resolve()
    output = ROOT / 'artifacts/audio-avalonia/linux' / ('run-' + uuid.uuid4().hex)
    output.mkdir(parents=True)
    result_path = output / 'result.txt'
    environment = dict(os.environ, AUDIO_AVALONIA_RESULT=str(result_path))
    with (output / 'stdout.log').open('w') as stdout, (output / 'stderr.log').open('w') as stderr:
        process = subprocess.run([str(app / 'AudioStreamer.Linux'), '--verify'], cwd=app, env=environment,
                                 stdout=stdout, stderr=stderr, timeout=45)
    result = result_path.read_text() if result_path.exists() else 'No result written after shutdown'
    check_result((output / 'stdout.log').read_text(), result, process.returncode)
    dependencies = json.loads((app / 'AudioStreamer.Linux.deps.json').read_text())['libraries']
    if any(name.startswith(('NativeChannels.Maui/', 'Microsoft.Maui.', 'Microsoft.WindowsAppSDK/'))
           for name in dependencies):
        raise ValueError('Unexpected UI framework dependency in Linux consumer')
    packages = []
    for identity in ('NativeChannels.Core', 'NativeChannels', 'NativeChannels.Avalonia', 'Plugin.Maui.AudioStreamer'):
        matches = list((ROOT / 'artifacts/packages').glob(identity + '.*.nupkg'))
        matches = [p for p in matches if p.name.removeprefix(identity + '.')[0].isdigit()]
        if len(matches) != 1:
            raise ValueError(f'Expected one local archive for {identity}')
        record = audit_package(matches[0])
        audit_platform_scope(matches[0], record, 'linux')
        with zipfile.ZipFile(matches[0]) as archive:
            assembly = archive.read('lib/net10.0/' + identity + '.dll')
            if identity == 'Plugin.Maui.AudioStreamer':
                if (app / 'libnc_audio.so').read_bytes() != archive.read('runtimes/linux-x64/native/libnc_audio.so'):
                    raise ValueError('Published C++ audio adapter differs from the local archive')
        if (app / (identity + '.dll')).read_bytes() != assembly:
            raise ValueError(f'Published {identity} differs from the local archive')
        packages.append(record)
    alsa = ctypes.CDLL('libasound.so.2')
    alsa.snd_asoundlib_version.restype = ctypes.c_char_p
    sources = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    source_hashes = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
                     for name in sorted(set(sources)) if name and (ROOT / name).is_file()}
    (output / 'source-files.json').write_text(json.dumps(source_hashes, indent=2) + '\n')
    evidence = {
        'os': platform.freedesktop_os_release(), 'kernel': platform.release(), 'architecture': platform.machine(),
        'alsa': alsa.snd_asoundlib_version().decode(), 'display': os.environ['DISPLAY'],
        'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'workingTreeDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT).strip()),
        'exitCode': process.returncode, 'result': result, 'packages': packages,
        'application': {p.relative_to(app).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                        for p in sorted(app.rglob('*')) if p.is_file()},
    }
    (output / 'verification.json').write_text(json.dumps(evidence, indent=2) + '\n')
    print('AUDIO_AVALONIA ' + result)
    print('Evidence: ' + str(output))


if __name__ == '__main__':
    main()
