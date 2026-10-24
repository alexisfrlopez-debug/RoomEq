#!/usr/bin/env python3
"""Détails des échecs CI — jobs + extraits de logs des étapes en échec."""
import json
import re
import sys
import urllib.request
import urllib.error

REPO_API = "https://api.github.com/repos/alexisfrlopez-debug/RoomEq"
RUN_ID = sys.argv[1] if len(sys.argv) > 1 else "40076992707"

token = open("/home/z/my-project/secrets/token_git.txt").read().strip()
HDRS = {
    "Authorization": f"Bearer {token}",
    "Accept": "application/vnd.github+json",
    "User-Agent": "RoomEq-relieve",
}


def get(url, raw=False):
    req = urllib.request.Request(url, headers=HDRS)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            data = r.read()
            return r.status, data if raw else json.loads(data.decode())
    except urllib.error.HTTPError as e:
        try:
            return e.code, json.loads(e.read().decode())
        except Exception:
            return e.code, {}
    except Exception as e:
        return 0, {"error": str(e)}


st, jobs = get(f"{REPO_API}/actions/runs/{RUN_ID}/jobs?per_page=30")
print(f"[jobs du run {RUN_ID}] HTTP {st}")
if st != 200:
    print(json.dumps(jobs)[:500])
    sys.exit(1)

for j in jobs.get("jobs", []):
    print(f"\n=== JOB: {j['name']}  concl={j['conclusion']}  ({j['runner_name']})")
    for s in j.get("steps", []):
        mark = "✗" if s["conclusion"] == "failure" else ("·" if s["conclusion"] in (None, "skipped") else "✓")
        print(f"  {mark} {s['name']} [{s['conclusion']}]")
    if j["conclusion"] == "failure":
        stl, log = get(f"{REPO_API}/actions/jobs/{j['id']}/logs", raw=True)
        if stl == 200:
            txt = log.decode(errors="replace")
            lines = txt.splitlines()
            # contexte autour des erreurs
            idxs = [i for i, l in enumerate(lines)
                    if re.search(r"##\[error\]|error [A-Z]?[0-9]+|: error |Error:|FAILED|Erreur",
                                 l, re.I)]
            shown = set()
            print(f"  --- log ({len(lines)} lignes), {len(idxs)} motifs d'erreur ---")
            for i in idxs[:14]:
                for k in range(max(0, i - 3), min(len(lines), i + 3)):
                    if k not in shown:
                        shown.add(k)
                        print(f"    {lines[k][:240]}")
        else:
            print(f"  (logs HTTP {stl})")
