#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
# BenchmarkDotNet's generated projects must also resolve the pinned local SDK.
export DOTNET_ROOT="$ROOT/.tools/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
if [[ $# -eq 0 ]]; then set -- --filter '*' --job short; fi
RESULTS="$ROOT/artifacts/benchmarks/$(date -u +%Y%m%dT%H%M%SZ)-$$"
exec bash eng/dotnet.sh run --project benchmarks/NativeChannels.Benchmarks -c Release -- \
    --artifacts "$RESULTS" "$@"
