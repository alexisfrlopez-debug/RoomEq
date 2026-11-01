#!/usr/bin/env python3
"""Génère natif/roomeq-vst/vst/headphone_bank.h : bibliothèque de corrections
casques/écouteurs embarquée dans le greffon VST (format binaire compact).

Source : src/RoomEq.Core/Headphones/headphone_presets.json (321 modèles,
données AutoEq — MIT, intégrées à l'application ; le greffon embarque les
MÊMES données pour que la banque soit accessible SANS l'application).

Format binaire (« RHP1 », petit boutien) :
  header  : magic char[4], uint16 nombre de modèles
  modèle  : uint8 lenId, id[], uint8 lenBrand, brand[], uint8 lenModel,
            model[], uint8 typeCode (0 over-ear, 1 in-ear, 2 earbud,
            3 gaming), uint8 lenSource, source[], float32 preampDb,
            uint8 nombre de filtres, filtres[]
  filtre  : uint8 typeCode (0 peaking, 1 low-shelf, 2 high-shelf),
            float32 f, float32 g, float32 q

Usage : python3 gen_headphone_bank.py (depuis scripts/ ou le dépôt).
Idempotent : réécrit le header seulement si le contenu change.
"""
import json
import struct
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SRC = REPO / "src/RoomEq.Core/Headphones/headphone_presets.json"
DST = REPO / "natif/roomeq-vst/vst/headphone_bank_data.h"

TYPES_MODEL = {"over-ear": 0, "in-ear": 1, "earbud": 2, "gaming": 3}
TYPES_FILTRE = {"peaking": 0, "low-shelf": 1, "high-shelf": 2}


def encode_str(text):
    b = text.encode("utf-8")
    assert len(b) < 256, f"chaîne trop longue : {text}"
    return bytes([len(b)]) + b


def build_blob():
    with open(SRC, encoding="utf-8") as f:
        data = json.load(f)
    models = data["models"]
    out = bytearray(b"RHP1")
    out += struct.pack("<H", len(models))
    for m in models:
        filters = m["filters"]
        assert len(filters) < 256
        out += encode_str(m["id"])
        out += encode_str(m["brand"])
        out += encode_str(m["model"])
        out += bytes([TYPES_MODEL.get(m.get("type", "over-ear"), 0)])
        out += encode_str(m.get("source", ""))
        out += struct.pack("<f", m.get("preampDb", 0.0))
        out += bytes([len(filters)])
        for flt in filters:
            kind = flt.get("type", "peaking")
            assert kind in TYPES_FILTRE, f"type de filtre inconnu : {kind}"
            out += bytes([TYPES_FILTRE[kind]])
            out += struct.pack("<fff", flt["f"], flt["g"], flt["q"])
    return bytes(out), len(models)


def main():
    blob, count = build_blob()
    lines = [
        "// FICHIER GÉNÉRÉ — ne pas éditer à la main.",
        "// Source : src/RoomEq.Core/Headphones/headphone_presets.json",
        f"// ({count} modèles, données AutoEq — MIT). Régénérer :",
        "//   python3 scripts/gen_headphone_bank.py",
        "#pragma once",
        "",
        "#include <cstddef>",
        "",
        "namespace roomeq_vst {",
        "",
        "/// Banque casques/écouteurs embarquée (format « RHP1 », voir",
        "/// scripts/gen_headphone_bank.py) — décodée par headphone_bank.cpp.",
        f"inline constexpr unsigned char kHeadphoneBankData[] = {{",
    ]
    for i in range(0, len(blob), 20):
        chunk = blob[i:i + 20]
        lines.append("    " + ",".join(str(b) for b in chunk) + ",")
    lines += [
        "};",
        f"inline constexpr size_t kHeadphoneBankSize = sizeof(kHeadphoneBankData);",
        "",
        "} // namespace roomeq_vst",
        "",
    ]
    content = "\n".join(lines)
    if DST.exists() and DST.read_text(encoding="utf-8") == content:
        print(f"{DST.name} : inchangé ({len(blob)} octets, {count} modèles)")
        return 0
    DST.write_text(content, encoding="utf-8")
    print(f"{DST.name} : écrit ({len(blob)} octets, {count} modèles)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
