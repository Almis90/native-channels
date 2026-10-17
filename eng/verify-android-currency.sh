#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-android-currency.sh EMULATOR_SERIAL [Debug|Release] [android-arm64|android-x64]}"
CONFIGURATION="${2:-Debug}"
ADB="${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
RID="${3:-}"
APK="$ROOT/samples/Maui/Currency/bin/$CONFIGURATION/net10.0-android/${RID:+$RID/}dev.nativechannels.currency.sample-Signed.apk"
"$ADB" -s "$DEVICE" install -r "$APK"
"$ADB" -s "$DEVICE" shell am force-stop dev.nativechannels.currency.sample
"$ADB" -s "$DEVICE" logcat -c
ACTIVITY="$("$ADB" -s "$DEVICE" shell cmd package resolve-activity --brief dev.nativechannels.currency.sample | tr -d '\r' | tail -1)"
"$ADB" -s "$DEVICE" shell am start -n "$ACTIVITY"
for ((attempt=0; attempt<45; attempt++)); do
    result="$("$ADB" -s "$DEVICE" logcat -d -v brief | grep 'CURRENCY_SAMPLE' || true)"
    if [[ -n "$result" ]]; then
        printf '%s\n' "$result"
        [[ "$result" == *'CURRENCY_SAMPLE PASS android:'* ]]
        exit "$?"
    fi
    sleep 1
done
echo 'FAIL: No sample result within 45 seconds' >&2
exit 1
