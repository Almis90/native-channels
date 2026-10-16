#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
LANGUAGE="${1:-csharp}"
CHECK="${2:-}"
if [[ $# -gt 2 || ( -n "$CHECK" && "$CHECK" != '--check' ) ]]; then
    echo 'Usage: bash eng/format.sh [csharp|kotlin|swift|all] [--check]' >&2
    exit 2
fi

format_csharp() {
    bash eng/dotnet.sh tool restore
    local command='format'
    if [[ "$CHECK" == '--check' ]]; then command='check'; fi
    bash eng/dotnet.sh csharpier "$command" src tests samples plugins benchmarks native/windows NativeChannels.slnx \
        eng/NativeChannels.Platforms.slnx eng/NativeChannels.Core.slnx Directory.Build.props Directory.Packages.props
}

format_kotlin() {
    local jar="$ROOT/.tools/formatters/ktfmt-0.54.jar"
    local expected='5e7eb28a0b2006d1cefbc9213bfc73a8191ec2f85d639ec4fc4ec0cd04212e82'
    local java='java'
    if [[ -n "${JAVA_HOME:-}" ]]; then
        java="$JAVA_HOME/bin/java"
    elif [[ -x '/Applications/Android Studio.app/Contents/jbr/Contents/Home/bin/java' ]]; then
        java='/Applications/Android Studio.app/Contents/jbr/Contents/Home/bin/java'
    fi
    mkdir -p "$(dirname "$jar")"
    if [[ ! -f "$jar" ]]; then
        curl --fail --location --silent --show-error \
            'https://repo.maven.apache.org/maven2/com/facebook/ktfmt/0.54/ktfmt-0.54-jar-with-dependencies.jar' \
            --output "$jar"
    fi
    python3 - "$jar" "$expected" <<'PY'
import hashlib
from pathlib import Path
import sys
if hashlib.sha256(Path(sys.argv[1]).read_bytes()).hexdigest() != sys.argv[2]:
    sys.exit('ktfmt checksum mismatch; remove the cached jar and retry.')
PY
    local files=()
    while IFS= read -r -d '' path; do files+=("$path"); done < <(
        git ls-files --cached --others --exclude-standard -z -- 'native/android/*.kt' 'native/android/*.kts' 'plugins/*.kt' 'plugins/*.kts'
    )
    local options=(--kotlinlang-style)
    if [[ "$CHECK" == '--check' ]]; then options+=(--dry-run --set-exit-if-changed); fi
    "$java" -jar "$jar" "${options[@]}" "${files[@]}"
}

format_swift() {
    if [[ "$CHECK" == '--check' ]]; then
        xcrun swift-format lint --strict --recursive native/apple plugins/Plugin.Maui.Currency/native/apple plugins/Plugin.Maui.AudioStreamer/native/apple plugins/Plugin.Maui.VolumeMonitor/native/apple plugins/Plugin.Maui.PdfViewer/native/apple
    else
        xcrun swift-format format --in-place --recursive native/apple plugins/Plugin.Maui.Currency/native/apple plugins/Plugin.Maui.AudioStreamer/native/apple plugins/Plugin.Maui.VolumeMonitor/native/apple plugins/Plugin.Maui.PdfViewer/native/apple
    fi
}

case "$LANGUAGE" in
    csharp) format_csharp ;;
    kotlin) format_kotlin ;;
    swift) format_swift ;;
    all) format_csharp; format_kotlin; format_swift ;;
    *) echo 'Usage: bash eng/format.sh [csharp|kotlin|swift|all] [--check]' >&2; exit 2 ;;
esac
