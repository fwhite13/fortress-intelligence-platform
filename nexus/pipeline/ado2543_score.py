#!/usr/bin/env python3
"""ADO#2543 — Score all 10 checklist items against v2 Bedrock output."""

import json

with open("/home/fredw/projects/fip/nexus/pipeline/nexus-prompt-validation-output-v2.json") as f:
    wis = json.load(f)

print(f"Total WIs: {len(wis)}\n")

# Type counts
type_counts = {}
for wi in wis:
    t = wi.get("type", "Unknown")
    type_counts[t] = type_counts.get(t, 0) + 1
print("Type counts:", type_counts)

# Epics
epics = [wi for wi in wis if wi["type"] == "Epic"]
print(f"\n=== EPICS ({len(epics)}) ===")
for e in epics:
    print(f"  {e['title']}")

# Features
features = [wi for wi in wis if wi["type"] == "Feature"]
print(f"\n=== FEATURES ({len(features)}) ===")
for f_ in features:
    print(f"  {f_['title']} (parent: {f_.get('parentTitle')})")

# --- Item 1: Infrastructure WIs in scaffold feature ---
print("\n\n=== ITEM 1: Infrastructure WIs in scaffold feature ===")
scaffold_features = [wi for wi in wis if wi["type"] == "Feature" and any(
    kw in wi["title"].lower() for kw in ["scaffold", "infrastructure", "ecs service"]
)]
print(f"Scaffold features found: {[f['title'] for f in scaffold_features]}")

infra_wis = [wi for wi in wis if wi.get("wiTemplate") == "infrastructure"]
print(f"Infrastructure WIs: {len(infra_wis)}")
for iw in infra_wis:
    print(f"  - {iw['title']} | wiTemplate={iw.get('wiTemplate')}")

item1 = len(infra_wis) > 0
print(f"ITEM 1: {'PASS' if item1 else 'FAIL'}")

# --- Item 2: Rob CF task tags ---
print("\n=== ITEM 2: Rob CF task — tags include blocked-external AND owner-rob-nethery ===")
rob_wis = [wi for wi in wis if wi.get("externalOwner") and "rob" in wi["externalOwner"].lower()]
for rw in rob_wis:
    print(f"  Title: {rw['title']}")
    print(f"  isExternalDependency: {rw.get('isExternalDependency')}")
    print(f"  externalOwner: {rw.get('externalOwner')}")
    print(f"  tags: {rw.get('tags')}")
    tags = rw.get("tags", [])
    has_blocked = "blocked-external" in tags
    has_owner = "owner-rob-nethery" in tags
    print(f"  blocked-external: {has_blocked}, owner-rob-nethery: {has_owner}")

item2 = (len(rob_wis) > 0 and
         rob_wis[0].get("isExternalDependency") == True and
         "blocked-external" in rob_wis[0].get("tags", []) and
         "owner-rob-nethery" in rob_wis[0].get("tags", []))
print(f"ITEM 2: {'PASS' if item2 else 'FAIL'}")

# --- Item 3: IAM permissions WI ---
print("\n=== ITEM 3: IAM permissions WI ===")
iam_wis = [wi for wi in wis if wi.get("externalOwner") and "iam" in wi["externalOwner"].lower()]
for iw in iam_wis:
    print(f"  Title: {iw['title']}")
    print(f"  isExternalDependency: {iw.get('isExternalDependency')}")
    print(f"  externalOwner: {iw.get('externalOwner')}")

item3 = (len(iam_wis) > 0 and iam_wis[0].get("isExternalDependency") == True)
print(f"ITEM 3: {'PASS' if item3 else 'FAIL'}")

# --- Item 4: search_kb story ≥4 Test Cases ---
print("\n=== ITEM 4: search_kb ≥4 Test Cases ===")
search_stories = [wi for wi in wis if wi["type"] == "User Story" and "search_kb" in wi["title"].lower()]
print(f"search_kb stories: {[s['title'] for s in search_stories]}")
search_tcs = []
for ss in search_stories:
    tcs = [wi for wi in wis if wi["type"] == "Test Case" and wi.get("parentTitle") == ss["title"]]
    search_tcs.extend(tcs)
    print(f"  Test Cases for '{ss['title']}': {len(tcs)}")
    for tc in tcs:
        print(f"    - {tc['title']}")

# Also check testedByTitles on search_kb stories
for ss in search_stories:
    tbt = ss.get("testedByTitles", [])
    if tbt:
        print(f"  testedByTitles on story: {len(tbt)} entries")

item4 = len(search_tcs) >= 4
print(f"ITEM 4: {'PASS' if item4 else 'FAIL'} ({len(search_tcs)} TCs)")

# --- Item 5: add_to_kb ≥2 Test Cases ---
print("\n=== ITEM 5: add_to_kb ≥2 Test Cases ===")
add_stories = [wi for wi in wis if wi["type"] == "User Story" and "add_to_kb" in wi["title"].lower()]
print(f"add_to_kb stories: {[s['title'] for s in add_stories]}")
add_tcs = []
for as_ in add_stories:
    tcs = [wi for wi in wis if wi["type"] == "Test Case" and wi.get("parentTitle") == as_["title"]]
    add_tcs.extend(tcs)
    print(f"  Test Cases: {len(tcs)}")
    for tc in tcs:
        print(f"    - {tc['title']}")

item5 = len(add_tcs) >= 2
print(f"ITEM 5: {'PASS' if item5 else 'FAIL'} ({len(add_tcs)} TCs)")

# --- Item 6: get_job_status ≥1 Test Case ---
print("\n=== ITEM 6: get_job_status ≥1 Test Case ===")
job_stories = [wi for wi in wis if wi["type"] == "User Story" and "get_job_status" in wi["title"].lower()]
print(f"get_job_status stories: {[s['title'] for s in job_stories]}")
job_tcs = []
for js in job_stories:
    tcs = [wi for wi in wis if wi["type"] == "Test Case" and wi.get("parentTitle") == js["title"]]
    job_tcs.extend(tcs)
    print(f"  Test Cases: {len(tcs)}")
    for tc in tcs:
        print(f"    - {tc['title']}")

item6 = len(job_tcs) >= 1
print(f"ITEM 6: {'PASS' if item6 else 'FAIL'} ({len(job_tcs)} TCs)")

# --- Item 7: 2 Epics + cross-Epic predecessors ---
print("\n=== ITEM 7: 2 Epics + FAIT v2 DB cross-Epic predecessors ===")
print(f"Epic count: {len(epics)}")
for e in epics:
    print(f"  {e['title']}")

# Look for FAIT v2 DB stories with predecessorTitles referencing fip-mcp
fait_stories = [wi for wi in wis if wi["type"] == "User Story" and any(
    kw in wi["title"].lower() for kw in ["team", "entitlement", "kb_entitlement", "project kb data"]
)]
print(f"\nFAIT v2 DB-related stories: {len(fait_stories)}")
cross_epic_found = False
for fs in fait_stories:
    preds = fs.get("predecessorTitles")
    print(f"  {fs['title']}")
    print(f"    predecessorTitles: {preds}")
    if preds:
        cross_epic_found = True

# Also check all stories for cross-epic predecessors
print("\nAll stories with predecessorTitles:")
for wi in wis:
    if wi.get("predecessorTitles"):
        print(f"  {wi['title']}: {wi['predecessorTitles']}")

item7 = len(epics) == 2 and cross_epic_found
print(f"ITEM 7: {'PASS' if item7 else 'FAIL'} (epics={len(epics)}, cross-epic={cross_epic_found})")

# --- Item 8: Exactly 3 external deps ---
print("\n=== ITEM 8: Exactly 3 isExternalDependency=true WIs ===")
ext_deps = [wi for wi in wis if wi.get("isExternalDependency") == True]
print(f"External dep count: {len(ext_deps)}")
for ed in ext_deps:
    print(f"  {ed['title']} | owner={ed.get('externalOwner')}")

item8 = len(ext_deps) == 3
print(f"ITEM 8: {'PASS' if item8 else 'FAIL'} ({len(ext_deps)} deps)")

# --- Item 9: 3 distinct external owners ---
print("\n=== ITEM 9: 3 distinct external dep entries ===")
owners = set()
for ed in ext_deps:
    owners.add(ed.get("externalOwner", "unknown"))
print(f"Distinct owners: {owners}")

item9 = len(owners) >= 3
print(f"ITEM 9: {'PASS' if item9 else 'FAIL'} ({len(owners)} distinct)")

# --- Item 10: FIRM migration WI ---
print("\n=== ITEM 10: FIRM migration WI ===")
migration_wis = [wi for wi in wis if wi.get("wiTemplate") == "migration"]
print(f"Migration WIs: {len(migration_wis)}")
firm_migration = [wi for wi in wis if any(
    kw in wi.get("title", "").lower() for kw in ["firm", "migrate", "migration"]
) and any(
    kw in wi.get("title", "").lower() for kw in ["firm", "meeting", "summariz"]
)]
if not firm_migration:
    firm_migration = [wi for wi in wis if wi.get("wiTemplate") == "migration" and "firm" in wi.get("description", "").lower()]
if not firm_migration:
    firm_migration = migration_wis  # fallback

print(f"FIRM migration candidates: {len(firm_migration)}")
for fm in firm_migration:
    desc = fm.get("description", "")
    print(f"  Title: {fm['title']}")
    print(f"  wiTemplate: {fm.get('wiTemplate')}")
    has_before = "before" in desc.lower() or "**Before:**" in desc
    has_after = "after" in desc.lower() or "**After:**" in desc
    has_validation = "validation" in desc.lower() or "**Validation:**" in desc
    print(f"  Before/After/Validation in desc: {has_before}/{has_after}/{has_validation}")

item10 = (len(firm_migration) > 0 and
          firm_migration[0].get("wiTemplate") == "migration" and
          "before" in firm_migration[0].get("description", "").lower() and
          "after" in firm_migration[0].get("description", "").lower() and
          "validation" in firm_migration[0].get("description", "").lower())
print(f"ITEM 10: {'PASS' if item10 else 'FAIL'}")

# --- Additional checks ---
print("\n\n=== ADDITIONAL CHECKS ===")

# specReference on User Stories
stories = [wi for wi in wis if wi["type"] == "User Story"]
stories_with_spec = [s for s in stories if s.get("specReference")]
print(f"User Stories with specReference: {len(stories_with_spec)}/{len(stories)}")

# rationale on Test Cases
test_cases = [wi for wi in wis if wi["type"] == "Test Case"]
tcs_with_rationale = [tc for tc in test_cases if tc.get("rationale")]
print(f"Test Cases with rationale: {len(tcs_with_rationale)}/{len(test_cases)}")

# --- SUMMARY ---
print("\n\n" + "="*60)
print("SCORING SUMMARY")
print("="*60)
items = [
    (1, "Infrastructure WIs carry wiTemplate=infrastructure", item1),
    (2, "Rob CF tags: blocked-external + owner-rob-nethery", item2),
    (3, "IAM WI: isExternalDependency=true, externalOwner=AWS IAM", item3),
    (4, "search_kb ≥4 Test Cases", item4),
    (5, "add_to_kb ≥2 Test Cases", item5),
    (6, "get_job_status ≥1 Test Case", item6),
    (7, "2 Epics + cross-Epic predecessors", item7),
    (8, "Exactly 3 external deps", item8),
    (9, "3 distinct external dep entries", item9),
    (10, "FIRM migration wiTemplate=migration + Before/After/Validation", item10),
]

score = 0
for num, desc, result in items:
    status = "PASS" if result else "FAIL"
    v1_fail = " ← was FAIL in v1" if num in [2, 7, 8] else ""
    print(f"  [{status}] #{num}: {desc}{v1_fail}")
    if result:
        score += 1

print(f"\nFinal Score: {score}/10")
