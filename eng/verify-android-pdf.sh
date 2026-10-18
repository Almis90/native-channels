#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-android-pdf.sh EMULATOR_SERIAL [Debug|Release] [android-arm64|android-x64]}"
CONFIGURATION="${2:-Debug}"
ADB="${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
RID="${3:-}"
APK="$ROOT/samples/Maui/PdfViewer/bin/$CONFIGURATION/net10.0-android/${RID:+$RID/}dev.nativechannels.pdf.sample-Signed.apk"
"$ADB" -s "$DEVICE" install --no-incremental -r "$APK"
"$ADB" -s "$DEVICE" shell am force-stop dev.nativechannels.pdf.sample
"$ADB" -s "$DEVICE" logcat -c
ACTIVITY="$("$ADB" -s "$DEVICE" shell cmd package resolve-activity --brief dev.nativechannels.pdf.sample | tr -d '\r' | tail -1)"
"$ADB" -s "$DEVICE" shell am start -n "$ACTIVITY"
for ((attempt=0; attempt<90; attempt++)); do
    result="$("$ADB" -s "$DEVICE" logcat -d -v brief | grep 'PDF_SAMPLE' || true)"
    if [[ -n "$result" ]]; then
        printf '%s\n' "$result"
        [[ "$result" == *'PDF_SAMPLE PASS document'* ]]
        exit "$?"
    fi
    sleep 1
done
echo 'FAIL: No sample result within 90 seconds' >&2
exit 1
