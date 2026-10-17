#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-ios-volume.sh SIMULATOR_UDID [Debug|Release]}"
CONFIGURATION="${2:-Debug}"
APP="$ROOT/samples/Maui/VolumeMonitor/bin/$CONFIGURATION/net10.0-ios/iossimulator-arm64/VolumeMonitor.Sample.app"
xcrun simctl install "$DEVICE" "$APP"
CONTAINER="$(xcrun simctl get_app_container "$DEVICE" dev.nativechannels.volume.sample data)"
rm -f "$CONTAINER/Library/volume-result.txt" "$CONTAINER/Documents/volume-result.txt"
mkdir -p "$ROOT/artifacts/ios-consumers"
LOGS="$(mktemp -d "$ROOT/artifacts/ios-consumers/volume.XXXXXX")"
printf 'App logs: %s\n' "$LOGS"
xcrun simctl launch --terminate-running-process --stdout="$LOGS/stdout.log" --stderr="$LOGS/stderr.log" "$DEVICE" dev.nativechannels.volume.sample
python3 "$ROOT/eng/wait-sample-result.py" --timeout 45 \
    --success '^PASS initial levels' --logs "$LOGS/stdout.log" "$LOGS/stderr.log" \
    "$CONTAINER/Library/volume-result.txt" "$CONTAINER/Documents/volume-result.txt"
