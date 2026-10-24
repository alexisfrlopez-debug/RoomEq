#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Générateur de fragments WiX pour l'installateur RoomEq.

Produit un fichier .wxs contenant un ComponentGroup avec UN composant par
fichier (File = clé, Guid auto) — équivalent multiplateforme de « heat dir »,
sans dépendre d'un outil Windows.

Usage :
    python3 gen_wix_fragment.py <répertoire_source> <groupe> <répertoire_wix_id> \
        <var_source> <fichier_sortie.wxs>

Exemple (application) :
    python3 gen_wix_fragment.py publish/ AppFiles INSTALLDIR var.PublishRoot AppFiles.wxs
"""
import os
import sys
from pathlib import Path


def esc(s: str) -> str:
    return (s.replace("&", "&amp;").replace("<", "&lt;")
             .replace(">", "&gt;").replace('"', "&quot;"))


def xml_id(rel: Path) -> str:
    """Identifiant WiX stable depuis le chemin relatif (alphanumérique + _)."""
    raw = str(rel).replace("\\", "/").replace("/", "_").replace(".", "_").replace("-", "_")
    out = "".join(c for c in raw if c.isalnum() or c == "_")
    return ("F_" + out)[:70]


def main() -> int:
    if len(sys.argv) != 6:
        print(__doc__)
        return 2
    src_root = Path(sys.argv[1]).resolve()
    group = sys.argv[2]
    dir_id = sys.argv[3]
    var_src = sys.argv[4]  # ex. var.PublishRoot
    out_path = Path(sys.argv[5])

    if not src_root.is_dir():
        print(f"répertoire introuvable : {src_root}", file=sys.stderr)
        return 1

    lines = [
        '<?xml version="1.0" encoding="utf-8"?>',
        '<!-- Généré automatiquement par gen_wix_fragment.py — ne pas éditer à la main. -->',
        '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">',
        '  <Fragment>',
        f'    <ComponentGroup Id="{esc(group)}" Directory="{esc(dir_id)}">',
    ]

    seen = set()
    for path in sorted(p for p in src_root.rglob("*") if p.is_file()):
        rel = path.relative_to(src_root)
        cid = xml_id(rel)
        # collisions improbable mais surveillées
        while cid in seen:
            cid += "_"
        seen.add(cid)
        # séparateur '/' accepté par wix sous Windows ET Linux (normalisé au build)
        src = f"$({var_src})/{rel.as_posix()}"
        lines += [
            f'      <Component Id="{cid}" Guid="*">',
            f'        <File Id="{cid}File" Source="{esc(src)}" />',
            '      </Component>',
        ]

    lines += ['    </ComponentGroup>', '  </Fragment>', '</Wix>', '']
    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"✓ {out_path} : {len(seen)} fichiers")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
