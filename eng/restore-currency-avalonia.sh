#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET=Android
MARKER=currency-avalonia
if [[ "${1:-}" == ios ]]; then
    TARGET=iOS
    MARKER=currency-avalonia-ios
    shift
fi
python3 "$ROOT/eng/cache-dependencies.py"
python3 "$ROOT/eng/clean-consumer.py" "samples/Currency.Avalonia.$TARGET"
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
printf '%s\n' "$CACHE" > "$ROOT/artifacts/$MARKER-cache-path.txt"
python3 "$ROOT/eng/consumer-config.py" "$CACHE/NuGet.Config"
bash "$ROOT/eng/dotnet.sh" restore "$ROOT/samples/Currency.Avalonia.$TARGET/Currency.Avalonia.$TARGET.csproj" \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache "$@"
python3 "$ROOT/eng/verify-consumer-assets.py" "$ROOT/samples/Currency.Avalonia.$TARGET/obj/project.assets.json"
