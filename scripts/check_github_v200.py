#!/usr/bin/env python3
"""Relève GitHub — état distant de RoomEq v2.0.0 (commits, tag, CI, artefacts)."""
import json
import sys
import urllib.request

BASE = "/home/z/my-project"
REPO_API = "https://api.github.com/repos/alexisfrlopez-debug/RoomEq"

token = open(f"{BASE}/secrets/token_git.txt").read().strip()
HDRS = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/vnd.github+json",
    "User-Agent": "RoomEq-relieve",
}


def get(url):
    req = urllib.request.Request(url, headers=HDRS)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return r.status, json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode())
        except Exception:
            return e.code, {}
    except Exception as e:
        return 0, {"error": str(e)}


# 1. Dépôt
st, repo = get(REPO_API)
print(f"[repo] HTTP {st}")
if st != 200:
    print(json.dumps(repo)[:400])
    sys.exit(1)
print(f"  nom={repo.get('full_name')} branche_défaut={repo.get('default_branch')} "
      f"privé={repo.get('private')} poussé_le={repo.get('pushed_at')}")

# 2. Derniers commits
st, commits = get(f"{REPO_API}/commits?per_page=5")
print(f"[commits] HTTP {st}")
for c in commits if isinstance(commits, list) else []:
    print(f"  {c['sha'][:7]}  {c['commit']['committer']['date']}  "
          f"{c['commit']['message'].splitlines()[0][:80]}")

# 3. Tags
st, tags = get(f"{REPO_API}/tags?per_page=10")
print(f"[tags] HTTP {st}")
for t in tags if isinstance(tags, list) else []:
    print(f"  {t['name']} → {t['commit']['sha'][:7]}")

# 4. Runs CI
st, runs = get(f"{REPO_API}/actions/runs?per_page=8")
print(f"[actions runs] HTTP {st}")
for r in runs.get("workflow_runs", []):
    print(f"  #{r['id']} {r['name']!r} event={r['event']} branche={r['head_branch']} "
          f"statut={r['status']} concl={r['conclusion']} sha={r['head_sha'][:7]} "
          f"{r['created_at']} html={r['html_url']}")

# 5. Artefacts de chaque run terminé
for r in runs.get("workflow_runs", []):
    if r["status"] == "completed":
        st, arts = get(f"{REPO_API}/actions/runs/{r['id']}/artifacts")
        if st == 200 and arts.get("artifacts"):
            print(f"[artefacts run #{r['id']} concl={r['conclusion']}]")
            for a in arts["artifacts"]:
                print(f"  - {a['name']} ({a['size_in_bytes']/1e6:.1f} Mo, "
                      f"expir {a['expires_at']})")

# 6. Releases
st, rels = get(f"{REPO_API}/releases?per_page=5")
print(f"[releases] HTTP {st}")
for rel in rels if isinstance(rels, list) else []:
    print(f"  {rel.get('tag_name')} actif={rel.get('draft')} "
          f"actifs={len(rel.get('assets', []))}")
