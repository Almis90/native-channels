#!/usr/bin/env bash
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$ROOT/artifacts/ios-consumers"
{
    date -u
    xcrun simctl list devices
    uptime
    ps -axo pid,pcpu,pmem,comm | sort -k2 -nr | head -25
    if [[ -n "${1:-}" ]]; then
        xcrun simctl spawn "$1" log show --last 3m --style compact \
            --predicate 'process CONTAINS "Sample" OR process == "SpringBoard"'
    fi
} > "$ROOT/artifacts/ios-consumers/simulator-diagnostics.log" 2>&1
