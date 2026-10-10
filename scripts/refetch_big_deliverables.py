#!/usr/bin/env python3
"""Re-télécharge les gros livrables (page 'virus scan' avec alt=media simple) en
ajoutant confirm=t, puis vérifie les SHA-256 contre le manifeste."""
import hashlib
import json
import sys
import urllib.request

MANIFEST = "/tmp/manifest.json"
DOWNLOAD = "/home/z/my-project/download"


def download(id_: str) -> bytes:
    url = f"https://drive.usercontent.google.com/download?id={id_}&export=download&confirm=t"
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=600) as r:
        return r.read()


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main():
    man = json.load(open(MANIFEST))
    files = man["files"]
    targets = [p for p in files if p.startswith("livrables/")]
    for path in targets:
        entry = files[path]
        local = DOWNLOAD + "/" + path.split("/", 1)[1]
        ok = False
        if path.endswith(".zip"):
            try:
                with open(local, "rb") as f:
                    head = f.read(4)
                ok = head[:2] == b"PK"
            except FileNotFoundError:
                pass
        if ok:
            print(f"[OK]  {path} (déjà valide)")
            continue
        print(f"→ re-téléchargement {path} ({entry['size']} octets)…")
        data = download(entry["id"])
        digest = sha256(data)
        if digest != entry["sha256"]:
            print(f"  ⚠ SHA-256 divergent : {digest} ≠ {entry['sha256']}")
            sys.exit(1)
        with open(local, "wb") as f:
            f.write(data)
        print(f"  ✓ {len(data)} octets, SHA-256 conforme")


if __name__ == "__main__":
    main()
