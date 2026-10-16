#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CONFIGURATION="${1:-Debug}"
RID="${2:-maccatalyst-arm64}"
shopt -s nullglob
apps=("$ROOT/samples/NativeChannels.Sample/bin/$CONFIGURATION/net10.0-maccatalyst/$RID/"*.app)
if [[ "${#apps[@]}" -ne 1 ]]; then
    echo 'FAIL: Expected exactly one compiled Catalyst app' >&2
    exit 1
fi
APP="${apps[0]}"
mkdir -p "$ROOT/artifacts"
LOG="$ROOT/artifacts/sample-catalyst-process.log"
"$APP/Contents/MacOS/NativeChannels.Sample" > "$LOG" 2>&1 &
APP_PID=$!
trap 'kill "$APP_PID" 2>/dev/null || true' EXIT
for ((attempt=0; attempt<60; attempt++)); do
    result="$(grep 'NATIVECHANNELS_SAMPLE' "$LOG" || true)"
    if [[ -n "$result" ]]; then
        printf '%s\n' "$result"
        [[ "$result" == *'NATIVECHANNELS_SAMPLE PASS apple:'* && "$result" == *'view stress: 32 reconnects'* ]]
        exit "$?"
    fi
    if ! kill -0 "$APP_PID" 2>/dev/null; then
        cat "$LOG"
        echo 'FAIL: Catalyst process exited before reporting a result' >&2
        exit 1
    fi
    sleep 1
done
cat "$LOG"
echo 'FAIL: No Catalyst sample result within 60 seconds' >&2
exit 1
