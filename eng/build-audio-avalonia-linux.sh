#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
mkdir -p artifacts/packages
if compgen -G 'artifacts/packages/*.nupkg' >/dev/null; then
    echo 'Preserve the existing artifacts/packages feed and start with an empty feed.' >&2
    exit 1
fi
bash eng/dotnet.sh pack src/NativeChannels.Core -c Release -o artifacts/packages
bash eng/dotnet.sh pack src/NativeChannels -c Release -p:NativeChannelsLinuxOnly=true -o artifacts/packages
bash eng/pack-avalonia.sh
python3 eng/cache-dependencies.py
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
PROJECT=plugins/Plugin.AudioStreamer/src/Plugin.AudioStreamer/Plugin.AudioStreamer.csproj
bash eng/dotnet.sh restore "$PROJECT" -p:NativeChannelsLinuxOnly=true --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py "$(dirname "$PROJECT")/obj/project.assets.json"
bash eng/dotnet.sh pack "$PROJECT" -c Release -p:NativeChannelsLinuxOnly=true --no-restore -o artifacts/packages
python3 plugins/Plugin.AudioStreamer/eng/inspect-package.py --linux-only
python3 eng/package_audit.py artifacts/packages/*.nupkg --platform-only linux
python3 eng/clean-consumer.py samples/Avalonia/AudioStreamer/Linux
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
echo "$CACHE" > artifacts/audio-avalonia-linux-cache-path.txt
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
PROJECT=samples/Avalonia/AudioStreamer/Linux/AudioStreamer.Linux.csproj
bash eng/dotnet.sh restore "$PROJECT" -r linux-x64 -p:SelfContained=true --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py "$(dirname "$PROJECT")/obj/project.assets.json"
bash eng/dotnet.sh publish "$PROJECT" -c Release -r linux-x64 -p:SelfContained=true --no-restore -o artifacts/audio-avalonia/linux/app
