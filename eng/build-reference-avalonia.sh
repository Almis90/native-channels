#!/usr/bin/env bash
# Build/package only. This script never launches applications or workflows.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
TARGET="${1:-}"
case "$TARGET" in
    android) FLAG=NativeChannelsAndroidOnly=true; HEAD=Android ;;
    ios) FLAG=NativeChannelsAppleTarget=ios; HEAD=iOS ;;
    macos) FLAG=NativeChannelsAppleTarget=macos; HEAD=macOS ;;
    windows) FLAG=NativeChannelsWindowsOnly=true; HEAD=Windows ;;
    linux) FLAG=NativeChannelsLinuxOnly=true; HEAD=Linux ;;
    browser) FLAG=NativeChannelsBrowserOnly=true; HEAD=Browser ;;
    *) echo 'Usage: build-reference-avalonia.sh android|ios|windows|macos|linux|browser'; exit 2 ;;
esac
FEED="$ROOT/artifacts/reference-avalonia/$TARGET/packages"
CACHE="$ROOT/artifacts/reference-avalonia/$TARGET/cache"
if [[ -d "$FEED" || -d "$CACHE" ]]; then
    echo "Preserve/remove the previous generated artifacts/reference-avalonia/$TARGET directory before rebuilding." >&2
    exit 1
fi
mkdir -p "$FEED" "$CACHE"
CONFIG="$CACHE/NuGet.Config"
python3 - "$CONFIG" "$FEED" <<'PY'
import sys, xml.etree.ElementTree as ET
root=ET.Element('configuration'); sources=ET.SubElement(root,'packageSources'); ET.SubElement(sources,'clear')
ET.SubElement(sources,'add',key='local',value=sys.argv[2]); ET.SubElement(sources,'add',key='nuget',value='https://api.nuget.org/v3/index.json')
mapping=ET.SubElement(root,'packageSourceMapping')
local=ET.SubElement(mapping,'packageSource',key='local')
for pattern in ['NativeChannels*','Plugin.*']: ET.SubElement(local,'package',pattern=pattern)
ET.SubElement(ET.SubElement(mapping,'packageSource',key='nuget'),'package',pattern='*')
ET.ElementTree(root).write(sys.argv[1],encoding='utf-8',xml_declaration=True)
PY
ARGS=("-p:RestoreConfigFile=$CONFIG" "-p:RestorePackagesPath=$CACHE" -p:EnableWindowsTargeting=true)
if [[ "$TARGET" == android ]]; then
    (cd native/android && ./gradlew --no-daemon :library:assembleRelease :currency:assembleRelease :audio:assembleRelease :volume:assembleRelease :pdf:assembleRelease)
elif [[ "$TARGET" == ios || "$TARGET" == macos ]]; then
    APPLE=(); if [[ "$TARGET" == macos ]]; then APPLE=(--macos-only); fi
    python3 eng/build-apple.py "${APPLE[@]}"
    for plugin in Plugin.Currency Plugin.AudioStreamer Plugin.VolumeMonitor Plugin.Maui.PdfViewer; do python3 "plugins/$plugin/eng/build-apple.py" "${APPLE[@]}"; done
elif [[ "$TARGET" == browser ]]; then
    (cd plugins/Plugin.Avalonia.PdfViewer && npm ci --ignore-scripts --no-audit --no-fund)
fi
pack() { bash eng/dotnet.sh pack "$1" -c Release -o "$FEED" "${ARGS[@]}" "${@:2}"; }
pack src/NativeChannels.Core
if [[ "$TARGET" == android ]]; then pack src/NativeChannels.Native.Android; fi
if [[ "$TARGET" == ios || "$TARGET" == macos ]]; then pack src/NativeChannels.Native.Apple "-p:$FLAG"; fi
pack src/NativeChannels "-p:$FLAG" -p:NativeChannelsUseCorePackage=true
pack src/NativeChannels.Avalonia -p:NativeChannelsUseCorePackage=true
for plugin in Plugin.Currency Plugin.AudioStreamer Plugin.VolumeMonitor Plugin.Avalonia.PdfViewer; do
    pack "plugins/$plugin/src/$plugin" "-p:$FLAG"
done
if [[ "$TARGET" == android ]]; then
    python3 eng/package_audit.py "$FEED"/*.nupkg --android-only --output "$FEED/audit.json"
else
    python3 eng/package_audit.py "$FEED"/*.nupkg --platform-only "$TARGET" --output "$FEED/audit.json"
fi
BUILD_ARGS=()
if [[ "$TARGET" == linux ]]; then BUILD_ARGS=(-r linux-x64); fi
if [[ "$TARGET" == windows ]]; then BUILD_ARGS=(-r win-x64); fi
build() {
    bash eng/dotnet.sh build "$1" -c Release "${ARGS[@]}" "${BUILD_ARGS[@]}" "${@:2}"
    python3 eng/verify-consumer-assets.py "$(dirname "$1")/obj/project.assets.json" "$FEED"
}
for feature in AudioStreamer VolumeMonitor PdfViewer; do
    if [[ "$feature" == AudioStreamer && "$TARGET" == windows ]]; then
        build samples/Avalonia/AudioStreamer/AudioStreamer.Windows.csproj
    elif [[ "$feature" == PdfViewer && "$TARGET" == linux ]]; then
        for backend in Qt Gtk; do build "samples/Avalonia/PdfViewer/Linux$backend/PdfViewer.Linux$backend.csproj"; done
    else
        build "samples/Avalonia/$feature/$HEAD/$feature.$HEAD.csproj"
    fi
done
case "$TARGET" in
    android|ios|browser) build "samples/Avalonia/Currency/Currency.$HEAD/Currency.$HEAD.csproj" ;;
    *) build samples/Avalonia/Currency/Currency.Desktop/Currency.Desktop.csproj "-p:CurrencyDesktopTarget=$TARGET" ;;
esac
echo "Built all Avalonia reference samples for $TARGET. Runtime verification remains separate."
