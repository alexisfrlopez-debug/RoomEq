#!/usr/bin/env python3
"""Attend la fin du run CI du tag v2.0.0 et télécharge les artefacts
(driver+ASIO, MSI, pkgs macOS) vers tool-results/artifacts/."""
import json
import os
import sys
import time
import urllib.request
import urllib.error

REPO_API = "https://api.github.com/repos/alexisfrlopez-debug/RoomEq"
SHA = sys.argv[1] if len(sys.argv) > 1 else ""
OUT = "/home/z/my-project/tool-results/artifacts"
os.makedirs(OUT, exist_ok=True)

token = open("/home/z/my-project/secrets/token_git.txt").read().strip()
HDRS = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/vnd.github+json",
    "User-Agent": "RoomEq-relieve",
}


def get(url, raw=False):
    req = urllib.request.Request(url, headers=HDRS)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            data = r.read()
            return r.status, data if raw else json.loads(data.decode())
    except urllib.error.HTTPError as e:
        try:
            return e.code, e.read()
        except Exception:
            return e.code, b""
    except Exception as e:
        return 0, str(e).encode()


# 1. retrouver le run du tag (event=push, head_branch=v2.0.0, sha cible)
print(f"[attente] run du tag v2.0.0 (sha={SHA or 'le plus récent'})…")
run = None
for attempt in range(120):  # jusqu'à 60 min
    st, runs = get(f"{REPO_API}/actions/runs?per_page=10")
    for r in runs.get("workflow_runs", []):
        if r["event"] == "push" and r["head_branch"] == "v2.0.0":
            if not SHA or r["head_sha"].startswith(SHA):
                run = r
                break
    if run:
        break
    time.sleep(30)
if not run:
    print("run introuvable")
    sys.exit(1)

run_id = run["id"]
print(f"[run] #{run_id} sha={run['head_sha'][:7]}")

# 2. attendre la fin
status = run["status"]
while status in ("queued", "in_progress"):
    time.sleep(30)
    st, r2 = get(f"{REPO_API}/actions/runs/{run_id}")
    if st != 200:
        continue
    status = r2["status"]
    concl = r2.get("conclusion")
    print(f"  …{status} ({concl})")
    run = r2

print(f"[conclusion] {run.get('conclusion')}")
if run.get("conclusion") != "success":
    print("!!! Le run n'est pas un succès — voir les logs.")
    sys.exit(2)

# 3. lister + télécharger les artefacts du run
st, arts = get(f"{REPO_API}/actions/runs/{run_id}/artifacts")
print(f"[artefacts] HTTP {st}")
for a in arts.get("artifacts", []):
    name, aid, size = a["name"], a["id"], a["size_in_bytes"]
    print(f"  - {name} ({size/1e6:.1f} Mo)…", end=" ", flush=True)
    st2, z = get(f"{REPO_API}/actions/artifacts/{aid}/zip", raw=True)
    if st2 == 200:
        path = os.path.join(OUT, name + ".zip")
        with open(path, "wb") as f:
            f.write(z)
        print(f"→ {path}")
    else:
        print(f"HTTP {st2}")
print("[terminé]")
