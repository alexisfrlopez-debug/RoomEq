#!/usr/bin/env bash
# Sauvegarde systématique RoomEq v3 (décision D7) :
#   1. tests (verify-then-commit, R5) — abandon si échec sauf --force
#   2. commit git si nécessaire
#   3. push GitHub (branche v3-recodage)
#   4. miroir Drive (dossier Equalizer/RoomEq-v3 + bundle git)
set -uo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
FORCE="${1:-}"

# 1) vérification (tous les projets de tests du dépôt)
if [ "$FORCE" != "--force" ]; then
  echo "=== Tests (R5) ==="
  FAIL=0
  dotnet test tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj -c Debug --nologo -v q 2>&1 | tail -3 || FAIL=1
  dotnet test tests/RoomEq.Audio.Tests/RoomEq.Audio.Tests.csproj -c Debug --nologo -v q 2>&1 | tail -3 || FAIL=1
  dotnet test tests/RoomEq.App.Tests/RoomEq.App.Tests.csproj -c Debug --nologo -v q 2>&1 | tail -3 || FAIL=1
  if [ "$FAIL" != "0" ]; then
    echo "ÉCHEC DES TESTS — commit refusé (utiliser --force pour outrepasser)."
    exit 1
  fi
fi

# 2) commit si nécessaire
if [ -n "$(git status --porcelain)" ]; then
  git add -A
  git -c user.name="RoomEq" -c user.email="roomeq@users.noreply.github.com" commit -m "sauvegarde systématique : $(date +%F' '%H:%M) — ${FORCE:+(forcée)}"
  echo "=== Commit créé ==="
else
  echo "=== Rien à committer ==="
fi

# 3) push GitHub (HEAD → branche distante v3-recodage, puis tags de
#    cristallisation — ex. v3.0.0 déclenche la release CI, D10/D12)
TOKEN=$(cut -d: -f2 ../triade/.gdrive/token_git.txt | tr -d ' \r')
GITURL="https://alexisfrlopez-debug:${TOKEN}@github.com/alexisfrlopez-debug/RoomEq.git"
if git push -q "$GITURL" HEAD:v3-recodage 2>&1; then
  echo "=== Push GitHub : HEAD → v3-recodage OK ==="
else
  echo "!!! Échec push GitHub (branche v3-recodage)"
fi
if git tag -l 'v*' | grep -q .; then
  git push -q "$GITURL" --tags 2>&1 \
    && echo "=== Push GitHub : tags OK ===" \
    || echo "!!! Échec push GitHub (tags)"
fi

# 4) miroir Drive
echo "=== Miroir Drive ==="
python3 scripts/drive_sync_roomeq.py
