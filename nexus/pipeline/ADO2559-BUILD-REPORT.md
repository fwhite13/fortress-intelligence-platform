# Prompt Validation Report v5: ADO#2578

**Date:** 2026-04-29
**Prompt source:** nexus-prompt-v5-candidate.md
**Checklist:** §G — 13-item Generalized Validation Checklist
**Run history:** v1=7/10 (ADO#2531), v2=3/10 (ADO#2543), v3=6/10 (ADO#2555), v4=TBD (ADO#2558)

## Bedrock Call Details
- Model: us.anthropic.claude-sonnet-4-20250514-v1:0
- Max tokens: 32768
- Input tokens: 12927
- Output tokens: 27440
- JSON parse: PASS

## WI Count Summary
| Type | Count |
|------|-------|
| Epic | 2 |
| Feature | 5 |
| User Story | 17 |
| Task | 34 |
| Test Case | 14 |
| **Total** | **72** |

## §G Checklist Results

| # | Check | Result | Notes |
|---|-------|--------|-------|
| G1 | Infra WIs wiTemplate=infrastructure | PASS | All 9 infra WIs (ECR, ECS, IAM, ALB, Secrets) correctly tagged |
| G2 | Ext dep WIs have blocked-external + owner tags | PASS | Rob Nethery: `blocked-external` + `owner-rob-nethery`; Product Owner: `blocked-external` + `owner-product-owner` |
| G3 | All external owners identified | PASS | Rob Nethery (§5 CF) and Fred/Product Owner (§8 #1,2,5,6) both have WIs. Tony (#4,7,8) correctly excluded as internal. |
| G4 | No duplicate ext dep WIs per owner | PASS | 1 WI per owner, no duplicates |
| G5 | Open Questions → 1 consolidated WI per owner | PASS | Fred's 4 open items (#1,2,5,6) consolidated into 1 WI. Rob has 1 WI for CF config. |
| G6 | TC Rule A fires (security keywords) | FAIL | 5 standard stories with security keywords have 0 TCs (see deep dive below) |
| G7 | TC Rule B fires (4+ ACs unconditional) | FAIL | 8 standard stories with 4+ ACs have 0 TCs — Rule B still not firing (see deep dive below) |
| G8 | Separate Epic for separate app DB work | PASS | "FAIT v2 DB: KB Entitlements Schema" is a separate Epic — first time this has passed! |
| G9 | Prerequisite schema work tracked | PASS | kb_entitlements + teams + team_memberships tables all appear as User Stories under FAIT v2 DB Epic — first time this has passed! |
| G10 | Follow-on migration WI for deferred work | FAIL | FIRM and NEXUS migrations generated correctly; FAIT v1 migration (§7) missing entirely |
| G11 | Every standard story has >=2 Tasks | PASS | All 17 stories have exactly 2 Task children |
| G12 | Every User Story has specReference | PASS | 0 missing |
| G13 | No TC parented under security-only story | PASS | All 3 TC parent stories are implementing stories, not security-only |

**Score: 10/13**

## Additional Checks
- Epic count: 2 — "FORGE KB MCP Server: Core Infrastructure & Knowledge Base Gateway", "FAIT v2 DB: KB Entitlements Schema"
- All User Stories have specReference: PASS (0 missing)
- All Test Cases have rationale: PASS (0 missing)

## Key Item Deep Dives

### G6 — TC Rule A (Security Keywords) — Failures

5 standard stories with security/behavior keywords in title or ACs have 0 Test Cases:

| Story | Keywords Found | ACs | TCs |
|-------|---------------|-----|-----|
| SSE endpoints story | `auth` | 5 | 0 |
| list_kbs story | `auth`, `entitlement`, `permission` | 5 | 0 |
| get_kb_metadata story | `entitlement`, `validate` | 5 | 0 |
| get_job_status story | `async` | 5 | 0 |
| kb_entitlements table story | `entitlement`, `permission`, `validate` | 4 | 0 |

3 stories that correctly triggered Rule A:
- Token validation story (keywords: `auth`, `token`, `validate`, `jwt`) — 5 TCs
- search_kb story (keywords: `auth`, `entitlement`, `scoping`, `validate`, `unauthorized`, `403`) — 5 TCs
- add_to_kb story (keywords: `entitlement`, `validate`, `async`) — 4 TCs

**Pattern:** The model generates TCs only for the 3 most obviously security-critical stories but ignores the remaining 5 stories despite clear keyword matches. The model appears to apply a severity threshold rather than the keyword-match rule.

### G7 — TC Rule B (4+ ACs) — Full Story-by-Story Check

**8 standard stories with 4+ ACs and 0 TCs — ALL violations:**

| Story | ACs | TCs | Verdict |
|-------|-----|-----|---------|
| SSE endpoints | 5 | 0 | FAIL |
| CORS handling | 6 | 0 | FAIL |
| list_kbs | 5 | 0 | FAIL |
| get_kb_metadata | 5 | 0 | FAIL |
| get_job_status polling | 5 | 0 | FAIL |
| CloudFlare routing (ext dep) | 5 | 0 | FAIL |
| kb_entitlements table (FAIT v2 DB) | 4 | 0 | FAIL |
| teams/team_memberships (FAIT v2 DB) | 4 | 0 | FAIL |

**3 standard stories with 4+ ACs that correctly got TCs:**

| Story | ACs | TCs | Verdict |
|-------|-----|-----|---------|
| Token validation | 6 | 5 | PASS |
| search_kb with scoping | 8 | 5 | PASS |
| add_to_kb async | 6 | 4 | PASS |

**Root cause analysis:** The v5 restructuring (separate Rule A and Rule B headings) did NOT fix the problem. The model still only generates TCs for the 3 stories it considers "most security-important" and ignores all others — even though 5 of the 8 failing stories ALSO trigger Rule A keywords. The model treats TC generation as a selective decision based on perceived importance, not a rule-based trigger.

**Possible v6 fix directions:**
1. Move Rule B self-check from OUTPUT SELF-CHECK into a MANDATORY POST-PROCESSING PASS that runs after the initial array is built — forcing a second pass over every story
2. Add explicit negative examples: "A story about listing KBs with 5 ACs and the word 'entitlement' in its ACs MUST have Test Cases even though it seems like a simple read operation"
3. Reduce the keyword list to only truly security-critical terms (remove `async`, `validate`, `contract` which may dilute signal) and make Rule B the primary TC driver

### G8+G9 — Separate Epic + Prerequisite Schema Work

Spec references FAIT v2 DB schema changes (kb_entitlements, team_memberships, teams tables) in §3.2, §4, and §8 (Open Questions/Prerequisites).

- Was a separate FAIT v2 DB Epic generated? **YES** — "FAIT v2 DB: KB Entitlements Schema"
- FAIT v2 DB Feature: "Implement KB Entitlements Database Schema"
- FAIT v2 DB stories found:
  1. "As a database administrator, I want kb_entitlements table created..." (specRef: §3.2)
  2. "As a FAIT user, I want teams and team_memberships tables..." (specRef: §4)
- Do they have predecessorTitles? YES — both point to "Confirm open items with Product Owner"

**This is the first time G8+G9 have passed.** The PREREQUISITE SCHEMA WORK RULE and the updated WRONG (v3/v4) example successfully prevented the model from omitting prerequisite DB work.

### G3+G4+G5 — External Dependency Coverage

**External owners mentioned in spec:**
1. **Rob Nethery** — must configure CloudFlare routing (§5)
2. **Fred / Product Owner** — must confirm FAIT v2 DB timeline (#1), file IAM request (#2), confirm team_memberships schema (#5), confirm Project KB data source ID (#6)
3. **Tony / pipeline** — ECR repo creation (#4), S3 vs direct ingestion (#7), MCP SDK version (#8) — **internal, correctly excluded**

**isExternalDependency=true WIs generated:**
1. "As a system administrator, I want CloudFlare routing configured..." | owner=Rob Nethery | tags=[blocked-external, owner-rob-nethery]
2. "Confirm open items with Product Owner" | owner=Product Owner | tags=[blocked-external, owner-product-owner]

**Open Questions section analysis:**
- Fred (#1,2,5,6): 4 items → 1 consolidated WI ("Confirm open items with Product Owner") — CORRECT
- Rob (#3): 1 item → 1 WI (CloudFlare routing story) — CORRECT
- Tony (#4,7,8): 3 items → correctly excluded as internal developer — CORRECT

### G10 — Follow-on Migration WI

**FIRM migration:**
- Found: YES
- Title: "As a development team, I want FIRM migrated from direct Bedrock calls to use fip-mcp tools so that KB access is centralized"
- wiTemplate: migration
- Before section: YES
- After section: YES
- Validation section: YES

**NEXUS migration:**
- Found: YES
- Title: "As a development team, I want NEXUS migrated from direct Bedrock calls to use fip-mcp tools so that discovery question generation is centralized"
- wiTemplate: migration
- Before section: YES
- After section: YES
- Validation section: YES

**FAIT v1 migration:**
- Found: **NO**
- Spec §7 explicitly describes FAIT v1 migration: "Migrate Retrieve calls to search_kb. Coordinate with FAIT v2 timeline — may be skipped if FAIT v1 is being retired in parallel."
- The follow-on migration rule states: generate migration WIs even when the spec labels them deferred or out of scope
- The "may be skipped" language does not exempt this from WI generation — the WI should exist with a note about the conditional skip

## Overall Verdict

**NEEDS FURTHER REFINEMENT**

## Progress Tracker

| Item | v1 | v2 | v3 | v4 | v5 |
|------|----|----|----|----|-----|
| G1 Infra wiTemplate | - | - | - | - | PASS |
| G2 Ext dep tags | PASS | PASS | PASS | - | PASS |
| G3 All ext owners | PASS | PASS | PASS | - | PASS |
| G4 No dup ext deps | PASS | PASS | PASS | - | PASS |
| G5 Open Q consolidation | PASS | PASS | PASS | - | PASS |
| G6 TC Rule A (keywords) | - | - | - | - | FAIL |
| G7 TC Rule B (4+ ACs) | FAIL | FAIL | FAIL | - | **FAIL (5th consecutive)** |
| G8 Separate DB Epic | FAIL | FAIL | FAIL | - | **PASS (NEW)** |
| G9 Prereq schema tracked | FAIL | FAIL | FAIL | - | **PASS (NEW)** |
| G10 Migration WI | PASS | PASS | PASS | - | FAIL (FAIT v1 missing) |
| G11 >=2 Tasks per story | - | - | - | - | PASS |
| G12 specReference | - | - | - | - | PASS |
| G13 No TC under sec-only | PASS | FAIL | PASS | - | PASS |

## Remaining Issues

### Issue 1: G6+G7 — TC generation still selective (CRITICAL — 5th consecutive failure for Rule B)

The model generates TCs for only 3 out of 11 eligible standard stories. All 3 receiving TCs are the most obviously security-critical stories (token validation, search_kb scoping, add_to_kb entitlements). The remaining 8 stories are ignored despite:
- 5 of them having security keywords (should trigger Rule A)
- All 8 having 4+ ACs (should trigger Rule B unconditionally)

The v5 restructuring (separate Rule A/B headings) did not fix this. The model is making a judgment call about which stories "deserve" TCs rather than following the rules mechanically.

**Suggested v6 approach:** Consider a two-pass architecture in the prompt — first generate all WIs without TCs, then run a mandatory second pass that mechanically checks every standard story against Rule A and Rule B and adds TCs where missing. This separates the creative decomposition step from the rule-compliance step.

### Issue 2: G10 — FAIT v1 migration WI missing

Spec §7 describes FAIT v1 migration work. The model generated FIRM and NEXUS migrations but omitted FAIT v1, likely because the spec says "may be skipped if FAIT v1 is being retired in parallel." The follow-on migration rule explicitly covers this case — "even when the spec labels them deferred or out of scope."

**Fix:** Add an example in the follow-on migration rule: "Even if the spec says migration 'may be skipped' or 'is conditional' — still generate the migration WI. It exists as a planning artifact to track the decision."
