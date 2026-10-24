#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Restauration RoomEq depuis Google Drive — variante API tolérante.

Contraire à `drive_backup.py --restore` (qui suit le manifeste figé du 10-10
et échoue au premier 404), ce script :
  1. liste l'arborescence Drive ACTUELLE via l'API v3 (token OAuth frais) ;
  2. télécharge chaque fichier authentifié (alt=media) — pas de lien public ;
  3. tolère les fichiers absents (déplacés/supprimés) en les signalant.

Mappage (miroir de MAPPING de drive_backup.py) :
  data/RoomEq/**            → workspace/roomq-source/RoomEq/**
  data/worklog.md           → worklog.md
  data/scripts/**           → scripts/**
  livrables/**              → download/**               (hors Archives/)
  INSTRUCTIONS-REPRISE.md   → download/INSTRUCTIONS-REPRISE.md
"""
import json
import os
import sys
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import drive_backup as db  # réutilise http(), get_access_token(), API

ROOT_ID = db.MAPPING and "__ID_RACINE_DRIVE__"
DATA_ID = "__ID_DOSSIER_DATA__"
LIVRABLES_ID = "__ID_DOSSIER_LIVRABLES__"
BASE = "/home/z/my-project"

# Fichiers d'identifiants : jamais restaurés dans l'arbre de travail.
SECRET_PREFIXES = ("client_secret", "oauth_token", "projet-gcp-prive", "token_git",
                   "service_account", "keystore", "nyx")


def list_tree(token, folder_id, path="", out=None):
    """Liste récursivement (fichiers non supprimés) → [(chemin_drive, id)]."""
    out = out if out is not None else []
    q = urllib.parse.quote(f"'{folder_id}' in parents and trashed = false")
    page_token = None
    while True:
        url = (f"{db.API}/files?q={q}&fields=nextPageToken,"
               f"files(id,name,mimeType)&pageSize=200")
        if page_token:
            url += f"&pageToken={page_token}"
        r = db.api_get(url, token)
        for f in r.get("files", []):
            child = f"{path}/{f['name']}" if path else f['name']
            if f["mimeType"] == db.FOLDER_MIME:
                list_tree(token, f["id"], child, out)
            else:
                out.append((child, f["id"]))
        page_token = r.get("nextPageToken")
        if not page_token:
            break
    return out


def main():
    token = db.get_access_token()
    print("→ Liste de l'arborescence Drive (data/ + livrables/ + racine)…")
    data_files = list_tree(token, DATA_ID)
    livr_files = list_tree(token, LIVRABLES_ID)
    root_files = [f for f in list_tree(token, ROOT_ID) if "/" not in f[0]]

    plan = []  # (chemin_local, id)
    skipped = []
    for path, fid in data_files:
        name = os.path.basename(path)
        if name.startswith(SECRET_PREFIXES):
            skipped.append(path); continue
        if path == "worklog.md":
            plan.append((os.path.join(BASE, "worklog.md"), fid))
        elif path.startswith("scripts/"):
            plan.append((os.path.join(BASE, path), fid))
        elif path.startswith("RoomEq/"):
            plan.append((os.path.join(BASE, "workspace/roomq-source", path), fid))
        elif path.startswith("snapshots/"):
            skipped.append(path); continue
        elif "/" not in path and name.startswith("_manifest"):
            skipped.append(path); continue
        else:
            skipped.append(f"(non mappé) {path}")
    for path, fid in livr_files:
        if path.startswith("Archives/"):
            skipped.append(path); continue
        plan.append((os.path.join(BASE, "download", path), fid))
    for path, fid in root_files:
        if path == "INSTRUCTIONS-REPRISE.md":
            plan.append((os.path.join(BASE, "download", path), fid))
        else:
            skipped.append(f"(racine non mappée) {path}")

    print(f"→ {len(plan)} fichiers à restaurer, {len(skipped)} ignorés.")
    ok = miss = 0
    for i, (dest, fid) in enumerate(plan, 1):
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        if os.path.exists(dest) and os.path.getsize(dest) > 0:
            ok += 1; continue  # déjà présent (reprise)
        try:
            body = db.http(f"{db.API}/files/{fid}?alt=media", token=token, raw=True)
            with open(dest, "wb") as fh:
                fh.write(body)
            ok += 1
        except Exception as e:
            miss += 1
            print(f"  ✗ {dest} — {str(e)[:90]}")
        if i % 40 == 0:
            print(f"   {i}/{len(plan)}…")
    print(f"✓ Restauration : {ok} OK, {miss} inaccessible(s).")
    if skipped:
        print("Ignorés (secrets / snapshots / non mappés) :")
        for s in skipped[:12]:
            print(f"  - {s}")
        if len(skipped) > 12:
            print(f"  … et {len(skipped) - 12} autres")


if __name__ == "__main__":
    main()
