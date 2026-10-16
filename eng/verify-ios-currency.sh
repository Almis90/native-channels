#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-ios-currency.sh SIMULATOR_UDID [Debug|Release]}"
CONFIGURATION="${2:-Debug}"
APP="$ROOT/samples/Currency.Sample/bin/$CONFIGURATION/net10.0-ios/iossimulator-arm64/Currency.Sample.app"
xcrun simctl install "$DEVICE" "$APP"
CONTAINER="$(xcrun simctl get_app_container "$DEVICE" dev.nativechannels.currency.sample data)"
rm -f "$CONTAINER/Library/currency-result.txt" "$CONTAINER/Documents/currency-result.txt"
mkdir -p "$ROOT/artifacts/ios-consumers"
LOGS="$(mktemp -d "$ROOT/artifacts/ios-consumers/currency.XXXXXX")"
printf 'App logs: %s\n' "$LOGS"
xcrun simctl launch --terminate-running-process --stdout="$LOGS/stdout.log" --stderr="$LOGS/stderr.log" "$DEVICE" dev.nativechannels.currency.sample
python3 "$ROOT/eng/wait-sample-result.py" --timeout 45 \
    --success '^PASS apple:' --logs "$LOGS/stdout.log" "$LOGS/stderr.log" \
    "$CONTAINER/Library/currency-result.txt" "$CONTAINER/Documents/currency-result.txt"
