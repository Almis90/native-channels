#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/.tools"
if [[ ! -x "$ROOT/.tools/dotnet/dotnet" ]]; then
    curl --fail --location --proto '=https' https://dot.net/v1/dotnet-install.sh -o "$ROOT/.tools/dotnet-install.sh"
    bash "$ROOT/.tools/dotnet-install.sh" --version 10.0.401 --install-dir "$ROOT/.tools/dotnet" --no-path
fi
if [[ "${1:-}" == "--platforms" ]]; then
    bash "$ROOT/eng/dotnet.sh" workload install maui-android maui-ios maui-maccatalyst --version 10.0.401
fi
