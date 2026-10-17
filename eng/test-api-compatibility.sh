#!/usr/bin/env bash
# Prove the real validator rejects a source-compatible but binary-breaking optional parameter.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROBE="$(mktemp -d)"
trap 'rm -rf "$PROBE"' EXIT
cat > "$PROBE/Probe.csproj" <<'XML'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>ApiProbe</PackageId></PropertyGroup></Project>
XML
printf '%s\n' 'public static class Api { public static void Call(int value) {} }' > "$PROBE/Api.cs"
bash "$ROOT/eng/dotnet.sh" pack "$PROBE/Probe.csproj" -o "$PROBE/baseline" --verbosity quiet
bash "$ROOT/eng/dotnet.sh" apicompat package "$PROBE/baseline/ApiProbe.1.0.0.nupkg" \
    --baseline-package "$PROBE/baseline/ApiProbe.1.0.0.nupkg" --run-api-compat
printf '%s\n' 'public static class Api { public static void Call(int value, bool optional = false) {} }' > "$PROBE/Api.cs"
bash "$ROOT/eng/dotnet.sh" pack "$PROBE/Probe.csproj" -o "$PROBE/broken" --verbosity quiet
if bash "$ROOT/eng/dotnet.sh" apicompat package "$PROBE/broken/ApiProbe.1.0.0.nupkg" \
    --baseline-package "$PROBE/baseline/ApiProbe.1.0.0.nupkg" --run-api-compat > "$PROBE/result.log" 2>&1; then
    echo 'FAIL: API validator accepted a removed method signature' >&2
    exit 1
fi
grep -q CP0002 "$PROBE/result.log"
echo 'PASS: API validator rejected a removed method signature (CP0002)'
