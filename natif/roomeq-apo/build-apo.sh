#!/usr/bin/env bash
# RoomEq APO — build de livraison (spec I-AUD-09) : cross-compile la DLL COM
# « RoomEqApo » pour Windows x64 ET arm64 avec Zig (MinGW-w64 + libc++
# embarqués, aucun sudo requis), puis vérifie les binaires (PE32+, DLL,
# exports COM, aucun dépendance manquante).
#
# Usage : natif/roomeq-apo/build-apo.sh [chemin-de-zig]
#   ZIG (défaut : zig du PATH, puis ~/tools/zig-*, puis /home/z/my-project/tools/zig-*)
# Sortie : natif/roomeq-apo/dist/RoomEqApo_x64.dll + RoomEqApo_arm64.dll
#          (+ dist/RoomEq-APO-<v>-win.zip)
#
# Pourquoi deux architectures : audiodg.exe charge l'APO dans SON processus —
# l'architecture de la DLL doit correspondre à celle du moteur audio de la
# machine (x64 sur la quasi-totalité des PC, ARM64 sur les Snapdragon/Insider
# ARM). L'installateur C# choisit à l'activation (ApoSetupRunner).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AU_CORE="$HERE/../roomeq-au/core"
DIST="$HERE/dist"
VERSION=$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$HERE/../../src/Directory.Build.props" 2>/dev/null | head -1 || true)
[[ -n "$VERSION" ]] || VERSION="3.9.1"

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
    echo "ÉCHEC : Zig introuvable (nécessaire à la cross-compilation)." >&2
    echo "  curl -L https://ziglang.org/download/0.14.1/zig-x86_64-linux-0.14.1.tar.xz | tar -xJ -C ~/tools" >&2
    exit 1
fi
echo "— Zig      : $ZIG_BIN ($("$ZIG_BIN" version)"

# ------------------------------------------------------------------- sources
SOURCES=(
    "$HERE/apo/RoomEqApo.cpp"
    "$HERE/apo/apo_config.cpp"
    "$HERE/apo/win_compat.cpp"
    "$HERE/apo/dllmain.cpp"
    "$AU_CORE/roomeq/dsp/biquad.cpp"
    "$AU_CORE/roomeq/dsp/eq_chain.cpp"
    "$AU_CORE/roomeq/json/mini_json.cpp"
    "$AU_CORE/roomeq/preset/preset_json.cpp"
)
for f in "${SOURCES[@]}"; do
    [[ -f "$f" ]] || { echo "ÉCHEC : source absent : $f" >&2; exit 1; }
done

mkdir -p "$DIST"
rm -f "$DIST/RoomEqApo_x64.dll" "$DIST/RoomEqApo_arm64.dll"

# ----------------------------------------------------------------- compilation
# -mconsole interdit : DLL pure ; entry DllMain via le CRT MinGW.
# Les seules importations autorisées : kernel32 (DisableThreadLibraryCalls,
# GetFileAttributesExA), ole32 (CoTaskMemAlloc/Free) + CRT statique.
build_one() {
    local target="$1" out="$2" arch_grep="$3" arch_name="$4"
    echo "— Compilation $arch_name ($target)…"
    "$ZIG_BIN" c++ -target "$target" \
        -std=c++17 -O2 -Wall -Wextra -Werror \
        -DNDEBUG \
        -I"$HERE/apo" -I"$AU_CORE" \
        -shared -o "$out" \
        "${SOURCES[@]}" \
        "$HERE/apo/roomeq_apo.def" \
        -lole32 -lkernel32 \
        || { echo "ÉCHEC compilation $arch_name" >&2; exit 1; }
    echo "  $(du -h "$out" | cut -f1) : $out"

    # ---------------------------------------------------------- contrôles PE
    # bannière file (PE32+ + architecture + DLL) puis vérificateur autonome
    # (scripts/check_pe_exports.py : tables export/import relues directement —
    # binutils-objdump ne reconnaît pas le PE ARM64 sur cette chaîne)
    local fo
    fo="$(file "$out")"
    echo "  $fo"
    grep -q "PE32+" <<<"$fo" || { echo "ÉCHEC : pas un PE32+ (64 bits)" >&2; exit 1; }
    grep -qi "$arch_grep" <<<"$fo" || { echo "ÉCHEC : mauvaise architecture ($arch_name attendu)" >&2; exit 1; }
    grep -q "(DLL)" <<<"$fo" || { echo "ÉCHEC : pas une DLL" >&2; exit 1; }

    # exports COM exigés par CoCreateInstance (audiodg) + dépendances système
    python3 "$HERE/../../scripts/check_pe_exports.py" "$out" "$arch_name" \
        DllGetClassObject DllCanUnloadNow \
        || { echo "ÉCHEC : contrôles PE $arch_name" >&2; exit 1; }
}

build_one x86_64-windows-gnu   "$DIST/RoomEqApo_x64.dll"   "x86-64"     "x64"
build_one aarch64-windows-gnu  "$DIST/RoomEqApo_arm64.dll"  "ARM64"      "arm64"

# ------------------------------------------------------------------- zip
if command -v zip >/dev/null 2>&1; then
    cp "$HERE/README.md" "$DIST/RoomEq-APO-LISEZMOI.md" 2>/dev/null || true
    (cd "$DIST" && rm -f "RoomEq-APO-$VERSION-win.zip" \
        && zip -q -9 "RoomEq-APO-$VERSION-win.zip" RoomEqApo_x64.dll RoomEqApo_arm64.dll \
        && [[ -f RoomEq-APO-LISEZMOI.md ]] && zip -q -9 "RoomEq-APO-$VERSION-win.zip" RoomEq-APO-LISEZMOI.md) \
        || true
    echo "— Paquet : dist/RoomEq-APO-$VERSION-win.zip"
fi

echo "— OK : DLL APO prêtes (version $VERSION)"
