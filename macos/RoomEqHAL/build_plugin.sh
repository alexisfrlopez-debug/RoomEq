#!/usr/bin/env bash
# build_plugin.sh — compile le greffon RoomEqHAL (AudioServerPlugIn macOS).
#
# Produit un binaire UNIVERSEL (arm64 + x86_64) dans RoomEqHAL.driver/Contents/MacOS/.
# Réutilise le noyau C du pont (asio/bridge_core) — aucune dépendance tierce.
#
# Usage :   bash build_plugin.sh [--sign "Developer ID Application: ..."]
#   sans --sign  : signature ad-hoc (tests locaux ; macOS 15+ peut exiger une
#                  vraie signature pour que coreaudiod charge le greffon —
#                  voir INSTALLATION-MACOS.md)
#   avec --sign  : signature pour distribution (Developer ID + notarisation
#                  hors de ce script)
set -euo pipefail
cd "$(dirname "$0")"

SRC=roomeq_hal.cpp
CORE=../../asio/bridge_core/roomeq_asio_bridge.c
INC=../../asio/bridge_core
DRIVER=RoomEqHAL.driver
OUT="$DRIVER/Contents/MacOS/RoomEqHAL"

SIGN_ARGS=("${@:-}")   # tous les arguments passés tels quels à codesign

mkdir -p "$DRIVER/Contents/MacOS" "$DRIVER/Contents/Resources"

echo "==> Compilation (universel arm64 + x86_64)"
clang++ -std=c++17 -O2 -Wall -Wextra -fvisibility=hidden \
    -arch arm64 -arch x86_64 \
    -I"$INC" "$SRC" "$CORE" \
    -dynamiclib \
    -framework CoreFoundation -framework CoreAudio \
    -o "$OUT"

echo "==> Signature"
if [[ ${#SIGN_ARGS[@]} -gt 0 ]]; then
    codesign --force --timestamp --options runtime "${SIGN_ARGS[@]}" "$DRIVER"
else
    # ad-hoc : identité « - », suffisante pour un Mac de développement < macOS 15
    codesign --force --sign - "$DRIVER"
fi

echo "==> Vérifications"
codesign --verify --deep "$DRIVER"
lipo -info "$OUT"
plutil -lint "$DRIVER/Contents/Info.plist"

echo ""
echo "Greffon prêt : $DRIVER"
echo "Installation locale :"
echo "  sudo cp -R $DRIVER /Library/Audio/Plug-Ins/HAL/"
echo "  sudo killall coreaudiod   # (rechargement automatique)"
echo "Le périphérique « RoomEq Virtual » apparaît dans Réglages Système › Son."
