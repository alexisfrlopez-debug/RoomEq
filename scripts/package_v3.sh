#!/bin/bash
# package_v3.sh — Livrables RoomEq v3 (adapté du package_2.0.0.sh legacy, D10)
#
# Spécificités v3 (recodage TRIADE, parité v2.2.2) :
#   • Application intégralement recodée (Core/Audio/App) — specs I/ + CARTE.md ;
#     les composants natifs (pilote, ASIO, installateurs) sont SCELLÉS v2.2.2
#     (décision D1) et ne sont pas recompilés ici : le pilote s'installe via
#     RoomEqDriverSetup-2.2.2.exe (artefact legacy, natif/REFERENCE-NATIF.md).
#   • AppFlavor : RoomEq.exe = public silencieux PAR DÉFAUT ;
#     « test » dans le nom (ou ROOMEQ_TEST=1) = version journalisée.
#   • Zips : PUBLIC = RoomEq.exe seul ; TEST = RoomEq.exe + RoomEq-test.exe +
#     telemetrie.json (réel si présent, sinon l'example sans identifiants).
#   • Zip source : tout le dépôt v3 (I/ src/ tests/ docs/ natif/ scripts/)
#     SANS identifiants (telemetrie.json réel exclu) ni artefacts de build.
#   • Portabilité CI (D14) : aucun outil hors du socle standard — grep -E
#     (et NON rg, absent des runners GitHub) ; le dossier de sortie (OUT,
#     « artifacts » en CI) est exclu du zip source.
set -euo pipefail

SRC="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${OUT:-/home/z/my-project/download}"
PUBLISH="$SRC/publish/win-x64"
VERSION=3.0.2
# Télémétrie : fichier réel (ignoré par git) ou repli sur l'example.
TELEMETRY="${TELEMETRY:-$SRC/scripts/telemetrie.json}"
TELEMETRY_EXAMPLE="$SRC/scripts/telemetrie.json.example"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

export PATH="$HOME/.dotnet:$PATH"
cd "$SRC"
mkdir -p "$OUT"

echo "── Publication win-x64 auto-contenue…"
rm -rf "$PUBLISH"
dotnet publish src/RoomEq.App -c Release -r win-x64 --self-contained true \
    -p:PublishDir="$PUBLISH" 2>&1 | tail -3

echo "── Vérification de la version…"
propagated=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/Directory.Build.props | head -1)
echo "   version props : $propagated"
[[ "$propagated" == "$VERSION" ]] || { echo "ERREUR : version attendue $VERSION"; exit 1; }

echo "── Vérification de la version d'assemblage dans les DLL…"
python3 - "$PUBLISH/RoomEq.App.dll" "$PUBLISH/RoomEq.Core.dll" "$PUBLISH/RoomEq.Audio.dll" "$VERSION" <<'PY'
import sys
ok = True
version = sys.argv[-1]
for path in sys.argv[1:-1]:
    data = open(path, "rb").read()
    found = (version.encode("utf-8") in data
             or version.encode("utf-16-le") in data
             or (version.split("-")[0] + ".0").encode("utf-8") in data)
    print(("   ✓ " if found else "   ✗ ") + path.rsplit("/", 1)[-1])
    ok &= found
sys.exit(0 if ok else 1)
PY

echo "── Vérifications structurelles v3 (gouvernance TRIADE + parité)…"
[[ -f "$SRC/CARTE.md" && -d "$SRC/I" ]] \
  && echo "   ✓ Gouvernance TRIADE : CARTE.md + specs I/ présentes"
N_SPECS=$(find "$SRC/I" -name 'I-*.md' | wc -l)
[[ "$N_SPECS" -ge 30 ]] && echo "   ✓ $N_SPECS specs I"
grep -q '🔒' "$SRC/CARTE.md" \
  && echo "   ✓ Carte cristallisée (marqueurs 🔒)"
grep -qE 'class RoomEqVirtualProvider' "$SRC/src/RoomEq.Audio/Routing/VirtualAudioProviders.cs" \
  && grep -qE 'class LoopbackProvider' "$SRC/src/RoomEq.Audio/Routing/VirtualAudioProviders.cs" \
  && ! grep -qiE 'vb-cable|voicemeeter|vb-audio' "$SRC/src/RoomEq.Audio/Routing/VirtualAudioProviders.cs" \
  && echo "   ✓ Fournisseurs : carte RoomEq + boucle WASAPI (zéro tiers)"
[[ -f "$SRC/src/RoomEq.Audio/Routing/DriverSetupManager.cs" ]] \
  && grep -qE 'pnputil' "$SRC/src/RoomEq.Audio/Routing/DriverSetupManager.cs" \
  && echo "   ✓ DriverSetupManager (pnputil local — pilote scellé v2.2.2)"
grep -qE 'AsioBridgeHost' "$SRC/src/RoomEq.Audio/Asio/AsioBridgeHost.cs" \
  && grep -qE 'TrackSourceKind.AsioBridge' "$SRC/src/RoomEq.Audio/Routing/ConsoleChannel.cs" \
  && grep -qE 'AsioStatusText' "$SRC/src/RoomEq.App/ViewModels/MixerViewModel.cs" \
  && echo "   ✓ Pont ASIO : hôte + piste DAW + statut"
grep -qE 'ROOMEQ_TEST' "$SRC/src/RoomEq.Core/Io/AppFlavor.cs" \
  && grep -qE 'AppFlavorKind.Public;' "$SRC/src/RoomEq.Core/Io/AppFlavor.cs" \
  && echo "   ✓ AppFlavor inversé : public silencieux par défaut"
python3 - "$SRC/src/RoomEq.Core/Headphones/headphone_presets.json" <<'PY'
import json, sys
n = len(json.load(open(sys.argv[1]))["models"])
print(f"   modèles casques : {n}")
sys.exit(0 if n >= 320 else 1)
PY
[[ $? -eq 0 ]] && echo "   ✓ Parité casques : ≥ 320 modèles (D9)"
[[ -f "$SRC/LISEZMOI.txt" ]] && echo "   ✓ LISEZMOI.txt v3"

echo "── Exécutables de version (même binaire, noms différents)…"
[[ -f "$PUBLISH/RoomEq.App.exe" ]] || { echo "ERREUR : RoomEq.App.exe absent"; exit 1; }
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq.exe"         # public (silencieux)
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq-test.exe"    # test (journalisé)
rm "$PUBLISH/RoomEq.App.exe"

echo "── Télémétrie (version test)…"
if [[ -f "$TELEMETRY" ]]; then
    cp "$TELEMETRY" "$PUBLISH/telemetrie.json"
    echo "   ✓ telemetrie.json réel embarqué (zip TEST uniquement)"
else
    cp "$TELEMETRY_EXAMPLE" "$PUBLISH/telemetrie.json"
    echo "   ⚠ telemetrie.json réel absent — example (placeholders) embarqué"
fi

echo "── LISEZMOI.txt dans le dossier publié…"
cp "$SRC/LISEZMOI.txt" "$PUBLISH/LISEZMOI.txt"

echo "── Zip TEST ($VERSION) : RoomEq.exe + RoomEq-test.exe + telemetrie.json…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-test.zip"
mkdir -p "$WORK/test/RoomEq"
rsync -a "$PUBLISH/" "$WORK/test/RoomEq/"
(cd "$WORK/test" && zip -qr "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq)

echo "── Zip PUBLIC ($VERSION) : RoomEq.exe seul, sans telemetrie.json…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-public.zip"
mkdir -p "$WORK/public/RoomEq"
rsync -a --exclude telemetrie.json --exclude RoomEq-test.exe --exclude logs \
      "$PUBLISH/" "$WORK/public/RoomEq/"
(cd "$WORK/public" && zip -qr "$OUT/RoomEq-$VERSION-win-x64-public.zip" RoomEq)

echo "── Zip SOURCE ($VERSION) — SANS identifiants, AVEC specs I + Carte…"
rm -f "$OUT/RoomEq-$VERSION-source.zip"
rm -rf "$WORK/srczip"; mkdir -p "$WORK/srczip"
rsync -a --exclude bin --exclude obj --exclude publish --exclude .git \
      --exclude .vs --exclude .idea --exclude test-results \
      --exclude .avalonia-build-tasks --exclude __pycache__ \
      --exclude 'scripts/telemetrie.json' \
      --exclude artifacts --exclude download \
      "$SRC/" "$WORK/srczip/RoomEq/"
(cd "$WORK/srczip" && zip -qr "$OUT/RoomEq-$VERSION-source.zip" RoomEq)

echo "── Contrôles…"
lisezmoi=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq/LISEZMOI.txt | head -1)
echo "   LISEZMOI : $lisezmoi"

t_main=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | grep -cE "RoomEq/RoomEq\.exe" || true)
t_test=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | grep -cE "RoomEq/RoomEq-test\.exe" || true)
t_telemetry=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-test.zip" RoomEq/telemetrie.json | python3 -c "import json,sys; d=json.load(sys.stdin); print('ok' if 'folder_id' in d else 'ko')" 2>/dev/null || echo ABSENT)
t_no_app=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-test.zip" | grep -cE "RoomEq\.App\.exe" || true)
echo "   test zip  : RoomEq.exe=$t_main  RoomEq-test.exe=$t_test  telemetrie=$t_telemetry  RoomEq.App.exe résiduel=$t_no_app (attendu 0)"

p_main=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | grep -cE "RoomEq/RoomEq\.exe" || true)
p_no_test=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | grep -cE "RoomEq-test\.exe" || true)
p_no_telemetry=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-public.zip" | grep -cE "telemetrie" || true)
echo "   public zip: RoomEq.exe=$p_main  RoomEq-test.exe=$p_no_test (attendu 0)  telemetrie=$p_no_telemetry (attendu 0)"

s_telemetry=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | grep -cE "telemetrie\.json$" || true)
s_specs=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | grep -cE "RoomEq/I/.*/I-.*\.md" || true)
s_carte=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | grep -cE "RoomEq/CARTE\.md" || true)
s_lisezmoi=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | grep -cE "RoomEq/LISEZMOI\.txt" || true)
echo "   source zip: telemetrie réel=$s_telemetry (attendu 0)  specs I=$s_specs  CARTE.md=$s_carte (attendu 1)  LISEZMOI=$s_lisezmoi (attendu 1)"

echo "   tailles   : test=$(du -h "$OUT/RoomEq-$VERSION-win-x64-test.zip" | cut -f1)  public=$(du -h "$OUT/RoomEq-$VERSION-win-x64-public.zip" | cut -f1)  source=$(du -h "$OUT/RoomEq-$VERSION-source.zip" | cut -f1)"

[[ "$t_main" -ge 1 && "$t_test" -ge 1 && "$t_no_app" == "0" && "$t_telemetry" == "ok" ]] \
    || { echo "ERREUR : contenu du zip TEST incorrect"; exit 1; }
[[ "$p_main" -ge 1 && "$p_no_test" == "0" && "$p_no_telemetry" == "0" ]] \
    || { echo "ERREUR : contenu du zip PUBLIC incorrect"; exit 1; }
[[ "$s_telemetry" == "0" && "$s_specs" -ge 30 && "$s_carte" == "1" && "$s_lisezmoi" == "1" ]] \
    || { echo "ERREUR : zip SOURCE incorrect"; exit 1; }

echo "── Presets casques embarqués dans la DLL publiée (321 modèles)…"
python3 - "$PUBLISH/RoomEq.Core.dll" <<'PY'
import sys
data = open(sys.argv[1], 'rb').read()
markers = [b'sennheiserhd25', 'sennheiserhd25'.encode('utf-16-le')]
found = [m in data for m in markers]
print('   presets casques dans RoomEq.Core.dll : %s' % ('oui' if any(found) else 'NON'))
sys.exit(0 if any(found) else 1)
PY

echo "── Tests finaux…"
dotnet test tests/RoomEq.Core.Tests --nologo -v q 2>&1 | grep -E "Passed!|Failed!" | tail -1
dotnet test tests/RoomEq.Audio.Tests --nologo -v q 2>&1 | grep -E "Passed!|Failed!" | tail -1
dotnet test tests/RoomEq.App.Tests --nologo -v q 2>&1 | grep -E "Passed!|Failed!" | tail -1

echo "✓ Livrables $VERSION prêts dans $OUT"
