# Prompt Validation Report v2: ADO#2543

**Date:** 2026-04-29
**Predecessor:** ADO#2531 -- 7/10
**Patched items:** Tags fix (item 2), 2-Epic fix (item 7), dedup fix (item 8)

## Bedrock Call Details

| Property | Value |
|----------|-------|
| Model | `us.anthropic.claude-sonnet-4-20250514-v1:0` |
| Beta header | `output-128k-2025-02-19` |
| Max tokens | 32768 |
| Input tokens | 10,725 |
| Output tokens | 13,317 |
| Stop reason | `end_turn` |
| JSON parseable | Yes -- no preamble, clean array |
| Output file | `pipeline/nexus-prompt-validation-output-v2.json` |

## WI Count Summary

| Type | v1 Count | v2 Count |
|------|----------|----------|
| Epic | 1 | 1 |
| Feature | 6 | 5 |
| User Story | 19 | 16 |
| Task | 20 | 0 |
| Test Case | 11 | 8 |
| **Total** | **57** | **30** |

**Notable:** v2 produced 0 Tasks (v1 had 20). The model collapsed Task-level detail into User Story descriptions. Total WI count dropped from 57 to 30.

## S11 Checklist Results

| # | Criterion | v1 Result | v2 Result | Notes |
|---|-----------|-----------|-----------|-------|
| 1 | Infrastructure WIs carry `wiTemplate=infrastructure` | PASS | PASS | 4 infra WIs found (ECR, ECS, ALB, kb_entitlements) |
| 2 | Rob CF task: `blocked-external` + `owner-rob-nethery` tags | **FAIL** | **PASS** | Tags array: `[auto-generated, needs-review, spec-section-5, blocked-external, owner-rob-nethery]` |
| 3 | IAM WI: `isExternalDependency=true`, `externalOwner=AWS IAM` | PASS | PASS | Present and correctly tagged |
| 4 | search_kb story: >=4 Test Cases for scoping | PASS | **FAIL** | 0 TCs under search_kb story. 4 scoping TCs exist but parented under "KB auto-scoping filters enforced" story |
| 5 | add_to_kb story: >=2 Test Cases | PASS | **FAIL** | 0 TCs under add_to_kb story |
| 6 | get_job_status: >=1 Test Case | PASS | **FAIL** | 0 TCs under get_job_status story |
| 7 | 2 Epics + cross-Epic predecessors | **FAIL** | **FAIL** | Still 1 Epic. No FAIT v2 DB Epic. No cross-Epic predecessorTitles |
| 8 | Exactly 3 external deps | **FAIL** | **FAIL** | 2 found (Rob Nethery, AWS IAM). Missing: Fred/Project KB data source |
| 9 | 3 distinct external dep entries | PASS | **FAIL** | Only 2 distinct owners |
| 10 | FIRM migration WI: `wiTemplate=migration` + Before/After/Validation | PASS | **FAIL** | No migration-template WI found. add_to_kb story mentions FIRM but uses `wiTemplate=standard` |

## Previously Failing Items -- Detailed Verification

### Item 2 -- Rob CF tags (FIXED)

**v1:** Tags array missing `blocked-external` and `owner-rob-nethery`.
**v2:** Now correct.

```json
{
  "title": "As Rob Nethery, I need CloudFlare route configuration for api.fortressam.ai/mcp/* so that MCP clients can reach the service through the proxy",
  "isExternalDependency": true,
  "externalOwner": "Rob Nethery",
  "tags": ["auto-generated", "needs-review", "spec-section-5", "blocked-external", "owner-rob-nethery"]
}
```

**Verdict:** PASS

### Item 7 -- 2 Epics + cross-Epic predecessors (STILL FAILING)

**Expected Epics:**
1. "fip-mcp Gateway Service: Core Infrastructure & KB Tools"
2. "FAIT v2 DB: Teams, Entitlements & KB Access"

**Actual Epics:**
1. "FORGE KB MCP Server: Unified Bedrock KB Gateway" (only one)

The model collapsed everything into a single Epic. No FAIT v2 DB stories with cross-Epic `predecessorTitles`. The kb_entitlements table is present but lives under the same Epic as infrastructure.

Only 2 WIs have `predecessorTitles` at all:
- ECS service story -> depends on ECR repo story (same Epic, infra sequencing)
- ALB target group story -> depends on ECS service story (same Epic, infra sequencing)

**Verdict:** FAIL -- prompt Epic boundary rules not strong enough to force service-boundary splits

### Item 8 -- External dep count (STILL FAILING)

**Expected:** 3 external deps (Rob Nethery, AWS IAM, Fred/Project KB)
**Actual:** 2 external deps

| # | Title | externalOwner | Tags |
|---|-------|---------------|------|
| 1 | IAM permissions for task execution role | AWS IAM | blocked-external, owner-aws-iam |
| 2 | CloudFlare route configuration | Rob Nethery | blocked-external, owner-rob-nethery |

**Missing:** Fred -- Confirm Project KB data source ID (A5U1GKN0TS). The spec mentions this in Section 8 (Open Questions #6) but the model did not generate a WI for it.

**Verdict:** FAIL -- prompt does not have a signal pattern for "confirm or create data source" -> Fred as external owner

## New Regressions (items that PASSED in v1, FAIL in v2)

| # | Item | Analysis |
|---|------|----------|
| 4 | search_kb TCs | v2 generated 4 scoping TCs but parented them under a separate "KB auto-scoping" story instead of the search_kb story. Structural mismatch, not a content gap. |
| 5 | add_to_kb TCs | v2 generated 3 auth TCs (write entitlement, metadata required) but parented under auth story. |
| 6 | get_job_status TCs | v2 generated 0 TCs for job status polling. |
| 9 | 3 distinct owners | Follows from item 8 -- only 2 external deps found. |
| 10 | FIRM migration | v2 did not classify any WI as `wiTemplate=migration`. The FIRM->add_to_kb migration is mentioned in description text but not given its own WI with migration template. |

## Additional Checks

| Check | Result |
|-------|--------|
| 2 Epics in output | FAIL -- 1 Epic |
| All User Stories have non-null `specReference` | PASS -- 16/16 |
| All Test Cases have non-null `rationale` | PASS -- 8/8 |
| JSON parseable with no preamble | PASS |

## Root Cause Analysis

The v2 output regressed in 4 areas relative to v1:

1. **Test Case parenting** -- The model created auth/security-focused stories and put TCs there, rather than putting TCs under the tool stories (search_kb, add_to_kb, get_job_status). The prompt's Test Case rules say "Generate separate Test Case WIs for a User Story ONLY if... the story enforces auth/scoping..." -- the model interpreted this by creating dedicated auth stories and putting all TCs there, rather than annotating the tool stories with auth TCs.

2. **Epic boundaries** -- The prompt's Epic rule says "Multiple distinct services (separate repos, separate deployments, separate DB schemas) -> one Epic per service." The model treated FAIT v2 DB work as part of the same Epic since it's supporting infrastructure for fip-mcp. The rule needs to be more explicit: "schema changes to an existing separate app's DB always warrant a separate Epic."

3. **Fred external dep** -- The prompt's external owner signal list doesn't include a pattern for "data source ID confirmation" or "Fred" as an external owner. The spec's Open Questions section mentions Fred but the signal list in the prompt doesn't match.

4. **Migration template** -- The FIRM migration is described in Section 7 as a follow-on action, not an immediate WI. The model didn't generate a migration WI because the spec says "Migration is NOT part of the initial fip-mcp build." The prompt may need to override this and generate migration WIs for flagged follow-on work.

5. **Tasks missing entirely** -- v2 produced 0 Task WIs (v1 had 20). This is a significant structural regression.

**Checklist score: 3/10**

## Overall Verdict

**NEEDS FURTHER REFINEMENT**

The v2 prompt patch fixed item 2 (Rob CF tags) but introduced regressions in items 4, 5, 6, 9, 10. Items 7 and 8 remain unfixed. The prompt needs additional work on:
- Explicit Epic boundary examples in the prompt (not just rules)
- Test Case parenting clarification (TCs go under the tool story, not a separate auth story)
- External owner signal expansion (add "data source", "confirm", "Fred" patterns)
- Migration WI generation for flagged follow-on work sections
- Task generation enforcement (the prompt schema includes Tasks but the model skipped them entirely)
