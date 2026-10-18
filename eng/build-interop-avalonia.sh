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
    *) echo 'Usage: build-interop-avalonia.sh android|ios|windows|macos|linux|browser'; exit 2 ;;
esac
FEED="$ROOT/artifacts/interop-avalonia/$TARGET/packages"
CACHE="$ROOT/artifacts/interop-avalonia/$TARGET/cache"
if [[ -d "$FEED" || -d "$CACHE" ]]; then
    echo "Preserve/remove the previous generated artifacts/interop-avalonia/$TARGET directory before rebuilding." >&2
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
for pattern in ['NativeChannels*','Plugin.Maui.*','Plugin.Avalonia.*']: ET.SubElement(local,'package',pattern=pattern)
ET.SubElement(ET.SubElement(mapping,'packageSource',key='nuget'),'package',pattern='*')
ET.ElementTree(root).write(sys.argv[1],encoding='utf-8',xml_declaration=True)
PY
ARGS=("-p:RestoreConfigFile=$CONFIG" "-p:RestorePackagesPath=$CACHE" -p:EnableWindowsTargeting=true)
if [[ "$TARGET" == android ]]; then
    (cd native/android && ./gradlew --no-daemon :library:assembleRelease :sample:assembleRelease)
elif [[ "$TARGET" == ios || "$TARGET" == macos ]]; then
    APPLE=(); if [[ "$TARGET" == macos ]]; then APPLE=(--macos-only); fi
    python3 eng/build-apple.py "${APPLE[@]}"
fi
pack() { bash eng/dotnet.sh pack "$1" -c Release -o "$FEED" "${ARGS[@]}" "${@:2}"; }
pack src/NativeChannels.Core
if [[ "$TARGET" == android ]]; then pack src/NativeChannels.Native.Android; fi
if [[ "$TARGET" == ios || "$TARGET" == macos ]]; then pack src/NativeChannels.Native.Apple "-p:$FLAG"; fi
pack src/NativeChannels "-p:$FLAG" -p:NativeChannelsUseCorePackage=true
pack src/NativeChannels.Avalonia -p:NativeChannelsUseCorePackage=true
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
build "samples/Avalonia/Interop/$HEAD/Interop.$HEAD.csproj"
echo "Built Avalonia interoperability sample for $TARGET. Run checks in the app separately."
