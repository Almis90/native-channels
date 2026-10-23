#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
    echo 'The current Linux native packages target Linux x64; build on a Linux x64 host.' >&2
    exit 1
fi
mkdir -p artifacts/native/linux-x64
EXTRA=()
case "${1:-}" in
    sample) SOURCE=native/linux/sample.cpp; LIBRARY=nc_sample; DEPS='gtk+-3.0 x11' ;;
    currency) SOURCE=plugins/Plugin.Currency/native/linux/currency.cpp; LIBRARY=nc_currency; DEPS=icu-uc ;;
    audio) SOURCE=plugins/Plugin.AudioStreamer/native/linux/audio.cpp; LIBRARY=nc_audio; DEPS=alsa ;;
    volume) SOURCE=plugins/Plugin.VolumeMonitor/native/linux/volume.cpp; LIBRARY=nc_volume; DEPS=libpulse ;;
    pdf) bash "$0" pdf-qt; bash "$0" pdf-gtk; exit ;;
    pdf-qt) SOURCE=plugins/Plugin.Avalonia.PdfViewer/native/linux/pdf_qt.cpp; LIBRARY=nc_pdf_qt; DEPS=Qt6Widgets
        QT_INCLUDE="$(pkg-config --variable=includedir Qt6Core)"
        EXTRA=("-I$QT_INCLUDE/QtPdf" "-I$QT_INCLUDE/QtPdfWidgets" -lQt6Pdf -lQt6PdfWidgets) ;;
    pdf-gtk) SOURCE=plugins/Plugin.Avalonia.PdfViewer/native/linux/pdf_gtk.cpp; LIBRARY=nc_pdf_gtk; DEPS='gtk+-3.0 poppler-glib x11' ;;
    *) echo 'Usage: build-linux-native.sh sample|currency|audio|volume|pdf|pdf-qt|pdf-gtk' >&2; exit 1 ;;
esac
# System headers provide real native APIs; shared objects retain their system SONAME dependencies.
g++ -std=c++17 -O2 -g -Wall -Wextra -Werror -fPIC -shared -pthread \
    -I native/linux/include $(pkg-config --cflags $DEPS) "$SOURCE" \
    -Wl,-z,defs -Wl,-soname,lib${LIBRARY}.so $(pkg-config --libs $DEPS) "${EXTRA[@]}" \
    -o "artifacts/native/linux-x64/lib${LIBRARY}.so"
