#!/usr/bin/env bash
# build_pkg.sh — pipeline complet macOS : application + greffon RoomEqHAL + paquet .pkg.
#
# Usage :  bash installer/macos/build_pkg.sh [arm64|x64|both]     (défaut : both)
#
# Produit dans installer/macos/build/ :
#   RoomEq-<version>-macos-<arch>.pkg   (installateur : RoomEq.app + greffon)
#
# Signature : ad-hoc (codesign -). Pour la distribution, re-signez avec un
# Developer ID et notarisez (voir INSTALLATION-MACOS.md) — équivalent macOS
# de la signature test/attestation Windows.
set -euo pipefail
cd "$(dirname "$0")/../.."

ARCH="${1:-both}"
VERSION="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' Directory.Build.props | head -1)"
OUT="installer/macos/build"

echo "==> RoomEq $VERSION — cible(s) : $ARCH"

# ---------------------------------------------------------------- 1. greffon
echo "==> [1/3] Greffon RoomEqHAL (universel arm64 + x86_64)"
bash macos/RoomEqHAL/build_plugin.sh

# ---------------------------------------------------------------- 2. app
publish_app() {
    local rid="$1" name="$2"
    echo "==> [2/3] Publication application ($name)"
    local pub="$OUT/$name/app"
    rm -rf "$pub"
    dotnet publish src/RoomEq.App -c Release -r "$rid" --self-contained true -o "$pub"

    local bundle="$OUT/$name/RoomEq.app"
    rm -rf "$bundle"
    mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
    cp -R "$pub/." "$bundle/Contents/MacOS/"
    cp installer/macos/roomeq.icns "$bundle/Contents/Resources/"

    cat > "$bundle/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>fr</string>
    <key>CFBundleExecutable</key>
    <string>RoomEq.App</string>
    <key>CFBundleIconFile</key>
    <string>roomeq</string>
    <key>CFBundleIdentifier</key>
    <string>com.roomeq.RoomEq</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>RoomEq</string>
    <key>CFBundleDisplayName</key>
    <string>RoomEq</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.music</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <!-- Capture micro (calibration) : macOS exige la description + le consentement -->
    <key>NSMicrophoneUsageDescription</key>
    <string>RoomEq utilise le micro pour mesurer la réponse acoustique de la pièce et construire la correction d'égalisation.</string>
    <key>NSSupportsAutomaticGraphicsSwitching</key>
    <true/>
</dict>
</plist>
PLIST

    # signature ad-hoc de TOUS les éléments embarqués (dylib Skia, apphost…)
    codesign --force --deep --sign - "$bundle"
}

# ---------------------------------------------------------------- 3. pkg
make_pkg() {
    local name="$1"
    local stage="$OUT/$name/stage"
    echo "==> [3/3] Paquet .pkg ($name)"
    rm -rf "$stage"
    mkdir -p "$stage/Applications" "$stage/Library/Audio/Plug-Ins/HAL"
    cp -R "$OUT/$name/RoomEq.app" "$stage/Applications/"
    cp -R macos/RoomEqHAL/RoomEqHAL.driver "$stage/Library/Audio/Plug-Ins/HAL/"

    pkgbuild \
        --root "$stage" \
        --scripts installer/macos/scripts \
        --identifier com.roomeq.RoomEq \
        --version "$VERSION" \
        --install-location / \
        "$OUT/RoomEq-$VERSION-macos-$name.pkg"

    echo "    → $OUT/RoomEq-$VERSION-macos-$name.pkg"
}

case "$ARCH" in
    arm64|x64)
        publish_app "osx-$ARCH" "$ARCH"
        make_pkg "$ARCH"
        ;;
    both)
        publish_app osx-arm64 arm64
        publish_app osx-x64 x64
        make_pkg arm64
        make_pkg x64
        ;;
    *)
        echo "architecture inconnue : $ARCH (attendu arm64|x64|both)" >&2
        exit 2
        ;;
esac

echo ""
echo "Paquet(s) prêt(s) :"
ls -1 "$OUT"/*.pkg || true
echo ""
echo "Installation en développement :"
echo "  sudo installer -pkg $OUT/RoomEq-$VERSION-macos-$(uname -m | sed 's/arm64/arm64/').pkg -target /"
echo "  (ou double-clic : Installer.app)"
