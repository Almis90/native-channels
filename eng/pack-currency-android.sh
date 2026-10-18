#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
PROJECT=plugins/Plugin.Maui.Currency/src/Plugin.Maui.Currency/Plugin.Maui.Currency.csproj
# Use the existing Android framework feed; this is a development package, not a combined release.
python3 eng/package_audit.py artifacts/packages/*.nupkg --android-only
native/android/gradlew -p native/android --no-daemon :currency:assembleRelease :currency:testReleaseUnitTest
python3 eng/cache-dependencies.py
CACHE="$(mktemp -d "$ROOT/artifacts/consumer-packages.XXXXXX")"
python3 eng/consumer-config.py "$CACHE/NuGet.Config"
bash eng/dotnet.sh restore "$PROJECT" -p:NativeChannelsAndroidOnly=true \
    --packages "$CACHE" --configfile "$CACHE/NuGet.Config" --force --no-http-cache
python3 eng/verify-consumer-assets.py "$(dirname "$PROJECT")/obj/project.assets.json"
bash eng/dotnet.sh pack "$PROJECT" -c Release -p:NativeChannelsAndroidOnly=true --no-restore -o artifacts/packages
python3 plugins/Plugin.Maui.Currency/eng/inspect-package.py --android-only
python3 eng/package_audit.py artifacts/packages/*.nupkg --android-only
