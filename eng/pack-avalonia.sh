#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
python3 eng/cache-dependencies.py
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
PROJECT=src/NativeChannels.Avalonia/NativeChannels.Avalonia.csproj
bash eng/dotnet.sh restore "$PROJECT" -p:NativeChannelsUseCorePackage=true \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py src/NativeChannels.Avalonia/obj/project.assets.json
bash eng/dotnet.sh pack "$PROJECT" -c Release -p:NativeChannelsUseCorePackage=true --no-restore -o artifacts/packages
