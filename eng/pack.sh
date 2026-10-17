#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
projects=(NativeChannels.Core NativeChannels NativeChannels.Maui)
shared_args=()
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" == true ]]; then
    projects=(NativeChannels NativeChannels.Maui)
    shared_args=(-p:NativeChannelsUseCorePackage=true
        "-p:RestoreConfigFile=$ROOT/artifacts/shared-core.NuGet.Config"
        "-p:RestorePackagesPath=$ROOT/artifacts/framework-cache")
fi
bash "$ROOT/eng/pack-bridges.sh" ${shared_args[@]+"${shared_args[@]}"}
for project in "${projects[@]}"; do
    bash "$ROOT/eng/dotnet.sh" pack "$ROOT/src/$project" -c Release -o "$ROOT/artifacts/packages" ${shared_args[@]+"${shared_args[@]}"}
done
if [[ "${NATIVECHANNELS_SHARED_CORE:-false}" == true ]]; then
    python3 "$ROOT/.github/scripts/shared-core.py" verify
fi
