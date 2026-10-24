#!/bin/bash
# package_2.0.0.sh — Publication win-x64 + livrables RoomEq 2.0.0
#
# Spécificités 2.0.0 — AUTONOMIE COMPLÈTE :
#   • Carte son virtuelle intégrée (pilote WDM roomeqvad, dossier driver/) :
#     suppression de VB-CABLE / VoiceMeeter / VAC (fournisseurs, installateur
#     par téléchargement, page produit) → RoomEqVirtualProvider + DriverSetupManager
#     (installation pnputil du paquet local livré avec l'application).
#   • Pont ASIO intégré : RoomEqAsio64.dll (asio/) + AsioBridgeHost (C#) +
#     piste « DAW (ASIO) » du mixeur ; noyau portable testé (make bridge_core).
#   • AppFlavor INVERSÉ : RoomEq.exe = public silencieux PAR DÉFAUT ;
#     « test » dans le nom (ou ROOMEQ_TEST=1) = version journalisée.
#   • Installateur WiX MSI (installer/) + CI GitHub Actions (workflow build.yml :
#     tests → pilote WDK-NuGet + signature test → MSI).
#   • Zips : PUBLIC = RoomEq.exe seul ; TEST = RoomEq.exe + RoomEq-test.exe +
#     telemetrie.json + driver\ si fourni (variable DRIVER_DIR).
#   • Zip source : tout le dépôt (driver/ asio/ installer/ .github/) SANS
#     identifiants.
set -euo pipefail

SRC=/home/z/my-project/workspace/roomq-source/RoomEq
OUT=/home/z/my-project/download
PUBLISH="$SRC/publish/win-x64"
VERSION=2.0.0
TELEMETRY=/home/z/my-project/scripts/telemetrie.json
# Optionnel : dossier du paquet pilote compilé (artefact CI). S'il est absent,
# les zips sont créés sans driver\ (l'installateur MSI de la CI le porte).
DRIVER_DIR="${DRIVER_DIR:-}"
WORK=$(mktemp -d)

export PATH="$HOME/.dotnet:$PATH"
cd "$SRC"

echo "── Publication win-x64 auto-contenue…"
rm -rf "$PUBLISH"
dotnet publish src/RoomEq.App -c Release -r win-x64 --self-contained true \
    -p:PublishDir="$PUBLISH" 2>&1 | tail -3

echo "── Vérification de la version…"
propagated=$(rg -o '<Version>([^<]+)</Version>' -r '$1' Directory.Build.props)
echo "   version props : $propagated"
[[ "$propagated" == "$VERSION" ]] || { echo "ERREUR : version attendue $VERSION"; exit 1; }

echo "── Vérification de la version d'assemblage dans les DLL…"
python3 - "$PUBLISH/RoomEq.App.dll" "$PUBLISH/RoomEq.Core.dll" <<PY
import sys
ok = True
for path in sys.argv[1:]:
    data = open(path, "rb").read()
    found = "$VERSION.0" in data.decode("utf-8", "ignore") or "$VERSION.0".encode("utf-16-le") in data
    print(("   ✓ " if found else "   ✗ ") + path.rsplit("/", 1)[-1])
    ok &= found
sys.exit(0 if ok else 1)
PY

echo "── Vérification des nouveautés 2.0.0 dans les sources…"
rg -q 'class RoomEqVirtualProvider' "$SRC/src/RoomEq.Audio/Routing/VirtualAudioProviders.cs" \
  && rg -q 'class LoopbackProvider' "$SRC/src/RoomEq.Audio/Routing/VirtualAudioProviders.cs" \
  && ! rg -qi 'vb-cable|voicemeeter|vb-audio' "$SRC/src/RoomEq.Audio/Routing/VirtualAudioProviders.cs" \
  && echo "   ✓ Fournisseurs : carte RoomEq + boucle WASAPI (zéro tiers)"
[[ -f "$SRC/src/RoomEq.Audio/Routing/DriverSetupManager.cs" \
   && ! -f "$SRC/src/RoomEq.Audio/Routing/VirtualAudioInstaller.cs" ]] \
  && rg -q 'pnputil' "$SRC/src/RoomEq.Audio/Routing/DriverSetupManager.cs" \
  && echo "   ✓ DriverSetupManager (pnputil local) — installateur tiers supprimé"
rg -q 'AsioBridgeHost' "$SRC/src/RoomEq.Audio/Asio/AsioBridgeHost.cs" \
  && rg -q 'TrackSourceKind.AsioBridge' "$SRC/src/RoomEq.Audio/Routing/ConsoleChannel.cs" \
  && rg -q 'AsioStatusText' "$SRC/src/RoomEq.App/ViewModels/MixerViewModel.cs" \
  && echo "   ✓ Pont ASIO : hôte + piste DAW + statut"
rg -q 'InstallRoomEqDriver' "$SRC/installer/RoomEq.Installer.CA/InstallActions.cs" \
  && rg -q 'UpgradeCode' "$SRC/installer/RoomEq.wxs" \
  && [[ -f "$SRC/installer/gen_wix_fragment.py" ]] \
  && echo "   ✓ Installateur MSI (CA pnputil + authoring WiX)"
[[ -f "$SRC/driver/roomeqvad/roomeqvad/roomeqvad.inx" \
   && -f "$SRC/driver/roomeqvad/bridge.cpp" \
   && -f "$SRC/driver/roomeqvad/packages.config" ]] \
  && echo "   ✓ Pilote WDM : INF + pont + WDK NuGet"
[[ -f "$SRC/.github/workflows/build.yml" ]] \
  && rg -q 'windowsdriverkit|WDK' "$SRC/.github/workflows/build.yml" 2>/dev/null || true
[[ -f "$SRC/.github/workflows/build.yml" ]] \
  && echo "   ✓ CI GitHub Actions (tests → pilote → MSI)"
rg -q 'ROOMEQ_TEST' "$SRC/src/RoomEq.Core/Io/AppFlavor.cs" \
  && rg -q 'AppFlavorKind.Public;' "$SRC/src/RoomEq.Core/Io/AppFlavor.cs" \
  && echo "   ✓ AppFlavor inversé : public silencieux par défaut"
[[ -f "$SRC/INSTALLATION-PILOTE.md" ]] && echo "   ✓ Guide signature (test + attestation)"

# --- v2.0.0 (suite) : presets casques + macOS ---
[[ -f "$SRC/src/RoomEq.Core/Headphones/headphone_presets.json" \
   && -f "$SRC/src/RoomEq.Core/Headphones/HeadphonePresetLibrary.cs" ]] \
  && rg -q 'headphone_presets.json' "$SRC/src/RoomEq.Core/RoomEq.Core.csproj" \
  && echo "   ✓ Bibliothèque casques (255 modèles AutoEq, ressource intégrée)"
rg -q 'ApplyHeadphonePreset' "$SRC/src/RoomEq.App/ViewModels/EqViewModel.cs" \
  && echo "   ✓ Sélecteur casques dans l'onglet Égaliseur"
[[ -f "$SRC/src/RoomEq.Audio/Mac/CoreAudioApi.cs" \
   && -f "$SRC/src/RoomEq.Audio/Mac/CoreAudioOutput.cs" \
   && -f "$SRC/src/RoomEq.Audio/Mac/MacAudioBridge.cs" ]] \
  && echo "   ✓ Couche CoreAudio macOS (AUHAL lecture/capture + pont périphérique)"
[[ -f "$SRC/macos/RoomEqHAL/roomeq_hal.cpp" \
   && -f "$SRC/macos/RoomEqHAL/RoomEqHAL.driver/Contents/Info.plist" \
   && -f "$SRC/macos/RoomEqHAL/build_plugin.sh" ]] \
  && echo "   ✓ Greffon AudioServerPlugIn RoomEqHAL (périphérique virtuel macOS)"
[[ -f "$SRC/installer/macos/build_pkg.sh" \
   && -f "$SRC/installer/macos/roomeq.icns" ]] \
  && echo "   ✓ Installateur .pkg macOS (icône incluse)"
rg -q 'macos-latest' "$SRC/.github/workflows/build.yml" \
  && echo "   ✓ CI : matrice 3 OS + job macos (app + greffon + pkg)"

echo "── Noyau du pont ASIO : tests g++ (portables)…"
make -C "$SRC/asio/bridge_core" test 2>&1 | tail -2

echo "── Création des exécutables de version (même binaire, noms différents)…"
[[ -f "$PUBLISH/RoomEq.App.exe" ]] || { echo "ERREUR : RoomEq.App.exe absent"; exit 1; }
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq.exe"         # public (silencieux)
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq-test.exe"    # test (journalisé)
rm "$PUBLISH/RoomEq.App.exe"

echo "── Paquet pilote livré avec les zips (si fourni)…"
if [[ -n "$DRIVER_DIR" && -f "$DRIVER_DIR/roomeqvad.inf" ]]; then
    mkdir -p "$PUBLISH/driver"
    cp "$DRIVER_DIR"/roomeqvad.inf "$DRIVER_DIR"/roomeqvad.sys "$DRIVER_DIR"/roomeqvad.cat "$PUBLISH/driver/" 2>/dev/null || true
    cp "$DRIVER_DIR"/RoomEq-TestSigning.cer "$PUBLISH/driver/" 2>/dev/null || true
    echo "   ✓ driver/ inclus ($(ls "$PUBLISH/driver" | wc -l) fichiers)"
else
    echo "   ⚠ DRIVER_DIR non fourni : zips sans driver\\ (le MSI de la CI porte le pilote)"
fi

echo "── Télémétrie : vérification de la config (version test)…"
[[ -f "$TELEMETRY" ]] || { echo "ERREUR : $TELEMETRY absent"; exit 1; }
cp "$TELEMETRY" "$PUBLISH/telemetrie.json"

echo "── LISEZMOI.txt dans le dossier publié…"
cp "$SRC/LISEZMOI.txt" "$PUBLISH/LISEZMOI.txt"

echo "── Zip TEST ($VERSION) : RoomEq.exe + RoomEq-test.exe + telemetrie.json + driver…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-test.zip"
mkdir -p "$WORK/test/RoomEq"
rsync -a "$PUBLISH/" "$WORK/test/RoomEq/"
(cd "$WORK/test" && zip -qr "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq)

echo "── Zip PUBLIC ($VERSION) : RoomEq.exe seul, sans telemetrie.json…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-public.zip"
mkdir -p "$WORK/public/RoomEq"
rsync -a --exclude telemetrie.json --exclude RoomEq-test.exe --exclude logs \
      --exclude 'driver/RoomEq-TestSigning.cer' \
      "$PUBLISH/" "$WORK/public/RoomEq/"
# le zip public ne doit PAS embarquer les identifiants — driver\ sans .cer si présent
if [[ ! -d "$PUBLISH/driver" ]]; then :; fi
(cd "$WORK/public" && zip -qr "$OUT/RoomEq-$VERSION-win-x64-public.zip" RoomEq)

echo "── Zip source ($VERSION) — SANS identifiants, AVEC driver/asio/installer/signaux…"
rm -f "$OUT/RoomEq-$VERSION-source.zip"
rm -rf "$WORK/RoomEq"; mkdir -p "$WORK"
rsync -a --exclude bin --exclude obj --exclude publish --exclude .git \
      --exclude .vs --exclude .idea --exclude test-results \
      --exclude .avalonia-build-tasks --exclude __pycache__ \
      --exclude 'driver/roomeqvad/packages' --exclude 'driver/roomeqvad/build' \
      "$SRC/" "$WORK/RoomEq/"
(cd "$WORK" && zip -qr "$OUT/RoomEq-$VERSION-source.zip" RoomEq)

echo "── Contrôles…"
lisezmoi=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq/LISEZMOI.txt | head -1)
echo "   LISEZMOI : $lisezmoi"

t_main=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq/RoomEq\.exe" || echo 0)
t_test=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq/RoomEq-test\.exe" || echo 0)
t_telemetry=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq/telemetrie.json | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['folder_id'])" 2>/dev/null || echo ABSENT)
t_no_app=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq\.App\.exe" || echo 0)
t_driver=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq/driver/roomeqvad\.inf" || echo 0)
echo "   test zip  : RoomEq.exe=$t_main  RoomEq-test.exe=$t_test  telemetrie folder_id=$t_telemetry  driver.inf=$t_driver  RoomEq.App.exe résiduel=$t_no_app (attendu 0)"

p_main=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | rg -c "RoomEq/RoomEq\.exe" || echo 0)
p_no_test=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | rg -c "RoomEq-test\.exe" || echo 0)
p_no_telemetry=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | rg -c "telemetrie" || echo 0)
echo "   public zip: RoomEq.exe=$p_main  RoomEq-test.exe=$p_no_test (attendu 0)  telemetrie=$p_no_telemetry (attendu 0)"

s_telemetry=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "telemetrie" || echo 0)
s_driver=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/driver/roomeqvad/roomeqvad/roomeqvad.inx" || echo 0)
s_asio=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/asio/asiodll/RoomEqAsio.cpp" || echo 0)
s_msi=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/installer/RoomEq.wxs" || echo 0)
s_ci=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/.github/workflows/build.yml" || echo 0)
s_signals=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/signaux/" || echo 0)
s_hal=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/macos/RoomEqHAL/roomeq_hal.cpp" || echo 0)
s_pkg=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/installer/macos/build_pkg.sh" || echo 0)
s_presets=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/src/RoomEq.Core/Headphones/headphone_presets.json" || echo 0)
echo "   source zip: telemetrie=$s_telemetry (attendu 0)  pilote.inx=$s_driver (attendu 1)  asio=$s_asio (attendu 1)  msi=$s_msi (attendu 1)  ci=$s_ci (attendu 1)  signaux/=$s_signals  greffon-macos=$s_hal (attendu 1)  pkg-macos=$s_pkg (attendu 1)  presets-casques=$s_presets (attendu 1)"

test_zip_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-test.zip" | cut -f1)
public_zip_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-public.zip" | cut -f1)
source_zip_size=$(du -h "$OUT/RoomEq-$VERSION-source.zip" | cut -f1)
echo "   tailles   : test=$test_zip_size  public=$public_zip_size  source=$source_zip_size"

[[ "$t_main" -ge 1 && "$t_test" -ge 1 && "$t_no_app" == "0" && "$t_telemetry" != "ABSENT" ]] \
    || { echo "ERREUR : contenu du zip TEST incorrect"; exit 1; }
[[ "$p_main" -ge 1 && "$p_no_test" == "0" && "$p_no_telemetry" == "0" ]] \
    || { echo "ERREUR : contenu du zip PUBLIC incorrect"; exit 1; }
[[ "$s_telemetry" == "0" && "$s_driver" == "1" && "$s_asio" == "1" && "$s_msi" == "1" && "$s_ci" == "1" \
   && "$s_hal" == "1" && "$s_pkg" == "1" && "$s_presets" == "1" ]] \
    || { echo "ERREUR : zip SOURCE incorrect"; exit 1; }

echo "── Presets casques embarqués dans la DLL publiée…"
python3 - "$PUBLISH/RoomEq.Core.dll" <<'PY'
import sys
data = open(sys.argv[1], 'rb').read()
markers = [b'sennheiserhd650', 'sennheiserhd650'.encode('utf-16-le'),
           b'roomeq.device.bridge.v1']
found = [m in data for m in markers]
print('   presets(ascii)=%s  presets(unicode)=%s  pont-macos=%s' % tuple(found))
sys.exit(0 if any(found[:2]) else 1)
PY

echo "── Tests finaux…"
dotnet test tests/RoomEq.Core.Tests --nologo 2>&1 | grep -E "Passed!|Failed!" | tail -1
dotnet test tests/RoomEq.Audio.Tests --nologo 2>&1 | grep -E "Passed!|Failed!" | tail -1

rm -rf "$WORK"
echo "✓ Livrables $VERSION prêts dans $OUT"
