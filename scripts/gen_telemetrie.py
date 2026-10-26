#!/usr/bin/env python3
"""Reconstitue scripts/telemetrie.json (version test) pour le packaging v3.

Lit les jetons OAuth du dépôt TRIADE (triade/.gdrive/), échange le refresh
token contre un access token, retrouve l'ID du dossier « journaux-app » du
Drive « Equalizer » (racine 1vRB4hEr9thaRnJsPjz8JCezGuzzD_yJR) et écrit la
configuration attendue par TelemetryConfig (snake_case).

Le fichier produit est IGNORÉ par git (.gitignore) : jamais commité, il
n'embarque que ce que la version test de la v2.2.2 embarquait déjà.
"""
import json
import sys
import urllib.request
import urllib.parse

GDRIVE = "/home/z/my-project/triade/.gdrive"
DRIVE_ROOT = "1vRB4hEr9thaRnJsPjz8JCezGuzzD_yJR"  # dossier « Equalizer »
OUT = "/home/z/my-project/roomeq/scripts/telemetrie.json"


def http_json(url, data=None, headers=None, method=None):
    req = urllib.request.Request(url, data=data, headers=headers or {}, method=method)
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read().decode("utf-8"))


def main():
    with open(f"{GDRIVE}/oauth_token_relais.json") as f:
        tok = json.load(f)
    with open(f"{GDRIVE}/client_secret.json") as f:
        cs_file = json.load(f)
    cs = cs_file.get("installed") or cs_file.get("web")
    if not cs:
        sys.exit("client_secret.json : clé 'installed'/'web' introuvable")

    # échange refresh -> access
    form = urllib.parse.urlencode({
        "client_id": cs["client_id"],
        "client_secret": cs["client_secret"],
        "refresh_token": tok["refresh_token"],
        "grant_type": "refresh_token",
    }).encode()
    tok_resp = http_json("https://oauth2.googleapis.com/token", data=form)
    access = tok_resp["access_token"]
    print("✓ access token obtenu")

    # liste le racine pour trouver « journaux-app »
    q = urllib.parse.quote(f"'{DRIVE_ROOT}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false")
    listing = http_json(
        f"https://www.googleapis.com/drive/v3/files?q={q}&fields=files(id,name)",
        headers={"Authorization": f"Bearer {access}"})
    folders = {f["name"]: f["id"] for f in listing.get("files", [])}
    print("dossiers du Drive « Equalizer » :", ", ".join(sorted(folders)) or "(aucun)")

    logs_id = folders.get("journaux-app")
    if not logs_id:
        sys.exit("dossier « journaux-app » introuvable — créer le sous-dossier puis relancer")

    cfg = {
        "client_id": cs["client_id"],
        "client_secret": cs["client_secret"],
        "refresh_token": tok["refresh_token"],
        "token_uri": "https://oauth2.googleapis.com/token",
        "folder_id": logs_id,
        "enabled": True,
    }
    with open(OUT, "w") as f:
        json.dump(cfg, f, indent=2)
        f.write("\n")
    print(f"✓ {OUT} écrit (folder_id journaux-app = {logs_id})")


if __name__ == "__main__":
    main()
