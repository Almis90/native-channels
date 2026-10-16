#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-android-probe.sh EMULATOR_SERIAL [Debug|Release]}"
CONFIGURATION="${2:-Debug}"
ADB="${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
APK="$ROOT/tests/PackageConsumer/bin/$CONFIGURATION/net10.0-android/dev.nativechannels.probe-Signed.apk"
"$ADB" -s "$DEVICE" install -r "$APK"
"$ADB" -s "$DEVICE" shell am force-stop dev.nativechannels.probe
"$ADB" -s "$DEVICE" logcat -c
ACTIVITY="$("$ADB" -s "$DEVICE" shell cmd package resolve-activity --brief dev.nativechannels.probe | tr -d '\r' | tail -1)"
"$ADB" -s "$DEVICE" shell am start -n "$ACTIVITY"
for ((attempt=0; attempt<45; attempt++)); do
    result="$("$ADB" -s "$DEVICE" logcat -d -v brief | grep 'NATIVECHANNELS_PROBE' || true)"
    if [[ -n "$result" ]]; then
        printf '%s\n' "$result"
        [[ "$result" == *'NATIVECHANNELS_PROBE PASS Kotlin:managed:hello'* ]]
        exit "$?"
    fi
    sleep 1
done
echo 'FAIL: No probe result within 45 seconds' >&2
exit 1
