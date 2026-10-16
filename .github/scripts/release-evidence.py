"""Require full, same-source framework evidence before assembling release packages."""
import json
import os
from pathlib import Path


def validate(record, sha):
    expected = {"android", "ios", "catalyst", "windows"}
    if (record.get("sourceCommit") != sha or record.get("success") is not True
            or record.get("depth") != "Runtime" or set(record.get("platforms", [])) != expected
            or record.get("core") is not True or record.get("failedJobs") or record.get("unverifiedStages")):
        raise ValueError("Release packaging requires a successful All / Runtime framework run at this commit")
    rows = record.get("results", [])
    if len(rows) != 4 or {row.get("platform") for row in rows} != expected:
        raise ValueError("Incomplete platform results")
    required_compiles = {"android": {"android-x64", "android-arm64"}, "ios": {"iossimulator-arm64"},
                         "catalyst": {"maccatalyst-arm64"}, "windows": {"win-x64", "win-arm64"}}
    for row in rows:
        if (row.get("selected") is not True or row.get("compiled") is not True or row.get("jobResult") != "success"
                or set(row.get("compiledRuntimes", [])) != required_compiles[row["platform"]]
                or (row["platform"] != "windows" and row.get("executed") is not True)):
            raise ValueError(f"Incomplete evidence for {row['platform']}")


if __name__ == "__main__":
    validate(json.loads(Path("artifacts/verification.json").read_text()), os.environ["GITHUB_SHA"])
    print("PASS: all platforms compiled; Android, iOS simulator and Catalyst executed at this source commit")
