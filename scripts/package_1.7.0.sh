#!/bin/bash
# package_1.7.0.sh — Publication win-x64 + zips livrables pour RoomEq 1.7.0
# Spécificité 1.7 : injection de telemetrie.json (télémétrie Drive) dans le zip portable.
set -euo pipefail

SRC=/home/z/my-project/workspace/roomq-source/RoomEq
OUT=/home/z/my-project/download
PUBLISH="$SRC/publish/win-x64"
VERSION=1.7.0
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
[[ "$propagated" == "1.7.0" ]] || { echo "ERREUR : version attendue 1.7.0"; exit 1; }

echo "── Télémétrie : injection de telemetrie.json…"
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

echo "── Zip portable ($VERSION)…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-portable.zip"
(cd "$PUBLISH/.." && zip -qr "$OUT/RoomEq-$VERSION-win-x64-portable.zip" win-x64)

echo "── Zip source ($VERSION) — SANS les identifiants de télémétrie…"
rm -f "$OUT/RoomEq-$VERSION-source.zip"
rm -rf "$WORK/RoomEq"; mkdir -p "$WORK"
rsync -a --exclude bin --exclude obj --exclude publish --exclude .git \
      --exclude .vs --exclude .idea --exclude test-results \
      --exclude .avalonia-build-tasks --exclude __pycache__ \
      "$SRC/" "$WORK/RoomEq/"
(cd "$WORK" && zip -qr "$OUT/RoomEq-$VERSION-source.zip" RoomEq)

echo "── Contrôles…"
portable_files=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-portable.zip" | tail -1 | awk '{print $2}')
source_files=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | tail -1 | awk '{print $2}')
portable_size=$(du -h "$OUT/RoomEq-$VERSION-win-x64-portable.zip" | cut -f1)
source_size=$(du -h "$OUT/RoomEq-$VERSION-source.zip" | cut -f1)
lisezmoi=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-portable.zip" win-x64/LISEZMOI.txt | head -1)
exe_present=$(unzip -l "$OUT/RoomEq-$VERSION-win-x64-portable.zip" | rg -c "RoomEq.App.exe" || true)
telemetry_in_zip=$(unzip -p "$OUT/RoomEq-$VERSION-win-x64-portable.zip" win-x64/telemetrie.json | python3 -c "import json,sys; d=json.load(sys.stdin); print('folder_id='+d['folder_id'])")
telemetry_in_src=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "telemetrie" || echo 0)
helpbtn=$(unzip -l "$OUT/RoomEq-$VERSION-source.zip" | rg -c "HelpButton.cs" || true)
tests=$(cd "$SRC" && export PATH="$HOME/.dotnet:$PATH" && dotnet test --nologo 2>&1 | tail -2 | head -1 || true)

echo "   portable : $portable_files fichiers, $portable_size — LISEZMOI: $lisezmoi — exe: $exe_present"
echo "   télémétrie : $telemetry_in_zip"
echo "   source   : $source_files fichiers, $source_size — HelpButton.cs: $helpbtn — telemetrie dans source: $telemetry_in_src (attendu 0)"
[[ "$exe_present" -ge 1 ]] || { echo "ERREUR : RoomEq.App.exe absent du zip"; exit 1; }
[[ "$telemetry_in_src" == "0" ]] || { echo "ERREUR : identifiants de télémétrie dans le zip SOURCE !"; exit 1; }
[[ "$helpbtn" -ge 1 ]] || { echo "ERREUR : HelpButton.cs absent du zip source"; exit 1; }

rm -rf "$WORK"
echo "✓ Livrables $VERSION prêts dans $OUT"
echo "$tests"
