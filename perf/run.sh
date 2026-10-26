#!/usr/bin/env bash
# Harnais de mesure D15 — Release obligatoire (les chiffres Debug ne
# signifient rien : pas d'inlining, pas d'optimisation du JIT).
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"
dotnet run -c Release
