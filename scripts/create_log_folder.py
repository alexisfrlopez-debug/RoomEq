#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Crée (ou retrouve) le dossier Google Drive qui reçoit les journaux de
diagnostic envoyés par la version EXE de test de RoomEq, y dépose une note
explicative, et écrit scripts/telemetrie.json (config embarquée dans l'EXE).

Usage : python3 scripts/create_log_folder.py
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from drive_backup import (  # noqa: E402
    API, FOLDER_MIME, ROOT_FOLDER_ID, UPLOAD, find_child, get_access_token, http,
)

FOLDER_NAME = "journaux-app"
TOKEN_FILE = "/home/z/my-project/secrets/oauth_token_relais.json"


def main():
    token = get_access_token()
    print("✓ Access token OK")

    # retrouver le dossier s'il existe déjà
    child = find_child(token, ROOT_FOLDER_ID, FOLDER_NAME)
    if child and child.get("mimeType") == FOLDER_MIME:
        fid = child["id"]
        print(f"✓ Dossier existant : {FOLDER_NAME} (id {fid})")
    else:
        meta = json.dumps({"name": FOLDER_NAME, "mimeType": FOLDER_MIME,
                           "parents": [ROOT_FOLDER_ID]})
        r = http(f"{API}/files", data=meta.encode(),
                 headers={"Content-Type": "application/json"}, token=token)
        fid = r["id"]
        print(f"✓ Dossier créé : {FOLDER_NAME} (id {fid})")

    # note explicative dans le dossier
    note = (
        "Journaux de diagnostic envoyés automatiquement par la version EXE de test "
        "de RoomEq (télémétrie v1.7.0+).\n\n"
        "- Un fichier par machine et par jour : <machine>-roomeq-AAAA-MM-JJ.log\n"
        "- Envoi à la fermeture de l'application, puis toutes les 5 minutes.\n"
        "- Consultation : python3 scripts/fetch_app_logs.py [--download DIR]\n"
    )
    body = note.encode()
    note_child = find_child(token, fid, "LISEZMOI.txt")
    if note_child:
        http(f"{UPLOAD}/files/{note_child['id']}?uploadType=media", data=body,
             headers={"Content-Type": "text/plain"}, token=token, method="PATCH")
    else:
        meta = json.dumps({"name": "LISEZMOI.txt", "parents": [fid]})
        boundary = "==roomq=="
        payload = (f"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n"
                   f"{meta}\r\n--{boundary}\r\nContent-Type: text/plain\r\n\r\n").encode() \
                  + body + f"\r\n--{boundary}--".encode()
        http(f"{UPLOAD}/files?uploadType=multipart",
             data=payload, headers={"Content-Type": f"multipart/related; boundary={boundary}"},
             token=token)

    # configuration de télémétrie pour l'application C#
    tok = json.load(open(TOKEN_FILE))
    out = {
        "client_id": tok["client_id"],
        "client_secret": tok["client_secret"],
        "refresh_token": tok["refresh_token"],
        "token_uri": tok.get("token_uri", "https://oauth2.googleapis.com/token"),
        "folder_id": fid,
        "enabled": True,
    }
    dest = "/home/z/my-project/scripts/telemetrie.json"
    with open(dest, "w", encoding="utf-8") as f:
        json.dump(out, f, indent=2)
    print(f"✓ Configuration télémétrie écrite : {dest}")
    print(f"  FOLDER_ID = {fid}")


if __name__ == "__main__":
    main()
