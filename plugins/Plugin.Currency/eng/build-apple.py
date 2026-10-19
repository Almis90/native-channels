#!/usr/bin/env python3
"""Build the currency adapter against the already-built generic Apple bridge."""
import argparse
import importlib.util
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("apple_builder", ROOT / "eng/build-apple.py")
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)
NAME = "MauiCurrencyNative"
SOURCE = Path(__file__).resolve().parents[1] / "native/apple/Sources"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--macos-only", action="store_true")
args = parser.parse_args()
slices = []
platforms = [("macosx", "", ["arm64", "x86_64"])] if args.macos_only else [
    ("iphoneos", "", ["arm64"]),
    ("iphonesimulator", "-simulator", ["arm64", "x86_64"]),
    ("macosx", "-macabi", ["arm64", "x86_64"]),
]
for sdk, suffix, arches in platforms:
    frameworks = []
    for arch in arches:
        bridge = builder.OUT / f"{sdk}{suffix}-{arch}/NativeChannelsNative.framework"
        if not bridge.exists():
            raise SystemExit("Build the generic bridge first: python3 eng/build-apple.py")
        frameworks.append(builder.framework(NAME, SOURCE, sdk, arch, suffix, [bridge]))
    destination = builder.OUT / (sdk + suffix) / f"{NAME}.framework"
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(frameworks[0], destination)
    if len(frameworks) > 1:
        builder.run("xcrun", "lipo", "-create", *[f / NAME for f in frameworks], "-output", destination / NAME)
        for extra in frameworks[1:]:
            shutil.copytree(extra / "Modules", destination / "Modules", dirs_exist_ok=True)
    slices.append(destination)

output = builder.OUT / f"{NAME}.xcframework"
if output.exists():
    shutil.rmtree(output)
arguments = ["xcodebuild", "-create-xcframework"]
for framework in slices:
    arguments.extend(["-framework", framework])
builder.run(*arguments, "-output", output)
