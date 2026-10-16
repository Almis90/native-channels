#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-ios-sample.sh SIMULATOR_UDID [Debug|Release]}"
CONFIGURATION="${2:-Debug}"
APP="$ROOT/samples/NativeChannels.Sample/bin/$CONFIGURATION/net10.0-ios/iossimulator-arm64/NativeChannels.Sample.app"
xcrun simctl install "$DEVICE" "$APP"
CONTAINER="$(xcrun simctl get_app_container "$DEVICE" dev.nativechannels.sample data)"
rm -f "$CONTAINER/Library/sample-result.txt" "$CONTAINER/Documents/sample-result.txt"
mkdir -p "$ROOT/artifacts/ios-consumers"
LOGS="$(mktemp -d "$ROOT/artifacts/ios-consumers/sample.XXXXXX")"
printf 'App logs: %s\n' "$LOGS"
xcrun simctl launch --terminate-running-process --stdout="$LOGS/stdout.log" --stderr="$LOGS/stderr.log" "$DEVICE" dev.nativechannels.sample
python3 "$ROOT/eng/wait-sample-result.py" --timeout 90 \
    --success '^PASS apple:.*view stress: 32 reconnects' --logs "$LOGS/stdout.log" "$LOGS/stderr.log" \
    "$CONTAINER/Library/sample-result.txt" "$CONTAINER/Documents/sample-result.txt"
