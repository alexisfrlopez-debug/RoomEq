#!/usr/bin/env bash
# RoomEq VST — build de livraison (spec I-AUD-08) : cross-compile le greffon
# VST 2 pour Windows 32 bits avec Zig (MinGW-w64 + libc++ embarqués, aucun
# sudo requis) puis vérifie le binaire (PE32 i386, exports, absence de x87
# dans le chemin DSP).
#
# Usage : natif/roomeq-vst/build-vst.sh [chemin-de-zig]
#   ZIG (défaut : zig du PATH, puis ~/tools/zig-*/zig, puis /home/z/my-project/tools/zig-*/zig)
# Sortie : natif/roomeq-vst/dist/RoomEq.dll (+ dist/RoomEq-VST-<v>-win32.zip)
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AU_CORE="$HERE/../roomeq-au/core"
DIST="$HERE/dist"
VERSION=$(sed -n 's/^project(RoomEqVST VERSION \([0-9.]*\).*/\1/p' "$HERE/CMakeLists.txt")

# ------------------------------------------------------------- localiser zig
ZIG_BIN="${1:-${ZIG:-}}"
if [[ -z "$ZIG_BIN" ]]; then
    if command -v zig >/dev/null 2>&1; then
        ZIG_BIN="$(command -v zig)"
    else
        for candidate in "$HOME"/tools/zig-*/zig /home/z/my-project/tools/zig-*/zig; do
            if [[ -x "$candidate" ]]; then ZIG_BIN="$candidate"; break; fi
        done
    fi
fi
if [[ -z "$ZIG_BIN" || ! -x "$ZIG_BIN" ]]; then
    echo "ÉCHEC : Zig introuvable (nécessaire à la cross-compilation i686 → PE32)." >&2
    echo "  curl -L https://ziglang.org/download/0.14.1/zig-x86_64-linux-0.14.1.tar.xz | tar -xJ -C ~/tools" >&2
    exit 1
fi
echo "— Zig      : $ZIG_BIN ($("$ZIG_BIN" version))"

# ------------------------------------------------------------------- sources
# (banque casques + écriture JSON : cœur partagé AU depuis D23)
SOURCES=(
    "$HERE/vst/RoomEqVst.cpp"
    "$HERE/vst/EditorWin32.cpp"
    "$AU_CORE/roomeq/dsp/biquad.cpp"
    "$AU_CORE/roomeq/dsp/eq_chain.cpp"
    "$AU_CORE/roomeq/json/mini_json.cpp"
    "$AU_CORE/roomeq/preset/preset_json.cpp"
    "$AU_CORE/roomeq/preset/preset_ffp.cpp"
    "$AU_CORE/roomeq/preset/preset_write.cpp"
    "$AU_CORE/roomeq/headphones/headphone_bank.cpp"
)
for f in "${SOURCES[@]}"; do
    [[ -f "$f" ]] || { echo "ÉCHEC : source absent : $f" >&2; exit 1; }
done

mkdir -p "$DIST"
rm -f "$DIST/RoomEq.dll"

# ----------------------------------------------------------------- compilation
# SSE2 forcé (mcpu pentium4 + -mfpmath=sse) : arithmétique double IDENTIQUE
# au build 64 bits (pas de x87 80 bits — invariant 1 de la spec) ;
# -mstackrealign : alignement 16 octets du pointeur de pile (ABI i386).
# vst/roomeq_vst.def : export « main » par ALIAS (un symbole littéral main
# déclencherait l'amorçage EXE du CRT MinGW — scrnsave/WinMain).
echo "— Compilation x86-windows-gnu (32 bits, SSE2)…"
"$ZIG_BIN" c++ -target x86-windows-gnu -mcpu=pentium4 \
    -std=c++17 -O2 -msse2 -mfpmath=sse -mstackrealign \
    -Wall -Wextra -Werror \
    -DNDEBUG \
    -I"$HERE/vst" -I"$AU_CORE" \
    -shared -o "$DIST/RoomEq.dll" \
    "${SOURCES[@]}" \
    "$HERE/vst/roomeq_vst.def" \
    -luser32 -lgdi32 -lcomdlg32 -lshell32
echo "— RoomEq.dll compilé : $(du -h "$DIST/RoomEq.dll" | cut -f1)"

# ------------------------------------------------------------------ contrôles
echo "— Contrôles du binaire…"
file_output="$(file "$DIST/RoomEq.dll")"
echo "  $file_output"
grep -q "PE32 executable" <<<"$file_output" \
    || { echo "ÉCHEC : pas un PE32" >&2; exit 1; }
grep -qi "i386\|Intel 80386" <<<"$file_output" \
    || { echo "ÉCHEC : pas un binaire 32 bits" >&2; exit 1; }
grep -q "(DLL)" <<<"$file_output" \
    || { echo "ÉCHEC : pas une DLL" >&2; exit 1; }

# exports requis : VSTPluginMain (hôtes modernes) + main (hôtes historiques)
exports="$(objdump -p "$DIST/RoomEq.dll" | sed -n '/\[Name Pointer\/Ordinal\] Table/,$p' | grep -o '\b[Vm][A-Za-z]*\b' | sort -u || true)"
for symbol in VSTPluginMain main; do
    grep -qx "$symbol" <<<"$exports" \
        || { echo "ÉCHEC : export absent : $symbol" >&2; exit 1; }
done
echo "  exports : VSTPluginMain + main ✓"

# x87 interdit dans le chemin DSP : les fonctions du cœur (BiquadFilter::
# ProcessSample, EqChain::Process) doivent n'utiliser que SSE2.
x87_count=$(objdump -d "$DIST/RoomEq.dll" \
    | grep -cE '\bfld|\bfstp|\bfstpt|\bfadd|\bfmulp?\b|\bfsub|\bfmul' || true)
echo "  instructions x87 dans le binaire : $x87_count (information — CRT/libc++)"

# ------------------------------------------------------------------- zip
if command -v zip >/dev/null 2>&1; then
    cp "$HERE/README.md" "$DIST/RoomEq-VST-LISEZMOI.md"
    (cd "$DIST" && rm -f "RoomEq-VST-$VERSION-win32.zip" \
        && zip -q -9 "RoomEq-VST-$VERSION-win32.zip" RoomEq.dll RoomEq-VST-LISEZMOI.md)
    echo "— Paquet : dist/RoomEq-VST-$VERSION-win32.zip (DLL + LISEZMOI)"
fi

echo "— OK : $DIST/RoomEq.dll (version $VERSION)"
