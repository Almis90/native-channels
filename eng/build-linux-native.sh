#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    echo 'The current Linux native packages target Linux x64; build on a Linux x64 host.' >&2
    exit 1
fi
mkdir -p artifacts/native/linux-x64
case "${1:-}" in
    currency) SOURCE=plugins/Plugin.Maui.Currency/native/linux/currency.cpp; LIBRARY=nc_currency; DEPS=icu-uc ;;
    audio) SOURCE=plugins/Plugin.Maui.AudioStreamer/native/linux/audio.cpp; LIBRARY=nc_audio; DEPS=alsa ;;
    *) echo 'Usage: build-linux-native.sh currency|audio' >&2; exit 1 ;;
esac
# System headers provide real native APIs; shared objects retain their system SONAME dependencies.
g++ -std=c++17 -O2 -g -Wall -Wextra -Werror -fPIC -shared -pthread \
    -I native/linux/include $(pkg-config --cflags "$DEPS") "$SOURCE" \
    -Wl,-z,defs -Wl,-soname,lib${LIBRARY}.so $(pkg-config --libs "$DEPS") \
    -o "artifacts/native/linux-x64/lib${LIBRARY}.so"
