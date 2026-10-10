#!/bin/bash
# package_1.5.sh — Publication win-x64 + zips livrables pour RoomEq 1.5.0
set -euo pipefail

SRC=/home/z/my-project/workspace/roomq-source/RoomEq
OUT=/home/z/my-project/download
PUBLISH="$SRC/publish/win-x64"
VERSION=1.5
WORK=$(mktemp -d)

export PATH="$HOME/.dotnet:$PATH"
cd "$SRC"

echo "── Publication win-x64 auto-contenue…"
dotnet publish src/RoomEq.App -c Release -r win-x64 --self-contained true \
    -p:PublishDir="$PUBLISH" 2>&1 | tail -3

echo "── Vérification de la version d'assembly…"
dotnet build src/RoomEq.App -c Release --no-restore 2>/dev/null >/dev/null || true
 propagated=$(rg -o '<Version>([^<]+)</Version>' -r '$1' Directory.Build.props)
echo "   version props : $propagated"
[[ "$propagated" == "1.5.0" ]] || { echo "ERREUR : version attendue 1.5.0"; exit 1; }

echo "── LISEZMOI.txt dans le dossier publié…"
cp "$SRC/LISEZMOI.txt" "$PUBLISH/LISEZMOI.txt"

echo "── Zip portable ($VERSION)…"
rm -f "$OUT/RoomEq-$VERSION-win-x64-portable.zip"
(cd "$PUBLISH/.." && zip -qr "$OUT/RoomEq-$VERSION-win-x64-portable.zip" win-x64)

echo "── Zip source ($VERSION)…"
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

echo "   portable : $portable_files fichiers, $portable_size — LISEZMOI: $lisezmoi — exe: $exe_present"
echo "   source   : $source_files fichiers, $source_size"
[[ "$exe_present" -ge 1 ]] || { echo "ERREUR : RoomEq.App.exe absent du zip"; exit 1; }

rm -rf "$WORK"
echo "✓ Livrables $VERSION prêts dans $OUT"
