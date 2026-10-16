#!/usr/bin/env python3
"""Reuse third-party package archives, never NativeChannels packages, for isolated restores."""
import os
import pathlib

root = pathlib.Path(__file__).resolve().parent.parent
feed = root / ".tools/dependency-feed"
feed.mkdir(parents=True, exist_ok=True)
for cache in (root / "artifacts").glob("consumer-packages.*"):
    for package in cache.glob("*/*/*.nupkg"):
        if package.name.lower().startswith(("nativechannels.", "plugin.maui.currency.", "plugin.maui.audiostreamer.", "plugin.maui.volumemonitor.", "plugin.maui.pdfviewer.")):
            continue
        target = feed / package.name
        if not target.exists():
            os.link(package, target)
