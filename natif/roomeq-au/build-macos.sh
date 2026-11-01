#!/bin/bash
# RoomEq AU — construction et validation du greffon .component (spec I-AUD-07).
# Usage (macOS, Xcode + CMake installés) :
#   bash natif/roomeq-au/build-macos.sh  # build + tests + signature + installation
#                                        # utilisateur + validation auval
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
BUILD="$ROOT/build-macos"
COMPONENT="$BUILD/RoomEq.component"

echo "── 1/4 Compilation (cœur C++ + tests natifs + greffon AU) ─────────────"
# CMAKE_OSX_ARCHITECTURES via la ligne de commande : le set() du CMakeLists
# (après project()) ne produit pas de binaire universel — garantie Apple
# Silicon + Intel (vérifié par le contrôle d'architectures ci-dessous).
cmake -S "$ROOT" -B "$BUILD" -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_OSX_ARCHITECTURES="arm64;x86_64" >/dev/null
cmake --build "$BUILD" --parallel

echo "── 1bis/4 Contrôle du bundle universel ───────────────────────────────"
ARCHS=$(lipo -archs "$COMPONENT/Contents/MacOS/RoomEq")
echo "Architectures du greffon : $ARCHS"
echo "$ARCHS" | grep -q arm64
echo "$ARCHS" | grep -q x86_64

echo "── 2/4 Tests natifs du cœur (parité avec RoomEq.Core) ─────────────────"
"$BUILD/roomeq_au_tests" "$ROOT/tests/data"

echo "── 3/4 Signature ad hoc (chargeable localement) ──────────────────────"
codesign --force --sign - "$COMPONENT"

echo "── 4/4 Validation auval (outil officiel d'Apple) ─────────────────────"
# auval ne voit que les composants ENREGISTRÉS : installer le bundle fraîchement
# construit AVANT de valider (recette de la CI, job plugin-au)
DEST="$HOME/Library/Audio/Plug-Ins/Components"
mkdir -p "$DEST"
rm -rf "$DEST/RoomEq.component"
cp -R "$COMPONENT" "$DEST/"
# un .component téléchargé peut porter un attribut de quarantaine qui
# bloque le chargement dans les DAW : le retirer explicitement
xattr -cr "$DEST/RoomEq.component" 2>/dev/null || true
killall -9 AudioComponentRegistrar 2>/dev/null || true
sleep 3
auval -v aufx Rmeq Rmeq

echo ""
echo "Installé et validé : $DEST/RoomEq.component"
echo "Relancez Ableton Live (ou rescannez les greffons AudioUnit) :"
echo "  Ableton Live › Préférences › Plug-ins › Utiliser les périphériques Audio Unit"
echo "  Le greffon apparaît comme « RoomEq: Correction »."

echo ""
echo "Bundle : $COMPONENT"
