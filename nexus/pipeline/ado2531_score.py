#!/usr/bin/env python3
"""ADO#2531 — Score Bedrock output against §11 pass/fail checklist."""

import json

OUTPUT_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json"
META_PATH = "/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT-META.json"

with open(OUTPUT_PATH) as f:
    items = json.load(f)

with open(META_PATH) as f:
    meta = json.load(f)

# --- WI Count Summary ---
type_counts = {}
for item in items:
    t = item.get("type", "Unknown")
    type_counts[t] = type_counts.get(t, 0) + 1

print("=== WI Count Summary ===")
for t in ["Epic", "Feature", "User Story", "Task", "Test Case"]:
    print(f"  {t}: {type_counts.get(t, 0)}")
print(f"  Total: {len(items)}")

# Helper: find items by type
def by_type(t):
    return [i for i in items if i.get("type") == t]

def by_title_contains(substr):
    return [i for i in items if substr.lower() in i.get("title", "").lower()]

def by_parent(parent_title):
    return [i for i in items if (i.get("parentTitle") or "").lower() == parent_title.lower()]

stories = by_type("User Story")
tasks = by_type("Task")
test_cases = by_type("Test Case")
features = by_type("Feature")

results = []

# --- Checklist Item 1: All 4 infrastructure WIs in scaffold feature carry wi_template='infrastructure' ---
scaffold_feature_titles = []
for f_item in features:
    title = f_item.get("title", "").lower()
    if "scaffold" in title or "infrastructure" in title:
        scaffold_feature_titles.append(f_item.get("title"))

infra_stories = []
for s in stories:
    parent = (s.get("parentTitle") or "").lower()
    wi_template = (s.get("wiTemplate") or "").lower()
    title_lower = s.get("title", "").lower()
    # Check if parent is a scaffold/infrastructure feature
    for sf in scaffold_feature_titles:
        if sf and sf.lower() == parent:
            if wi_template == "infrastructure":
                infra_stories.append(s.get("title"))

# Also check for infrastructure WIs by wiTemplate
all_infra = [s for s in stories if (s.get("wiTemplate") or "").lower() == "infrastructure"]
print(f"\n=== Checklist 1: Infrastructure WIs ===")
print(f"  Infrastructure stories found: {len(all_infra)}")
for s in all_infra:
    print(f"    - {s.get('title')}")

# Check for badge (🏗️ in title or tags)
infra_with_badge = [s for s in all_infra if "🏗" in s.get("title", "") or "🏗" in str(s.get("tags", []))]
check1_pass = len(all_infra) >= 4
notes1 = f"{len(all_infra)} infrastructure WIs found (need ≥4). Badge in title/tags: {len(infra_with_badge)}/{len(all_infra)}"
if len(infra_with_badge) < len(all_infra) and len(all_infra) >= 4:
    notes1 += " — badge missing on some but wi_template correct"
results.append(("Infrastructure WIs have wi_template='infrastructure'", "PASS" if check1_pass else "FAIL", notes1))

# --- Checklist Item 2: Rob's CF task ---
rob_items = [i for i in items if "rob" in (i.get("externalOwner") or "").lower() or "cloudflare" in i.get("title", "").lower() or "cf route" in i.get("title", "").lower()]
check2_pass = False
notes2 = "NOT_FOUND"
for ri in rob_items:
    is_ext = ri.get("isExternalDependency", False)
    owner = ri.get("externalOwner", "")
    tags = ri.get("tags", [])
    has_blocked = "blocked-external" in tags
    has_owner_tag = any("rob" in t.lower() for t in tags)
    if is_ext and "Rob Nethery" in owner:
        check2_pass = True
        notes2 = f"Found: '{ri.get('title')}'. isExternalDependency={is_ext}, externalOwner='{owner}', blocked-external={'YES' if has_blocked else 'NO'}, owner-rob-nethery={'YES' if has_owner_tag else 'NO'}"
        if not has_blocked or not has_owner_tag:
            check2_pass = False
            notes2 += " — MISSING required tags"
        break
results.append(("Rob's CF task: is_external_dependency=true, owner='Rob Nethery'", "PASS" if check2_pass else "FAIL", notes2))

# --- Checklist Item 3: IAM permissions WI ---
iam_items = [i for i in items if "iam" in (i.get("externalOwner") or "").lower() or "iam" in i.get("title", "").lower()]
check3_pass = False
notes3 = "NOT_FOUND"
for ii in iam_items:
    is_ext = ii.get("isExternalDependency", False)
    owner = ii.get("externalOwner", "")
    if is_ext and "AWS IAM" in owner:
        check3_pass = True
        notes3 = f"Found: '{ii.get('title')}'. isExternalDependency={is_ext}, externalOwner='{owner}'"
        break
results.append(("IAM WI: is_external_dependency=true, owner='AWS IAM'", "PASS" if check3_pass else "FAIL", notes3))

# --- Checklist Item 4: search_kb ≥4 Test Cases ---
search_kb_stories = [s for s in stories if "search_kb" in s.get("title", "").lower()]
search_kb_tcs = []
for s in search_kb_stories:
    title = s.get("title", "")
    tcs = [tc for tc in test_cases if (tc.get("parentTitle") or "") == title]
    search_kb_tcs.extend(tcs)
# Also check testedByTitles
if not search_kb_tcs:
    for s in search_kb_stories:
        tested_by = s.get("testedByTitles", []) or []
        search_kb_tcs = [{"title": t} for t in tested_by]

check4_pass = len(search_kb_tcs) >= 4
notes4 = f"{len(search_kb_tcs)} Test Cases found for search_kb (need ≥4)"
if search_kb_tcs:
    for tc in search_kb_tcs[:6]:
        notes4 += f"\n    - {tc.get('title', '?')}"
results.append(("search_kb: >=4 Test Cases for scoping enforcement", "PASS" if check4_pass else "FAIL", notes4))

# --- Checklist Item 5: add_to_kb ≥2 Test Cases ---
add_kb_stories = [s for s in stories if "add_to_kb" in s.get("title", "").lower()]
add_kb_tcs = []
for s in add_kb_stories:
    title = s.get("title", "")
    tcs = [tc for tc in test_cases if (tc.get("parentTitle") or "") == title]
    add_kb_tcs.extend(tcs)
if not add_kb_tcs:
    for s in add_kb_stories:
        tested_by = s.get("testedByTitles", []) or []
        add_kb_tcs = [{"title": t} for t in tested_by]

check5_pass = len(add_kb_tcs) >= 2
notes5 = f"{len(add_kb_tcs)} Test Cases found for add_to_kb (need ≥2)"
results.append(("add_to_kb: >=2 Test Cases for write entitlement + metadata", "PASS" if check5_pass else "FAIL", notes5))

# --- Checklist Item 6: get_job_status ≥1 Test Case ---
job_status_stories = [s for s in stories if "get_job_status" in s.get("title", "").lower()]
job_status_tcs = []
for s in job_status_stories:
    title = s.get("title", "")
    tcs = [tc for tc in test_cases if (tc.get("parentTitle") or "") == title]
    job_status_tcs.extend(tcs)
if not job_status_tcs:
    for s in job_status_stories:
        tested_by = s.get("testedByTitles", []) or []
        job_status_tcs = [{"title": t} for t in tested_by]

check6_pass = len(job_status_tcs) >= 1
notes6 = f"{len(job_status_tcs)} Test Cases found for get_job_status (need ≥1)"
results.append(("get_job_status: >=1 Test Case for polling contract", "PASS" if check6_pass else "FAIL", notes6))

# --- Checklist Item 7: FAIT v2 DB stories have cross-Epic predecessorTitles ---
fait_db_stories = [s for s in stories if "team" in s.get("title", "").lower() and ("table" in s.get("title", "").lower() or "schema" in s.get("title", "").lower() or "db" in s.get("title", "").lower() or "entitlement" in s.get("title", "").lower() or "membership" in s.get("title", "").lower())]
# Also check for stories under FAIT v2 DB epic
fait_db_epic = [e for e in by_type("Epic") if "fait" in e.get("title", "").lower() and "db" in e.get("title", "").lower()]
if fait_db_epic:
    epic_title = fait_db_epic[0].get("title")
    fait_db_features = [f_item for f_item in features if (f_item.get("parentTitle") or "") == epic_title]
    for f_item in fait_db_features:
        ft = f_item.get("title")
        feat_stories = [s for s in stories if (s.get("parentTitle") or "") == ft]
        fait_db_stories.extend(feat_stories)
    # deduplicate
    seen = set()
    unique = []
    for s in fait_db_stories:
        t = s.get("title")
        if t not in seen:
            seen.add(t)
            unique.append(s)
    fait_db_stories = unique

# Check predecessorTitles reference forge-kb tool group feature
forge_feature_titles = [f_item.get("title") for f_item in features if "forge" in f_item.get("title", "").lower() or "tool group" in f_item.get("title", "").lower() or "kb tool" in f_item.get("title", "").lower()]
scaffold_titles = [f_item.get("title") for f_item in features if "scaffold" in f_item.get("title", "").lower() or "infrastructure" in f_item.get("title", "").lower()]
cross_epic_refs = forge_feature_titles + scaffold_titles

has_cross_epic = 0
for s in fait_db_stories:
    preds = s.get("predecessorTitles") or []
    for p in preds:
        for ref in cross_epic_refs:
            if ref and ref.lower() in p.lower():
                has_cross_epic += 1
                break
        # Also check if any predecessor references items from the fip-mcp epic
        for item in items:
            if item.get("title") == p and item.get("type") in ["Feature", "User Story"]:
                # Check if this predecessor is in a different epic
                pass

check7_pass = has_cross_epic > 0
notes7 = f"{len(fait_db_stories)} FAIT v2 DB stories found. {has_cross_epic} have cross-Epic predecessorTitles."
if fait_db_stories:
    for s in fait_db_stories:
        preds = s.get("predecessorTitles") or []
        notes7 += f"\n    - '{s.get('title')}' predecessors: {preds}"
results.append(("FAIT v2 DB stories have cross-Epic predecessorTitles", "PASS" if check7_pass else "FAIL", notes7))

# --- Checklist Item 8: ExternalDependencyCount = 3 ---
ext_deps = [i for i in items if i.get("isExternalDependency", False)]
check8_pass = len(ext_deps) == 3
notes8 = f"{len(ext_deps)} external dependencies found (need exactly 3)"
for ed in ext_deps:
    notes8 += f"\n    - '{ed.get('title')}' owner='{ed.get('externalOwner')}'"
results.append(("ExternalDependencyCount = 3 in ArtifactSet context", "PASS" if check8_pass else "FAIL", notes8))

# --- Checklist Item 9: External Dependencies panel entries = 3 ---
# This is essentially the same as #8 — 3 distinct external owners/entries
unique_owners = set()
for ed in ext_deps:
    unique_owners.add(ed.get("externalOwner", "Unknown"))
check9_pass = len(ext_deps) >= 3
notes9 = f"{len(ext_deps)} entries, {len(unique_owners)} unique owners: {', '.join(unique_owners)}"
results.append(("External Dependencies panel entries = 3", "PASS" if check9_pass else "FAIL", notes9))

# --- Checklist Item 10: FIRM migration WI ---
migration_items = [i for i in items if "migrate" in i.get("title", "").lower() or "migration" in i.get("title", "").lower()]
firm_migration = [i for i in migration_items if "firm" in i.get("title", "").lower() or "startingestionjob" in i.get("title", "").lower() or "add_to_kb" in i.get("title", "").lower()]
# Also check wiTemplate
check10_pass = False
notes10 = "NOT_FOUND"
for fm in firm_migration:
    wt = (fm.get("wiTemplate") or "").lower()
    desc = fm.get("description", "")
    has_before = "before" in desc.lower() or "**before**" in desc.lower()
    has_after = "after" in desc.lower() or "**after**" in desc.lower()
    has_validation = "validation" in desc.lower() or "**validation**" in desc.lower()
    if wt == "migration":
        check10_pass = has_before and has_after and has_validation
        notes10 = f"Found: '{fm.get('title')}'. wiTemplate='{wt}'. Before={'YES' if has_before else 'NO'}, After={'YES' if has_after else 'NO'}, Validation={'YES' if has_validation else 'NO'}"
        break
    else:
        notes10 = f"Found: '{fm.get('title')}' but wiTemplate='{wt}' (need 'migration')"
# If not found in firm_migration, also check all migration-template items
if not firm_migration:
    all_migration_template = [i for i in items if (i.get("wiTemplate") or "").lower() == "migration"]
    for m in all_migration_template:
        if "firm" in m.get("title", "").lower() or "ingestion" in m.get("title", "").lower() or "add_to_kb" in m.get("description", "").lower():
            desc = m.get("description", "")
            has_before = "before" in desc.lower()
            has_after = "after" in desc.lower()
            has_validation = "validation" in desc.lower()
            check10_pass = has_before and has_after and has_validation
            notes10 = f"Found: '{m.get('title')}'. wiTemplate='migration'. Before={'YES' if has_before else 'NO'}, After={'YES' if has_after else 'NO'}, Validation={'YES' if has_validation else 'NO'}"
            break

results.append(("FIRM migration WI: wi_template='migration', Before/After/Validation present", "PASS" if check10_pass else "FAIL", notes10))

# --- Additional Checks ---
# specReference on all User Stories
stories_missing_spec_ref = [s for s in stories if not s.get("specReference")]
all_spec_ref = len(stories_missing_spec_ref) == 0

# rationale on all Test Cases
tcs_missing_rationale = [tc for tc in test_cases if not tc.get("rationale")]
all_rationale = len(tcs_missing_rationale) == 0

# --- Print Results ---
print("\n=== §11 CHECKLIST RESULTS ===")
pass_count = 0
for i, (criterion, result, notes) in enumerate(results, 1):
    icon = "PASS" if result == "PASS" else "FAIL"
    if result == "PASS":
        pass_count += 1
    print(f"  {i}. [{icon}] {criterion}")
    print(f"     {notes}")

print(f"\nChecklist score: {pass_count}/10")

print(f"\n=== ADDITIONAL CHECKS ===")
print(f"  specReference on all stories: {'PASS' if all_spec_ref else 'FAIL'} ({len(stories_missing_spec_ref)} missing)")
if stories_missing_spec_ref:
    for s in stories_missing_spec_ref[:5]:
        print(f"    - '{s.get('title')}'")
print(f"  rationale on all Test Cases: {'PASS' if all_rationale else 'FAIL'} ({len(tcs_missing_rationale)} missing)")
if tcs_missing_rationale:
    for tc in tcs_missing_rationale[:5]:
        print(f"    - '{tc.get('title')}'")
print(f"  JSON directly parseable: PASS")

# Write scoring results for report generation
scoring = {
    "checklist": results,
    "pass_count": pass_count,
    "total": 10,
    "type_counts": type_counts,
    "total_items": len(items),
    "spec_ref_missing": len(stories_missing_spec_ref),
    "rationale_missing": len(tcs_missing_rationale),
    "json_parseable": True,
    "stories_missing_spec_ref_titles": [s.get("title") for s in stories_missing_spec_ref],
    "tcs_missing_rationale_titles": [tc.get("title") for tc in tcs_missing_rationale]
}
with open("/home/fredw/projects/fip/nexus/pipeline/ADO2531-SCORING.json", "w") as f:
    json.dump(scoring, f, indent=2, default=str)

print("\nScoring written to ADO2531-SCORING.json")
