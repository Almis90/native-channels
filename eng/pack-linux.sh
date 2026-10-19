#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
mkdir -p artifacts/packages
# A scoped development feed must never accidentally reuse another platform's archives.
if compgen -G 'artifacts/packages/*.nupkg' >/dev/null; then
    echo 'Start with an empty artifacts/packages feed; preserve earlier packages before switching targets.' >&2
    exit 1
fi
bash eng/dotnet.sh pack src/NativeChannels.Core -c Release -o artifacts/packages
bash eng/dotnet.sh pack src/NativeChannels -c Release -p:NativeChannelsLinuxOnly=true -o artifacts/packages
bash eng/pack-avalonia.sh
python3 eng/cache-dependencies.py
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
PROJECT=plugins/Plugin.Currency/src/Plugin.Currency/Plugin.Currency.csproj
bash eng/dotnet.sh restore "$PROJECT" -p:NativeChannelsLinuxOnly=true \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py "$(dirname "$PROJECT")/obj/project.assets.json"
bash eng/dotnet.sh pack "$PROJECT" -c Release -p:NativeChannelsLinuxOnly=true --no-restore -o artifacts/packages
python3 eng/package_audit.py artifacts/packages/*.nupkg --platform-only linux
