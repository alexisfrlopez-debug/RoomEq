#!/usr/bin/env python3
"""Restaure le projet RoomEq depuis la sauvegarde Google Drive.

Mapping Drive -> local (inverse de drive_backup.py):
  data/RoomEq/**           -> workspace/roomq-source/RoomEq/**
  data/worklog.md          -> worklog.md
  livrables/**             -> download/**  (seulement ce qui existe)
  INSTRUCTIONS-REPRISE.md  -> download/INSTRUCTIONS-REPRISE.md
"""
import json
import os
import sys
import urllib.request
import urllib.parse
import urllib.error

ROOT_FOLDER_ID = "__ID_RACINE_DRIVE__"
OAUTH_FILE_ID = "__ID_FICHIER_TOKEN_OAUTH__"
BASE = "/home/z/my-project"

API = "https://www.googleapis.com/drive/v3"
TOKEN_URL = "https://oauth2.googleapis.com/token"


def http_json(url, data=None, headers=None, method=None):
    req = urllib.request.Request(url, data=data, headers=headers or {}, method=method)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def http_raw(url, headers=None, method=None):
    req = urllib.request.Request(url, data=None, headers=headers or {}, method=method)
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def get_oauth_creds():
    """Télécharge le fichier OAuth via le lien public uc?export=download."""
    url = f"https://drive.google.com/uc?export=download&id={OAUTH_FILE_ID}"
    return json.loads(http_raw(url).decode("utf-8"))


def refresh_access_token(creds):
    data = urllib.parse.urlencode({
        "client_id": creds["client_id"],
        "client_secret": creds["client_secret"],
        "refresh_token": creds["refresh_token"],
        "grant_type": "refresh_token",
    }).encode()
    return http_json(TOKEN_URL, data=data)["access_token"]


def drive_list(folder_id, token):
    """Liste les enfants directs d'un dossier Drive."""
    files = []
    page_token = None
    while True:
        params = {
            "q": f"'{folder_id}' in parents and trashed=false",
            "fields": "nextPageToken, files(id,name,mimeType,size,modifiedTime)",
            "pageSize": "200",
        }
        if page_token:
            params["pageToken"] = page_token
        url = f"{API}/files?" + urllib.parse.urlencode(params)
        res = http_json(url, headers={"Authorization": f"Bearer {token}"})
        files.extend(res.get("files", []))
        page_token = res.get("nextPageToken")
        if not page_token:
            break
    return files


def drive_download(file_id, token):
    url = f"{API}/files/{file_id}?alt=media"
    return http_raw(url, headers={"Authorization": f"Bearer {token}"})


def walk_folder(folder_id, token, prefix=""):
    """Parcourt récursivement, retourne [(chemin_relatif, file_id, modifiedTime)]."""
    out = []
    for f in drive_list(folder_id, token):
        name = f["name"]
        rel = f"{prefix}/{name}" if prefix else name
        if f["mimeType"] == "application/vnd.google-apps.folder":
            out.extend(walk_folder(f["id"], token, rel))
        else:
            out.append((rel, f["id"], f.get("modifiedTime", "")))
    return out


MAPPING = [
    ("data/RoomEq/", os.path.join(BASE, "workspace/roomq-source/RoomEq/")),
    ("data/worklog.md", os.path.join(BASE, "worklog.md")),
    ("INSTRUCTIONS-REPRISE.md", os.path.join(BASE, "download/INSTRUCTIONS-REPRISE.md")),
    ("livrables/", os.path.join(BASE, "download/")),
]


def local_path_for(rel):
    for drive_prefix, local_prefix in MAPPING:
        if rel == drive_prefix.rstrip("/"):
            return local_prefix
        if rel.startswith(drive_prefix):
            return os.path.join(local_prefix, rel[len(drive_prefix):])
    return None  # livrables et autres: ignores pour l'instant


def main():
    dry = "--dry-run" in sys.argv
    print("== Restauration RoomEq depuis Google Drive ==")
    creds = get_oauth_creds()
    print("[1/4] Credentials OAuth recuperees")
    token = refresh_access_token(creds)
    print("[2/4] Token d'acces obtenu")

    entries = walk_folder(ROOT_FOLDER_ID, token)
    print(f"[3/4] {len(entries)} entrees trouvees sur Drive")

    # Déduplication: garder la version la plus récente de chaque chemin
    best = {}
    for rel, fid, mtime in entries:
        if rel not in best or mtime > best[rel][1]:
            best[rel] = (fid, mtime)
    print(f"      {len(best)} fichiers uniques apres deduplication")

    restored, skipped = 0, 0
    for rel in sorted(best):
        fid, _ = best[rel]
        lp = local_path_for(rel)
        if not lp:
            skipped += 1
            continue
        if dry:
            print(f"  [dry] {rel} -> {lp}")
            restored += 1
            continue
        os.makedirs(os.path.dirname(lp), exist_ok=True)
        content = drive_download(fid, token)
        with open(lp, "wb") as fh:
            fh.write(content)
        restored += 1
    print(f"[4/4] {restored} fichiers restaures, {skipped} ignores")
    if not dry:
        print("Verif:")
        for p in ("worklog.md", "download/INSTRUCTIONS-REPRISE.md",
                  "workspace/roomq-source/RoomEq/RoomEq.csproj"):
            print(f"  {'OK ' if os.path.exists(os.path.join(BASE, p)) else 'MANQUE '}{p}")


if __name__ == "__main__":
    main()
