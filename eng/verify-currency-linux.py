#!/usr/bin/env python3
"""Execute the package-only Linux/X11 Currency app and retain reproducible evidence."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import subprocess
import time
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('shared_hosting', ROOT / 'eng/verify-shared-hosting.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


def check_log(log, returncode):
    if returncode != 0:
        raise ValueError(f'Linux application exited with code {returncode}')
    result = proof.runtime_proof({'avalonia': log}, 'linux')
    if log.splitlines().count('CURRENCY_AVALONIA_CLOSED') != 1:
        raise ValueError('Missing orderly desktop shutdown')
    return result


def source_evidence():
    names = subprocess.check_output(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], cwd=ROOT).decode().split('\0')
    files = {name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
             for name in sorted(set(names)) if name and (ROOT / name).is_file()}
    return files, hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--display-kind', choices=('desktop', 'xvfb'), default='desktop',
                        help='Record whether this is a desktop X11 session or an Xvfb test display')
    parser.add_argument('--interactive-close', action='store_true',
                        help='Keep the window open after checks, then close it with the window manager (requires xdotool and wmctrl)')
    args = parser.parse_args()
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        parser.error('Execute on Linux x64; other architectures require separate verification')
    if not os.environ.get('DISPLAY'):
        parser.error('An X11 display is required (desktop session or xvfb-run)')
    mode = 'window-close' if args.interactive_close else 'verification'
    output = ROOT / 'artifacts/shared-hosting-linux/linux-x64' / mode
    output.mkdir(parents=True, exist_ok=True)
    for name in ('verification.json', 'avalonia-result.txt', 'screenshot.png'):
        (output / name).unlink(missing_ok=True)
    project = ROOT / 'samples/Avalonia/Currency/Currency.Desktop'
    assets = json.loads((project / 'obj/project.assets.json').read_text())
    executable = project / 'bin/Release/net10.0/linux-x64/Currency.Desktop'
    header = executable.read_bytes()[:20]
    if header[:6] != b'\x7fELF\x02\x01' or int.from_bytes(header[18:20], 'little') != 62:
        raise ValueError('Expected a Linux x64 ELF application')
    sources, tree_hash = source_evidence()
    record = {
        'platform': 'linux', 'runtime': 'linux-x64', 'backend': 'X11', 'displayKind': args.display_kind, 'mode': mode,
        'configuration': 'Release', 'os': platform.freedesktop_os_release(), 'kernel': platform.release(),
        'sdk': subprocess.check_output(['bash', 'eng/dotnet.sh', '--version'], cwd=ROOT, text=True).strip(),
        'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        'workingTreeDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()),
        'sourceTreeSha256': tree_hash,
        'packages': proof.package_proof({'avalonia': assets}, ROOT / 'artifacts/packages', 'linux'),
        'executableSha256': hashlib.sha256(executable.read_bytes()).hexdigest(),
    }
    (output / 'source-files.json').write_text(json.dumps(sources, indent=2) + '\n')
    environment = os.environ.copy()
    environment['DOTNET_ROOT'] = str(ROOT / '.tools/dotnet')
    environment.pop('CURRENCY_AVALONIA_RESULT', None)
    if args.interactive_close:
        # Starting the app before the WM is ready makes focus/keyboard-based close unreliable.
        wm_deadline = time.monotonic() + 10
        while True:
            manager = subprocess.run(['wmctrl', '-m'], text=True, capture_output=True, timeout=10)
            if manager.returncode == 0:
                record['windowManager'] = manager.stdout.strip()
                break
            if time.monotonic() >= wm_deadline:
                raise ValueError('No EWMH window manager is available for the normal window-close check')
            time.sleep(0.1)
    log_path = output / 'avalonia-result.txt'
    with log_path.open('w') as log:
        process = subprocess.Popen([str(executable)] + ([] if args.interactive_close else ['--verify']),
                                   cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 90
        try:
            if args.interactive_close:
                while 'CURRENCY_AVALONIA PASS linux:' not in log_path.read_text():
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise ValueError('Interactive Currency verification did not pass while the window was open')
                    time.sleep(0.1)
                windows = subprocess.check_output(['xdotool', 'search', '--onlyvisible', '--pid', str(process.pid),
                                                   '--name', 'Currency'], text=True, timeout=10).splitlines()
                if len(windows) != 1:
                    raise ValueError(f'Expected one Currency window, got {windows}')
                record['windowId'] = windows[0]
                # Record the actually loaded system ICU, separately from managed package evidence.
                record['icuLibraries'] = sorted({line.split()[-1] for line in Path(f'/proc/{process.pid}/maps').read_text().splitlines()
                                                 if '/libicu' in line})
                if not record['icuLibraries']:
                    raise ValueError('No system ICU library loaded')
                native_paths = {line.split()[-1] for line in Path(f'/proc/{process.pid}/maps').read_text().splitlines()
                                if line.split()[-1].endswith('/libnc_currency.so')}
                if len(native_paths) != 1:
                    raise ValueError('Expected the loaded C++ Currency adapter')
                native_path = Path(next(iter(native_paths)))
                archives = list((ROOT / 'artifacts/packages').glob('Plugin.Maui.Currency.*.nupkg'))
                if len(archives) != 1:
                    raise ValueError('Expected one local Currency archive')
                with zipfile.ZipFile(archives[0]) as archive:
                    if native_path.read_bytes() != archive.read('runtimes/linux-x64/native/libnc_currency.so'):
                        raise ValueError('Loaded C++ Currency adapter differs from local archive')
                record['nativeAdapter'] = {'path': str(native_path), 'sha256': hashlib.sha256(native_path.read_bytes()).hexdigest()}
                import shutil
                if shutil.which('scrot'):
                    # Allow the compositor to paint the completed lookup before capturing evidence.
                    subprocess.run(['scrot', '--delay', '1', str(output / 'screenshot.png')], check=True, timeout=10)
                # _NET_CLOSE_WINDOW follows the normal WM_DELETE_WINDOW path without a
                # keyboard-focus race; never destroy the X window or kill a passing app.
                subprocess.run(['wmctrl', '-i', '-c', hex(int(windows[0]))], check=True, timeout=10)
            process.wait(timeout=max(1, deadline - time.monotonic()))
        except BaseException:
            process.kill()
            process.wait()
            raise
    record['results'] = check_log(log_path.read_text(), process.returncode)
    record['success'] = True
    (output / 'verification.json').write_text(json.dumps(record, indent=2) + '\n')
    print(f'PASS: package-only Linux Currency ({mode}); system ICU calls and orderly shutdown verified')


if __name__ == '__main__':
    main()
