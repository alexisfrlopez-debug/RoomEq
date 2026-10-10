#!/usr/bin/env python3
"""Restore bootstrap files from Google Drive after environment reset.

Downloads:
  1. OAuth credentials file (refresh token) -> .secrets/oauth_drive.json
  2. INSTRUCTIONS-REPRISE.md (recovery instructions) -> download/
"""
import json
import sys
import urllib.request
import urllib.parse

OAUTH_FILE_ID = "__ID_FICHIER_TOKEN_OAUTH__"
INSTR_FILE_ID = "__ID_FICHIER_INSTRUCTIONS__"

SECRETS_PATH = "/home/z/my-project/.secrets/oauth_drive.json"
INSTR_PATH = "/home/z/my-project/download/INSTRUCTIONS-REPRISE.md"


def http(url: str, data=None, headers=None, method="GET"):
    req = urllib.request.Request(url, data=data, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        return e.code, e.read()


def main():
    # 1. Fetch OAuth file (raw media)
    status, body = http(f"https://www.googleapis.com/drive/v3/files/{OAUTH_FILE_ID}?alt=media")
    if status != 200:
        print(f"ERROR fetching oauth file: {status} {body[:500]!r}")
        sys.exit(1)
    with open(SECRETS_PATH, "wb") as f:
        f.write(body)
    print(f"[OK] oauth file saved -> {SECRETS_PATH} ({len(body)} bytes)")
    try:
        js = json.loads(body)
        # Print keys only, not secrets
        print(f"     keys present: {sorted(js.keys())}")
    except Exception:
        print("     (not JSON?)")

    # 2. Fetch recovery instructions
    status, body = http(f"https://www.googleapis.com/drive/v3/files/{INSTR_FILE_ID}?alt=media")
    if status != 200:
        print(f"ERROR fetching instructions: {status} {body[:500]!r}")
        sys.exit(1)
    with open(INSTR_PATH, "wb") as f:
        f.write(body)
    text = body.decode("utf-8", errors="replace")
    print(f"[OK] instructions saved -> {INSTR_PATH} ({len(body)} bytes)")
    print("=" * 60)
    print(text[:6000])
    print("=" * 60)


if __name__ == "__main__":
    main()
