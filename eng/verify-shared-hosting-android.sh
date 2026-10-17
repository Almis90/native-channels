#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-shared-hosting-android.sh EMULATOR_SERIAL}"
ADB="${ANDROID_HOME:?Set ANDROID_HOME}/platform-tools/adb"
OUTPUT="$ROOT/artifacts/shared-hosting"
mkdir -p "$OUTPUT"

run_host() {
    local host="$1" project="$2" package="$3" marker="$4"
    local apk="$ROOT/samples/$project/bin/Release/net10.0-android/android-x64/$package-Signed.apk"
    local activity pid result
    "$ADB" -s "$DEVICE" install -r "$apk" || return 1
    "$ADB" -s "$DEVICE" shell am force-stop "$package" || return 1
    "$ADB" -s "$DEVICE" logcat -c || return 1
    activity="$("$ADB" -s "$DEVICE" shell cmd package resolve-activity --brief "$package" | tr -d '\r' | tail -1)" || return 1
    [[ "$activity" == "$package/"* ]] || { echo "Cannot resolve $package activity: $activity" >&2; return 1; }
    "$ADB" -s "$DEVICE" shell am start -W -n "$activity" || return 1
    for ((attempt=0; attempt<90; attempt++)); do
        "$ADB" -s "$DEVICE" logcat -d -v brief > "$OUTPUT/$host-logcat.txt" || return 1
        pid="$("$ADB" -s "$DEVICE" shell pidof "$package" | tr -d '\r')" || pid=''
        if [[ -n "$pid" ]]; then
            "$ADB" -s "$DEVICE" logcat -d -v brief --pid="$pid" > "$OUTPUT/$host-result.txt" || return 1
            result="$(grep "$marker " "$OUTPUT/$host-result.txt" || true)"
            if [[ -n "$result" ]]; then
                printf '%s\n' "$result"
                [[ "$result" == *"$marker PASS android:"* && "$result" != *"$marker FAIL"* ]] || return 1
                "$ADB" -s "$DEVICE" shell am force-stop "$package"
                return 0
            fi
        fi
        sleep 1
    done
    echo "FAIL: No $host result within 90 seconds (see $OUTPUT/$host-logcat.txt)" >&2
    return 1
}

# Attempt both hosts even if one fails, so both diagnostics are available.
status=0
run_host maui Currency.Sample dev.nativechannels.currency.sample CURRENCY_SAMPLE || status=1
run_host avalonia Avalonia/Currency/Currency.Android dev.nativechannels.currency.avalonia CURRENCY_AVALONIA || status=1
if (( status )); then
    exit "$status"
fi
python3 "$ROOT/eng/verify-shared-hosting.py" results
