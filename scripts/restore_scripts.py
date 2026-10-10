#!/usr/bin/env python3
"""Restaure uniquement data/scripts depuis Google Drive (outil backup + packaging)."""
import json
import os
import sys
import urllib.request
import urllib.parse

ROOT_FOLDER_ID = "__ID_RACINE_DRIVE__"
OAUTH_FILE_ID = "__ID_FICHIER_TOKEN_OAUTH__"
API = "https://www.googleapis.com/drive/v3"
TOKEN_URL = "https://oauth2.googleapis.com/token"
DEST = "/home/z/my-project/scripts"


def http_json(url, data=None, headers=None):
    req = urllib.request.Request(url, data=data, headers=headers or {})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))


def http_raw(url, headers=None):
    req = urllib.request.Request(url, data=None, headers=headers or {})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


creds = json.loads(http_raw(
    f"https://drive.google.com/uc?export=download&id={OAUTH_FILE_ID}").decode())
token = http_json(TOKEN_URL, data=urllib.parse.urlencode({
    "client_id": creds["client_id"],
    "client_secret": creds["client_secret"],
    "refresh_token": creds["refresh_token"],
    "grant_type": "refresh_token",
}).encode())["access_token"]

H = {"Authorization": f"Bearer {token}"}


def find_child(folder_id, name):
    params = {
        "q": f"'{folder_id}' in parents and trashed=false and name='{name}'",
        "fields": "files(id,name,mimeType)",
    }
    res = http_json(f"{API}/files?" + urllib.parse.urlencode(params), headers=H)
    res["files"] = [f for f in res.get("files", [])
                    if f["name"] == name]  # échapper les apostrophes
    return res["files"][0] if res["files"] else None


def list_children(folder_id):
    params = {
        "q": f"'{folder_id}' in parents and trashed=false",
        "fields": "files(id,name,mimeType,modifiedTime)", "pageSize": "200",
    }
    return http_json(f"{API}/files?" + urllib.parse.urlencode(params), headers=H)["files"]


def walk(folder_id, prefix=""):
    out = []
    for f in list_children(folder_id):
        rel = f"{prefix}/{f['name']}" if prefix else f["name"]
        if f["mimeType"] == "application/vnd.google-apps.folder":
            out.extend(walk(f["id"], rel))
        else:
            out.append((rel, f["id"], f.get("modifiedTime", "")))
    return out


root = find_child(ROOT_FOLDER_ID, "data")
scripts_folder = find_child(root["id"], "scripts")
entries = walk(scripts_folder["id"])
best = {}
for rel, fid, mt in entries:
    if rel not in best or mt > best[rel][1]:
        best[rel] = (fid, mt)

print(f"{len(best)} scripts a restaurer")
for rel in sorted(best):
    lp = os.path.join(DEST, rel)
    os.makedirs(os.path.dirname(lp), exist_ok=True)
    content = http_raw(f"{API}/files/{best[rel][0]}?alt=media", headers=H)
    with open(lp, "wb") as fh:
        fh.write(content)
    print(f"  OK {rel}")
