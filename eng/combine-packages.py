#!/usr/bin/env python3
"""Combine same-commit mobile and Windows packages without rebuilding either payload."""

import argparse
from copy import deepcopy
import json
from pathlib import Path
import shutil
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from package_audit import audit_package

COMBINED = {
    "NativeChannels", "NativeChannels.Maui", "Plugin.Maui.Currency",
    "Plugin.Maui.AudioStreamer", "Plugin.Maui.VolumeMonitor", "Plugin.Maui.PdfViewer",
}
MOBILE = {"android", "ios", "maccatalyst"}
GROUPED = {"dependencies", "frameworkReferences"}


def xml(data):
    root = ET.fromstring(data)
    namespace = root.tag.split("}")[0].lstrip("{") if "}" in root.tag else ""
    for node in root.iter():
        node.tag = node.tag.rsplit("}", 1)[-1]
    return root, namespace


def signature(node):
    return (node.tag, sorted(node.attrib.items()), (node.text or "").strip(),
            sorted(signature(child) for child in node))


def serialize(root, namespace):
    if namespace:
        root.set("xmlns", namespace)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def platform(tfm):
    return next((name for name in MOBILE | {"windows"} if tfm.startswith("net10.0-" + name)), None)


def read_archive(path):
    with zipfile.ZipFile(path) as archive:
        names = [info.orig_filename for info in archive.infolist()]
        if len(names) != len(set(names)) or len(names) != len(set(n.lower() for n in names)):
            raise ValueError(f"{path.name}: duplicate archive entries")
        for name in names:
            if "\\" in name or name.startswith("/") or ":" in name or ".." in name.split("/"):
                raise ValueError(f"{path.name}: unsafe archive path {name!r}")
        if ".signature.p7s" in names:
            raise ValueError("Combine unsigned build outputs before signing")
        return {name: archive.read(name) for name in names}


def merge_manifest(left, right):
    root, namespace = xml(left)
    other, other_namespace = xml(right)
    if namespace != other_namespace or root.attrib != other.attrib:
        raise ValueError("Different nuspec schemas")
    metadata, other_metadata = root.find("metadata"), other.find("metadata")
    if metadata is None or other_metadata is None:
        raise ValueError("Missing package metadata")
    # Everything except per-framework groups must describe the same package build.
    common = lambda m: sorted(signature(n) for n in m if n.tag not in GROUPED)
    if common(metadata) != common(other_metadata):
        raise ValueError("Package metadata differs between mobile and Windows builds")
    for name in GROUPED:
        destination = metadata.find(name)
        source = other_metadata.find(name)
        if source is None:
            continue
        if destination is None:
            destination = ET.SubElement(metadata, name)
        groups = {n.get("targetFramework"): n for n in destination}
        for group in source:
            tfm = group.get("targetFramework")
            if group.tag != "group" or not tfm:
                raise ValueError(f"Ungrouped {name} are not supported")
            if tfm in groups:
                if signature(groups[tfm]) != signature(group):
                    raise ValueError(f"Conflicting {name} for {tfm}")
            else:
                destination.append(deepcopy(group))
                groups[tfm] = group
        destination[:] = sorted(destination, key=lambda n: n.get("targetFramework", ""))
    return serialize(root, namespace)


def merge_content_types(left, right):
    root, namespace = xml(left)
    other, other_namespace = xml(right)
    if namespace != other_namespace:
        raise ValueError("Different content type schemas")
    key = lambda n: (n.tag, n.get("Extension", n.get("PartName")))
    entries = {key(n): n for n in root}
    for node in other:
        if key(node) in entries and signature(entries[key(node)]) != signature(node):
            raise ValueError("Conflicting ZIP content types")
        entries[key(node)] = node
    root[:] = [entries[k] for k in sorted(entries)]
    return serialize(root, namespace)


def check_targets(entries, package_id, expected, symbols=False):
    extension = "pdb" if symbols else "dll"
    tfms = {name.split("/")[1] for name in entries
            if name.startswith("lib/") and name.endswith(f"/{package_id}.{extension}")}
    if len(tfms) != len(expected) or {platform(tfm) for tfm in tfms} != expected:
        raise ValueError(f"{package_id}: expected {sorted(expected)} {extension} targets, found {sorted(tfms)}")
    spec, _ = xml(entries[f"{package_id}.nuspec"])
    groups = spec.findall("metadata/dependencies/group")
    if {n.get("targetFramework") for n in groups} != tfms or len(groups) != len(tfms):
        raise ValueError(f"{package_id}: dependency groups do not match assemblies")
    for group in groups:
        if platform(group.get("targetFramework")) == "windows":
            if any(n.get("id", "").startswith("NativeChannels.Native.") for n in group):
                raise ValueError("Mobile binding dependency leaked into Windows")


def combine(mobile, windows, destination, package_id, symbols=False):
    left, right = read_archive(mobile), read_archive(windows)
    check_targets(left, package_id, MOBILE, symbols)
    check_targets(right, package_id, {"windows"}, symbols)
    spec = f"{package_id}.nuspec"
    merged = dict(left)
    merged[spec] = merge_manifest(left[spec], right[spec])
    merged["[Content_Types].xml"] = merge_content_types(left["[Content_Types].xml"], right["[Content_Types].xml"])
    for name, content in right.items():
        # OPC relationship/core-property parts describe the mobile package and remain valid.
        if name in (spec, "[Content_Types].xml", "_rels/.rels") or name.startswith("package/services/metadata/"):
            continue
        if name in merged and merged[name] != content:
            # Checkout line endings may differ between hosts; preserve the mobile documentation.
            if name.endswith((".md", ".targets")) and merged[name].replace(b"\r\n", b"\n") == content.replace(b"\r\n", b"\n"):
                continue
            raise ValueError(f"{package_id}: conflicting shared payload {name}")
        merged[name] = content
    check_targets(merged, package_id, MOBILE | {"windows"}, symbols)
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in sorted(merged):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, merged[name])


def assemble(mobile, windows, output, source_commit, plugins=None):
    known_plugins = {name for name in COMBINED if name.startswith("Plugin.Maui.")}
    selected = known_plugins if plugins is None else set(plugins)
    if not selected.issubset(known_plugins):
        raise ValueError("Unknown example plugin")
    combined = {"NativeChannels", "NativeChannels.Maui"} | selected
    if output.exists() and any(output.iterdir()):
        raise ValueError("Output directory must be empty; never overwrite input packages")
    mobile_packages = [audit_package(p, source_commit) for p in sorted(mobile.glob("*.nupkg"))]
    windows_packages = [audit_package(p, source_commit) for p in sorted(windows.glob("*.nupkg"))]
    left, right = ({p["id"]: p for p in packages} for packages in (mobile_packages, windows_packages))
    if len(left) != len(mobile_packages) or len(right) != len(windows_packages):
        raise ValueError("Multiple versions of one package in an input feed")
    required = combined | {"NativeChannels.Core"}
    allowed_mobile = required | {"NativeChannels.Native.Android", "NativeChannels.Native.Apple"}
    if not required.issubset(left) or not set(left).issubset(allowed_mobile) or set(right) != required:
        raise ValueError("Missing or unexpected platform packages")
    for name in right:
        if name not in left or left[name]["version"] != right[name]["version"]:
            raise ValueError(f"{name}: platform package versions differ")
    # Symbol archives must belong to the audited binary, not just to each other.
    for directory, records in ((mobile, left), (windows, right)):
        for name, record in records.items():
            symbol = directory / (record["file"].removesuffix(".nupkg") + ".snupkg")
            if not symbol.is_file():
                raise ValueError(f"Missing {'Windows ' if directory == windows else ''}symbols: {symbol.name}")
            contents = read_archive(symbol)
            metadata, _ = xml(contents[f"{name}.nuspec"])
            repository = metadata.find("metadata/repository")
            if (metadata.findtext("metadata/id") != name or metadata.findtext("metadata/version") != record["version"]
                    or repository is None or repository.get("commit") != source_commit):
                raise ValueError(f"{symbol.name}: symbol identity or source commit differs from binary package")
    output.parent.mkdir(parents=True, exist_ok=True)
    # Finish and audit all files before making the output feed visible.
    with tempfile.TemporaryDirectory(dir=output.parent) as staging:
        stage = Path(staging)
        for name, record in left.items():
            filename = record["file"]
            if name in combined:
                combine(mobile / filename, windows / filename, stage / filename, name)
            else:
                shutil.copyfile(mobile / filename, stage / filename)
            symbol_name = filename.removesuffix(".nupkg") + ".snupkg"
            if name in combined:
                combine(mobile / symbol_name, windows / symbol_name, stage / symbol_name, name, symbols=True)
            else:
                shutil.copyfile(mobile / symbol_name, stage / symbol_name)
        results = [audit_package(p, source_commit) for p in sorted(stage.glob("*.nupkg"))]
        (stage / "package-audit.json").write_text(json.dumps({"schemaVersion": 1, "packages": results}, indent=2) + "\n")
        output.mkdir(exist_ok=True)
        for path in stage.iterdir():
            path.replace(output / path.name)
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mobile", type=Path, required=True)
    parser.add_argument("--windows", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--scope", choices=("all", "framework", "example"), default="all")
    parser.add_argument("--plugin", choices=("Currency", "AudioStreamer", "VolumeMonitor", "PdfViewer"))
    args = parser.parse_args()
    if (args.scope == "example") != bool(args.plugin):
        parser.error("--plugin is required only for --scope example")
    plugins = None if args.scope == "all" else ([f"Plugin.Maui.{args.plugin}"] if args.plugin else [])
    try:
        results = assemble(args.mobile, args.windows, args.output, args.source_commit, plugins)
    except (ValueError, OSError, KeyError, ET.ParseError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Package assembly failed: {error}\n")
    print(f"PASS {len(results)} packages assembled; Windows/mobile payloads and symbols preserved")


if __name__ == "__main__":
    main()
