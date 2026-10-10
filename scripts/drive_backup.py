#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
drive_backup.py — Sauvegarde incrémentielle de l'environnement RoomEq vers Google Drive.
=========================================================================================
Projet : RoomEq (.NET 8 / NAudio / Avalonia 11) — voir INSTRUCTIONS-REPRISE.md à la racine
du Drive pour le protocole complet de reprise.

Principe :
  - Chaque fichier local est haché (SHA-256) et comparé au manifeste de la dernière
    sauvegarde (data/_manifest_backup.json sur le Drive).
  - Seuls les fichiers NOUVEAUX ou MODIFIÉS sont envoyés (incrémentiel).
  - data/          → mode MIROIR (les suppressions locales sont propagées au Drive).
  - livrables/     → mode AJOUT (ajout/mise à jour, jamais de suppression).
  - Racine         → INSTRUCTIONS-REPRISE.md (mise à jour en place, l'ID Drive reste stable).
  - ARCHIVAGE AUTO → à chaque sauvegarde, les livrables RoomEq-<ancienne version>-*.zip
                     présents à la racine de livrables/ sont DÉPLACÉS dans
                     livrables/Archives/ (et download/Archives/ en local) dès que la
                     version courante du projet (Directory.Build.props) est plus récente.
                     Les fichiers conservent leur ID Drive (liens publics toujours valides).
  - --snapshot     → archive datée complète dans data/snapshots/ (5 instantanés max).
  - --restore DIR  → reconstruction de l'environnement local depuis le manifeste Drive
                     (téléchargement public, sans authentification).

Usage :
  python3 drive_backup.py                 # sauvegarde incrémentielle (+ archivage auto)
  python3 drive_backup.py --dry-run       # simulation (aucun envoi, aucun déplacement)
  python3 drive_backup.py --verify        # vérifie l'intégrité du Drive vs manifeste
  python3 drive_backup.py --snapshot      # + archive datée (5 max conservées)
  python3 drive_backup.py --archive-versions 1.5   # force la version « courante » pour l'archivage
  python3 drive_backup.py --restore /chemin --manifest-id <FILE_ID>

Dépendances : Python 3 standard uniquement (urllib).
"""

import argparse
import datetime as dt
import hashlib
import io
import json
import os
import re
import shutil
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

# ─────────────────────────── Configuration ───────────────────────────
ROOT_FOLDER_ID   = "__ID_RACINE_DRIVE__"      # racine de la sauvegarde Drive
MANIFEST_NAME    = "_manifest_backup.json"                   # stocké dans data/
SNAPSHOT_DIR     = "data/snapshots"
SNAPSHOT_KEEP    = 5

# Fichiers d'identifiants déjà présents dans data/ (ne JAMAIS les ré-uploader).
SECRET_NAMES = {
    "client_secret.json", "oauth_token_relais.json", "service_account.json",
    "worklog-drive.md", "access_token.json",
}

BASE = "/home/z/my-project"
SECRETS_DIR = os.path.join(BASE, "secrets")
TOKEN_FILE  = os.path.join(SECRETS_DIR, "oauth_token_relais.json")

# ID Drive connu du fichier oauth_token_relais.json (data/) — utilisé
# par le bootstrap quand les identifiants locaux sont absents.
OAUTH_FILE_ID = "__ID_FICHIER_TOKEN_OAUTH__"

# Mappage local → Drive.  prune=True ⇒ mode miroir (propage les suppressions).
MAPPING = [
    (os.path.join(BASE, "workspace/roomq-source/RoomEq"), "data/RoomEq",            True),
    (os.path.join(BASE, "worklog.md"),                    "data/worklog.md",        True),
    (os.path.join(BASE, "scripts"),                       "data/scripts",           True),
    (os.path.join(BASE, "download"),                      "livrables",              False),
    (os.path.join(BASE, "download/INSTRUCTIONS-REPRISE.md"), "INSTRUCTIONS-REPRISE.md", False),
]

EXCLUDE_DIRS  = {"bin", "obj", "publish", ".git", ".vs", ".idea", "node_modules",
                 "test-results", "__pycache__", ".avalonia-build-tasks"}
# INSTRUCTIONS-REPRISE.md : exclu de la promenade d'arborescence (sinon doublon dans
# livrables/) mais ajouté à la racine via un mappage direct dans MAPPING.
EXCLUDE_FILES = SECRET_NAMES | {".drive_manifest.json", ".env", "INSTRUCTIONS-REPRISE.md"}

API = "https://www.googleapis.com/drive/v3"
UPLOAD = "https://www.googleapis.com/upload/drive/v3"
FOLDER_MIME = "application/vnd.google-apps.folder"


# ─────────────────────────── Utilitaires HTTP ───────────────────────────
def http(url, data=None, headers=None, method=None, token=None, raw=False, retries=3):
    """Requête HTTP robuste avec réessais (5xx/429)."""
    hdrs = dict(headers or {})
    if token:
        hdrs["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, data=data, headers=hdrs, method=method)
    last = None
    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                body = r.read()
                return body if raw else (json.loads(body) if body else {})
        except urllib.error.HTTPError as e:
            err = e.read().decode(errors="replace")
            if e.code in (429, 500, 502, 503, 504) and attempt < retries - 1:
                time.sleep(2 * (attempt + 1)); last = e; continue
            raise RuntimeError(f"HTTP {e.code} sur {url}\n{err[:500]}") from e
        except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
            if attempt < retries - 1:
                time.sleep(2 * (attempt + 1)); last = e; continue
            raise RuntimeError(f"Réseau indisponible ({url}) : {e}") from e
    raise RuntimeError(f"Échec après {retries} tentatives : {url}") from last


def api_get(url, token):
    return http(url, token=token)


# ─────────────────────────── Authentification ───────────────────────────
def get_access_token() -> str:
    """Rafraîchit l'access token OAuth (cache local 50 min)."""
    cache = os.path.join(SECRETS_DIR, "access_token.json")
    if os.path.exists(cache):
        c = json.load(open(cache))
        if c.get("_fetched_at", 0) > time.time() - 3000:
            return c["access_token"]
    if not os.path.exists(TOKEN_FILE):
        # Bootstrap : récupère le token depuis le Drive (lien public).
        os.makedirs(SECRETS_DIR, exist_ok=True)
        print("→ Identifiants absents : téléchargement depuis le Drive…")
        body = http(f"https://drive.usercontent.google.com/download?id={OAUTH_FILE_ID}"
                    f"&export=download", raw=True)
        open(TOKEN_FILE, "wb").write(body)
    tok = json.load(open(TOKEN_FILE))
    data = urllib.parse.urlencode({
        "client_id": tok["client_id"], "client_secret": tok["client_secret"],
        "refresh_token": tok["refresh_token"], "grant_type": "refresh_token",
    }).encode()
    resp = http(tok["token_uri"], data=data)
    if "access_token" not in resp:
        raise RuntimeError(
            "Refresh token refusé (invalid_grant ?). Le token OAuth expire tous les 7 jours —\n"
            "il faut une nouvelle autorisation interactive : demander à l'utilisateur un\n"
            "nouveau fichier oauth_token_relais_*.json (voir INSTRUCTIONS-REPRISE.md §5).")
    resp["_fetched_at"] = time.time()
    json.dump(resp, open(cache, "w"))
    return resp["access_token"]


# ─────────────────────────── Opérations Drive ───────────────────────────
def find_child(token, parent_id, name):
    """Retourne {id, mimeType} de l'enfant nommé `name` (non corbeille), sinon None."""
    q = urllib.parse.quote(f"'{parent_id}' in parents and name = '{name}' and trashed = false")
    r = api_get(f"{API}/files?q={q}&fields=files(id,name,mimeType)&pageSize=5", token)
    files = r.get("files", [])
    return files[0] if files else None


def get_or_create_folder(token, name, parent_id, cache):
    key = f"{parent_id}/{name}"
    if key in cache:
        return cache[key]
    child = find_child(token, parent_id, name)
    if child and child.get("mimeType") == FOLDER_MIME:
        fid = child["id"]
    else:
        meta = json.dumps({"name": name, "mimeType": FOLDER_MIME, "parents": [parent_id]})
        r = http(f"{API}/files", data=meta.encode(),
                 headers={"Content-Type": "application/json"}, token=token)
        fid = r["id"]
    cache[key] = fid
    return fid


def resolve_path(token, drive_path, cache):
    """Crée/résout l'arborescence 'data/RoomEq/src' → folder/file id."""
    parts = drive_path.strip("/").split("/")
    cur = ROOT_FOLDER_ID
    for p in parts[:-1]:
        cur = get_or_create_folder(token, p, cur, cache)
    return cur, parts[-1]


def upload_new(token, drive_path, parent_id, name, content):
    boundary = "==" + hashlib.md5(str(time.time()).encode()).hexdigest() + "=="
    meta = json.dumps({"name": name, "parents": [parent_id]})
    body = (f"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n{meta}\r\n"
            f"--{boundary}\r\nContent-Type: application/octet-stream\r\n\r\n").encode() \
           + content + f"\r\n--{boundary}--".encode()
    url = f"{UPLOAD}/files?uploadType=multipart&fields=id"
    r = http(url, data=body, headers={"Content-Type": f"multipart/related; boundary={boundary}"},
             token=token)
    return r["id"]


def upload_update(token, file_id, content):
    url = f"{UPLOAD}/files/{file_id}?uploadType=media"
    r = http(url, data=content, headers={"Content-Type": "application/octet-stream"},
             token=token, method="PATCH")
    return r["id"]


def trash_file(token, file_id):
    http(f"{API}/files/{file_id}", data=b'{"trashed": true}',
         headers={"Content-Type": "application/json"}, token=token, method="PATCH")


def move_file(token, file_id, add_parent, remove_parent):
    """Déplace un fichier Drive d'un dossier à l'autre (l'ID et les liens publics
    restent inchangés)."""
    url = (f"{API}/files/{file_id}?addParents={add_parent}&removeParents={remove_parent}"
           f"&fields=id,parents")
    http(url, data=b"{}", headers={"Content-Type": "application/json"},
         token=token, method="PATCH")


def list_children(token, parent_id):
    q = urllib.parse.quote(f"'{parent_id}' in parents and trashed = false")
    r = api_get(f"{API}/files?q={q}&fields=files(id,name,mimeType)&pageSize=200", token)
    return r.get("files", [])


def download_public(file_id) -> bytes:
    return http(f"https://drive.usercontent.google.com/download?id={file_id}"
                f"&export=download", raw=True)


# ─────────────────────────── Inventaire local ───────────────────────────
def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


VERSION_RE = re.compile(r"^RoomEq-(\d+(?:\.\d+){0,3})-(.+)\.(zip|tar\.gz)$", re.IGNORECASE)
VERSION_PROPS = os.path.join(BASE, "workspace/roomq-source/RoomEq/Directory.Build.props")
DOWNLOAD_DIR = os.path.join(BASE, "download")
ARCHIVES_SUBDIR = "Archives"


def parse_version(s):
    """'1.4' ou '1.4.0' → tuple (1, 4, 0) comparable."""
    parts = [int(p) for p in s.split(".")]
    while len(parts) < 3:
        parts.append(0)
    return tuple(parts[:3])


def read_current_version():
    """Version courante du projet, lue dans Directory.Build.props (None si introuvable)."""
    try:
        text = open(VERSION_PROPS, encoding="utf-8").read()
        m = re.search(r"<Version>([0-9.]+)</Version>", text)
        return m.group(1) if m else None
    except OSError:
        return None


def archive_old_versions(token, manifest, dry_run, folder_cache, version_override=None):
    """Archive les livrables des versions précédentes dans livrables/Archives :
     - déplacement local download/<ancien>.zip → download/Archives/ ;
     - déplacement Drive livrables/<ancien>.zip → livrables/Archives/ (ID conservé) ;
     - mise à jour des chemins du manifeste (aucun re-téléversement nécessaire).
    Détecte automatiquement la version courante dans Directory.Build.props —
    donc l'archivage se déclenche tout seul à chaque nouvelle version livrée.
    Ne touche ni README.md ni INSTRUCTIONS-REPRISE.md (non versionnés)."""
    current = version_override or read_current_version()
    if not current:
        print("→ Archivage des livrables : version courante inconnue (Directory.Build.props absent) — ignoré.")
        return 0
    try:
        cur = parse_version(current)
    except ValueError:
        print(f"→ Archivage des livrables : version '{current}' illisible — ignoré.")
        return 0

    def is_old(name):
        m = VERSION_RE.match(name)
        if not m:
            return False
        try:
            return parse_version(m.group(1)) < cur
        except ValueError:
            return False

    # --- côté local ---
    archives_local = os.path.join(DOWNLOAD_DIR, ARCHIVES_SUBDIR)
    old_local = sorted(f for f in os.listdir(DOWNLOAD_DIR)
                       if os.path.isfile(os.path.join(DOWNLOAD_DIR, f)) and is_old(f)) \
        if os.path.isdir(DOWNLOAD_DIR) else []

    # --- côté Drive ---
    livrables_id = get_or_create_folder(token, "livrables", ROOT_FOLDER_ID, folder_cache)
    archives_id = get_or_create_folder(token, ARCHIVES_SUBDIR, livrables_id, folder_cache)
    drive_children = list_children(token, livrables_id)
    drive_old = [c for c in drive_children if is_old(c["name"])]
    archives_names = {c["name"] for c in list_children(token, archives_id)}

    to_move = sorted({*old_local, *[c["name"] for c in drive_old]})
    if not to_move:
        print(f"→ Archivage des livrables : rien à archiver (version courante {current}).")
        return 0

    print(f"→ Archivage des livrables : {len(to_move)} fichier(s) de versions "
          f"précédentes → {ARCHIVES_SUBDIR}/")
    moved = 0
    for name in to_move:
        if name in archives_names:
            print(f"  [ARCH] {name} — déjà présent dans {ARCHIVES_SUBDIR}/, ignoré")
            continue
        if not dry_run:
            # local
            src = os.path.join(DOWNLOAD_DIR, name)
            if os.path.isfile(src):
                os.makedirs(archives_local, exist_ok=True)
                shutil.move(src, os.path.join(archives_local, name))
            # Drive (ID conservé)
            child = next((c for c in drive_old if c["name"] == name), None)
            if child:
                move_file(token, child["id"], archives_id, livrables_id)
            # manifeste : chemin racine livrables/ → livrables/Archives/ (aucun re-envoi)
            old_key = f"livrables/{name}"
            new_key = f"livrables/{ARCHIVES_SUBDIR}/{name}"
            if old_key in manifest:
                manifest[new_key] = manifest.pop(old_key)
        print(f"  [ARCH] {name}")
        moved += 1
    if not dry_run:
        print(f"✓ {moved} livrable(s) archivé(s) dans livrables/{ARCHIVES_SUBDIR}/ (IDs conservés).")
    return moved


def is_excluded(dirpath, fn, is_dir=False):
    if is_dir and fn in EXCLUDE_DIRS:
        return True
    if not is_dir and fn in EXCLUDE_FILES:
        return True
    if not is_dir and fn.endswith(".bin"):
        return True
    return False


def collect_local(root_local, root_drive):
    """Liste {chemin_drive: (chemin_local, contenu)} — contenu lu paresseusement ? Non : lu direct."""
    out = {}
    for dirpath, dirnames, filenames in os.walk(root_local):
        dirnames[:] = [d for d in dirnames if not is_excluded(dirpath, d, is_dir=True)]
        for fn in sorted(filenames):
            if is_excluded(dirpath, fn):
                continue
            lp = os.path.join(dirpath, fn)
            rel = os.path.relpath(lp, root_local).replace(os.sep, "/")
            out[f"{root_drive}/{rel}"] = lp
    return out


def build_local_index():
    """Index complet local → {drive_path: local_path}."""
    index = {}
    for local, drive, _prune in MAPPING:
        if not os.path.exists(local):
            continue
        if os.path.isdir(local):
            index.update(collect_local(local, drive))
        else:
            index[drive] = local
    return index


# ─────────────────────────── Manifeste ───────────────────────────
def fetch_remote_manifest(token):
    """Télécharge le manifeste distant (source de vérité des IDs)."""
    data_dir = get_or_create_folder(token, "data", ROOT_FOLDER_ID, {})
    child = find_child(token, data_dir, MANIFEST_NAME)
    if not child:
        return {}, data_dir
    body = http(f"{API}/files/{child['id']}?alt=media", token=token, raw=True)
    try:
        return json.loads(body), data_dir
    except json.JSONDecodeError:
        print("⚠ Manifeste distant illisible — nouvelle sauvegarde complète.")
        return {}, data_dir


# ─────────────────────────── Sauvegarde ───────────────────────────
def run_backup(dry_run=False, snapshot=False, archive_version=None):
    token = get_access_token()
    print("✓ Access token OK")
    remote, data_dir = fetch_remote_manifest(token)
    manifest = dict(remote.get("files", {})) if remote else {}
    folder_cache = {}
    created = updated = unchanged = deleted = 0

    # Archivage automatique des versions précédentes (localement + Drive + manifeste)
    # AVANT la promenade : les fichiers déplacés restent inchangés à leur nouveau chemin.
    try:
        archive_old_versions(token, manifest, dry_run, folder_cache, archive_version)
    except Exception as e:
        print(f"⚠ Archivage des livrables impossible (la sauvegarde continue) : {e}")

    index = build_local_index()
    print(f"→ {len(index)} fichiers locaux à considérer.")

    for drive_path, local_path in sorted(index.items()):
        content = open(local_path, "rb").read()
        h = sha256(content)
        entry = manifest.get(drive_path)
        if entry and entry.get("sha256") == h and entry.get("size") == len(content):
            unchanged += 1
            continue
        if dry_run:
            if entry:
                updated += 1; print(f"  [MAJ ] {drive_path}")
            else:
                created += 1; print(f"  [NOUV] {drive_path}")
            continue
        if entry and entry.get("id"):
            upload_update(token, entry["id"], content)
            entry.update(sha256=h, size=len(content))
        else:
            parent_id, name = resolve_path(token, drive_path, folder_cache)
            fid = upload_new(token, drive_path, parent_id, name, content)
            manifest[drive_path] = {"id": fid, "sha256": h, "size": len(content)}
        if entry: updated += 1; print(f"  [MAJ ] {drive_path}")
        else: created += 1; print(f"  [NOUV] {drive_path}")

    # Suppressions — uniquement pour les sous-arbres en mode miroir.
    prune_prefixes = {d for _l, d, p in MAPPING if p}
    for drive_path in list(manifest.keys()):
        if any(drive_path == p or drive_path.startswith(p + "/") for p in prune_prefixes):
            if drive_path not in index:
                if not dry_run:
                    try:
                        trash_file(token, manifest[drive_path]["id"])
                    except RuntimeError as e:
                        print(f"  ⚠ corbeille impossible pour {drive_path} : {e}")
                        continue
                print(f"  [SUPR] {drive_path} (absent en local)")
                del manifest[drive_path]
                deleted += 1

    # Nettoyage des instantanés anciens (data/snapshots, 5 max).
    if snapshot and not dry_run:
        snap_dir = get_or_create_folder(token, "snapshots", data_dir, folder_cache)
        make_snapshot(token, snap_dir)
        prune_snapshots(token, snap_dir)

    if dry_run:
        print(f"DRY-RUN terminé : {created} nouveaux, {updated} maj, {deleted} suppr., "
              f"{unchanged} inchangés.")
        return

    # Écrit du manifeste (local + Drive).
    payload = {"generated_at": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
               "root_folder_id": ROOT_FOLDER_ID, "files": manifest}
    body = json.dumps(payload, indent=1, ensure_ascii=False).encode()
    json.dump(payload, open(os.path.join(BASE, ".drive_manifest.json"), "w"), indent=1,
              ensure_ascii=False)
    child = find_child(token, data_dir, MANIFEST_NAME)
    if child:
        upload_update(token, child["id"], body)
    else:
        fid = upload_new(token, f"data/{MANIFEST_NAME}", data_dir, MANIFEST_NAME, body)
        manifest[MANIFEST_NAME] = {"id": fid}
    print(f"✓ Sauvegarde terminée : {created} nouveaux, {updated} maj, {deleted} suppr., "
          f"{unchanged} inchangés.")
    print(f"✓ Manifeste : data/{MANIFEST_NAME} ({len(manifest)} entrées)")


def make_snapshot(token, snap_dir):
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    zip_name = f"RoomEq-env-snapshot-{stamp}.zip"
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
        for drive_path, local_path in sorted(build_local_index().items()):
            z.write(local_path, drive_path)
    content = buf.getvalue()
    fid = upload_new(token, f"{SNAPSHOT_DIR}/{zip_name}", snap_dir, zip_name, content)
    print(f"✓ Instantané : {zip_name} ({len(content)/1e6:.1f} Mo)")


def prune_snapshots(token, snap_dir):
    q = urllib.parse.quote(f"'{snap_dir}' in parents and trashed = false")
    r = api_get(f"{API}/files?q={q}&fields=files(id,name)&pageSize=100", token)
    snaps = sorted(r.get("files", []), key=lambda f: f["name"])
    for old in snaps[:-SNAPSHOT_KEEP]:
        trash_file(token, old["id"])
        print(f"  [SUPR] ancien instantané {old['name']}")


# ─────────────────────────── Vérification ───────────────────────────
def run_verify():
    token = get_access_token()
    remote, _ = fetch_remote_manifest(token)
    manifest = remote.get("files", {})
    ok = bad = 0
    for path, e in sorted(manifest.items()):
        try:
            body = http(f"{API}/files/{e['id']}?alt=media", token=token, raw=True)
            if sha256(body) == e.get("sha256"):
                ok += 1
            else:
                print(f"  ✗ CORROMPU : {path}")
                bad += 1
        except RuntimeError as err:
            print(f"  ✗ ILLISIBLE : {path} — {str(err)[:80]}")
            bad += 1
    print(f"✓ Vérification : {ok} OK, {bad} problème(s) sur {len(manifest)} entrées.")


# ─────────────────────────── Restauration ───────────────────────────
def run_restore(target, manifest_id):
    """Reconstruit l'environnement depuis le manifeste (téléchargements publics)."""
    print(f"→ Téléchargement du manifeste {manifest_id}…")
    man = json.loads(download_public(manifest_id))
    files = man.get("files", {})
    print(f"→ {len(files)} fichiers à restaurer dans {target}")
    n = 0
    for path, e in sorted(files.items()):
        dest = os.path.join(target, path)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        open(dest, "wb").write(download_public(e["id"]))
        n += 1
        if n % 25 == 0:
            print(f"   {n}/{len(files)}…")
    print(f"✓ {n} fichiers restaurés.")
    print("⚠ Les identifiants OAuth ne sont PAS dans le manifeste (exclusion volontaire) :")
    print(f"   curl -L 'https://drive.usercontent.google.com/download?id={OAUTH_FILE_ID}"
          f"&export=download' -o {SECRETS_DIR}/oauth_token_relais.json")


# ─────────────────────────── Point d'entrée ───────────────────────────
def main():
    ap = argparse.ArgumentParser(description="Sauvegarde incrémentielle RoomEq → Google Drive")
    ap.add_argument("--dry-run", action="store_true", help="simulation sans envoi")
    ap.add_argument("--verify", action="store_true", help="vérifie l'intégrité Drive")
    ap.add_argument("--snapshot", action="store_true", help="crée aussi un instantané daté (5 max)")
    ap.add_argument("--restore", metavar="DIR", help="restaure l'environnement depuis le Drive")
    ap.add_argument("--manifest-id", metavar="ID", help="ID Drive du manifeste (pour --restore)")
    ap.add_argument("--archive-versions", metavar="VER", default=None,
                    help="force la version « courante » pour l'archivage des livrables "
                         "(défaut : lue dans Directory.Build.props)")
    args = ap.parse_args()

    if args.restore:
        if not args.manifest_id:
            sys.exit("ERREUR : --restore nécessite --manifest-id (voir INSTRUCTIONS-REPRISE.md §3)")
        return run_restore(args.restore, args.manifest_id)
    if args.verify:
        return run_verify()
    return run_backup(dry_run=args.dry_run, snapshot=args.snapshot,
                      archive_version=args.archive_versions)


if __name__ == "__main__":
    main()
