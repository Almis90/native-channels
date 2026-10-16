"""Record one actual compile or execution step for the coordinating workflow."""
import argparse
import json
import os
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("platform", choices=("android", "ios", "catalyst", "windows"))
parser.add_argument("stage", choices=("compile", "runtime"))
parser.add_argument("--runtime", default="default")
args = parser.parse_args()
record = {"sourceCommit": os.environ["GITHUB_SHA"], "platform": args.platform, "stage": args.stage,
          "runtime": args.runtime, "outcome": os.environ.get("STEP_OUTCOME", "skipped") or "skipped"}
directory = Path("artifacts/evidence")
directory.mkdir(parents=True, exist_ok=True)
(directory / f"{args.platform}-{args.stage}-{args.runtime}.json").write_text(json.dumps(record, indent=2) + "\n")
with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as summary:
    summary.write(f"\n{args.platform} / {args.runtime} / **{args.stage}**: **{record['outcome']}**\n")
