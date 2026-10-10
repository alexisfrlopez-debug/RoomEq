#!/bin/bash
# package_1.9.1.sh — Publication win-x64 + livrables RoomEq 1.9.1
# Spécificités 1.9.1 :
#   • Les deux curseurs de volume (barre du haut + « Vol. carte physique » du
#     Mixage) restent SYNCHRONISÉS quel que soit l'ordre de manipulation :
#     VolumeSyncPolicy (pur, testé) + OnRoutedVolumeChanged (le curseur du
#     Mixage écrit la carte PUIS recale la barre du haut) + SyncVolumeFromRouted
#     (drapeau anti-boucle côté barre du haut) + nudges clavier / changement de
#     sortie / réglages Windows externes suivis (poll dans Tick()).
#   • Base 1.9.0 : menu déroulant des signaux de test + « Télécharger d'autres… »
#     + curseur Volume audible sur la sortie routée.
#   • Même mécanique de livraison que 1.9.0 : RoomEq-test.exe + RoomEq-public.exe
#     (même binaire, nom différent), telemetrie.json dans le zip test uniquement ;
#     le zip source embarque le dossier signaux/ (manifeste + WAV).
set -euo pipefail

SRC=/home/z/my-project/workspace/roomq-source/RoomEq
OUT=/home/z/my-project/download
PUBLISH="$SRC/publish/win-x64"
VERSION=1.9.1
TELEMETRY=/home/z/my-project/scripts/telemetrie.json
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

echo "── Vérification des nouveautés 1.9.1 dans les sources…"
rg -q 'class VolumeSyncPolicy' "$SRC/src/RoomEq.Audio/Routing/VolumeSyncPolicy.cs" \
  && echo "   ✓ VolumeSyncPolicy : règles pures de synchronisation"
rg -q 'OnRoutedVolumeChanged' "$SRC/src/RoomEq.App/ViewModels/MixerViewModel.cs" \
  && rg -q 'SyncTopBarFromEndpoint' "$SRC/src/RoomEq.App/ViewModels/MixerViewModel.cs" \
  && echo "   ✓ MixerViewModel : curseur Mixage → carte + barre du haut"
rg -q 'SyncVolumeFromRouted' "$SRC/src/RoomEq.App/ViewModels/MainViewModel.cs" \
  && rg -q '_syncingVolumeFromMixer' "$SRC/src/RoomEq.App/ViewModels/MainViewModel.cs" \
  && echo "   ✓ MainViewModel : recadrage sans boucle"
rg -q 'PollExternalEndpointVolume' "$SRC/src/RoomEq.App/ViewModels/MixerViewModel.cs" \
  && echo "   ✓ Suivi des réglages Windows externes (poll Tick)"
rg -q 'RoutedVolume, Mode=TwoWay' "$SRC/src/RoomEq.App/Views/MixerTab.axaml" \
  && rg -q 'Volume, Mode=TwoWay' "$SRC/src/RoomEq.App/Views/MainWindow.axaml" \
  && echo "   ✓ XAML : Mode=TwoWay explicite sur les deux curseurs"

echo "── Vérification des nouveautés 1.9.0 dans les sources…"
rg -q 'SignalChoices' "$SRC/src/RoomEq.App/Views/MainWindow.axaml" \
  && rg -q 'Télécharger d autres…' "$SRC/src/RoomEq.App/Views/MainWindow.axaml" \
  && echo "   ✓ MainWindow : menu déroulant des signaux + téléchargement"
rg -q 'ApplyTopBarVolume' "$SRC/src/RoomEq.App/ViewModels/MainViewModel.cs" \
  && rg -q 'SliderToEndpoint' "$SRC/src/RoomEq.Audio/Routing/EndpointVolumeController.cs" \
  && echo "   ✓ Volume : curseur → carte physique routée"
[[ -f "$SRC/src/RoomEq.Audio/Playback/TestSignalCatalog.cs" \
   && -f "$SRC/src/RoomEq.Core/Io/SignalPack.cs" \
   && -f "$SRC/src/RoomEq.Core/Io/SignalStore.cs" \
   && -f "$SRC/src/RoomEq.Core/Io/SignalCatalogClient.cs" \
   && -f "$SRC/src/RoomEq.App/Controls/SignalCatalogDialog.cs" ]] \
  && echo "   ✓ Catalogue de signaux : 5 fichiers présents"

echo "── Vérification du pack de signaux publié…"
[[ -f "$SRC/signaux/manifest.json" ]] || { echo "ERREUR : signaux/manifest.json absent"; exit 1; }
python3 - <<PY
import json, os, hashlib
src = "$SRC/signaux"
m = json.load(open(os.path.join(src, "manifest.json"), encoding="utf-8"))
assert m.get("version") == 1 and len(m["signals"]) == 4, "manifeste inattendu"
for s in m["signals"]:
    p = os.path.join(src, s["fileName"])
    assert os.path.isfile(p), "fichier absent : " + s["fileName"]
    assert os.path.getsize(p) == s["bytes"], "taille != manifeste : " + s["fileName"]
    sha = hashlib.sha256(open(p, "rb").read()).hexdigest()
    assert sha == s["sha256"], "sha256 != manifeste : " + s["fileName"]
    print("   ✓", s["fileName"], f'({s["bytes"]:,d} o)'.replace(",", " "))
print("   ✓ manifest.json cohérent (4 signaux)")
PY

echo "── Création des exécutables de version (même binaire, noms différents)…"
[[ -f "$PUBLISH/RoomEq.App.exe" ]] || { echo "ERREUR : RoomEq.App.exe absent"; exit 1; }
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq-test.exe"
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq-public.exe"
rm "$PUBLISH/RoomEq.App.exe"   # seuls les exécutables nommés sont livrés

echo "── Télémétrie : injection de telemetrie.json (version test)…"
[[ -f "$TELEMETRY" ]] || { echo "ERREUR : $TELEMETRY absent"; exit 1; }
python3 - "$TELEMETRY" <<'PY'
import json, sys
cfg = json.load(open(sys.argv[1]))
assert cfg.get("enabled") is True, "télémétrie désactivée ?"
assert all(cfg.get(k) for k in ("client_id", "client_secret", "refresh_token", "folder_id")), "config incomplète"
print("   folder_id :", cfg["folder_id"])
PY
cp "$TELEMETRY" "$PUBLISH/telemetrie.json"

echo "── LISEZMOI.txt dans le dossier publié…"
cp "$SRC/LISEZMOI.txt" "$PUBLISH/LISEZMOI.txt"

echo "── Zip TEST ($VERSION) : les deux exécutables + telemetrie.json…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-test.zip"
mkdir -p "$WORK/test/RoomEq-test"
rsync -a "$PUBLISH/" "$WORK/test/RoomEq-test/"
(cd "$WORK/test" && zip -qr "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq-test)

echo "── Zip PUBLIC ($VERSION) : RoomEq-public.exe seul, sans telemetrie.json…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-public.zip"
mkdir -p "$WORK/public/RoomEq-public"
rsync -a --exclude telemetrie.json --exclude RoomEq-test.exe --exclude logs \
      "$PUBLISH/" "$WORK/public/RoomEq-public/"
(cd "$WORK/public" && zip -qr "$OUT/RoomEq-$VERSION-win-x64-public.zip" RoomEq-public)

echo "── Zip source ($VERSION) — SANS les identifiants de télémétrie, AVEC signaux/…"
rm -f "$OUT/RoomEq-$VERSION-source.zip"
rm -rf "$WORK/RoomEq"; mkdir -p "$WORK"
rsync -a --exclude bin --exclude obj --exclude publish --exclude .git \
      --exclude .vs --exclude .idea --exclude test-results \
      --exclude .avalonia-build-tasks --exclude __pycache__ \
      "$SRC/" "$WORK/RoomEq/"
(cd "$WORK" && zip -qr "$OUT/RoomEq-$VERSION-source.zip" RoomEq)

echo "── Contrôles…"
lisezmoi=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq-test/LISEZMOI.txt | head -1)
echo "   LISEZMOI : $lisezmoi"

t_test_exe=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq-test/RoomEq-test\.exe" || echo 0)
t_public_exe=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq-test/RoomEq-public\.exe" || echo 0)
t_telemetry=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq-test/telemetrie.json | python3 -c "import json,sys; d=json.load(sys.stdin); print(d['folder_id'])" 2>/dev/null || echo ABSENT)
t_no_app=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | rg -c "RoomEq\.App\.exe" || echo 0)
echo "   test zip  : RoomEq-test.exe=$t_test_exe  RoomEq-public.exe=$t_public_exe  telemetrie folder_id=$t_telemetry  RoomEq.App.exe résiduel=$t_no_app (attendu 0)"

p_public_exe=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | rg -c "RoomEq-public/RoomEq-public\.exe" || echo 0)
p_no_test=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | rg -c "RoomEq-test\.exe" || echo 0)
p_no_telemetry=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | rg -c "telemetrie" || echo 0)
echo "   public zip: RoomEq-public.exe=$p_public_exe  RoomEq-test.exe=$p_no_test (attendu 0)  telemetrie=$p_no_telemetry (attendu 0)"

s_telemetry=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "telemetrie" || echo 0)
s_signals=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "RoomEq/signaux/" || echo 0)
s_catalog=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "SignalPack\.cs|SignalStore\.cs|SignalCatalogClient\.cs|TestSignalCatalog\.cs|SignalCatalogDialog\.cs" || echo 0)
echo "   source zip: telemetrie=$s_telemetry (attendu 0)  signaux/=$s_signals (attendu 6 : 4 wav + manifest + entrée dossier)  fichiers catalogue=$s_catalog (attendu 6 : 5 nouveaux + TestSignalCatalog historique de calibration)"

test_zip_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-test.zip" | cut -f1)
public_zip_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-public.zip" | cut -f1)
source_zip_size=$(du -h "$OUT/RoomEq-$VERSION-source.zip" | cut -f1)
echo "   tailles   : test=$test_zip_size  public=$public_zip_size  source=$source_zip_size"

[[ "$t_test_exe" -ge 1 && "$t_public_exe" -ge 1 && "$t_no_app" == "0" && "$t_telemetry" != "ABSENT" ]] \
    || { echo "ERREUR : contenu du zip TEST incorrect"; exit 1; }
[[ "$p_public_exe" -ge 1 && "$p_no_test" == "0" && "$p_no_telemetry" == "0" ]] \
    || { echo "ERREUR : contenu du zip PUBLIC incorrect"; exit 1; }
[[ "$s_telemetry" == "0" && "$s_signals" == "6" && "$s_catalog" == "6" ]] \
    || { echo "ERREUR : zip SOURCE incorrect (identifiants présents ou signaux/catalogue absents)"; exit 1; }

echo "── Tests finaux…"
dotnet test --nologo 2>&1 | grep -E "Passed!|Failed!" | tail -2

rm -rf "$WORK"
echo "✓ Livrables $VERSION prêts dans $OUT"
