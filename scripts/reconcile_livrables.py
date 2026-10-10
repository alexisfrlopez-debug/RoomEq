#!/usr/bin/env python3
"""Réconciliation ponctuelle du Drive livrables ↔ manifeste :
1. Met à la corbeille le README.md orphelin (contenu template de sandbox).
2. Ajoute au manifeste les fichiers de livrables/Archives/ qui en sont absents
   (les zips v1.0 retrouvés, hors manifeste depuis une session précédente),
   en conservant leurs IDs Drive existants.
3. Re-télécharge localement les archives manquantes (miroir complet download/Archives/).
"""
import hashlib
import json
import sys

sys.path.insert(0, "/home/z/my-project/scripts")
import drive_backup as db

token = db.get_access_token()
cache = {}
folder_cache = {}
liv = db.get_or_create_folder(token, "livrables", db.ROOT_FOLDER_ID, folder_cache)
arc = db.get_or_create_folder(token, "Archives", liv, folder_cache)
remote, data_dir = db.fetch_remote_manifest(token)
manifest = remote.get("files", {})
manifest_child = db.find_child(token, data_dir, db.MANIFEST_NAME)
changed = False

# 1. README orphelin
man_readme_id = manifest["livrables/README.md"]["id"]
for c in db.list_children(token, liv):
    if c["name"] == "README.md" and c["id"] != man_readme_id:
        db.trash_file(token, c["id"])
        print(f"[SUPR] doublon livrables/README.md (id {c['id']}) mis à la corbeille")
        changed = True

# 2. Entrées de manifeste manquantes pour livrables/Archives/*
for c in db.list_children(token, arc):
    key = f"livrables/Archives/{c['name']}"
    if key in manifest:
        continue
    body = db.http(f"{db.API}/files/{c['id']}?alt=media", token=token, raw=True)
    manifest[key] = {"id": c["id"], "sha256": db.sha256(body), "size": len(body)}
    print(f"[MAN ] entrée ajoutée : {key} (id {c['id']}, {len(body)} octets)")
    changed = True

    # 3. miroir local
    import os
    dest = f"/home/z/my-project/download/Archives/{c['name']}"
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    open(dest, "wb").write(body)
    print(f"       miroir local : {dest}")

if changed:
    payload = {"generated_at": __import__("datetime").datetime.now(__import__("datetime").timezone.utc).isoformat(timespec="seconds"),
               "root_folder_id": db.ROOT_FOLDER_ID, "files": manifest}
    body = json.dumps(payload, indent=1, ensure_ascii=False).encode()
    db.upload_update(token, manifest_child["id"], body)
    json.dump(payload, open("/home/z/my-project/.drive_manifest.json", "w"), indent=1, ensure_ascii=False)
    print(f"✓ Manifeste réécrit ({len(manifest)} entrées) — local + Drive.")
else:
    print("Rien à réconcilier.")
