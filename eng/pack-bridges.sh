#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/artifacts/packages"
# Apple binding builds can retain resources.zip after an XCFramework directory changes.
# Remove generated binding outputs so every package embeds the current native slices.
python3 - "$ROOT" <<'PYTHON'
from pathlib import Path
import shutil, sys
project = Path(sys.argv[1]) / "src/NativeChannels.Native.Apple"
for folder in ("bin", "obj"):
    shutil.rmtree(project / folder / "Release", ignore_errors=True)
PYTHON
bash "$ROOT/eng/dotnet.sh" pack "$ROOT/src/NativeChannels.Native.Android" -c Release -o "$ROOT/artifacts/packages" "$@"
bash "$ROOT/eng/dotnet.sh" pack "$ROOT/src/NativeChannels.Native.Apple" -c Release -o "$ROOT/artifacts/packages" "$@"
