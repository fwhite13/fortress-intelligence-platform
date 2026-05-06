#!/usr/bin/env python3
"""
run_v7_g2_recheck.py — ADO#2824
Re-scores G2 (ExternalDependency classification) against the cached ADO2808-BEDROCK-OUTPUT.json
using the UPDATED EXT_DEP_SIGNALS (bedrock-agent-runtime removed).

Does NOT call Bedrock. Reads cached output, re-applies WiClassifier logic, prints G2 result.
"""
import json, sys, re
from pathlib import Path

OUTPUT_JSON_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2808-BEDROCK-OUTPUT.json"

# Updated signals — bedrock-agent-runtime REMOVED (ADO#2824)
EXT_DEP_SIGNALS = [
    "rob", "rob nethery", "cloudflare", "cf config", "cf route",
    "azure access", "iam request", "iam permissions",
    "secrets manager access", "ado pat", "pat token"
]

def contains_any(text, signals):
    tl = text.lower()
    return any(s.lower() in tl for s in signals)

def is_external_dep(item):
    text = f"{item.get('title','')}\n{item.get('description','')}".lower()
    return contains_any(text, EXT_DEP_SIGNALS)

def get_tags(item):
    return [str(t).lower() for t in (item.get("tags") or [])]

def get_title(item):
    return item.get("title") or ""

def get_type(item):
    return item.get("type") or item.get("workItemType", "")

print("=== ADO#2824 — G2 Re-check (cached output, updated signals) ===")

with open(OUTPUT_JSON_PATH) as f:
    items = json.load(f)

print(f"Loaded {len(items)} WIs from {OUTPUT_JSON_PATH}")

# Re-apply isExternalDependency with updated signals
for item in items:
    item["isExternalDependency"] = is_external_dep(item)
    # Also honour blocked-external tag (same as run_v7_validation.py)
    tags = item.get("tags") or []
    if "blocked-external" in [t.lower() for t in tags]:
        item["isExternalDependency"] = True

ext_dep_wis = [w for w in items if w.get("isExternalDependency") == True]
print(f"\nExt dep WIs after signal update: {len(ext_dep_wis)}")
for w in ext_dep_wis:
    print(f"  - [{get_type(w)}] {get_title(w)[:90]}")
    print(f"    tags: {get_tags(w)}")

# G2 scoring
g2_fails = []
for w in ext_dep_wis:
    tags = get_tags(w)
    has_blocked = "blocked-external" in tags
    has_owner = any(t.startswith("owner-") for t in tags)
    if not has_blocked or not has_owner:
        g2_fails.append({
            "title": get_title(w)[:80],
            "tags": tags,
            "missing": (["blocked-external"] if not has_blocked else []) + (["owner-*"] if not has_owner else [])
        })

g2_pass = len(g2_fails) == 0
print(f"\nG2: {'PASS' if g2_pass else 'FAIL'} — {len(ext_dep_wis)} ext dep WIs, {len(g2_fails)} missing required tags")
if g2_fails:
    for f in g2_fails:
        print(f"  FAIL: {f['title']}")
        print(f"    tags: {f['tags']}")
        print(f"    missing: {f['missing']}")

print(f"\nResult: G2 {'PASS ✓' if g2_pass else 'FAIL ✗'}")
sys.exit(0 if g2_pass else 1)
