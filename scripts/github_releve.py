#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
github_releve.py — Relève des activités GitHub du dépôt RoomEq.
================================================================
Routine de début/fin de session (INSTRUCTIONS-REPRISE.md §7bis) :
liste les issues, pull requests et commentaires nouveaux depuis la dernière
relève, prêts à être traités selon le protocole §7bis.4.

Usage :
  python3 scripts/github_releve.py                     # depuis la dernière relève
  python3 scripts/github_releve.py --since 2026-10-10T12:00:00Z
  python3 scripts/github_releve.py --all               # tout l'historique ouvert
  python3 scripts/github_releve.py --no-save           # ne met pas à jour l'état

Authentification : token GitHub lu dans /home/z/my-project/secrets/token_git.txt
(ou variable d'environnement GITHUB_TOKEN). L'état « dernière relève » est
persisté dans scripts/github_releve_state.json (fichier local, non publié).

Dépendances : Python 3 standard uniquement (urllib).
"""

import argparse
import datetime as dt
import json
import os
import sys
import urllib.request
from pathlib import Path

REPO = "alexisfrlopez-debug/RoomEq"
API = f"https://api.github.com/repos/{REPO}"
STATE_FILE = Path(__file__).resolve().with_name("github_releve_state.json")
TOKEN_CANDIDATES = [
    Path("/home/z/my-project/secrets/token_git.txt"),
    Path(__file__).resolve().with_name("token_git.txt"),
]


def read_token() -> str:
    tok = os.environ.get("GITHUB_TOKEN", "").strip()
    if tok:
        return tok
    for p in TOKEN_CANDIDATES:
        if p.is_file():
            txt = p.read_text().strip()
            if txt:
                return txt
    sys.exit("✗ Token GitHub introuvable : secrets/token_git.txt ou GITHUB_TOKEN.")


def api_get(path: str, token: str):
    req = urllib.request.Request(
        f"https://api.github.com{path}",
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "User-Agent": "roomeq-releve-script",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            remaining = r.headers.get("X-RateLimit-Remaining", "?")
            data = json.loads(r.read().decode("utf-8"))
            return data, remaining
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")[:200]
        if e.code in (401, 403):
            sys.exit(f"✗ HTTP {e.code} (token invalide ou quota) : {body}")
        sys.exit(f"✗ HTTP {e.code} sur {path} : {body}")


def fmt_when(iso_str: str) -> str:
    return (iso_str or "").replace("T", " ").replace("Z", " UTC")


def main() -> None:
    ap = argparse.ArgumentParser(description="Relève GitHub RoomEq")
    ap.add_argument("--since", help="ISO 8601 (ex. 2026-10-10T12:00:00Z)")
    ap.add_argument("--all", action="store_true", help="ignorer la dernière relève")
    ap.add_argument("--no-save", action="store_true", help="ne pas maj l'état")
    args = ap.parse_args()

    token = read_token()
    last = None
    if STATE_FILE.is_file() and not args.all:
        try:
            last = json.loads(STATE_FILE.read_text()).get("last_check")
        except Exception:
            last = None
    since = args.since or last
    since_q = f"&since={since}" if since else ""
    print(f"═ Relève GitHub {REPO} " + (f"(depuis {since})" if since else "(tout l'historique ouvert)"))

    # --- Issues (l'API issues inclut les PR : on les sépare) -------------------
    items, remaining = api_get(
        f"/repos/{REPO}/issues?state=open&sort=updated&direction=desc&per_page=100{since_q}", token)
    issues = [i for i in items if "pull_request" not in i]
    prs_items = [i for i in items if "pull_request" in i]

    # --- PR ouvertes (endpoint dédié, plus complet) ---------------------------
    prs, _ = api_get(f"/repos/{REPO}/pulls?state=open&per_page=100", token)
    pr_open = {p["number"]: p for p in prs}
    for n, p in pr_open.items():
        if not any(i["number"] == n for i in prs_items):
            prs_items.append(p)

    # --- Rapport ---------------------------------------------------------------
    now_iso = dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    attention = 0
    print(f"\n┌─ ISSUES OUVERTES : {len(issues)}")
    for i in sorted(issues, key=lambda x: x["updated_at"], reverse=True):
        labels = ", ".join(l["name"] for l in i.get("labels", [])) or "sans étiquette"
        new_comments = ""
        if since and i["comments"]:
            cs, _ = api_get(f"/repos/{REPO}/issues/{i['number']}/comments?since={since}", token)
            mine = [c for c in cs if c["user"]["login"] != "alexisfrlopez-debug"]
            if mine:
                new_comments = f" — {len(mine)} NOUVEAU(X) commentaire(s)"
                attention += 1
        print(f"│ #{i['number']} · {i['title']}\n│    [{labels}] ouverte {fmt_when(i['created_at'])}"
              f", maj {fmt_when(i['updated_at'])} · {i['comments']} commentaire(s){new_comments}")
        attention += 1 if not since else 0
    if not issues:
        print("│ (aucune)")

    print(f"\n┌─ PULL REQUESTS OUVERTES : {len(pr_open)}")
    for p in sorted(prs_items, key=lambda x: x["updated_at"], reverse=True):
        print(f"│ PR #{p['number']} · {p['title']} (branche {p['head']['ref']})"
              f" — maj {fmt_when(p['updated_at'])}")
        attention += 1
    if not pr_open:
        print("│ (aucune)")

    # --- Issues fermées récemment (info) ---------------------------------------
    if since:
        closed, _ = api_get(
            f"/issues?state=closed&sort=updated&direction=desc&per_page=20{since_q}", token)
        recently = [i for i in closed
                    if "pull_request" not in i and i["closed_at"] and i["closed_at"] >= since]
        print(f"\n┌─ ISSUES FERMÉES DEPUIS LA DERNIÈRE RELÈVE : {len(recently)}")
        for i in recently:
            print(f"│ #{i['number']} · {i['title']} (fermée {fmt_when(i['closed_at'])})")
        if not recently:
            print("│ (aucune)")

    print(f"\n┌─ Étoiles/forks : ", end="")
    repo_info, _ = api_get(f"/repos/{REPO}", token)
    print(f"{repo_info.get('stargazers_count', 0)} ★ / {repo_info.get('forks_count', 0)} forks"
          f" / {repo_info.get('subscribers_count', '?')} watchers")

    print(f"\n→ {attention} élément(s) à traiter selon le protocole §7bis.4"
          f" (classifier → agir → répondre → worklog).")
    if remaining != "?" and int(remaining) < 50:
        print(f"⚠ Quota API restant faible : {remaining} requêtes.")

    if not args.no_save:
        STATE_FILE.write_text(json.dumps(
            {"last_check": now_iso, "repo": REPO}, indent=2) + "\n")
        print(f"État mis à jour : {STATE_FILE.name} → {now_iso}")


if __name__ == "__main__":
    main()
