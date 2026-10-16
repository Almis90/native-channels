#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
if (( $# )); then
    echo 'Usage: bash eng/pack-android.sh (framework only; use the optional example workflow for plugins)' >&2
    exit 2
fi
# Android development archives use the same identities as full packages. Never mix the feeds.
shopt -s nullglob
existing=(artifacts/packages/*.nupkg artifacts/packages/*.snupkg)
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" != true ]] && (( ${#existing[@]} )); then
    echo 'Use a clean checkout/feed: artifacts/packages already contains package archives.' >&2
    exit 1
fi
mkdir -p artifacts/packages
shared_args=()
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" == true ]]; then
    shared_args=("-p:NativeChannelsUseCorePackage=true" "-p:RestoreConfigFile=$ROOT/artifacts/shared-core.NuGet.Config" "-p:RestorePackagesPath=$ROOT/artifacts/framework-cache")
else
    bash eng/dotnet.sh pack src/NativeChannels.Core -c Release -o artifacts/packages
fi
bash eng/dotnet.sh pack src/NativeChannels.Native.Android -c Release -o artifacts/packages "${shared_args[@]}"
for project in NativeChannels NativeChannels.Maui; do
    bash eng/dotnet.sh pack "src/$project" -c Release -p:NativeChannelsAndroidOnly=true -o artifacts/packages "${shared_args[@]}"
done
python3 eng/inspect-packages.py --android-only
python3 eng/package_audit.py artifacts/packages/*.nupkg --android-only
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" == true ]]; then
    python3 .github/scripts/shared-core.py verify
fi
