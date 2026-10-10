#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Consultation des journaux envoyés par la version EXE de test de RoomEq.

Liste (et optionnellement télécharge) les journaux du dossier Drive
« journaux-app » : un fichier par machine et par jour.

Usage :
  python3 scripts/fetch_app_logs.py                     # liste les journaux
  python3 scripts/fetch_app_logs.py --machine MON-PC    # filtre par machine
  python3 scripts/fetch_app_logs.py --day 2026-10-10    # filtre par jour
  python3 scripts/fetch_app_logs.py --tail 100          # affiche les N dernières lignes
  python3 scripts/fetch_app_logs.py --download DIR      # télécharge vers DIR
"""
import argparse
import os
import sys
import urllib.parse

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from drive_backup import API, get_access_token, http  # noqa: E402
from create_log_folder import FOLDER_NAME  # noqa: E402


def list_logs(token, folder_id):
    q = urllib.parse.quote(f"'{folder_id}' in parents and trashed = false")
    r = http(f"{API}/files?q={q}&fields=files(id,name,size,modifiedTime)&pageSize=200&"
             f"orderBy={urllib.parse.quote('modifiedTime desc')}", token=token)
    return sorted(r.get("files", []), key=lambda f: f["name"])


def matches(f, machine, day):
    name = f["name"]
    if machine and machine.lower() not in name.lower():
        return False
    if day and day not in name:
        return False
    return True


def main():
    ap = argparse.ArgumentParser(description="Journaux de télémétrie RoomEq (Drive)")
    ap.add_argument("--machine", help="filtre par nom de machine")
    ap.add_argument("--day", help="filtre par jour (AAAA-MM-JJ)")
    ap.add_argument("--tail", type=int, default=0, help="affiche les N dernières lignes de chaque journal")
    ap.add_argument("--download", metavar="DIR", help="télécharge les journaux filtrés vers DIR")
    args = ap.parse_args()

    token = get_access_token()

    # retrouver le dossier par son nom (l'ID est stable mais on reste tolérant)
    from drive_backup import ROOT_FOLDER_ID, find_child, FOLDER_MIME
    child = find_child(token, ROOT_FOLDER_ID, FOLDER_NAME)
    if not child:
        sys.exit(f"Dossier « {FOLDER_NAME} » introuvable sous la racine.")
    folder_id = child["id"]

    logs = [f for f in list_logs(token, folder_id) if matches(f, args.machine, args.day)]
    if not logs:
        print("Aucun journal correspondant.")
        return

    print(f"{len(logs)} journal(aux) :")
    for f in logs:
        size_kb = int(f.get("size", 0)) / 1024
        print(f"  {f['name']:55s} {size_kb:8.1f} Ko   modifié {f.get('modifiedTime', '?')[:19]}")

    if args.download:
        os.makedirs(args.download, exist_ok=True)
        for f in logs:
            body = http(f"{API}/files/{f['id']}?alt=media", token=token, raw=True)
            dest = os.path.join(args.download, f["name"])
            open(dest, "wb").write(body)
            print(f"  ↓ {dest}")
        return

    if args.tail:
        n = args.tail
        for f in logs:
            body = http(f"{API}/files/{f['id']}?alt=media", token=token, raw=True)
            lines = body.decode(errors="replace").splitlines()
            print(f"\n──── {f['name']} — {n} dernières lignes ────")
            print("\n".join(lines[-n:]))


if __name__ == "__main__":
    main()
