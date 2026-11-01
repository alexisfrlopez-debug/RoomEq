#!/usr/bin/env python3
"""Miroir Drive du dépôt RoomEq v3 — dossier « RoomEq-v3 » de la racine Equalizer.

Idempotent par MD5 : un fichier identique n'est pas re-téléversé. Les jetons
sont lus depuis ../triade/.gdrive/ (jamais dans le dépôt). Un bundle git
complet est téléversé pour restaurabilité (git clone roomeq-v3.bundle).
Les livrables (zips de download/, préfixe RoomEq-) vont dans le sous-dossier
« Livrables » — téléversement resumable (42 Mo > limite multipart).

D22 (archivage des anciennes versions) : au premier niveau de « Livrables »
ne reste que la DERNIÈRE version de chaque produit (appli, VST, AU) ;
les versions antérieures sont déplacées dans « Livrables/Archive »
(jamais supprimées — retour arrière toujours possible).
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request
import urllib.parse

REPO = os.environ.get("ROOMEQ_REPO", "/home/z/my-project/workspace/RoomEq")
TOKENS = os.environ.get("ROOMEQ_TOKENS", "/home/z/my-project/workspace/triade/.gdrive")
DOWNLOAD_DIR = os.environ.get("ROOMEQ_DOWNLOAD", "/home/z/my-project/download")
DRIVE_ROOT = "1vRB4hEr9thaRnJsPjz8JCezGuzzD_yJR"      # dossier « Equalizer »
FOLDER_NAME = "RoomEq-v3"
DELIVERABLES_FOLDER = "Livrables"
ARCHIVE_FOLDER = "Archive"
EXCLUDE_DIRS = {".git", "bin", "obj", ".vs", "tmp-diag", "roomeq-git-ref",
                "roomeq-legacy", "publish", "dist", "build", "__pycache__"}

# Famille produit + version depuis un nom de livrable :
#   RoomEq-VST-3.5.1-win32.zip   → ("RoomEq-VST-", 3.5.1)
#   RoomEq-3.4.0-win-x64-test.zip → ("RoomEq-",     3.4.0)
#   RoomEq-win-Setup.exe          → None (non versionné, jamais archivé)
_VERSION_RE = re.compile(
    r"^(RoomEq-(?:VST-|AU-)?)(\d+)\.(\d+)\.(\d+)(?:-dev\.(\d+))?(?=[-.]|$)")


def version_key(name):
    """(famille, clé de tri) d'un livrable, ou None si non versionné.

    Clé : (majeure, mineure, correctif, final>dev, n°dev) — ainsi
    3.0.0-dev.1 < 3.0.0 < 3.0.1 < 3.4.0.
    """
    m = _VERSION_RE.match(name)
    if not m:
        return None
    family, maj, min_, pat, dev = m.groups()
    key = (int(maj), int(min_), int(pat), 1 if dev is None else 0, int(dev or 0))
    return family, key


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


def resumable_upload(path, token, name=None, file_id=None, parents=None,
                      content_type="application/zip"):
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
                                          "X-Upload-Content-Type": content_type,
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


def sync_deliverables(liv_id, token):
    """Téléverse les livrables de download/ dans « Livrables » (liv_id).

    D20 : zips RoomEq-* + installateurs 1 clic (RoomEq-win-Setup.exe,
    RoomEq-AU-*.pkg) + LISEZMOI du greffon (RoomEq-AU-LISEZMOI.md).
    """
    if not os.path.isdir(DOWNLOAD_DIR):
        print("Livrables : dossier download/ absent — rien à téléverser")
        return
    content_types = {
        ".zip": "application/zip",
        ".exe": "application/vnd.microsoft.portable-executable",
        ".pkg": "application/x-newton-compatible-pkg",
        ".md": "text/markdown",
    }
    files = sorted(f for f in os.listdir(DOWNLOAD_DIR)
                   if f.startswith("RoomEq-")
                   and f.endswith(tuple(content_types)))
    if not files:
        print("Livrables : aucun fichier RoomEq-* dans download/ — rien à téléverser")
        return
    children = list_children(liv_id, token)
    print(f"Livrables → {FOLDER_NAME}/{DELIVERABLES_FOLDER} ({len(files)} fichiers) :")
    for z in files:
        p = os.path.join(DOWNLOAD_DIR, z)
        size_mb = os.path.getsize(p) / (1024 * 1024)
        with open(p, "rb") as f:
            md5 = hashlib.md5(f.read()).hexdigest()
        remote = children.get(z)
        ctype = content_types[z[z.rfind("."):]]
        if remote is None:
            resumable_upload(p, token, name=z, parents=[liv_id],
                             content_type=ctype)
            print(f"  + {z} ({size_mb:.1f} Mo) : téléversé")
        elif remote.get("md5Checksum") != md5:
            resumable_upload(p, token, file_id=remote["id"],
                             content_type=ctype)
            print(f"  ± {z} ({size_mb:.1f} Mo) : mis à jour")
        else:
            print(f"  = {z} ({size_mb:.1f} Mo) : inchangé")


def archive_old_versions(liv_id, token):
    """D22 — archivage des anciennes versions (idempotent).

    Au premier niveau de « Livrables » ne reste que la dernière version de
    chaque produit (familles : appli zips « RoomEq- », VST, AU) ; les versions
    antérieures sont DÉPLACÉES dans « Livrables/Archive » — jamais supprimées
    (retour arrière possible). Les fichiers non versionnés (Setup.exe,
    LISEZMOI) restent au premier niveau. Un homonyme déjà présent dans
    l'Archive (version re-livrée) fait corbeiller la copie du premier niveau.
    """
    children = list_children(liv_id, token)
    versioned = []
    for name, f in children.items():
        if f["mimeType"] == "application/vnd.google-apps.folder":
            continue
        vk = version_key(name)
        if vk:
            versioned.append((vk[0], vk[1], name))
    if not versioned:
        print("Archivage D22 : aucun livrable versionné — rien à faire")
        return
    latest = {}
    for family, key, name in versioned:
        if family not in latest or key > latest[family][0]:
            latest[family] = (key, name)
    to_archive = sorted(name for family, key, name in versioned
                        if key < latest[family][0])
    if not to_archive:
        kept = sorted(name for _, name in latest.values())
        print("Archivage D22 : rien à archiver (une version par produit) — "
              + ", ".join(kept))
        return
    arch_id = ensure_folder(liv_id, ARCHIVE_FOLDER, token)
    arch_children = list_children(arch_id, token)
    print(f"Archivage D22 : {len(to_archive)} ancienne(s) version(s) → "
          f"{FOLDER_NAME}/{DELIVERABLES_FOLDER}/{ARCHIVE_FOLDER} :")
    for name in to_archive:
        f = children[name]
        if name in arch_children:
            drive_api(f"/drive/v3/files/{f['id']}", token, method="PATCH",
                      data=json.dumps({"trashed": True}).encode())
            print(f"  ~ {name} : homonyme déjà archivé — "
                  "copie du premier niveau corbeillée")
            continue
        drive_api(f"/drive/v3/files/{f['id']}", token,
                  params={"addParents": arch_id, "removeParents": liv_id,
                          "fields": "id"},
                  method="PATCH", data=json.dumps({}).encode())
        print(f"  → {name} : archivé")
    kept = sorted(name for _, name in latest.values())
    unversioned = sorted(n for n, f in children.items()
                         if f["mimeType"] != "application/vnd.google-apps.folder"
                         and not version_key(n))
    print("Premier niveau de « Livrables » (version courante par produit) : "
          + ", ".join(kept + unversioned))


def main():
    token = get_access_token()
    folder_id = ensure_folder(DRIVE_ROOT, FOLDER_NAME, token)
    print(f"Dossier Drive : {FOLDER_NAME} ({folder_id})")

    up, upd, unch = mirror(REPO, folder_id, token)
    print(f"Fichiers : {up} créés, {upd} mis à jour, {unch} inchangés")

    liv_id = ensure_folder(folder_id, DELIVERABLES_FOLDER, token)
    sync_deliverables(liv_id, token)
    archive_old_versions(liv_id, token)

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
