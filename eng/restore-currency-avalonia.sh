#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TARGET=Android
MARKER=currency-avalonia
target_args=()
if [[ "${1:-}" == ios ]]; then
    TARGET=iOS
    MARKER=currency-avalonia-ios
    shift
elif [[ "${1:-}" == browser ]]; then
    TARGET=Browser
    MARKER=currency-avalonia-browser
    shift
elif [[ "${1:-}" == linux ]]; then
    TARGET=Desktop
    MARKER=currency-avalonia-linux
    target_args=(-p:CurrencyDesktopTarget=linux)
    shift
elif [[ "${1:-}" == macos ]]; then
    TARGET=Desktop
    MARKER=currency-avalonia-macos
    shift
elif [[ "${1:-}" == catalyst ]]; then
    TARGET=iOS
    MARKER=currency-avalonia-ios
    target_args=(-p:CurrencyAppleTarget=maccatalyst)
    shift
fi
PROJECT="samples/Avalonia/Currency/Currency.$TARGET"
python3 "$ROOT/eng/cache-dependencies.py"
if [[ "$TARGET" == Desktop ]]; then
    python3 "$ROOT/eng/clean-consumer.py" "$PROJECT" "$MARKER"
else
    python3 "$ROOT/eng/clean-consumer.py" "$PROJECT"
fi
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
printf '%s\n' "$CACHE" > "$ROOT/artifacts/$MARKER-cache-path.txt"
python3 "$ROOT/eng/consumer-config.py" "$CACHE/NuGet.Config"
bash "$ROOT/eng/dotnet.sh" restore "$ROOT/$PROJECT/Currency.$TARGET.csproj" \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache ${target_args[@]+"${target_args[@]}"} "$@"
python3 "$ROOT/eng/verify-consumer-assets.py" "$ROOT/$PROJECT/obj/project.assets.json"
