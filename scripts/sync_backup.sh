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

# 1) vérification
if [ "$FORCE" != "--force" ]; then
  echo "=== Tests (R5) ==="
  if ! dotnet test tests/RoomEq.Core.Tests/RoomEq.Core.Tests.csproj -c Debug --nologo -v q 2>&1 | tail -3; then
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

# 3) push GitHub
TOKEN=$(cut -d: -f2 ../triade/.gdrive/token_git.txt | tr -d ' \r')
if git push -q "https://alexisfrlopez-debug:${TOKEN}@github.com/alexisfrlopez-debug/RoomEq.git" v3-recodage:v3-recodage 2>&1; then
  echo "=== Push GitHub : v3-recodage OK ==="
else
  git branch -q v3-recodage 2>/dev/null || true
  git push -q --set-upstream "https://alexisfrlopez-debug:${TOKEN}@github.com/alexisfrlopez-debug/RoomEq.git" v3-recodage 2>&1 && echo "=== Push GitHub : v3-recodage créé ===" || echo "!!! Échec push GitHub"
fi

# 4) miroir Drive
echo "=== Miroir Drive ==="
python3 scripts/drive_sync_roomeq.py
