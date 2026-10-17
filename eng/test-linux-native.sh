#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p artifacts/native/linux-tests
g++ -std=c++17 -g -O1 -Wall -Wextra -Werror -pthread -fsanitize=address,undefined -fno-omit-frame-pointer \
    -I native/linux/include -I plugins/Plugin.Maui.AudioStreamer/native/linux \
    tests/native/linux/audio_tests.cpp -o artifacts/native/linux-tests/audio-tests
ASAN_OPTIONS=detect_leaks=1 artifacts/native/linux-tests/audio-tests
