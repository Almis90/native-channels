#!/usr/bin/env python3
"""Build plain-.NET macOS transport assets and optional Currency adapter; requires macOS/Xcode."""
import argparse
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--arch', choices=('arm64', 'x86_64'), required=True)
    parser.add_argument('--currency', action='store_true')
    parser.add_argument('--sample', action='store_true')
    args = parser.parse_args()
    rid = 'osx-arm64' if args.arch == 'arm64' else 'osx-x64'
    output = ROOT / 'artifacts/native/apple-desktop' / rid
    output.mkdir(parents=True, exist_ok=True)
    sdk = subprocess.check_output(['xcrun', '--sdk', 'macosx', '--show-sdk-path'], text=True).strip()
    common = ['xcrun', '--sdk', 'macosx', 'swiftc', '-swift-version', '5', '-parse-as-library',
              '-emit-library', '-emit-module', '-target', f'{args.arch}-apple-macosx14.0', '-sdk', sdk]

    def build(name, sources, references=()):
        library = f'lib{name}.dylib'
        subprocess.run([*common, '-module-name', name, '-emit-module-path', str(output / f'{name}.swiftmodule'),
                        '-Xlinker', '-install_name', '-Xlinker', f'@rpath/{library}',
                        '-Xlinker', '-rpath', '-Xlinker', '@loader_path',
                        '-I', str(output), '-L', str(output), *[f'-l{ref}' for ref in references],
                        '-o', str(output / library), *map(str, sorted(sources))], check=True)

    build('NativeChannelsNative', (ROOT / 'native/apple/Sources').glob('*.swift'))
    if args.currency:
        build('MauiCurrencyNative', (ROOT / 'plugins/Plugin.Currency/native/apple/Sources').glob('*.swift'),
              ['NativeChannelsNative'])
    if args.sample:
        build('NativeChannelsSample', [ROOT / 'native/apple/Samples/SampleModule.swift'],
              ['NativeChannelsNative'])


if __name__ == '__main__':
    main()
