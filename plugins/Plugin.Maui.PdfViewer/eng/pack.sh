#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
PROJECT="$ROOT/plugins/Plugin.Maui.PdfViewer/src/Plugin.Maui.PdfViewer/Plugin.Maui.PdfViewer.csproj"
python3 "$ROOT/eng/cache-dependencies.py"
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 "$ROOT/eng/consumer-config.py" "$CACHE/NuGet.Config"
bash "$ROOT/eng/dotnet.sh" restore "$PROJECT" --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 "$ROOT/eng/verify-consumer-assets.py" "$(dirname "$PROJECT")/obj/project.assets.json"
bash "$ROOT/eng/dotnet.sh" pack "$PROJECT" -c Release --no-restore -o "$ROOT/artifacts/packages"
