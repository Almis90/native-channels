"""Summarize observed stages; compilation and runtime execution are separate evidence."""
import json
import os
from pathlib import Path
import sys

RUNTIMES = {"android": {"android-x64", "android-arm64"}, "ios": {"iossimulator-arm64"},
            "catalyst": {"maccatalyst-arm64"}, "windows": {"win-x64", "win-arm64"}}

EXECUTION_RUNTIMES = {**RUNTIMES, "android": {"android-x64"}, "windows": set()}


def summarize(selection, needs, evidence, sha):
    if selection["sourceCommit"] != sha:
        raise ValueError("Selection belongs to another source revision")
    required = {"select"}
    if selection["core"]:
        required.add("core")
    required.update(selection["platforms"])
    if selection.get('sharedHosting'):
        required.add('shared-hosting')
    if selection.get('sharedHostingIos'):
        required.add('shared-hosting-ios')
    if selection.get('sharedHostingWindows'):
        required.add('shared-hosting-windows')
    if set(selection["platforms"]) & {"ios", "catalyst"}:
        required.add("apple-native")
    failed = sorted(name for name in required if needs.get(name, {}).get("result") != "success")
    stages = {}
    for item in evidence:
        if item["sourceCommit"] != sha:
            raise ValueError("Stage evidence belongs to another source revision")
        key = (item["platform"], item["stage"], item["runtime"])
        if key in stages:
            raise ValueError(f"Duplicate stage evidence: {key}")
        stages[key] = item["outcome"]
    rows, missing = [], []
    for platform, runtimes in RUNTIMES.items():
        selected = platform in selection["platforms"]
        compiled = selected and all(stages.get((platform, "compile", rid)) == "success" for rid in runtimes)
        runtime_requested = selected and selection["depth"] == "Runtime" and bool(EXECUTION_RUNTIMES[platform])
        executed = runtime_requested and all(stages.get((platform, "runtime", rid)) == "success" for rid in EXECUTION_RUNTIMES[platform])
        if selected and not compiled:
            missing.append(f"{platform}: compilation")
        if runtime_requested and not executed:
            missing.append(f"{platform}: execution")
        rows.append({"platform": platform, "selected": selected, "compiled": compiled, "executed": executed,
                     "compiledRuntimes": sorted(rid for rid in runtimes if selected and stages.get((platform, "compile", rid)) == "success"),
                     "executedRuntimes": sorted(rid for rid in EXECUTION_RUNTIMES[platform] if runtime_requested and stages.get((platform, "runtime", rid)) == "success"),
                     "jobResult": needs.get(platform, {}).get("result", "skipped")})
    return {**selection, "success": not failed and not missing, "results": rows,
            "failedJobs": failed, "unverifiedStages": missing, "evidence": evidence}


def main():
    selection = json.loads(Path("artifacts/selection.json").read_text())
    evidence = [json.loads(p.read_text()) for p in sorted(Path("artifacts/evidence").glob("*.json"))]
    record = summarize(selection, json.loads(os.environ["JOB_RESULTS"]), evidence, os.environ["GITHUB_SHA"])
    Path("artifacts/verification.json").write_text(json.dumps(record, indent=2) + "\n")
    with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as summary:
        summary.write("### Framework verification\n\n| Platform | Job | Compiled | Executed |\n| --- | --- | --- | --- |\n")
        for row in record["results"]:
            summary.write(f"| {row['platform']} | {row['jobResult']} | {row['compiled']} | {row['executed']} |\n")
        summary.write("\nAndroid compiles x64 and ARM64; only x64 executes in the emulator. ARM64 device execution remains a separate check. Windows is compile-only on hosted CI. iOS execution is simulator-only. Catalyst runs directly on the Mac runner.\n")
        if record.get('sharedHosting'):
            outcome = json.loads(os.environ['JOB_RESULTS']).get('shared-hosting', {}).get('result', 'missing')
            summary.write(f"\nShared hosting / Android Currency: **{outcome}**. This separate proof executes both MAUI and Avalonia Currency apps even at Compile depth; see the shared-hosting-android artifact.\n")
        if record.get('sharedHostingIos'):
            outcome = json.loads(os.environ['JOB_RESULTS']).get('shared-hosting-ios', {}).get('result', 'missing')
            summary.write(f"\nShared hosting / iOS Currency: **{outcome}**. This separate proof executes both MAUI and Avalonia Currency apps even at Compile depth; see the shared-hosting-ios artifact. Simulator execution does not establish physical-device support.\n")
        if record.get('sharedHostingWindows'):
            outcome = json.loads(os.environ['JOB_RESULTS']).get('shared-hosting-windows', {}).get('result', 'missing')
            summary.write(f"\nShared hosting / Windows Currency: **{outcome}**. Both hosts compile for x64 and ARM64 with identical shared archives per architecture; native API tests execute on the x64 runner. Desktop application execution is a separate local check, not claimed by this job. See shared-hosting-windows-* artifacts.\n")
    if not record["success"]:
        sys.exit("Incomplete verification: " + ", ".join(record["failedJobs"] + record["unverifiedStages"]))


if __name__ == "__main__":
    main()
