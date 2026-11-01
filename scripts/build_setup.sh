#!/bin/bash
# build_setup.sh — Installateur Windows « 1 clic » RoomEq (décision D20)
#
# Produit RoomEq-win-Setup.exe (Velopack, CROSS-COMPILÉ Linux → win-x64 :
# aucune machine Windows requise) :
#   • double-clic → application installée : raccourcis Bureau + Menu
#     démarrer, désinstalleur « Paramètres → Applications » ;
#   • installation utilisateur SANS droits administrateur
#     (%LOCALAPPDATA%\RoomEq — dossier inscriptible : logs\ à côté de
#     l'exe OK) ;
#   • variante PUBLIQUE (RoomEq.exe silencieux seul ; RoomEq-test.exe et
#     telemetrie.json exclus de l'empaquetage, jamais embarqués) ;
#   • SANS dépendance ajoutée à l'application : --skipVeloAppCheck (le
#     hook VelopackApp ne servirait qu'aux mises à jour automatiques —
#     décision future, D20) ; le paquet de mise à jour
#     RoomEq-<version>-full.nupkg est néanmoins produit en sortie CI.
#
# Portabilité CI (D14) : socle standard uniquement (dotnet, python3) ;
# vpk est installé/mis à jour à une version ÉPINGLÉE pour la
# reproductibilité des livrables.
set -euo pipefail

SRC="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${OUT:-/home/z/my-project/download}"
PUBLISH="$SRC/publish/win-x64"
VPK_VERSION=1.2.161   # épinglée (reproductibilité — D20)

export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(command -v dotnet)")}"
cd "$SRC"

VERSION=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/Directory.Build.props | head -1)
[[ -n "$VERSION" ]] || { echo "ERREUR : version introuvable dans src/Directory.Build.props"; exit 1; }
echo "── Installateur 1 clic RoomEq v$VERSION (Velopack $VPK_VERSION)…"

echo "── Publication win-x64 auto-contenue…"
rm -rf "$PUBLISH"
dotnet publish src/RoomEq.App -c Release -r win-x64 --self-contained true \
    -p:PublishDir="$PUBLISH" 2>&1 | tail -3

echo "── Exécutables de version (même binaire, noms différents)…"
[[ -f "$PUBLISH/RoomEq.App.exe" ]] || { echo "ERREUR : RoomEq.App.exe absent"; exit 1; }
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq.exe"         # public (silencieux)
cp "$PUBLISH/RoomEq.App.exe" "$PUBLISH/RoomEq-test.exe"    # test (journalisé)
rm "$PUBLISH/RoomEq.App.exe"

echo "── DLL APO (EQ système, D24) dans le dossier publié…"
for arch in x64 arm64; do
    [[ -f "$SRC/natif/roomeq-apo/dist/RoomEqApo_$arch.dll" ]] \
        || { echo "ERREUR : RoomEqApo_$arch.dll absente — lancez natif/roomeq-apo/build-apo.sh"; exit 1; }
done
mkdir -p "$PUBLISH/apo"
cp "$SRC/natif/roomeq-apo/dist/RoomEqApo_x64.dll" \
   "$SRC/natif/roomeq-apo/dist/RoomEqApo_arm64.dll" \
   "$PUBLISH/apo/"
echo "   ✓ apo/RoomEqApo_{x64,arm64}.dll déployées (l'activation les copie vers %ProgramData% avec élévation)"

echo "── Outil vpk (Velopack $VPK_VERSION, épinglé)…"
# vpk 1.x n'a pas d'option --version : la bannière de --help la porte.
# « || true » obligatoire : vpk absent → « command not found » → sans lui,
# set -euo pipefail tuerait le script ici (au lieu d'installer l'outil).
INSTALLED=$(vpk --help 2>&1 | grep -oE 'Velopack CLI [0-9.]+' | head -1 | grep -oE '[0-9.]+$' || true)
if [[ "$INSTALLED" != "$VPK_VERSION" ]]; then
    dotnet tool install -g vpk --version "$VPK_VERSION" 2>/dev/null \
        || dotnet tool update -g vpk --version "$VPK_VERSION"
fi
vpk --help 2>&1 | grep -oE 'Velopack CLI [0-9.]+' | head -1 | sed 's/^/   /'

echo "── Empaquetage Setup.exe (directive [win] = cross-compilation)…"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
vpk "[win]" pack \
    --runtime win-x64 \
    --packId RoomEq \
    --packVersion "$VERSION" \
    --packDir "$PUBLISH" \
    --mainExe RoomEq.exe \
    --packTitle "RoomEq" \
    --packAuthors "RoomEq" \
    --icon src/RoomEq.App/Assets/roomeq.ico \
    --exclude "RoomEq-test|telemetrie" \
    --noPortable \
    --skipVeloAppCheck \
    --shortcuts Desktop,StartMenu \
    --outputDir "$WORK"

echo "── Contrôles…"
[[ -f "$WORK/RoomEq-win-Setup.exe" ]] || { echo "ERREUR : RoomEq-win-Setup.exe absent"; exit 1; }
python3 - "$WORK/RoomEq-win-Setup.exe" "$WORK/RoomEq-$VERSION-full.nupkg" <<'PY'
import os, sys
# Setup.exe = exécutable Windows (PE, magic MZ) ; nupkg = archive ZIP (magic PK)
ok = True
for path, magic in ((sys.argv[1], b"MZ"), (sys.argv[2], b"PK")):
    size = os.path.getsize(path)
    head = open(path, "rb").read(2)
    good = size > 10_000_000 and head == magic
    print("   %s %s : %s octets, en-tête %s"
          % ("✓" if good else "✗", os.path.basename(path), f"{size:,}",
             "OK" if good else "INVALIDE"))
    ok &= good
sys.exit(0 if ok else 1)
PY

mkdir -p "$OUT"
cp "$WORK/RoomEq-win-Setup.exe" "$OUT/"
echo "── Livré : $OUT/RoomEq-win-Setup.exe ($(du -h "$OUT/RoomEq-win-Setup.exe" | cut -f1))"
echo "   (RoomEq-$VERSION-full.nupkg + releases.win.json produits en $WORK — artefacts CI,"
echo "    non joints à la release tant que le hook VelopackApp n'est pas adopté, D20)"
