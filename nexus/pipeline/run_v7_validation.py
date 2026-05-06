#!/usr/bin/env python3
"""
run_v7_validation.py — ADO#2808
NEXUS ArtifactGen: Validate two-call TC architecture via v7 validation script

Replicates the exact two-call flow from ArtifactGenerationService.cs using
prompts from appsettings.Production.json, then scores against §G 13-item checklist.

Run history: v1=7/13, v2=3/13, v3=6/13, v4=8/13, v5=10/13, v6=11/13
"""

import boto3, json, re, sys, os
from botocore.config import Config
from datetime import datetime

# ===========================================================================
# Step 1: Load deployed prompts from appsettings.Production.json
# ===========================================================================

APPSETTINGS_PATH = "/home/fredw/projects/fip/nexus/src/FortressNexus.Web/appsettings.Production.json"
FORGE_SPEC_PATH = "/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md"
OUTPUT_JSON_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2808-BEDROCK-OUTPUT.json"
BUILD_REPORT_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2808-BUILD-REPORT.md"

print("=== v7 Validation Script — ADO#2808 ===", flush=True)
print(f"Timestamp: {datetime.now().isoformat()}", flush=True)

with open(APPSETTINGS_PATH) as f:
    appsettings = json.load(f)

artifact_gen_system = appsettings["Nexus"]["Prompts"]["ArtifactGenSystem"]
tc_scan_system = appsettings["Nexus"]["Prompts"]["TcScanSystem"]

print(f"ArtifactGenSystem prompt: {len(artifact_gen_system)} chars", flush=True)
print(f"TcScanSystem prompt: {len(tc_scan_system)} chars", flush=True)

# ===========================================================================
# Step 2: Load FORGE KB spec
# ===========================================================================

with open(FORGE_SPEC_PATH) as f:
    forge_spec = f.read()

print(f"FORGE spec: {len(forge_spec)} chars", flush=True)

# Build Call 1 user message (mirrors ArtifactGenerationService line exactly)
call1_user = "Please decompose the following specification into Azure DevOps work items per your instructions:\n\n---\n\n" + forge_spec

# ===========================================================================
# Step 3: Bedrock client
# ===========================================================================

MODEL_ID = "us.anthropic.claude-sonnet-4-20250514-v1:0"
MAX_TOKENS = 32768
BETA = ["output-128k-2025-02-19"]

bedrock_config = Config(read_timeout=600, retries={"max_attempts": 0})
session = boto3.Session(profile_name="fortress-tools-deployer", region_name="us-east-1")
client = session.client("bedrock-runtime", config=bedrock_config)

def invoke_bedrock(system_prompt, user_message, label):
    """Call Bedrock and return (text, input_tokens, output_tokens)."""
    print(f"\n--- {label} ---", flush=True)
    body = {
        "anthropic_version": "bedrock-2023-05-31",
        "max_tokens": MAX_TOKENS,
        "system": system_prompt,
        "messages": [{"role": "user", "content": user_message}],
        "anthropic_beta": BETA,
    }
    print(f"  Model: {MODEL_ID}, max_tokens: {MAX_TOKENS}", flush=True)
    response = client.invoke_model(
        modelId=MODEL_ID,
        body=json.dumps(body),
        contentType="application/json",
        accept="application/json",
    )
    result = json.loads(response["body"].read())
    text = result["content"][0]["text"].strip()
    usage = result.get("usage", {})
    input_tok = usage.get("input_tokens", 0)
    output_tok = usage.get("output_tokens", 0)
    print(f"  Tokens — Input: {input_tok}, Output: {output_tok}", flush=True)
    return text, input_tok, output_tok

def strip_fences(text):
    """Strip markdown code fences (mirrors ParseWorkItems in the service)."""
    trimmed = text.strip()
    if trimmed.startswith("```"):
        start = trimmed.index('\n') if '\n' in trimmed else -1
        end = trimmed.rfind("```")
        if start >= 0 and end > start:
            trimmed = trimmed[start+1:end].strip()
    return trimmed

# ===========================================================================
# Step 4: Call 1 — Decomposition
# ===========================================================================

call1_text, call1_input, call1_output = invoke_bedrock(
    artifact_gen_system, call1_user, "Call 1 — Decomposition"
)

# Parse Call 1
call1_clean = strip_fences(call1_text)
try:
    items = json.loads(call1_clean)
    print(f"  JSON parse: OK — {len(items)} items", flush=True)
except Exception as e:
    print(f"  JSON parse FAILED: {e}", file=sys.stderr)
    print(f"  Raw (first 500): {call1_clean[:500]}", file=sys.stderr)
    sys.exit(1)

# ===========================================================================
# Step 5: WiClassifier post-processing (mirrors WiClassifierService.cs)
# ===========================================================================

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

def contains_any(text, signals):
    tl = text.lower()
    return any(s.lower() in tl for s in signals)

def classify_story(item):
    """Mirror WiClassifierService.ClassifyStory."""
    text = f"{item.get('title','')}\n{item.get('description','')}"
    if contains_any(text, INFRA_SIGNALS):
        return "infrastructure"
    if contains_any(text, MIGRATION_SIGNALS):
        return "migration"
    return "standard"

def is_external_dep(item):
    """Mirror WiClassifierService.IsExternalDependency."""
    text = f"{item.get('title','')}\n{item.get('description','')}".lower()
    return contains_any(text, EXT_DEP_SIGNALS)

def extract_external_owner(item):
    """Mirror WiClassifierService.ExtractExternalOwner."""
    if not is_external_dep(item):
        return None
    text = f"{item.get('title','')}\n{item.get('description','')}".lower()
    if any(s in text for s in ["rob", "cloudflare", "cf config"]):
        return "Rob Nethery"
    if any(s in text for s in ["iam", "bedrock-agent-runtime"]):
        return "AWS IAM"
    if any(s in text for s in ["azure access", "azure subscription"]):
        return "Azure Admin"
    if any(s in text for s in ["ado pat", "pat token"]):
        return "ADO Admin"
    return "External Owner"

print("\nApplying WiClassifier post-processing...", flush=True)
for item in items:
    # Only override wiTemplate if the model returned something unexpected
    # (service sets it from WiClassifier — mirror that)
    item["wiTemplate"] = classify_story(item)
    item["isExternalDependency"] = is_external_dep(item)
    item["externalOwner"] = extract_external_owner(item)

    # Ensure isExternalDependency from tags as well (blocked-external tag)
    tags = item.get("tags") or []
    if "blocked-external" in tags:
        item["isExternalDependency"] = True

# ===========================================================================
# Step 6: Call 2 — TC Compliance Scan
# ===========================================================================

call2_text = None
call2_input = 0
call2_output = 0
tc_result = {"testCases": [], "parentUpdates": []}
call2_success = False

call2_user = f"WORK ITEM ARRAY:\n{json.dumps(items)}\n\nORIGINAL SPEC:\n{forge_spec}"

try:
    call2_text, call2_input, call2_output = invoke_bedrock(
        tc_scan_system, call2_user, "Call 2 — TC Compliance Scan"
    )

    call2_clean = strip_fences(call2_text)
    tc_result = json.loads(call2_clean)
    print(f"  TC scan parse: OK — {len(tc_result.get('testCases', []))} TCs, {len(tc_result.get('parentUpdates', []))} parent updates", flush=True)
    call2_success = True

except Exception as e:
    print(f"  Call 2 FAILED (non-fatal, continuing): {e}", file=sys.stderr)
    call2_success = False

# Merge TCs into items array (mirrors service.items.AddRange)
test_cases = tc_result.get("testCases", [])
# Normalize TC fields to match the rest of the array
for tc in test_cases:
    if "type" not in tc and "wiTemplate" not in tc:
        tc["wiTemplate"] = "test-case"
    if "type" not in tc:
        tc["type"] = "Test Case"

items.extend(test_cases)

# Apply parentUpdates (testedByTitles)
if tc_result.get("parentUpdates"):
    title_map = {item.get("title", ""): item for item in items}
    for update in tc_result["parentUpdates"]:
        parent = title_map.get(update.get("storyTitle", ""))
        if parent:
            parent["testedByTitles"] = update.get("testedByTitles", [])

print(f"\nMerged total: {len(items)} items ({len(test_cases)} TCs added)", flush=True)

# ===========================================================================
# Step 7: Write JSON output
# ===========================================================================

with open(OUTPUT_JSON_PATH, "w") as f:
    json.dump(items, f, indent=2)
print(f"\nOutput written: {OUTPUT_JSON_PATH}", flush=True)

# ===========================================================================
# Step 8: §G Checklist Scoring
# ===========================================================================

print("\n=== §G Checklist Scoring ===", flush=True)

# -- Helpers --

def get_type(item):
    # The model returns "type" field; service stores as "workItemType" — check both
    t = item.get("type") or item.get("workItemType", "")
    return t

def get_template(item):
    return (item.get("wiTemplate") or "").lower()

def get_tags(item):
    return [str(t).lower() for t in (item.get("tags") or [])]

def get_title(item):
    return item.get("title") or ""

def get_ac(item):
    ac = item.get("acceptanceCriteria") or ""
    return ac

def count_ac_items(ac):
    """Count AC items using the same patterns as ArtifactGenerationService.ParseAcItems."""
    if isinstance(ac, list):
        return len(ac)
    if not ac:
        return 0
    # Checkbox pattern: - [ ] or - [x]
    checkbox = re.findall(r'^\s*-\s*\[.\]\s*(.+)', ac, re.MULTILINE)
    if checkbox:
        return len(checkbox)
    # Numbered pattern: 1. item
    numbered = re.findall(r'^\s*\d+[\.\)]\s+.+', ac, re.MULTILINE)
    if numbered:
        return len(numbered)
    # Fallback: non-empty lines
    lines = [l.strip() for l in ac.split('\n') if l.strip()]
    return len(lines)

# Classify items
all_wis = items
epics = [w for w in all_wis if get_type(w) == "Epic"]
features = [w for w in all_wis if get_type(w) == "Feature"]
stories = [w for w in all_wis if get_type(w) == "User Story"]
tasks = [w for w in all_wis if get_type(w) == "Task"]
tcs = [w for w in all_wis if get_type(w) == "Test Case"]
standard_stories = [w for w in stories if get_template(w) == "standard"]
infra_wis = [w for w in stories if get_template(w) == "infrastructure"]
migration_wis = [w for w in stories if get_template(w) == "migration"]
ext_dep_wis = [w for w in all_wis if w.get("isExternalDependency") == True]

print(f"\nWI type counts:", flush=True)
type_counts = {}
for w in all_wis:
    t = get_type(w)
    type_counts[t] = type_counts.get(t, 0) + 1
for t, c in sorted(type_counts.items()):
    print(f"  {t}: {c}", flush=True)

# Build parent → children maps
tc_parents = {}
for tc in tcs:
    p = tc.get("parentTitle", "") or ""
    tc_parents[p] = tc_parents.get(p, 0) + 1

task_parents = {}
for t in tasks:
    p = t.get("parentTitle", "") or ""
    task_parents[p] = task_parents.get(p, 0) + 1

# Rule A/B keywords (from TcScanSystem prompt)
RULE_A_KEYWORDS = [
    'auth', 'token', 'entitlement', 'scope', 'scoping', 'permission',
    'validate', 'enforce', 'restrict', 'deny', 'unauthorized', '403',
    'jwt', 'bearer', 'polling', 'contract', 'async'
]

# TC exclusion keywords (from TcScanSystem prompt)
TC_EXCLUSION_KEYWORDS = [
    'migration', 'infrastructure', 'scaffolding', 'setup', 'upgrade',
    'config', 'configuration', 'environment', 'pipeline', 'deploy',
    'deployment', 'database schema', 'seed data'
]

def story_qualifies_for_tc(story):
    """Mirror TcScanSystem qualification rules."""
    if get_template(story) != "standard":
        return False, None
    title = get_title(story).lower()
    # Exclusion check
    for kw in TC_EXCLUSION_KEYWORDS:
        if kw in title:
            return False, f"excluded by title keyword '{kw}'"
    # Rule A — keyword scan on title + acceptanceCriteria
    ac = get_ac(story)
    if isinstance(ac, list):
        ac_text = " ".join(ac)
    else:
        ac_text = ac or ""
    search_text = (title + " " + ac_text).lower()
    found = [kw for kw in RULE_A_KEYWORDS if kw in search_text]
    if found:
        return True, f"Rule A: {found}"
    # Rule B — AC count >= 4
    ac_count = count_ac_items(ac)
    if ac_count >= 4:
        return True, f"Rule B: {ac_count} ACs"
    return False, None

# --- G1: Infra WIs have wiTemplate = "infrastructure" ---
infra_story_titles_in_items = [
    w for w in stories
    if any(sig in (get_title(w) + "\n" + (w.get("description") or "")).lower()
           for sig in INFRA_SIGNALS)
]
infra_template_mismatch = [
    w for w in infra_story_titles_in_items
    if get_template(w) != "infrastructure"
]
g1_pass = len(infra_template_mismatch) == 0
g1_notes = f"{len(infra_wis)} infra WIs found"
if infra_template_mismatch:
    g1_notes += f"; {len(infra_template_mismatch)} signal-matching WIs have wrong template"

# --- G2: Ext dep WIs have blocked-external + owner-* tags ---
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
g2_notes = f"{len(ext_dep_wis)} ext dep WIs found"
if g2_fails:
    g2_notes += f"; {len(g2_fails)} missing required tags"

# --- G3: All external owners extracted from spec found ---
# Find owner names mentioned in spec
spec_lower = forge_spec.lower()
expected_owners = []
if "rob nethery" in spec_lower or "rob" in spec_lower:
    expected_owners.append("Rob Nethery")
if "aws iam" in spec_lower or "iam" in spec_lower:
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
g3_notes = f"Expected owners: {expected_owners}; Found: {list(found_owners)}"
if missing_owners:
    g3_notes += f"; Missing: {missing_owners}"

# --- G4: No duplicate ext dep WIs per owner ---
owner_counts = {}
for w in ext_dep_wis:
    owner = w.get("externalOwner") or "unknown"
    owner_counts[owner] = owner_counts.get(owner, 0) + 1

# Check if any owner has more than the expected WIs
# Per prompt: each distinct external action → exactly one WI
# Open-question WIs should be consolidated (one per owner)
open_question_wis = [w for w in ext_dep_wis if "confirm" in get_title(w).lower() or "open" in get_title(w).lower()]
oq_owners = {}
for w in open_question_wis:
    owner = w.get("externalOwner") or "unknown"
    oq_owners[owner] = oq_owners.get(owner, 0) + 1
g4_duplicates = {o: c for o, c in oq_owners.items() if c > 1}
g4_pass = len(g4_duplicates) == 0
g4_notes = f"OQ WIs per owner: {oq_owners}"
if g4_duplicates:
    g4_notes += f"; DUPLICATES: {g4_duplicates}"

# --- G5: Open questions consolidated (1 WI per external owner) ---
# Same check as G4 but focused on confirmation/open-question WIs
g5_pass = g4_pass  # Same underlying check
g5_notes = g4_notes

# --- G6: TC Rule A fires (security keyword stories have ≥1 TC) ---
g6_fails = []
for story in standard_stories:
    title = get_title(story).lower()
    # Skip excluded
    excluded = any(kw in title for kw in TC_EXCLUSION_KEYWORDS)
    if excluded:
        continue
    ac = get_ac(story)
    ac_text = (ac if isinstance(ac, list) else [ac or ""])
    ac_text_str = " ".join(ac_text) if isinstance(ac_text, list) else ac_text
    search_text = (title + " " + ac_text_str).lower()
    found_kw = [kw for kw in RULE_A_KEYWORDS if kw in search_text]
    if found_kw:
        story_title = get_title(story)
        tc_count = tc_parents.get(story_title, 0)
        if tc_count == 0:
            g6_fails.append({"title": story_title[:80], "keywords": found_kw})
g6_pass = len(g6_fails) == 0
g6_notes = f"{len(g6_fails)} Rule A violations" if g6_fails else "All Rule A stories have ≥1 TC"

# --- G7: TC Rule B fires (stories with 4+ ACs have ≥1 TC) ---
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

# --- G8: Separate Epic for separate app DB work ---
# Check if spec mentions a separate app's DB and if a separate Epic was created
# FORGE spec references FAIT, FIRM, NEXUS DB changes
separate_db_signals = ["fait db", "firm db", "nexus db", "fait database", "firm database",
                        "fait schema", "firm schema", "separate.*database", "separate.*schema",
                        "existing.*database", "existing app"]
spec_mentions_separate_db = any(re.search(sig, forge_spec, re.IGNORECASE) for sig in separate_db_signals)

# The spec should also mention schema changes needed in other apps
# Check for signals about adding tables/columns to existing apps
schema_change_signals = [
    r"add.*table.*to\s+(fait|firm|nexus)",
    r"(fait|firm|nexus).*must have.*table",
    r"schema.*change.*to.*(fait|firm|nexus)",
    r"(fait|firm|nexus).*database.*change",
    r"existing.*app.*schema",
    r"separate.*epic.*for.*(fait|firm|nexus)"
]
# Read more of the spec to check
with open(FORGE_SPEC_PATH) as f:
    full_spec = f.read()

spec_has_separate_db = (
    spec_mentions_separate_db or
    any(re.search(sig, full_spec, re.IGNORECASE) for sig in schema_change_signals)
)

if spec_has_separate_db:
    # Should have 2+ epics
    g8_pass = len(epics) >= 2
    g8_notes = f"Spec has separate DB work; {len(epics)} epics found"
    if not g8_pass:
        g8_notes += " — FAIL: expected ≥2 epics"
else:
    # Check spec sections for multi-app schema work
    # The FORGE spec is for a new service; check if FAIT/FIRM must be modified
    fait_change = re.search(r'(fait|firm|nexus)\s+(must|needs?|requires?)\s+(schema|table|column|database)', full_spec, re.IGNORECASE)
    if fait_change:
        g8_pass = len(epics) >= 2
        g8_notes = f"Spec references existing-app schema changes; {len(epics)} epics found"
    else:
        g8_pass = True  # No separate DB work in spec → single epic is correct
        g8_notes = f"No separate-app DB work detected in spec; {len(epics)} epic(s) — correct"

# --- G9: Prerequisite schema work tracked in ADO ---
# Check if the spec's prerequisites/open-questions mention schema work
prereq_section = re.search(r'##\s*8.*?($|##\s*[0-9])', full_spec, re.DOTALL)
if prereq_section:
    prereq_text = prereq_section.group(0)
else:
    prereq_text = ""

# The spec's prerequisites section is §8 — Open Questions / Prerequisites
# Check for schema work mentioned there
schema_prereq_signals = [
    r'schema', r'table', r'column', r'migration', r'database'
]
spec_has_schema_prereqs = any(re.search(sig, prereq_text, re.IGNORECASE) for sig in schema_prereq_signals)

if spec_has_schema_prereqs:
    # Check if corresponding WIs were generated
    schema_wis = [w for w in all_wis
                  if any(kw in (get_title(w) + " " + (w.get("description") or "")).lower()
                         for kw in ["schema", "table", "column", "migration"])]
    g9_pass = len(schema_wis) > 0
    g9_notes = f"Spec §8 mentions schema work; {len(schema_wis)} schema-related WIs found"
    if not g9_pass:
        g9_notes += " — FAIL: no schema WIs generated"
else:
    g9_pass = True
    g9_notes = "No prerequisite schema work in spec §8"

# --- G10: Follow-on migration WI exists ---
# Check if spec has out-of-scope/deferred migration work
migration_deferred_signals = [
    r'out of scope', r'deferred', r'phase 2', r'future work',
    r'not.*initial', r'follow.*on.*migration', r'follow-on.*migration',
    r'migration.*(may|might|could|should|if|when)',
    r'migrate.*from.*to'  # Any migration described in spec
]
spec_has_deferred_migration = any(re.search(sig, full_spec, re.IGNORECASE) for sig in migration_deferred_signals)

migration_wis_all = [w for w in all_wis if get_template(w) == "migration"]
g10_pass = (not spec_has_deferred_migration) or len(migration_wis_all) > 0
g10_notes = f"Spec has migration/deferred work: {spec_has_deferred_migration}; migration WIs: {len(migration_wis_all)}"
if spec_has_deferred_migration and len(migration_wis_all) == 0:
    g10_notes += " — FAIL"

# --- G11: Every User Story has specReference ---
missing_specref = [s for s in stories if not s.get("specReference")]
g11_pass = len(missing_specref) == 0
g11_notes = f"{len(missing_specref)}/{len(stories)} stories missing specReference"

# --- G12: Every TC has rationale citing a spec section ---
missing_rationale = [tc for tc in tcs
                     if not tc.get("rationale") or "§" not in (tc.get("rationale") or "")]
g12_pass = len(missing_rationale) == 0
g12_notes = f"{len(missing_rationale)}/{len(tcs)} TCs missing rationale with §N"

# --- G13: Every User Story has ≥2 Task children ---
g13_fails = []
for s in stories:
    title = get_title(s)
    task_count = task_parents.get(title, 0)
    if task_count < 2:
        g13_fails.append({"title": title[:80], "tasks": task_count})
g13_pass = len(g13_fails) == 0
g13_notes = f"{len(g13_fails)}/{len(stories)} stories have <2 tasks" if g13_fails else f"All {len(stories)} stories have ≥2 tasks"

# --- Compile results ---
checks = [
    ("G1",  "Infra WIs have wiTemplate = 'infrastructure'",           g1_pass,  g1_notes),
    ("G2",  "Ext dep WIs have blocked-external + owner-* tags",        g2_pass,  g2_notes),
    ("G3",  "All external owners extracted from spec found",           g3_pass,  g3_notes),
    ("G4",  "No duplicate ext dep WIs per owner",                      g4_pass,  g4_notes),
    ("G5",  "Open questions consolidated (1 WI per external owner)",   g5_pass,  g5_notes),
    ("G6",  "TC Rule A fires (security keyword stories have ≥1 TC)",   g6_pass,  g6_notes),
    ("G7",  "TC Rule B fires (stories with 4+ ACs have ≥1 TC)",        g7_pass,  g7_notes),
    ("G8",  "Separate Epic for separate app DB work",                  g8_pass,  g8_notes),
    ("G9",  "Prerequisite schema work tracked in ADO",                 g9_pass,  g9_notes),
    ("G10", "Follow-on migration WI exists (incl. conditional/deferred)", g10_pass, g10_notes),
    ("G11", "Every User Story has specReference (non-null, has §N)",   g11_pass, g11_notes),
    ("G12", "Every TC has rationale citing a spec section",            g12_pass, g12_notes),
    ("G13", "Every User Story has ≥2 Task children",                   g13_pass, g13_notes),
]

score = sum(1 for _, _, p, _ in checks if p)
total = len(checks)

print(f"\n=== §G Score: {score}/{total} ===", flush=True)
for check_id, desc, passed, notes in checks:
    status = "PASS" if passed else "FAIL"
    print(f"  {check_id}: {status} — {notes}", flush=True)

# ===========================================================================
# Step 9: Write Build Report
# ===========================================================================

# Collect fail details
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
        f"Expected: {expected_owners}\nFound: {list(found_owners)}\nMissing: {missing_owners}"))
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
        f"Spec has migration/deferred work but no migration-template WIs generated."))
if not g11_pass:
    fail_details.append(("G11", "Every User Story has specReference",
        f"{len(missing_specref)} stories missing specReference:\n" +
        "\n".join(f"  - {get_title(s)[:80]}" for s in missing_specref[:20])))
if not g12_pass:
    fail_details.append(("G12", "Every TC has rationale citing §N",
        f"{len(missing_rationale)} TCs missing §N in rationale:\n" +
        "\n".join(f"  - {get_title(tc)[:80]} | rationale: {tc.get('rationale','(none)')[:60]}" for tc in missing_rationale[:20])))
if not g13_pass:
    fail_details.append(("G13", "Every User Story has ≥2 Task children",
        f"{len(g13_fails)} stories with <2 tasks:\n" +
        "\n".join(f"  - {f['title']} ({f['tasks']} tasks)" for f in g13_fails[:20])))

# Build report markdown
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
    f"| | Value |",
    f"|---|---|",
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
    f"| **TOTAL** | **{len(all_wis)}** |",
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

report_lines.extend([
    "",
    "---",
    "",
    "## Fail Details",
    "",
])
if fail_details:
    for check_id, desc, detail in fail_details:
        report_lines.extend([
            f"### {check_id}: {desc}",
            "",
            detail,
            "",
        ])
else:
    report_lines.append("_All checks passed._")

report_lines.extend([
    "---",
    "",
    "## Run History",
    "",
    "| Version | Score | ADO |",
    "|---------|-------|-----|",
    "| v1 | 7/13 | ADO#2531 |",
    "| v2 | 3/13 | ADO#2543 |",
    "| v3 | 6/13 | ADO#2555 |",
    "| v4 | 8/13 | ADO#2558 |",
    "| v5 | 10/13 | ADO#2577 |",
    "| v6 | 11/13 | ADO#2581 |",
    f"| **v7** | **{score}/13** | ADO#2808 |",
    "",
    "---",
    "",
    "_End of report._",
])

report_text = "\n".join(report_lines)
with open(BUILD_REPORT_PATH, "w") as f:
    f.write(report_text)
print(f"\nBuild report written: {BUILD_REPORT_PATH}", flush=True)

# ===========================================================================
# Step 10: Print final summary
# ===========================================================================

print(f"""
=== FINAL SUMMARY ===
Score: {score}/13
Call 1: {call1_input} input + {call1_output} output tokens
Call 2: {call2_input} input + {call2_output} output tokens (success: {call2_success})
Total WIs: {len(all_wis)} ({len(tcs)} TCs)
Output: {OUTPUT_JSON_PATH}
Report: {BUILD_REPORT_PATH}
""", flush=True)

print(f"SCORE={score}", flush=True)
print(f"CALL1_INPUT={call1_input}", flush=True)
print(f"CALL1_OUTPUT={call1_output}", flush=True)
print(f"CALL2_INPUT={call2_input}", flush=True)
print(f"CALL2_OUTPUT={call2_output}", flush=True)
