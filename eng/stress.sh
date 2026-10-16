#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
REPETITIONS="${1:-20}"
if [[ $# -gt 1 || ! "$REPETITIONS" =~ ^[1-9][0-9]*$ || ${#REPETITIONS} -gt 3 ]]; then
    echo 'Usage: bash eng/stress.sh [repetitions: 1-999]' >&2
    exit 2
fi
bash eng/dotnet.sh build tests/NativeChannels.Core.Tests -c Release
RESULTS="$ROOT/artifacts/stress/$(date -u +%Y%m%dT%H%M%SZ)-$$"
for ((run=1; run<=REPETITIONS; run++)); do
    echo "Lifecycle stress run $run/$REPETITIONS"
    bash eng/dotnet.sh test tests/NativeChannels.Core.Tests -c Release --no-build --no-restore \
        --filter Category=Stress --logger "trx;LogFileName=run-$run.trx" \
        --results-directory "$RESULTS" --blame-hang-timeout 2m
done
echo "Stress results: $RESULTS"
