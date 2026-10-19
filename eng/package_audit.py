#!/usr/bin/env python3
"""Validate distributable metadata and record hashes without publishing packages."""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import xml.etree.ElementTree as ET
import zipfile


def audit_package(path, source_commit=None):
    with zipfile.ZipFile(path) as archive:
        # ZipInfo.filename normalizes backslashes on Windows; validate the original archive name.
        files = [entry.orig_filename for entry in archive.infolist()]
        if len(files) != len(set(files)):
            raise ValueError(f"{path.name}: duplicate archive entries")
        for name in files:
            if "\\" in name or PurePosixPath(name).is_absolute() or ".." in PurePosixPath(name).parts:
                raise ValueError(f"{path.name}: unsafe archive path {name!r}")
        specs = [name for name in files if name.endswith(".nuspec") and "/" not in name]
        if len(specs) != 1:
            raise ValueError(f"{path.name}: expected one root nuspec")
        root = ET.fromstring(archive.read(specs[0]))
        # NuGet has used several schema namespace versions; compare element local names.
        for element in root.iter():
            element.tag = element.tag.rsplit("}", 1)[-1]
        metadata = root.find("metadata")
        if metadata is None:
            raise ValueError(f"{path.name}: missing package metadata")
        package_id = metadata.findtext("id")
        version = metadata.findtext("version")
        if not package_id or not version or path.name != f"{package_id}.{version}.nupkg":
            raise ValueError(f"{path.name}: package identity does not match filename")
        license_node = metadata.find("license")
        if license_node is None or license_node.get("type") != "expression" or license_node.text != "MIT":
            raise ValueError(f"{path.name}: expected MIT license expression")
        readme = metadata.findtext("readme")
        if not readme or readme not in files or not archive.read(readme).strip():
            raise ValueError(f"{path.name}: missing or empty packaged README")
        repository = metadata.find("repository")
        if (repository is None or repository.get("type") != "git"
                or not repository.get("url", "").startswith("https://")
                or not re.fullmatch(r"[0-9a-f]{40}", repository.get("commit", ""))):
            raise ValueError(f"{path.name}: missing repository URL or source commit")
        commit = repository.get("commit")
        if source_commit is not None and commit != source_commit:
            raise ValueError(f"{path.name}: source commit {commit} differs from expected {source_commit}")
        assemblies = sorted(name for name in files if name.startswith("lib/") and name.endswith(".dll"))
        if not assemblies:
            raise ValueError(f"{path.name}: no managed assemblies")
        with path.open("rb") as package_file:
            digest = hashlib.sha256()
            for chunk in iter(lambda: package_file.read(1024 * 1024), b""):
                digest.update(chunk)
        return {
            "file": path.name,
            "id": package_id,
            "version": version,
            "sourceCommit": commit,
            "sha256": digest.hexdigest(),
            "sizeBytes": path.stat().st_size,
            "assemblies": assemblies,
        }


def audit_android_scope(path, record):
    """Reject other platform payloads/dependencies in an Android development feed."""
    package_id = record["id"]
    permitted = {
        "NativeChannels.Core", "NativeChannels", "NativeChannels.Maui", "NativeChannels.Avalonia", "NativeChannels.Native.Android",
        "Plugin.Currency", "Plugin.AudioStreamer", "Plugin.VolumeMonitor", "Plugin.Maui.PdfViewer", "Plugin.Avalonia.PdfViewer",
    }
    if package_id not in permitted:
        raise ValueError(f"{path.name}: unexpected Android feed package {package_id}")
    with zipfile.ZipFile(path) as archive:
        files = archive.namelist()
        frameworks = {name.split("/")[1] for name in files if name.startswith("lib/") and name.endswith(".dll")}
        if package_id in {"NativeChannels.Core", "NativeChannels.Avalonia"}:
            valid = frameworks == {"net10.0"}
        else:
            valid = len(frameworks) == 1 and all(re.fullmatch(r"net10\.0-android(?:[0-9]+(?:\.[0-9]+)*)?", tfm) for tfm in frameworks)
        if not valid:
            raise ValueError(f"{path.name}: unexpected Android feed frameworks {sorted(frameworks)}")
        if any(name.startswith(("native/apple/", "native/windows/")) or ".xcframework/" in name for name in files):
            raise ValueError(f"{path.name}: non-Android native payload in development feed")
        metadata = ET.fromstring(archive.read(f"{package_id}.nuspec"))
        for element in metadata.iter():
            element.tag = element.tag.rsplit("}", 1)[-1]
        groups = metadata.findall("metadata/dependencies/group")
        if len(groups) != len(frameworks) or {group.get("targetFramework") for group in groups} != frameworks:
            raise ValueError(f"{path.name}: dependency groups do not match Android feed assemblies")
        if any(dep.get("id") == "NativeChannels.Native.Apple" for dep in metadata.iter("dependency")):
            raise ValueError(f"{path.name}: Apple binding dependency in Android feed")


def audit_platform_scope(path, record, platform):
    """Validate a scoped Apple, Windows, Linux, or browser development archive."""
    if platform not in {"ios", "maccatalyst", "macos", "windows", "linux", "browser"}:
        raise ValueError(f"Unsupported development platform: {platform}")
    package_id = record["id"]
    apple = platform in {"ios", "maccatalyst", "macos"}
    permitted = {"NativeChannels.Core", "NativeChannels", "NativeChannels.Maui", "NativeChannels.Avalonia"}
    if platform in {"macos", "linux", "browser"}:
        permitted.remove("NativeChannels.Maui")
    permitted.update({"Plugin.Currency", "Plugin.AudioStreamer", "Plugin.VolumeMonitor", "Plugin.Avalonia.PdfViewer"})
    if platform in {"windows", "ios", "maccatalyst"}:
        permitted.add("Plugin.Maui.PdfViewer")
    if apple:
        permitted.add("NativeChannels.Native.Apple")
    if package_id not in permitted:
        raise ValueError(f"{path.name}: unexpected {platform} framework package {package_id}")
    with zipfile.ZipFile(path) as archive:
        files = archive.namelist()
        frameworks = {name.split("/")[1] for name in files if name.startswith("lib/") and name.endswith(".dll")}
        if package_id in {"NativeChannels.Core", "NativeChannels.Avalonia"} or platform == "linux":
            valid = frameworks == {"net10.0"}
        else:
            pattern = rf"net10\.0-{platform}(?:[0-9]+(?:\.[0-9]+)*)?"
            valid = len(frameworks) == 1 and all(re.fullmatch(pattern, tfm) for tfm in frameworks)
        if not valid:
            raise ValueError(f"{path.name}: unexpected {platform} frameworks {sorted(frameworks)}")
        forbidden = ("native/android/", "native/windows/") if apple else ("native/",)
        if any(name.startswith(forbidden) or name.endswith(".aar") or
               (not apple and (".xcframework/" in name or name.endswith(".resources.zip"))) for name in files):
            raise ValueError(f"{path.name}: unrelated native payload in {platform} feed")
        if platform == 'linux':
            expected = {'Plugin.Currency': ['libnc_currency.so'],
                        'Plugin.AudioStreamer': ['libnc_audio.so'],
                        'Plugin.VolumeMonitor': ['libnc_volume.so'],
                        'Plugin.Avalonia.PdfViewer': ['libnc_pdf_qt.so', 'libnc_pdf_gtk.so']}.get(package_id, [])
            runtime_files = {name for name in files if name.startswith('runtimes/') and not name.endswith('/')}
            required = {f'runtimes/linux-x64/native/{name}' for name in expected}
            if runtime_files != required:
                raise ValueError(f'{path.name}: unexpected Linux native runtime assets {sorted(runtime_files)}')
            for name in required:
                header = archive.read(name)[:20]
                if len(header) < 20 or header[:6] != b'\x7fELF\x02\x01' or header[18:20] != b'\x3e\x00':
                    raise ValueError(f'{path.name}: expected an ELF64 x86-64 native library')
        if platform == 'browser':
            required = {
                'NativeChannels': ['browser/nativechannels.mjs', 'browser/json-codec.mjs'],
                'Plugin.Currency': ['browser/currency.mjs'],
                'Plugin.AudioStreamer': ['browser/audiostreamer.mjs'],
                'Plugin.VolumeMonitor': ['browser/volumemonitor.mjs'],
                'Plugin.Avalonia.PdfViewer': ['browser/pdf-dom.mjs', 'browser/pdfviewer.mjs',
                    'browser/vendor/pdf.mjs', 'browser/vendor/pdf.worker.mjs', 'browser/vendor/LICENSE'],
            }.get(package_id, [])
            if any(name not in files or not archive.read(name) for name in required):
                raise ValueError(f'{path.name}: missing packaged browser adapter or PDF.js assets')
        metadata = ET.fromstring(archive.read(f"{package_id}.nuspec"))
        for element in metadata.iter():
            element.tag = element.tag.rsplit("}", 1)[-1]
        groups = metadata.findall("metadata/dependencies/group")
        if len(groups) != len(frameworks) or {group.get("targetFramework") for group in groups} != frameworks:
            raise ValueError(f"{path.name}: dependency groups do not match {platform} assemblies")
        for dependency in metadata.iter("dependency"):
            name = dependency.get("id", "")
            if platform in {"linux", "browser"} and (name == "NativeChannels.Maui" or name.startswith(("Microsoft.Maui", "Microsoft.WindowsAppSDK"))):
                raise ValueError(f"{path.name}: unrelated host dependency {name}")
            if name.startswith("NativeChannels.Native.") and (not apple or name != "NativeChannels.Native.Apple"):
                raise ValueError(f"{path.name}: unrelated binding dependency {name}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("packages", nargs="+", type=Path)
    parser.add_argument("--source-commit", help="Require this exact Git source commit for release validation")
    parser.add_argument("--output", type=Path, default=Path("artifacts/package-audit.json"))
    scope = parser.add_mutually_exclusive_group()
    scope.add_argument("--platform-only", choices=("ios", "maccatalyst", "macos", "windows", "linux", "browser"), help="Require a single-platform framework development feed")
    scope.add_argument("--android-only", action="store_true", help="Require Android development packages, not a combined release feed")
    args = parser.parse_args()
    if args.source_commit is not None and not re.fullmatch(r"[0-9a-f]{40}", args.source_commit):
        parser.error("--source-commit must be a full lowercase Git SHA")
    try:
        results = [audit_package(path, args.source_commit) for path in sorted(args.packages)]
        if args.android_only:
            for path, record in zip(sorted(args.packages), results):
                audit_android_scope(path, record)
        if args.platform_only:
            for path, record in zip(sorted(args.packages), results):
                audit_platform_scope(path, record, args.platform_only)
    except (ValueError, OSError, KeyError, ET.ParseError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Package audit failed: {error}\n")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"schemaVersion": 1, "packages": results}, indent=2) + "\n")
    print(f"PASS {len(results)} package metadata checks; manifest: {args.output}")


if __name__ == "__main__":
    main()
