#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
python3 "$ROOT/eng/cache-dependencies.py"
python3 "$ROOT/eng/clean-consumer.py" "samples/Maui/VolumeMonitor"
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
printf "%s\n" "$CACHE" > "$ROOT/artifacts/volume-cache-path.txt"
python3 "$ROOT/eng/consumer-config.py" "$CACHE/NuGet.Config"
bash "$ROOT/eng/dotnet.sh" restore "$ROOT/samples/Maui/VolumeMonitor/VolumeMonitor.Sample.csproj" \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache "$@"

python3 "$ROOT/eng/verify-consumer-assets.py" "$ROOT/samples/Maui/VolumeMonitor/obj/project.assets.json"
