#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
PROJECT=plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/Plugin.Maui.Currency.csproj
# Reuse the iOS framework feed and Apple native artifact from this workflow run.
python3 eng/package_audit.py artifacts/packages/*.nupkg --platform-only ios
python3 plugins/Plugin.Maui.Currency/eng/build-apple.py
xcrun swiftc -swift-version 5 plugins/Plugin.Maui.Currency/native/apple/Sources/CurrencySymbols.swift \
    plugins/Plugin.Maui.Currency/native/apple/Tests/main.swift -o artifacts/currency-apple-tests
artifacts/currency-apple-tests
python3 eng/cache-dependencies.py
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
bash eng/dotnet.sh restore "$PROJECT" -p:NativeChannelsAppleTarget=ios \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py "$(dirname "$PROJECT")/obj/project.assets.json"
bash eng/dotnet.sh pack "$PROJECT" -c Release -p:NativeChannelsAppleTarget=ios --no-restore -o artifacts/packages
python3 plugins/Plugin.Maui.Currency/eng/inspect-package.py --platform-only ios
python3 eng/package_audit.py artifacts/packages/*.nupkg
