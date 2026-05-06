#!/usr/bin/env python3
"""
run_v7_score_only.py — Score the already-generated ADO2808-BEDROCK-OUTPUT.json
against §G 13-item checklist. Uses token counts from the Bedrock run.
"""

import json, re, sys
from datetime import datetime

OUTPUT_JSON_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2808-BEDROCK-OUTPUT.json"
BUILD_REPORT_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2808-BUILD-REPORT.md"
FORGE_SPEC_PATH = "/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md"
MODEL_ID = "us.anthropic.claude-sonnet-4-20250514-v1:0"

# Token counts from the actual Bedrock run
call1_input  = 11888
call1_output = 25288
call2_input  = 31506
call2_output = 12817
call2_success = True

with open(OUTPUT_JSON_PATH) as f:
    items = json.load(f)

with open(FORGE_SPEC_PATH) as f:
    full_spec = f.read()

print(f"Loaded {len(items)} items from {OUTPUT_JSON_PATH}", flush=True)

# ===========================================================================
# Helpers
# ===========================================================================

def get_type(item):
    t = item.get("type") or item.get("workItemType", "")
    return t

def get_template(item):
    return (item.get("wiTemplate") or "").lower()

def get_tags(item):
    return [str(t).lower() for t in (item.get("tags") or [])]

def get_title(item):
    return item.get("title") or ""

def get_ac(item):
    return item.get("acceptanceCriteria") or ""

def count_ac_items(ac):
    if isinstance(ac, list):
        return len(ac)
    if not ac:
        return 0
    checkbox = re.findall(r'^\s*-\s*\[.\]\s*(.+)', ac, re.MULTILINE)
    if checkbox:
        return len(checkbox)
    numbered = re.findall(r'^\s*\d+[\.\)]\s+.+', ac, re.MULTILINE)
    if numbered:
        return len(numbered)
    lines = [l.strip() for l in ac.split('\n') if l.strip()]
    return len(lines)

INFRA_SIGNALS = [
    "create ecr", "ecr repo", "iam role", "ecs service", "alb target",
    "alb rule", "secrets manager secret", "target group",
    "fargate task definition", "ecr repository", "task execution role"
]
MIGRATION_SIGNALS = [
    "migrate", "replace", "move from", "deprecate",
    "switch from", "transition from", "cut over"
]
EXT_DEP_SIGNALS = [
    "rob", "rob nethery", "cloudflare", "cf config", "cf route",
    "azure access", "iam request", "iam permissions",
    "secrets manager access", "ado pat", "pat token",
    "bedrock-agent-runtime"
]
RULE_A_KEYWORDS = [
    'auth', 'token', 'entitlement', 'scope', 'scoping', 'permission',
    'validate', 'enforce', 'restrict', 'deny', 'unauthorized', '403',
    'jwt', 'bearer', 'polling', 'contract', 'async'
]
TC_EXCLUSION_KEYWORDS = [
    'migration', 'infrastructure', 'scaffolding', 'setup', 'upgrade',
    'config', 'configuration', 'environment', 'pipeline', 'deploy',
    'deployment', 'database schema', 'seed data'
]

def contains_any(text, signals):
    tl = text.lower()
    return any(s.lower() in tl for s in signals)

# ===========================================================================
# Classify items
# ===========================================================================

epics    = [w for w in items if get_type(w) == "Epic"]
features = [w for w in items if get_type(w) == "Feature"]
stories  = [w for w in items if get_type(w) == "User Story"]
tasks    = [w for w in items if get_type(w) == "Task"]
tcs      = [w for w in items if get_type(w) == "Test Case"]
standard_stories = [w for w in stories if get_template(w) == "standard"]
infra_wis     = [w for w in stories if get_template(w) == "infrastructure"]
migration_wis = [w for w in stories if get_template(w) == "migration"]
ext_dep_wis   = [w for w in items if w.get("isExternalDependency") == True]

type_counts = {}
for w in items:
    t = get_type(w)
    type_counts[t] = type_counts.get(t, 0) + 1
print("WI type counts:", type_counts, flush=True)
print(f"Ext dep WIs: {len(ext_dep_wis)}", flush=True)
print(f"Standard stories: {len(standard_stories)}", flush=True)
print(f"TCs: {len(tcs)}", flush=True)

# Parent lookup maps
tc_parents = {}
for tc in tcs:
    p = tc.get("parentTitle", "") or ""
    tc_parents[p] = tc_parents.get(p, 0) + 1

task_parents = {}
for t in tasks:
    p = t.get("parentTitle", "") or ""
    task_parents[p] = task_parents.get(p, 0) + 1

# ===========================================================================
# G1: Infra WIs have wiTemplate = "infrastructure"
# ===========================================================================

infra_signal_wis = [
    w for w in stories
    if contains_any(get_title(w) + "\n" + (w.get("description") or ""), INFRA_SIGNALS)
]
infra_template_mismatch = [w for w in infra_signal_wis if get_template(w) != "infrastructure"]
g1_pass = len(infra_template_mismatch) == 0
g1_notes = f"{len(infra_wis)} infra-template WIs; {len(infra_signal_wis)} infra-signal WIs"
if infra_template_mismatch:
    g1_notes += f"; {len(infra_template_mismatch)} signal-match but wrong template"

# ===========================================================================
# G2: Ext dep WIs have blocked-external + owner-* tags
# ===========================================================================

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
g2_notes = f"{len(ext_dep_wis)} ext dep WIs"
if g2_fails:
    g2_notes += f"; {len(g2_fails)} missing required tags"

# ===========================================================================
# G3: All external owners extracted from spec found
# ===========================================================================

spec_lower = full_spec.lower()
expected_owners = []
if "rob nethery" in spec_lower or ("rob" in spec_lower and "cloudflare" in spec_lower):
    expected_owners.append("Rob Nethery")
if "iam" in spec_lower or "aws iam" in spec_lower:
    expected_owners.append("AWS IAM")
if "product owner" in spec_lower:
    expected_owners.append("Product Owner")

found_owners = set()
for w in ext_dep_wis:
    owner = w.get("externalOwner")
    if owner:
        found_owners.add(owner)

missing_owners = [o for o in expected_owners if o not in found_owners]
g3_pass = len(missing_owners) == 0
g3_notes = f"Expected: {expected_owners}; Found: {sorted(found_owners)}"
if missing_owners:
    g3_notes += f"; Missing: {missing_owners}"

# ===========================================================================
# G4: No duplicate ext dep WIs per owner
# ===========================================================================

open_question_wis = [w for w in ext_dep_wis
                     if "confirm" in get_title(w).lower() or "open" in get_title(w).lower()]
oq_owners = {}
for w in open_question_wis:
    owner = w.get("externalOwner") or "unknown"
    oq_owners[owner] = oq_owners.get(owner, 0) + 1
g4_duplicates = {o: c for o, c in oq_owners.items() if c > 1}
g4_pass = len(g4_duplicates) == 0
g4_notes = f"OQ WIs per owner: {oq_owners}"
if g4_duplicates:
    g4_notes += f"; DUPLICATES: {g4_duplicates}"

# ===========================================================================
# G5: Open questions consolidated (1 WI per external owner)
# ===========================================================================
g5_pass = g4_pass
g5_notes = g4_notes

# ===========================================================================
# G6: TC Rule A fires
# ===========================================================================

g6_fails = []
for story in standard_stories:
    title = get_title(story).lower()
    excluded = any(kw in title for kw in TC_EXCLUSION_KEYWORDS)
    if excluded:
        continue
    ac = get_ac(story)
    ac_text_str = " ".join(ac) if isinstance(ac, list) else (ac or "")
    search_text = (title + " " + ac_text_str).lower()
    found_kw = [kw for kw in RULE_A_KEYWORDS if kw in search_text]
    if found_kw:
        story_title = get_title(story)
        tc_count = tc_parents.get(story_title, 0)
        if tc_count == 0:
            g6_fails.append({"title": story_title[:80], "keywords": found_kw})
g6_pass = len(g6_fails) == 0
g6_notes = f"{len(g6_fails)} Rule A violations" if g6_fails else "All Rule A stories have ≥1 TC"

# ===========================================================================
# G7: TC Rule B fires
# ===========================================================================

g7_fails = []
for story in standard_stories:
    title = get_title(story).lower()
    excluded = any(kw in title for kw in TC_EXCLUSION_KEYWORDS)
    if excluded:
        continue
    ac = get_ac(story)
    ac_count = count_ac_items(ac)
    if ac_count >= 4:
        story_title = get_title(story)
        tc_count = tc_parents.get(story_title, 0)
        if tc_count == 0:
            g7_fails.append({"title": story_title[:80], "ac_count": ac_count})
g7_pass = len(g7_fails) == 0
g7_notes = f"{len(g7_fails)} Rule B violations" if g7_fails else "All Rule B stories have ≥1 TC"

# ===========================================================================
# G8: Separate Epic for separate app DB work
# ===========================================================================

schema_change_signals = [
    r"add.*table.*to\s+(fait|firm|nexus)",
    r"(fait|firm|nexus).*must have.*table",
    r"schema.*change.*to.*(fait|firm|nexus)",
    r"(fait|firm|nexus).*database.*change",
    r"existing.*app.*schema",
    r"separate.*epic.*for.*(fait|firm|nexus)"
]
separate_db_signals_re = [
    r"fait\s+db", r"firm\s+db", r"nexus\s+db",
    r"fait\s+database", r"firm\s+database",
    r"separate.*database", r"separate.*schema",
    r"existing.*database", r"existing\s+app"
]
spec_has_separate_db = (
    any(re.search(sig, full_spec, re.IGNORECASE) for sig in separate_db_signals_re) or
    any(re.search(sig, full_spec, re.IGNORECASE) for sig in schema_change_signals)
)

if spec_has_separate_db:
    g8_pass = len(epics) >= 2
    g8_notes = f"Spec has separate-app DB work; {len(epics)} epics found"
    if not g8_pass:
        g8_notes += " — FAIL: expected ≥2 epics"
else:
    # Deeper check: does spec mention modifying another app's schema?
    fait_change = re.search(
        r'(fait|firm|nexus)\s+(must|needs?|requires?)\s+(schema|table|column|database)',
        full_spec, re.IGNORECASE)
    if fait_change:
        g8_pass = len(epics) >= 2
        g8_notes = f"Spec references existing-app schema changes; {len(epics)} epics"
    else:
        g8_pass = True
        g8_notes = f"No separate-app DB work detected; {len(epics)} epic(s) — correct"

# ===========================================================================
# G9: Prerequisite schema work tracked in ADO
# ===========================================================================

prereq_match = re.search(r'##\s*8.*?(?=##\s*[0-9]|\Z)', full_spec, re.DOTALL)
prereq_text = prereq_match.group(0) if prereq_match else ""

schema_prereq_signals = [r'schema', r'table', r'column', r'migration', r'database']
spec_has_schema_prereqs = any(re.search(sig, prereq_text, re.IGNORECASE) for sig in schema_prereq_signals)

if spec_has_schema_prereqs:
    schema_wis = [w for w in items
                  if any(kw in (get_title(w) + " " + (w.get("description") or "")).lower()
                         for kw in ["schema", "table", "column", "migration"])]
    g9_pass = len(schema_wis) > 0
    g9_notes = f"Spec §8 has schema prereqs; {len(schema_wis)} schema-related WIs found"
    if not g9_pass:
        g9_notes += " — FAIL"
else:
    g9_pass = True
    g9_notes = "No prerequisite schema work in §8"

# ===========================================================================
# G10: Follow-on migration WI exists
# ===========================================================================

migration_deferred_signals = [
    r'out of scope', r'phase 2', r'future work', r'not.*initial',
    r'follow.?on.*migration', r'migrate.*from', r'migration.*from',
    r'deprecat', r'replace.*with', r'switch.*from', r'transition.*from'
]
spec_has_migration = any(re.search(sig, full_spec, re.IGNORECASE) for sig in migration_deferred_signals)
migration_wis_all = [w for w in items if get_template(w) == "migration"]
g10_pass = (not spec_has_migration) or len(migration_wis_all) > 0
g10_notes = f"Spec has migration/deferred signals: {spec_has_migration}; migration WIs: {len(migration_wis_all)}"
if spec_has_migration and len(migration_wis_all) == 0:
    g10_notes += " — FAIL"

# ===========================================================================
# G11: Every User Story has specReference
# ===========================================================================

missing_specref = [s for s in stories if not s.get("specReference")]
g11_pass = len(missing_specref) == 0
g11_notes = f"{len(missing_specref)}/{len(stories)} stories missing specReference"

# ===========================================================================
# G12: Every TC has rationale citing §N
# ===========================================================================

missing_rationale = [tc for tc in tcs
                     if not tc.get("rationale") or "§" not in (tc.get("rationale") or "")]
g12_pass = len(missing_rationale) == 0
g12_notes = f"{len(missing_rationale)}/{len(tcs)} TCs missing rationale with §N"

# ===========================================================================
# G13: Every User Story has ≥2 Task children
# ===========================================================================

g13_fails = []
for s in stories:
    title = get_title(s)
    task_count = task_parents.get(title, 0)
    if task_count < 2:
        g13_fails.append({"title": title[:80], "tasks": task_count})
g13_pass = len(g13_fails) == 0
g13_notes = f"{len(g13_fails)}/{len(stories)} stories have <2 tasks" if g13_fails else f"All {len(stories)} stories have ≥2 tasks"

# ===========================================================================
# Compile + print results
# ===========================================================================

checks = [
    ("G1",  "Infra WIs have wiTemplate = 'infrastructure'",              g1_pass,  g1_notes),
    ("G2",  "Ext dep WIs have blocked-external + owner-* tags",           g2_pass,  g2_notes),
    ("G3",  "All external owners extracted from spec found",              g3_pass,  g3_notes),
    ("G4",  "No duplicate ext dep WIs per owner",                         g4_pass,  g4_notes),
    ("G5",  "Open questions consolidated (1 WI per external owner)",      g5_pass,  g5_notes),
    ("G6",  "TC Rule A fires (security keyword stories have ≥1 TC)",      g6_pass,  g6_notes),
    ("G7",  "TC Rule B fires (stories with 4+ ACs have ≥1 TC)",           g7_pass,  g7_notes),
    ("G8",  "Separate Epic for separate app DB work",                     g8_pass,  g8_notes),
    ("G9",  "Prerequisite schema work tracked in ADO",                    g9_pass,  g9_notes),
    ("G10", "Follow-on migration WI exists (incl. conditional/deferred)", g10_pass, g10_notes),
    ("G11", "Every User Story has specReference (non-null, has §N)",      g11_pass, g11_notes),
    ("G12", "Every TC has rationale citing a spec section",               g12_pass, g12_notes),
    ("G13", "Every User Story has ≥2 Task children",                      g13_pass, g13_notes),
]

score = sum(1 for _, _, p, _ in checks if p)
total = len(checks)

print(f"\n=== §G Score: {score}/{total} ===", flush=True)
for check_id, desc, passed, notes in checks:
    status = "PASS" if passed else "FAIL"
    print(f"  {check_id}: {status} — {notes}", flush=True)

# ===========================================================================
# Debug detail for failures
# ===========================================================================

if not g2_pass:
    print("\nG2 fail detail:", flush=True)
    for f in g2_fails:
        print(f"  {f}", flush=True)

if not g6_pass:
    print("\nG6 fail detail:", flush=True)
    for f in g6_fails:
        print(f"  {f}", flush=True)

if not g7_pass:
    print("\nG7 fail detail:", flush=True)
    for f in g7_fails:
        print(f"  {f}", flush=True)

if not g11_pass:
    print("\nG11 fail detail:", flush=True)
    for s in missing_specref[:10]:
        print(f"  {get_title(s)[:80]}", flush=True)

if not g12_pass:
    print("\nG12 fail detail:", flush=True)
    for tc in missing_rationale[:10]:
        print(f"  {get_title(tc)[:80]} | rationale: {tc.get('rationale','(none)')[:60]}", flush=True)

if not g13_pass:
    print("\nG13 fail detail:", flush=True)
    for f in g13_fails[:10]:
        print(f"  {f}", flush=True)

# ===========================================================================
# Write Build Report
# ===========================================================================

fail_details = []
if not g1_pass:
    fail_details.append(("G1", "Infra WIs have wiTemplate = 'infrastructure'",
        f"Found {len(infra_template_mismatch)} WI(s) matching infra signals but template != 'infrastructure':\n" +
        "\n".join(f"  - {get_title(w)}" for w in infra_template_mismatch)))
if not g2_pass:
    fail_details.append(("G2", "Ext dep WIs have blocked-external + owner-* tags",
        f"Found {len(g2_fails)} ext dep WI(s) missing required tags:\n" +
        "\n".join(f"  - {f['title']} | missing: {f['missing']}" for f in g2_fails)))
if not g3_pass:
    fail_details.append(("G3", "All external owners extracted from spec found",
        f"Expected: {expected_owners}\nFound: {sorted(found_owners)}\nMissing: {missing_owners}"))
if not g4_pass:
    fail_details.append(("G4", "No duplicate ext dep WIs per owner",
        f"Duplicate OQ WIs per owner: {g4_duplicates}"))
if not g5_pass:
    fail_details.append(("G5", "Open questions consolidated (1 WI per external owner)",
        f"OQ WI counts per owner: {oq_owners}"))
if not g6_pass:
    fail_details.append(("G6", "TC Rule A — security keyword stories missing TCs",
        "\n".join(f"  - {f['title']} [keywords: {f['keywords']}]" for f in g6_fails)))
if not g7_pass:
    fail_details.append(("G7", "TC Rule B — 4+ AC stories missing TCs",
        "\n".join(f"  - {f['title']} [{f['ac_count']} ACs]" for f in g7_fails)))
if not g8_pass:
    fail_details.append(("G8", "Separate Epic for separate app DB work", g8_notes))
if not g9_pass:
    fail_details.append(("G9", "Prerequisite schema work tracked in ADO", g9_notes))
if not g10_pass:
    fail_details.append(("G10", "Follow-on migration WI exists",
        f"Spec has migration/deferred signals but no migration-template WIs generated."))
if not g11_pass:
    fail_details.append(("G11", "Every User Story has specReference",
        f"{len(missing_specref)} stories missing specReference:\n" +
        "\n".join(f"  - {get_title(s)[:80]}" for s in missing_specref[:20])))
if not g12_pass:
    fail_details.append(("G12", "Every TC has rationale citing §N",
        f"{len(missing_rationale)} TCs missing §N in rationale:\n" +
        "\n".join(f"  - {get_title(tc)[:80]} | rationale: {tc.get('rationale','(none)')[:80]}" for tc in missing_rationale[:20])))
if not g13_pass:
    fail_details.append(("G13", "Every User Story has ≥2 Task children",
        f"{len(g13_fails)} stories with <2 tasks:\n" +
        "\n".join(f"  - {f['title']} ({f['tasks']} tasks)" for f in g13_fails[:20])))

report_lines = [
    "# Build Report — ADO#2808",
    "## NEXUS ArtifactGen: v7 Validation — Two-Call TC Architecture",
    "",
    f"**Date:** {datetime.now().strftime('%Y-%m-%d %H:%M:%S EDT')}",
    f"**Script:** `run_v7_validation.py`",
    f"**Input spec:** `forge-kb-mcp-server-spec-2026-04-27.md`",
    "",
    "---",
    "",
    "## Model & Token Usage",
    "",
    "| | Value |",
    "|---|---|",
    f"| Model | `{MODEL_ID}` |",
    f"| Call 1 (decomposition) input tokens | {call1_input:,} |",
    f"| Call 1 (decomposition) output tokens | {call1_output:,} |",
    f"| Call 2 (TC scan) input tokens | {call2_input:,} |",
    f"| Call 2 (TC scan) output tokens | {call2_output:,} |",
    f"| Call 2 success | {'✅ Yes' if call2_success else '❌ No (non-fatal)'} |",
    f"| Total tokens | {call1_input + call1_output + call2_input + call2_output:,} |",
    "",
    "---",
    "",
    "## WI Type Counts",
    "",
    "| Type | Count |",
    "|------|-------|",
]
for t, c in sorted(type_counts.items()):
    report_lines.append(f"| {t} | {c} |")
report_lines.extend([
    f"| **TOTAL** | **{len(items)}** |",
    "",
    "---",
    "",
    "## §G Checklist",
    "",
    f"**Score: {score}/13**",
    "",
    "| # | Check | Result | Notes |",
    "|---|-------|--------|-------|",
])
for check_id, desc, passed, notes in checks:
    status = "✅ PASS" if passed else "❌ FAIL"
    safe_notes = notes.replace("|", "\\|").replace("\n", " ")
    report_lines.append(f"| {check_id} | {desc} | {status} | {safe_notes} |")

report_lines.extend(["", "---", "", "## Fail Details", ""])
if fail_details:
    for check_id, desc, detail in fail_details:
        report_lines.extend([f"### {check_id}: {desc}", "", detail, ""])
else:
    report_lines.append("_All checks passed._")

report_lines.extend([
    "---", "",
    "## Run History", "",
    "| Version | Score | ADO |",
    "|---------|-------|-----|",
    "| v1 | 7/13 | ADO#2531 |",
    "| v2 | 3/13 | ADO#2543 |",
    "| v3 | 6/13 | ADO#2555 |",
    "| v4 | 8/13 | ADO#2558 |",
    "| v5 | 10/13 | ADO#2577 |",
    "| v6 | 11/13 | ADO#2581 |",
    f"| **v7** | **{score}/13** | ADO#2808 |",
    "", "---", "", "_End of report._",
])

report_text = "\n".join(report_lines)
with open(BUILD_REPORT_PATH, "w") as f:
    f.write(report_text)
print(f"\nBuild report written: {BUILD_REPORT_PATH}", flush=True)

print(f"""
=== FINAL SUMMARY ===
Score: {score}/13
Call 1: {call1_input:,} input + {call1_output:,} output tokens
Call 2: {call2_input:,} input + {call2_output:,} output tokens
Total WIs: {len(items)} ({len(tcs)} TCs)
""", flush=True)
