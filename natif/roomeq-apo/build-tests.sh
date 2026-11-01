#!/usr/bin/env bash
# RoomEq APO — harnais de tests natifs (spec I-AUD-09) : compile et exécute
# roomeq_apo_tests sous Linux (g++ du socle, aucun sudo). La classe COM est
# liée directement : ABI, parité de courbe bit à bit, continuité, RT-safety.
#
# Usage : natif/roomeq-apo/build-tests.sh
# Sortie : natif/roomeq-apo/build/roomeq_apo_tests
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AU_CORE="$HERE/../roomeq-au/core"
BUILD="$HERE/build"

for f in "$HERE/apo/RoomEqApo.cpp" "$HERE/apo/apo_config.cpp" \
         "$HERE/tests/test_main.cpp" \
         "$AU_CORE/roomeq/dsp/biquad.cpp" "$AU_CORE/roomeq/dsp/eq_chain.cpp" \
         "$AU_CORE/roomeq/json/mini_json.cpp" \
         "$AU_CORE/roomeq/preset/preset_json.cpp"; do
    [[ -f "$f" ]] || { echo "ÉCHEC : source absent : $f" >&2; exit 1; }
done

mkdir -p "$BUILD"
echo "— Compilation du harnais (g++ natif)…"
# -Wno-maybe-uninitialized : faux positif g++ 14 connu sur biquad.cpp du cœur
# scellé (switch exhaustif sur l'énumération) — le cœur n'est pas modifiable
# (module AU propriétaire de ses sources, REFERENCE-NATIF.md).
g++ -std=c++17 -O2 -Wall -Wextra -Werror -Wno-maybe-uninitialized \
    -I"$HERE" -I"$AU_CORE" \
    -pthread \
    "$HERE/tests/test_main.cpp" \
    "$HERE/apo/RoomEqApo.cpp" \
    "$HERE/apo/apo_config.cpp" \
    "$AU_CORE/roomeq/dsp/biquad.cpp" \
    "$AU_CORE/roomeq/dsp/eq_chain.cpp" \
    "$AU_CORE/roomeq/json/mini_json.cpp" \
    "$AU_CORE/roomeq/preset/preset_json.cpp" \
    -o "$BUILD/roomeq_apo_tests"

echo "— Exécution…"
"$BUILD/roomeq_apo_tests"
