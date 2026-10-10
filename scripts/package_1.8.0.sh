#!/bin/bash
# package_1.8.0.sh — Publication win-x64 + livrables RoomEq 1.8.0
# Spécificités 1.8 :
#   • DEUX versions d'exécutable issues du même binaire, distinguées par leur nom :
#       RoomEq-test.exe    (journalisation + télémétrie Drive si telemetrie.json)
#       RoomEq-public.exe  (aucun journal, aucun envoi)
#   • RoomEq-1.8.0-win-x64-test.zip   : les DEUX exécutables + telemetrie.json
#     (le dialogue de lancement de la version test propose la version publique).
#   • RoomEq-1.8.0-win-x64-public.zip : RoomEq-public.exe seul, SANS telemetrie.json.
#   • Zip source sans identifiants (comme toujours).
set -euo pipefail

SRC=/home/z/my-project/workspace/roomq-source/RoomEq
OUT=/home/z/my-project/download
PUBLISH="$SRC/publish/win-x64"
VERSION=1.8.0
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
[[ "$propagated" == "1.8.0" ]] || { echo "ERREUR : version attendue 1.8.0"; exit 1; }

echo "── Vérification de la version d'assemblage dans les DLL…"
python3 - "$PUBLISH/RoomEq.App.dll" "$PUBLISH/RoomEq.Core.dll" <<'PY'
import sys
ok = True
for path in sys.argv[1:]:
    data = open(path, "rb").read()
    found = "1.8.0.0" in data.decode("utf-8", "ignore") or "1.8.0.0".encode("utf-16-le") in data
    print(("   ✓ " if found else "   ✗ ") + path.rsplit("/", 1)[-1])
    ok &= found
sys.exit(0 if ok else 1)
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

echo "── Zip source ($VERSION) — SANS les identifiants de télémétrie…"
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
s_flavor=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "AppFlavor\.cs|FlavorDialog\.cs" || echo 0)
echo "   source zip: telemetrie=$s_telemetry (attendu 0)  AppFlavor/FlavorDialog=$s_flavor"

test_zip_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-test.zip" | cut -f1)
public_zip_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-public.zip" | cut -f1)
source_zip_size=$(du -h "$OUT/RoomEq-$VERSION-source.zip" | cut -f1)
echo "   tailles   : test=$test_zip_size  public=$public_zip_size  source=$source_zip_size"

[[ "$t_test_exe" -ge 1 && "$t_public_exe" -ge 1 && "$t_no_app" == "0" && "$t_telemetry" != "ABSENT" ]] \
    || { echo "ERREUR : contenu du zip TEST incorrect"; exit 1; }
[[ "$p_public_exe" -ge 1 && "$p_no_test" == "0" && "$p_no_telemetry" == "0" ]] \
    || { echo "ERREUR : contenu du zip PUBLIC incorrect"; exit 1; }
[[ "$s_telemetry" == "0" && "$s_flavor" -ge 2 ]] \
    || { echo "ERREUR : zip SOURCE incorrect (identifiants présents ou fichiers flavor absents)"; exit 1; }

echo "── Tests finaux…"
dotnet test --nologo 2>&1 | grep -E "Passed!|Failed!" | tail -2

rm -rf "$WORK"
echo "✓ Livrables $VERSION prêts dans $OUT"
