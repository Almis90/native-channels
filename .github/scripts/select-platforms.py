"""Select framework verification targets conservatively from a trusted comparison base."""
import json
import os
from pathlib import Path
import subprocess

ALL = {"android", "ios", "catalyst", "windows"}


def classify(paths):
    selected = set()
    core = False
    for path in paths:
        if path.startswith(("docs/", "plugins/", "benchmarks/", "tests/Plugin.Maui.")):
            continue
        if path.startswith("samples/") and not path.startswith("samples/NativeChannels.Sample/"):
            continue
        if path.endswith(".md") or path in {"LICENSE", "package.json", "package-lock.json", "eng/check-docs.mjs", ".github/workflows/pages.yml"}:
            continue
        core = True
        # Shared config and binding definitions widen the selection even when stored under one platform.
        if path.endswith((".props", ".targets", ".csproj", ".slnx")) or path.startswith(".github/"):
            selected |= ALL
        elif path.startswith("src/NativeChannels.Native.Android/"):
            selected.add("android")
        elif path.startswith("src/NativeChannels.Native.Apple/"):
            selected |= {"ios", "catalyst"}
        elif path.startswith("native/android/") or "/Platforms/Android/" in path:
            selected.add("android")
        elif path.startswith("native/apple/") or "/Platforms/Apple/" in path:
            selected |= {"ios", "catalyst"}
        elif "/Platforms/iOS/" in path:
            selected.add("ios")
        elif "/Platforms/MacCatalyst/" in path:
            selected.add("catalyst")
        elif path.startswith("tests/NativeChannels.Windows.Tests/") or path.startswith("native/windows/") or "/Platforms/Windows/" in path or path.endswith("/WindowsNativeView.cs"):
            selected.add("windows")
        elif path.startswith(("tests/NativeChannels.Core.Tests/", "tests/packaging/")):
            # These tests do not change shipping code; the shared job still executes them.
            continue
        else:
            selected |= ALL
    return selected, core


def changed_paths(base):
    if not base or base.startswith("-") or set(base) == {"0"}:
        raise ValueError("No usable comparison base")
    resolved = subprocess.check_output(["git", "rev-parse", "--verify", "--end-of-options", base + "^{commit}"], text=True).strip()
    ancestor = subprocess.check_output(["git", "merge-base", resolved, "HEAD"], text=True).strip()
    data = subprocess.check_output(["git", "diff", "--no-renames", "--name-only", "-z", ancestor, "HEAD"])
    return [p.decode("utf-8") for p in data.split(b"\0") if p]


def choose(requested, base):
    choices = {"Android": {"android"}, "iOS": {"ios"}, "Catalyst": {"catalyst"}, "Windows": {"windows"}, "All": ALL}
    if requested in choices:
        return choices[requested], True, "Explicit platform selection"
    if requested != "Auto":
        raise ValueError(f"Unknown platform selection: {requested}")
    try:
        paths = changed_paths(base)
    except (ValueError, subprocess.CalledProcessError, UnicodeError):
        return ALL, True, "Comparison unavailable; conservatively selected all platforms"
    selected, core = classify(paths)
    return selected, core, f"Compared against merge base of {base}; {len(paths)} changed paths"


def main():
    event = os.environ["GITHUB_EVENT_NAME"]
    payload = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    if event == "pull_request":
        requested, base, depth = "Auto", payload["pull_request"]["base"]["sha"], "Compile"
    elif event == "push":
        requested, base, depth = "Auto", payload.get("before", ""), "Compile"
    else:
        requested = os.environ.get("REQUESTED_PLATFORMS", "Auto")
        base = os.environ.get("COMPARISON_BASE", "")
        depth = os.environ.get("VERIFICATION_DEPTH", "Compile")
    if depth not in {"Compile", "Runtime"}:
        raise ValueError("Unknown verification depth")
    selected, core, reason = choose(requested, base)
    record = {"sourceCommit": os.environ["GITHUB_SHA"], "platforms": sorted(selected),
              "core": core, "depth": depth, "reason": reason}
    Path("artifacts").mkdir(exist_ok=True)
    Path("artifacts/selection.json").write_text(json.dumps(record, indent=2) + "\n")
    with open(os.environ["GITHUB_OUTPUT"], "a") as output:
        for platform in sorted(ALL):
            output.write(f"{platform}={str(platform in selected).lower()}\n")
        output.write(f"core={str(core).lower()}\n")
        output.write(f"apple={str(bool(selected & {'ios', 'catalyst'})).lower()}\n")
        output.write(f"depth={depth}\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as summary:
        summary.write(f"### Verification selection\n\n{reason}\n\nDepth: **{depth}**. Platforms: **{', '.join(sorted(selected)) or 'none'}**.\n")


if __name__ == "__main__":
    main()
