#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-ios-probe.sh SIMULATOR_UDID [Debug|Release]}"
CONFIGURATION="${2:-Debug}"
APP="$ROOT/tests/PackageConsumer/bin/$CONFIGURATION/net10.0-ios/iossimulator-arm64/NativeChannels.PackageConsumer.app"
xcrun simctl install "$DEVICE" "$APP"
CONTAINER="$(xcrun simctl get_app_container "$DEVICE" dev.nativechannels.probe data)"
rm -f "$CONTAINER/Library/probe-result.txt" "$CONTAINER/Documents/probe-result.txt"
xcrun simctl launch --terminate-running-process "$DEVICE" dev.nativechannels.probe
for ((attempt=0; attempt<45; attempt++)); do
    for result in "$CONTAINER/Library/probe-result.txt" "$CONTAINER/Documents/probe-result.txt"; do
        if [[ -f "$result" ]]; then
            cat "$result"
            grep -q '^PASS Swift:managed:hello$' "$result"
            exit "$?"
        fi
    done
    sleep 1
done
echo 'FAIL: No probe result within 45 seconds' >&2
exit 1
