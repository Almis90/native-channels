#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEVICE="${1:?Usage: verify-android-audio.sh EMULATOR_SERIAL [Debug|Release] [android-arm64|android-x64]}"
CONFIGURATION="${2:-Debug}"
ADB="${ANDROID_HOME:-$HOME/Library/Android/sdk}/platform-tools/adb"
RID="${3:-}"
RUNS="${AUDIO_VERIFICATION_RUNS:-1}"
if [[ ! "$RUNS" =~ ^([1-9]|1[0-9]|20)$ ]]; then
    echo 'AUDIO_VERIFICATION_RUNS must be between 1 and 20' >&2
    exit 2
fi
APK="$ROOT/samples/Maui/AudioStreamer/bin/$CONFIGURATION/net10.0-android/${RID:+$RID/}dev.nativechannels.audio.sample-Signed.apk"
"$ADB" -s "$DEVICE" install --no-incremental -r "$APK"
ACTIVITY="$("$ADB" -s "$DEVICE" shell cmd package resolve-activity --brief dev.nativechannels.audio.sample | tr -d '\r' | tail -1)"
mkdir -p "$ROOT/artifacts/android-consumers"
LOGS="$(mktemp -d "$ROOT/artifacts/android-consumers/audio.XXXXXX")"
for ((run=1; run<=RUNS; run++)); do
    echo "Audio startup verification $run/$RUNS"
    "$ADB" -s "$DEVICE" shell am force-stop dev.nativechannels.audio.sample
    "$ADB" -s "$DEVICE" logcat -c
    "$ADB" -s "$DEVICE" shell am start -W -n "$ACTIVITY"
    passed=false
    for ((attempt=0; attempt<45; attempt++)); do
        result="$("$ADB" -s "$DEVICE" logcat -d -v brief | grep 'AUDIO_SAMPLE' || true)"
        if [[ -n "$result" ]]; then
            printf '%s\n' "$result"
            if [[ "$result" == *'AUDIO_SAMPLE PASS native PCM'* && "$result" != *'AUDIO_SAMPLE FAIL'* ]]; then
                passed=true
            fi
            break
        fi
        sleep 1
    done
    "$ADB" -s "$DEVICE" logcat -d -v threadtime > "$LOGS/start-$run.log"
    if [[ "$passed" != true ]]; then
        "$ADB" -s "$DEVICE" shell dumpsys audio > "$LOGS/audio-focus.txt" || true
        "$ADB" -s "$DEVICE" shell dumpsys activity activities > "$LOGS/activities.txt" || true
        "$ADB" -s "$DEVICE" shell dumpsys appops --package dev.nativechannels.audio.sample > "$LOGS/appops.txt" || true
        echo "FAIL: Audio startup verification $run/$RUNS failed or timed out; logs: $LOGS" >&2
        exit 1
    fi
done
