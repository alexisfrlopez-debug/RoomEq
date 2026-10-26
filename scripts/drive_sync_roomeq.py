#!/usr/bin/env python3
"""Miroir Drive du dépôt RoomEq v3 — dossier « RoomEq-v3 » de la racine Equalizer.

Idempotent par MD5 : un fichier identique n'est pas re-téléversé. Les jetons
sont lus depuis ../triade/.gdrive/ (jamais dans le dépôt). Un bundle git
complet est téléversé pour restaurabilité (git clone roomeq-v3.bundle).
Les livrables (zips de download/, préfixe RoomEq-) vont dans le sous-dossier
« Livrables » — téléversement resumable (42 Mo > limite multipart).
"""
import hashlib
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request
import urllib.parse

REPO = os.environ.get("ROOMEQ_REPO", "/home/z/my-project/roomeq")
TOKENS = os.environ.get("ROOMEQ_TOKENS", "/home/z/my-project/triade/.gdrive")
DOWNLOAD_DIR = os.environ.get("ROOMEQ_DOWNLOAD", "/home/z/my-project/download")
DRIVE_ROOT = "1vRB4hEr9thaRnJsPjz8JCezGuzzD_yJR"      # dossier « Equalizer »
FOLDER_NAME = "RoomEq-v3"
DELIVERABLES_FOLDER = "Livrables"
EXCLUDE_DIRS = {".git", "bin", "obj", ".vs", "tmp-diag", "roomeq-git-ref", "roomeq-legacy", "publish"}


def get_access_token():
    with open(f"{TOKENS}/oauth_token_relais.json") as f:
        tok = json.load(f)
    with open(f"{TOKENS}/client_secret.json") as f:
        cs = json.load(f)
    inst = cs.get("installed", cs.get("web", {}))
    data = urllib.parse.urlencode({
        "client_id": inst["client_id"],
        "client_secret": inst["client_secret"],
        "refresh_token": tok["refresh_token"],
        "grant_type": "refresh_token",
    }).encode()
    req = urllib.request.Request("https://oauth2.googleapis.com/token", data=data)
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)["access_token"]


def drive_api(path, token, params=None, method="GET", data=None, content_type="application/json"):
    url = "https://www.googleapis.com" + path
    if params:
        url += "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, method=method, data=data,
                                 headers={"Authorization": f"Bearer {token}",
                                          "Content-Type": content_type})
    with urllib.request.urlopen(req, timeout=120) as r:
        body = r.read()
        return json.loads(body) if body else {}


def list_children(folder_id, token):
    params = {"q": f"'{folder_id}' in parents and trashed=false",
              "fields": "files(id,name,mimeType,md5Checksum,size)", "pageSize": 200}
    out, page = [], None
    while True:
        p = dict(params)
        if page:
            p["pageToken"] = page
        res = drive_api("/drive/v3/files", token, p)
        out.extend(res.get("files", []))
        page = res.get("nextPageToken")
        if not page:
            return {f["name"]: f for f in out}


def ensure_folder(parent, name, token):
    children = list_children(parent, token)
    if name in children:
        return children[name]["id"]
    meta = json.dumps({"name": name, "mimeType": "application/vnd.google-apps.folder",
                       "parents": [parent]}).encode()
    res = drive_api("/drive/v3/files", token, method="POST", data=meta)
    return res["id"]


def upload_new(folder_id, name, path, token):
    boundary = "==roomeq-v3=="
    with open(path, "rb") as f:
        content = f.read()
    meta = json.dumps({"name": name, "parents": [folder_id]})
    body = (f"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n{meta}\r\n"
            f"--{boundary}\r\nContent-Type: application/octet-stream\r\n\r\n").encode() + content \
           + f"\r\n--{boundary}--".encode()
    drive_api("/upload/drive/v3/files?uploadType=multipart&fields=id", token,
              method="POST", data=body, content_type=f"multipart/related; boundary={boundary}")


def update_existing(file_id, path, token):
    with open(path, "rb") as f:
        content = f.read()
    req = urllib.request.Request(
        f"https://www.googleapis.com/upload/drive/v3/files/{file_id}?uploadType=media",
        method="PATCH", data=content,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/octet-stream"})
    with urllib.request.urlopen(req, timeout=120) as r:
        r.read()


def resumable_upload(path, token, name=None, file_id=None, parents=None):
    """Téléversement resumable par morceaux de 8 Mo (gros fichiers).

    Création (name+parents) ou mise à jour (file_id) ; gère les réponses
    intermédiaires 308 (chunk accepté) et finales 200/201.
    """
    total = os.path.getsize(path)
    if file_id:
        init_url = (f"https://www.googleapis.com/upload/drive/v3/files/{file_id}"
                    "?uploadType=resumable&fields=id")
        init_data, method = b"{}", "PATCH"
    else:
        init_url = ("https://www.googleapis.com/upload/drive/v3/files"
                    "?uploadType=resumable&fields=id")
        init_data = json.dumps({"name": name, "parents": parents}).encode()
        method = "POST"
    req = urllib.request.Request(init_url, method=method, data=init_data,
                                 headers={"Authorization": f"Bearer {token}",
                                          "Content-Type": "application/json; charset=UTF-8",
                                          "X-Upload-Content-Type": "application/zip",
                                          "X-Upload-Content-Length": str(total)})
    with urllib.request.urlopen(req, timeout=120) as r:
        session_uri = r.headers["Location"]
    chunk = 8 * 1024 * 1024
    with open(path, "rb") as f:
        offset = 0
        while offset < total:
            data = f.read(chunk)
            end = offset + len(data) - 1
            req = urllib.request.Request(
                session_uri, method="PUT", data=data,
                headers={"Content-Range": f"bytes {offset}-{end}/{total}",
                         "Content-Length": str(len(data))})
            try:
                with urllib.request.urlopen(req, timeout=600) as r:
                    code, body = r.status, r.read()
            except urllib.error.HTTPError as e:
                code, body = e.code, (e.read() if e.fp else b"")
            if code in (200, 201):
                return json.loads(body) if body else {}
            if code != 308:
                raise RuntimeError(f"upload resumable {path} : HTTP {code}")
            offset += len(data)
    return {}


def mirror(local_dir, folder_id, token, rel=""):
    children = list_children(folder_id, token)
    uploaded = updated = unchanged = 0
    for entry in sorted(os.listdir(local_dir)):
        local_path = os.path.join(local_dir, entry)
        rel_path = f"{rel}/{entry}" if rel else entry
        if os.path.isdir(local_path):
            if entry in EXCLUDE_DIRS or entry.startswith("."):
                continue
            sub_id = ensure_folder(folder_id, entry, token)
            sub = mirror(local_path, sub_id, token, rel_path)
            uploaded += sub[0]; updated += sub[1]; unchanged += sub[2]
            continue
        with open(local_path, "rb") as f:
            md5 = hashlib.md5(f.read()).hexdigest()
        remote = children.get(entry)
        if remote is None:
            upload_new(folder_id, entry, local_path, token)
            uploaded += 1
        elif remote.get("md5Checksum") != md5:
            update_existing(remote["id"], local_path, token)
            updated += 1
        else:
            unchanged += 1
    return uploaded, updated, unchanged


def sync_deliverables(folder_id, token):
    """Téléverse les livrables RoomEq-*.zip de download/ dans « Livrables »."""
    if not os.path.isdir(DOWNLOAD_DIR):
        print("Livrables : dossier download/ absent — rien à faire")
        return
    zips = sorted(f for f in os.listdir(DOWNLOAD_DIR)
                  if f.startswith("RoomEq-") and f.endswith(".zip"))
    if not zips:
        print("Livrables : aucun zip RoomEq-* dans download/ — rien à faire")
        return
    liv_id = ensure_folder(folder_id, DELIVERABLES_FOLDER, token)
    children = list_children(liv_id, token)
    print(f"Livrables → {FOLDER_NAME}/{DELIVERABLES_FOLDER} ({len(zips)} zips) :")
    for z in zips:
        p = os.path.join(DOWNLOAD_DIR, z)
        size_mb = os.path.getsize(p) / (1024 * 1024)
        with open(p, "rb") as f:
            md5 = hashlib.md5(f.read()).hexdigest()
        remote = children.get(z)
        if remote is None:
            resumable_upload(p, token, name=z, parents=[liv_id])
            print(f"  + {z} ({size_mb:.1f} Mo) : téléversé")
        elif remote.get("md5Checksum") != md5:
            resumable_upload(p, token, file_id=remote["id"])
            print(f"  ± {z} ({size_mb:.1f} Mo) : mis à jour")
        else:
            print(f"  = {z} ({size_mb:.1f} Mo) : inchangé")


def main():
    token = get_access_token()
    folder_id = ensure_folder(DRIVE_ROOT, FOLDER_NAME, token)
    print(f"Dossier Drive : {FOLDER_NAME} ({folder_id})")

    up, upd, unch = mirror(REPO, folder_id, token)
    print(f"Fichiers : {up} créés, {upd} mis à jour, {unch} inchangés")

    sync_deliverables(folder_id, token)

    # bundle git pour restaurabilité complète (historique)
    bundle = os.path.join("/tmp", "roomeq-v3.bundle")
    subprocess.run(["git", "-C", REPO, "bundle", "create", bundle, "--all"], check=True)
    children = list_children(folder_id, token)
    if "roomeq-v3.bundle" in children:
        update_existing(children["roomeq-v3.bundle"]["id"], bundle, token)
        print("Bundle git : mis à jour")
    else:
        upload_new(folder_id, "roomeq-v3.bundle", bundle, token)
        print("Bundle git : créé")
    os.remove(bundle)
    print("Miroir Drive terminé.")


if __name__ == "__main__":
    sys.exit(main())
