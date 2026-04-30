# Prompt Validation Report v6: ADO#2581

**Date:** 2026-04-29
**Prompt source:** nexus-prompt-v6-candidate.md
**Checklist:** §G — 13-item Generalized Validation Checklist
**Run history:** v1=7/10 (ADO#2531), v2=3/10 (ADO#2543), v3=6/10 (ADO#2555), v4=8/10 (ADO#2558), v5=10/13 (ADO#2577)

## Bedrock Call Details
- Model: us.anthropic.claude-sonnet-4-20250514-v1:0
- Max tokens: 32768
- Beta: output-128k-2025-02-19
- Input tokens: 13678
- Output tokens: 32301
- JSON parse: PASS

## WI Count Summary
| Type | Count |
|------|-------|
| Epic | 2 |
| Feature | 6 |
| User Story | 18 |
| Task | 36 |
| Test Case | 21 |
| **Total** | **83** |

## §G Checklist Results

| # | Check | Result | Notes |
|---|-------|--------|-------|
| G1 | Infra WIs wiTemplate=infrastructure | PASS | 4 infra WIs (ECR, ECS, IAM, ALB) correctly tagged |
| G2 | Ext dep WIs have blocked-external + owner tags | PASS | Rob Nethery: `blocked-external` + `owner-rob-nethery`; Product Owner: `blocked-external` + `owner-product-owner` |
| G3 | All external owners identified | PASS | Rob Nethery (§5 CF) and Product Owner (§8 open items) both have WIs |
| G4 | No duplicate ext dep WIs per owner | PASS | 1 WI per owner, no duplicates |
| G5 | Open Questions → 1 consolidated WI per owner | PASS | Product Owner open items consolidated into 1 WI. Rob has 1 WI for CF config. |
| G6 | TC Rule A fires (security keywords) | FAIL | 5 standard stories with security keywords have 0 TCs (see deep dive) |
| G7 | TC Rule B fires (4+ ACs unconditional) | FAIL | 7 standard stories with 4+ ACs have 0 TCs (see deep dive) |
| G8 | Separate Epic for separate app DB work | PASS | "FAIT v2 DB: Schema Prerequisites" is a separate Epic |
| G9 | Prerequisite schema work tracked | PASS | kb_entitlements + teams/team_memberships tables appear as User Stories under FAIT v2 DB Epic |
| G10 | Follow-on migration WI for deferred work | **PASS** | FAIT v1 migration WI now exists (wiTemplate=migration, priority=4) — **NEW PASS vs v5** |
| G11 | Every standard story has >=2 Tasks | PASS | All 18 stories have >=2 Task children |
| G12 | Every User Story has specReference | PASS | 0 missing |
| G13 | No TC parented under security-only story | PASS | All 4 TC parent stories are implementing stories |

**Score: 11/13**

## What Changed vs v5

| Item | v5 | v6 | Delta |
|------|----|----|-------|
| G6 TC Rule A | FAIL (5 violations) | FAIL (5 violations) | Same count, different mix |
| G7 TC Rule B | FAIL (8 violations) | FAIL (7 violations) | -1 violation |
| G10 Migration | FAIL (FAIT v1 missing) | **PASS** | **FIXED** — conditional migration fix worked |
| TC total | 14 | 21 | +7 TCs generated |
| Stories with TCs | 3 | 4 | +1 (get_kb_metadata/get_job_status now covered) |

## Key Item Deep Dives

### G10 — FAIT v1 Migration WI — FIXED

The conditional migration language fix ("Conditional language does NOT exempt a migration from WI generation") worked exactly as intended:

- **Title:** "As a FAIT v1 developer, I want to evaluate KB migration to search_kb tool so that migration scope is determined"
- **wiTemplate:** migration
- **priority:** 4
- **Before/After/Validation:** All present
- **predecessorTitles:** Points to search_kb implementation story

The model correctly generated the WI while noting the conditional nature in the description ("Determine if FAIT v1 should migrate... or if service retirement timeline makes migration unnecessary").

### G6 — TC Rule A (Security Keywords) — Still Failing

5 standard stories with security/behavior keywords in title or ACs have 0 Test Cases:

| Story | Keywords Found | ACs | TCs |
|-------|---------------|-----|-----|
| list_kbs | `auth`, `token`, `entitlement` | 8 | 0 |
| health+CORS | `auth` | 6 | 0 |
| confirm open items (ext dep) | `entitlement` | 6 | 0 |
| kb_entitlements table (FAIT v2 DB) | `entitlement`, `permission`, `enforce` | 8 | 0 |
| teams/team_memberships (FAIT v2 DB) | `validate` | 6 | 0 |

**Comparison to v5:** get_kb_metadata/get_job_status was a v5 failure but is now covered (combined story, 4 TCs). However, health+CORS and teams/team_memberships are new failures not seen in v5.

4 stories that correctly triggered Rule A:
- Token validation (8 TCs) — keywords: auth, token, unauthorized, jwt, bearer
- search_kb (5 TCs) — keywords: token, permission, validate, 403
- add_to_kb (4 TCs) — keywords: token, entitlement, permission, validate, 403
- get_kb_metadata/get_job_status (4 TCs) — keywords: entitlement, validate, async

**Pattern:** The model expanded TC coverage from 3→4 stories (incremental improvement from two-pass architecture), but still applies a severity threshold rather than the mechanical keyword scan. Stories the model perceives as "core API tools" (search, add, metadata) get TCs; stories perceived as "supporting" (list, health, DB schema) don't.

### G7 — TC Rule B (4+ ACs) — Still Failing

7 standard stories with 4+ ACs and 0 TCs:

| Story | ACs | TCs | Also Rule A? |
|-------|-----|-----|-------------|
| MCP server scaffolding | 6 | 0 | No |
| list_kbs | 8 | 0 | Yes |
| health+CORS | 6 | 0 | Yes |
| CloudFlare routing (ext dep) | 5 | 0 | No |
| confirm open items (ext dep) | 6 | 0 | Yes |
| kb_entitlements table | 8 | 0 | Yes |
| teams/team_memberships | 6 | 0 | Yes |

**Improvement vs v5:** Down from 8 violations to 7. The get_kb_metadata/get_job_status combined story now has 4 TCs (was 0 in v5).

### Root Cause Analysis — Why Two-Pass Didn't Fix G6/G7

The two-pass MANDATORY SECOND PASS architecture produced only marginal improvement (+1 story with TCs, +7 TCs total). The model is still applying creative judgment about which stories "deserve" TCs rather than performing the mechanical scan.

**Hypothesis:** The two-pass instruction is being processed as part of the same generation step — the model reads the entire system prompt before generating output, so it doesn't truly separate "generate WIs" from "scan for compliance." The second pass instruction is treated as additional emphasis on existing rules rather than a distinct post-processing step.

**Possible v7 approaches:**
1. **Two-call architecture:** Actually split into two Bedrock calls — first call generates WIs without TCs, second call receives the WI array and adds TCs mechanically. This forces a true second pass.
2. **Invert the approach:** Instead of trying to get the model to generate TCs for ALL qualifying stories, generate TCs in a separate prompt call that receives only the standard stories and applies the rules.
3. **Reduce standard stories:** Reclassify some stories (ext dep WIs, DB schema WIs) to non-standard templates so they don't trigger Rule A/B. This narrows the gap but doesn't fix the underlying problem.
4. **Explicit enumeration in self-check:** Add "For each standard story, output a line: [title] → ACs=[N] → Keywords=[list] → TCs required: YES/NO" forcing the model to explicitly reason about each story before outputting.

## Progress Tracker

| Item | v1 | v2 | v3 | v4 | v5 | v6 |
|------|----|----|----|----|-----|-----|
| G1 Infra wiTemplate | - | - | - | - | PASS | PASS |
| G2 Ext dep tags | PASS | PASS | PASS | - | PASS | PASS |
| G3 All ext owners | PASS | PASS | PASS | - | PASS | PASS |
| G4 No dup ext deps | PASS | PASS | PASS | - | PASS | PASS |
| G5 Open Q consolidation | PASS | PASS | PASS | - | PASS | PASS |
| G6 TC Rule A (keywords) | - | - | - | - | FAIL | **FAIL (marginal improvement)** |
| G7 TC Rule B (4+ ACs) | FAIL | FAIL | FAIL | - | FAIL | **FAIL (6th consecutive)** |
| G8 Separate DB Epic | FAIL | FAIL | FAIL | - | PASS | PASS |
| G9 Prereq schema tracked | FAIL | FAIL | FAIL | - | PASS | PASS |
| G10 Migration WI | PASS | PASS | PASS | - | FAIL | **PASS (FIXED)** |
| G11 >=2 Tasks per story | - | - | - | - | PASS | PASS |
| G12 specReference | - | - | - | - | PASS | PASS |
| G13 No TC under sec-only | PASS | FAIL | PASS | - | PASS | PASS |

## Overall Verdict

**11/13 — NEEDS FURTHER REFINEMENT**

G10 fix (conditional migration language) confirmed working. G6/G7 (TC generation) remain the persistent blockers — the two-pass architecture produced marginal improvement (+1 story, +7 TCs) but did not achieve full compliance. A fundamentally different approach (two-call architecture or inverted TC generation) is likely needed for v7.

## Artifacts
- Script: `pipeline/run_v6_validation.py`
- Output: `pipeline/ADO2581-BEDROCK-OUTPUT.json`
- Model: `us.anthropic.claude-sonnet-4-20250514-v1:0` (32768 max tokens, output-128k beta)
