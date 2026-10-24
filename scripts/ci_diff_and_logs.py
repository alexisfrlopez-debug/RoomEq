#!/usr/bin/env python3
"""Diff du commit f935c9d (fix CI perdu) + essai archives de logs de run."""
import json
import sys
import urllib.request
import urllib.error

REPO_API = "https://api.github.com/repos/alexisfrlopez-debug/RoomEq"
token = open("/home/z/my-project/secrets/token_git.txt").read().strip()
HDRS = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/vnd.github+json",
    "User-Agent": "RoomEq-relieve",
}


def get(url, raw=False, accept=None):
    h = dict(HDRS)
    if accept:
        h["Accept"] = accept
    req = urllib.request.Request(url, headers=h)
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            data = r.read()
            return r.status, data if raw else json.loads(data.decode())
    except urllib.error.HTTPError as e:
        try:
            return e.code, e.read()
        except Exception:
            return e.code, b""
    except Exception as e:
        return 0, str(e).encode()


# 1. Commit f935c9d : message complet + fichiers modifiés
st, c = get(f"{REPO_API}/commits/f935c9d")
if st == 200:
    print("=== COMMIT f935c9d (dernier, session perdue) ===")
    print(c["commit"]["message"])
    print(f"\n--- {len(c['files'])} fichiers modifiés ---")
    for f in c["files"]:
        print(f"\n### {f['filename']}  (+{f['additions']}/-{f['deletions']})")
        if f.get("patch"):
            print(f["patch"][:3000])
else:
    print(f"commit HTTP {st}: {c[:300]}")

# 2. Jobs du run du commit précédent 0cf86e6 (main)
st, jobs = get(f"{REPO_API}/actions/runs/40075437447/jobs?per_page=30")
print("\n\n=== Jobs du run 40075437447 (0cf86e6, main) ===")
if st == 200:
    for j in jobs.get("jobs", []):
        bad = [s["name"] for s in j.get("steps", []) if s["conclusion"] == "failure"]
        print(f"  {j['name']}: {j['conclusion']}" + (f"  ✗ étapes: {bad}" if bad else ""))
else:
    print(f"HTTP {st}")

# 3. Essai : archive zip des logs du dernier run main
st, z = get(f"{REPO_API}/actions/runs/40076992707/logs", raw=True, accept="application/vnd.github+json")
print(f"\n=== Archive logs run 40076992707 : HTTP {st}, {len(z)} octets ===")
if st == 200:
    open("/home/z/my-project/tool-results/ci-logs-40076992707.zip", "wb").write(z)
    print("  sauvegardée → tool-results/ci-logs-40076992707.zip")
