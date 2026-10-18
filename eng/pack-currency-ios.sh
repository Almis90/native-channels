#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
TARGET="${1:-ios}"
if [[ $# -gt 1 || ( "$TARGET" != ios && "$TARGET" != maccatalyst && "$TARGET" != macos ) ]]; then
    echo "Usage: bash eng/pack-currency-ios.sh [ios|maccatalyst|macos]" >&2
    exit 2
fi
native_args=()
if [[ "$TARGET" == macos ]]; then native_args=(--macos-only); fi
PROJECT=plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/Plugin.Maui.Currency.csproj
# Reuse the selected framework feed and Apple native artifact from this workflow run.
python3 eng/package_audit.py artifacts/packages/*.nupkg --platform-only "$TARGET"
python3 plugins/Plugin.Maui.Currency/eng/build-apple.py ${native_args[@]+"${native_args[@]}"}
xcrun swiftc -swift-version 5 plugins/Plugin.Maui.Currency/native/apple/Sources/CurrencySymbols.swift \
    plugins/Plugin.Maui.Currency/native/apple/Tests/main.swift -o artifacts/currency-apple-tests
artifacts/currency-apple-tests
python3 eng/cache-dependencies.py
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
bash eng/dotnet.sh restore "$PROJECT" "-p:NativeChannelsAppleTarget=$TARGET" \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py "$(dirname "$PROJECT")/obj/project.assets.json"
bash eng/dotnet.sh pack "$PROJECT" -c Release "-p:NativeChannelsAppleTarget=$TARGET" --no-restore -o artifacts/packages
python3 plugins/Plugin.Maui.Currency/eng/inspect-package.py --platform-only "$TARGET"
python3 eng/package_audit.py artifacts/packages/*.nupkg
