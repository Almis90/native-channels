#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
python3 "$ROOT/eng/cache-dependencies.py"
python3 "$ROOT/eng/clean-consumer.py" samples/Currency.Avalonia.Android
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
printf '%s\n' "$CACHE" > "$ROOT/artifacts/currency-avalonia-cache-path.txt"
python3 "$ROOT/eng/consumer-config.py" "$CACHE/NuGet.Config"
bash "$ROOT/eng/dotnet.sh" restore "$ROOT/samples/Currency.Avalonia.Android/Currency.Avalonia.Android.csproj" \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache "$@"
python3 "$ROOT/eng/verify-consumer-assets.py" "$ROOT/samples/Currency.Avalonia.Android/obj/project.assets.json"
