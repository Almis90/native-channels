#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-shared-hosting-ios.sh SIMULATOR_UDID}"
OUTPUT="$ROOT/artifacts/shared-hosting-ios"
mkdir -p "$OUTPUT"

run_host() {
    local host="$1" project="$2" package="$3" filename="$4" marker="$5"
    local apps=("$ROOT/samples/$project/bin/Release/net10.0-ios/iossimulator-arm64/"*.app)
    if [[ ${#apps[@]} != 1 || ! -d "${apps[0]}" ]]; then
        printf 'Expected exactly one iOS app bundle for %s; restore and rebuild first.\n' "$project" >&2
        return 1
    fi
    local app="${apps[0]}"
    local container
    xcrun simctl install "$DEVICE" "$app" || return 1
    container="$(xcrun simctl get_app_container "$DEVICE" "$package" data)" || return 1
    [[ -n "$container" && -d "$container" ]] || return 1
    xcrun simctl terminate "$DEVICE" "$package" 2>/dev/null || true
    rm -f "$container/Library/$filename" "$container/Documents/$filename"
    : > "$OUTPUT/$host-stdout.log"
    : > "$OUTPUT/$host-stderr.log"
    xcrun simctl launch --terminate-running-process \
        --stdout="$OUTPUT/$host-stdout.log" --stderr="$OUTPUT/$host-stderr.log" "$DEVICE" "$package" || return 1
    if ! python3 "$ROOT/eng/wait-sample-result.py" --timeout 90 \
        --success '^PASS apple:.*; normalization, errors, concurrency, cancellation$' \
        --logs "$OUTPUT/$host-stdout.log" "$OUTPUT/$host-stderr.log" \
        "$container/Library/$filename" "$container/Documents/$filename" > "$OUTPUT/$host-wait.log" 2>&1; then
        cat "$OUTPUT/$host-wait.log" >&2
        xcrun simctl terminate "$DEVICE" "$package" 2>/dev/null || true
        return 1
    fi
    { printf '%s ' "$marker"; cat "$OUTPUT/$host-wait.log"; } > "$OUTPUT/$host-result.txt"
    cat "$OUTPUT/$host-result.txt"
    xcrun simctl io "$DEVICE" screenshot "$OUTPUT/$host.png" || return 1
    xcrun simctl terminate "$DEVICE" "$package" || return 1
}

# Attempt both hosts so a failed app still leaves diagnostics for its peer.
status=0
run_host maui Maui/Currency dev.nativechannels.currency.sample currency-result.txt CURRENCY_SAMPLE || status=1
run_host avalonia Avalonia/Currency/Currency.iOS dev.nativechannels.currency.avalonia currency-avalonia-result.txt CURRENCY_AVALONIA || status=1
if (( status )); then
    exit "$status"
fi
python3 "$ROOT/eng/verify-shared-hosting.py" results --platform ios
