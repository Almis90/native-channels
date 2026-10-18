"""Prove platform packaging consumes the Core archive from this workflow's shared job."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "eng"))
from package_audit import audit_package


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare():
    feed = ROOT / "artifacts/packages"
    archives = list(feed.glob("*.nupkg"))
    if len(archives) != 1 or not archives[0].name.startswith("NativeChannels.Core."):
        raise ValueError("Shared packaging must start with exactly the Core package artifact")
    record = audit_package(archives[0], os.environ["GITHUB_SHA"])
    if record["id"] != "NativeChannels.Core":
        raise ValueError("Unexpected shared package identity")
    symbols = archives[0].with_suffix(".snupkg")
    if not symbols.is_file():
        raise ValueError("Shared Core symbol package is missing")
    expected = {p.name: digest(p) for p in (archives[0], symbols)}
    (ROOT / "artifacts/shared-core.json").write_text(json.dumps(expected))
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="shared", value=str(feed))
    ET.SubElement(sources, "add", key="nuget", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(config, "packageSourceMapping")
    ET.SubElement(mapping, "clear")
    shared = ET.SubElement(mapping, "packageSource", key="shared")
    ET.SubElement(shared, "package", pattern="NativeChannels.Core")
    public = ET.SubElement(mapping, "packageSource", key="nuget")
    ET.SubElement(public, "package", pattern="*")
    ET.ElementTree(config).write(ROOT / "artifacts/shared-core.NuGet.Config", encoding="utf-8", xml_declaration=True)


def verify(feed, platform=None):
    expected = json.loads((ROOT / "artifacts/shared-core.json").read_text())
    for name, sha in expected.items():
        if digest(feed / name) != sha:
            raise ValueError(f"Shared Core bytes changed: {name}")
    if (ROOT / "src/NativeChannels.Core/bin").exists():
        raise ValueError("Core build outputs appeared in a platform job")
    for name in (["NativeChannels"] if platform == "macos" else ["NativeChannels", "NativeChannels.Maui"]):
        path = ROOT / "src" / name / "obj/project.assets.json"
        assets = json.loads(path.read_text())
        entries = [(identity, value) for identity, value in assets["libraries"].items()
                   if identity.split("/")[0].lower() == "nativechannels.core"]
        if len(entries) != 1 or entries[0][1]["type"] != "package":
            raise ValueError(f"{name} did not restore Core as a package")
        identity, value = entries[0]
        version = identity.split("/")[1]
        cached = next((Path(folder) / value["path"] / f"nativechannels.core.{version}.nupkg"
                       for folder in assets["packageFolders"]
                       if (Path(folder) / value["path"] / f"nativechannels.core.{version}.nupkg").is_file()), None)
        if cached is None or digest(cached) != expected[f"NativeChannels.Core.{version}.nupkg"]:
            raise ValueError(f"{name} restored different Core bytes")
    print("PASS: host packages consumed the exact shared Core package; no Core build outputs")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("prepare", "verify"))
    parser.add_argument("--feed", type=Path, default=ROOT / "artifacts/packages")
    parser.add_argument("--platform", choices=("ios", "maccatalyst", "macos"))
    args = parser.parse_args()
    try:
        prepare() if args.mode == "prepare" else verify(args.feed, args.platform)
    except (ValueError, OSError, KeyError, StopIteration) as error:
        parser.exit(1, f"Shared Core validation failed: {error}\n")
