#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
TARGET="${1:-ios}"
if [[ $# -gt 1 || ( "$TARGET" != ios && "$TARGET" != maccatalyst && "$TARGET" != macos ) ]]; then
    echo 'Usage: bash eng/pack-apple.sh [ios|maccatalyst|macos] (framework only)' >&2
    exit 2
fi
shopt -s nullglob
existing=(artifacts/packages/*.nupkg artifacts/packages/*.snupkg)
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" != true ]] && (( ${#existing[@]} )); then
    echo 'Use a clean checkout/feed: artifacts/packages already contains package archives.' >&2
    exit 1
fi
mkdir -p artifacts/packages
# Avoid retaining a resources.zip generated from another XCFramework or target.
rm -rf src/NativeChannels.Native.Apple/bin/Release src/NativeChannels.Native.Apple/obj/Release
shared_args=()
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" == true ]]; then
    shared_args=("-p:NativeChannelsUseCorePackage=true" "-p:RestoreConfigFile=$ROOT/artifacts/shared-core.NuGet.Config" "-p:RestorePackagesPath=$ROOT/artifacts/framework-cache")
else
    bash eng/dotnet.sh pack src/NativeChannels.Core -c Release -o artifacts/packages
fi
projects=(NativeChannels.Native.Apple NativeChannels)
if [[ "$TARGET" != macos ]]; then projects+=(NativeChannels.Maui); fi
for project in "${projects[@]}"; do
    bash eng/dotnet.sh pack "src/$project" -c Release "-p:NativeChannelsAppleTarget=$TARGET" -o artifacts/packages ${shared_args[@]+"${shared_args[@]}"}
done
python3 eng/inspect-packages.py --platform-only "$TARGET"
python3 eng/package_audit.py artifacts/packages/*.nupkg --platform-only "$TARGET"
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" == true ]]; then
    python3 .github/scripts/shared-core.py verify --platform "$TARGET"
fi
