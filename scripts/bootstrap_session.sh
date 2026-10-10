#!/usr/bin/env bash
# bootstrap_session.sh — Reprise du projet RoomEq dans un environnement vierge.
# Usage : bash bootstrap_session.sh
# Prérequis : IDs valides dans INSTRUCTIONS-REPRISE.md §3 (racine du Drive).
set -euo pipefail

BASE=/home/z/my-project
mkdir -p "$BASE"/{scripts,secrets,workspace}
cd "$BASE"

DRIVE_DL="https://drive.usercontent.google.com/download?id=__ID_OUTIL_BACKUP__&export=download"
MANIFEST_ID="__ID_MANIFESTE__"
OAUTH_ID="__ID_FICHIER_TOKEN_OAUTH__"

echo "==> 1/4  Récupération de l'outil de sauvegarde"
[ -s scripts/drive_backup.py ] || curl -sL "$DRIVE_DL" -o scripts/drive_backup.py

echo "==> 2/4  Restauration de l'environnement depuis le Drive"
python3 scripts/drive_backup.py --restore "$BASE" --manifest-id "$MANIFEST_ID"

echo "==> 3/4  Récupération des identifiants OAuth"
[ -s secrets/oauth_token_relais.json ] || \
  curl -sL "https://drive.usercontent.google.com/download?id=${OAUTH_ID}&export=download" \
       -o secrets/oauth_token_relais.json

echo "==> 4/4  Vérification du SDK .NET 8"
export PATH="$HOME/.dotnet:$PATH"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "    SDK absent → installation…"
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
fi
dotnet --version

echo "==> Build + tests"
cd "$BASE/workspace/roomq-source/RoomEq"
dotnet build RoomEq.sln -c Debug
dotnet test tests/RoomEq.Core.Tests
dotnet test tests/RoomEq.Audio.Tests

echo
echo "✓ Environnement prêt. Lire worklog.md puis INSTRUCTIONS-REPRISE.md §5"
echo  "  pour la procédure de sauvegarde incrémentielle en fin de session."
