#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-android-sample.sh DEVICE_SERIAL [Debug|Release] [android-arm64|android-x64] [APK_PATH]}"
CONFIGURATION="${2:-Debug}"
ADB="${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
RID="${3:-}"
APK="${4:-$ROOT/samples/NativeChannels.Sample/bin/$CONFIGURATION/net10.0-android/${RID:+$RID/}dev.nativechannels.sample-Signed.apk}"
"$ADB" -s "$DEVICE" install --no-incremental -r "$APK"
"$ADB" -s "$DEVICE" shell am force-stop dev.nativechannels.sample
"$ADB" -s "$DEVICE" logcat -c
ACTIVITY="$("$ADB" -s "$DEVICE" shell cmd package resolve-activity --brief dev.nativechannels.sample | tr -d '\r' | tail -1)"
"$ADB" -s "$DEVICE" shell am start -n "$ACTIVITY"
for ((attempt=0; attempt<90; attempt++)); do
    result="$("$ADB" -s "$DEVICE" logcat -d -v brief | grep 'NATIVECHANNELS_SAMPLE' || true)"
    if [[ -n "$result" ]]; then
        printf '%s\n' "$result"
        [[ "$result" == *'NATIVECHANNELS_SAMPLE PASS android:'* && "$result" == *'view stress: 32 reconnects'* && "$result" != *'NATIVECHANNELS_SAMPLE FAIL'* ]]
        exit "$?"
    fi
    sleep 1
done
echo 'FAIL: No sample result within 90 seconds' >&2
exit 1
