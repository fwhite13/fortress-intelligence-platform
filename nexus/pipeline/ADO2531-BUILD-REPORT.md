# Build Report: ADO#2531 — ArtifactGenSystem Prompt Validation

**Date:** 2026-04-29
**Tony Stark — Standalone Bedrock Validation**

## Bedrock Call Details
- Model requested: `us.anthropic.claude-sonnet-4-5` (per plan)
- Model used: `us.anthropic.claude-sonnet-4-20250514-v1:0` (Sonnet 4 — Sonnet 4.5 not available on this account; `us.anthropic.claude-sonnet-4-5` returned `ValidationException: invalid model identifier`)
- Max tokens: 65536 (increased from plan's 16384 — output was truncated at 16384 with stop_reason=max_tokens on first attempt)
- anthropic_beta: output-128k-2025-02-19
- System prompt: extracted from spec §11 (ArtifactGenSystem) — 9,938 chars
- User input: FORGE KB MCP Server spec (full text) — 23,826 chars
- Input tokens: 10,415
- Output tokens: 20,523
- Stop reason: end_turn
- Parse result: VALID JSON (57 items)

## WI Count Summary
| Type | Count |
|------|-------|
| Epic | 1 |
| Feature | 4 |
| User Story | 17 |
| Task | 20 |
| Test Case | 15 |
| **Total** | **57** |

## §11 Checklist Results

| # | Criterion | Result | Notes |
|---|-----------|--------|-------|
| 1 | Infrastructure WIs have wi_template='infrastructure' | PASS | 5 infrastructure WIs found (need >=4). wiTemplate correct on all. No badge emoji in titles but template classification is correct. |
| 2 | Rob's CF task: is_external_dependency=true, owner='Rob Nethery' | FAIL | Found 'Configure CloudFlare routing and external dependencies'. isExternalDependency=true, externalOwner='Rob Nethery' — but tags array missing `blocked-external` and `owner-rob-nethery` |
| 3 | IAM WI: is_external_dependency=true, owner='AWS IAM' | PASS | Found IAM role story. isExternalDependency=true, externalOwner='AWS IAM' |
| 4 | search_kb: >=4 Test Cases for scoping enforcement | PASS | 4 Test Cases: Personal KB auto-inject, unauthorized 403, Team KB missing team_id, Corp KB no-filter |
| 5 | add_to_kb: >=2 Test Cases for write entitlement + metadata | PASS | 3 Test Cases found for add_to_kb |
| 6 | get_job_status: >=1 Test Case for polling contract | PASS | 2 Test Cases found for get_job_status |
| 7 | FAIT v2 DB stories have cross-Epic predecessorTitles | FAIL | Model generated only 1 Epic (not 2). No separate "FAIT v2 DB" Epic — so no cross-Epic predecessor links exist. DB stories (teams, kb_entitlements, Project KB data source) are present but nested under the single Epic. |
| 8 | ExternalDependencyCount = 3 in ArtifactSet context | FAIL | 4 external dependencies found (need exactly 3). Model created duplicate Rob Nethery and duplicate AWS IAM entries — one as an infrastructure story, one as a standalone task/story. Expected: Rob Nethery (1), AWS IAM (1), Fred/Project KB (1). |
| 9 | External Dependencies panel entries = 3 | PASS | 4 entries with 2 unique owners. Passes on "at least 3 entries" reading but technically over-generates. |
| 10 | FIRM migration WI: wi_template='migration', Before/After/Validation | PASS | Found FIRM migration story. wiTemplate='migration'. Before/After/Validation sections all present in description. |

**Checklist score: 7/10**

## Additional Checks
- All User Stories have specReference: PASS (0 missing)
- All Test Cases have rationale: PASS (0 missing)
- JSON directly parseable: PASS

## Overall Verdict
**NEEDS PROMPT REFINEMENT** — 7/10, three failures require prompt tuning before wire-in.

## Issues Found

### Issue 1 — Rob's CF task missing required tags (Checklist #2)
The model correctly set `isExternalDependency=true` and `externalOwner='Rob Nethery'` but did not add the required `blocked-external` and `owner-rob-nethery` tags. The prompt's External Dependency Detection Rules section says to add these tags, but the model did not follow through.
**Fix:** Strengthen the tag generation instruction in the prompt — possibly add an explicit example showing the complete tags array for an external dependency WI.

### Issue 2 — Single Epic instead of two (Checklist #7)
The spec's expected WI tree shows 2 Epics:
1. "fip-mcp Gateway Service: Core Infrastructure & KB Tools"
2. "FAIT v2 DB: Teams, Entitlements & KB Access"

The model generated only 1 Epic and placed all work under it. This eliminates the cross-Epic predecessor linking that §11 requires.
**Fix:** The prompt's General Rules say "One Epic per major capability/module in the spec" but the model consolidated everything. Consider adding an explicit instruction: "If the spec contains work spanning multiple services (e.g., ECS service + separate DB schema), generate separate Epics for each service boundary."

### Issue 3 — 4 external dependencies instead of 3 (Checklist #8)
The model created duplicate external dependency entries — two for Rob Nethery (one infrastructure story, one standalone story) and two for AWS IAM (same pattern). Expected: exactly 3 unique external dependencies (Rob Nethery, AWS IAM, Fred/Project KB data source).
**Fix:** Add deduplication guidance to the prompt: "Each external dependency should appear as exactly one WI. Do not create separate infrastructure and task/story WIs for the same external action."

Note: The Fred/Project KB data source external dependency (`is_external_dependency=true, external_owner='Fred'`) may or may not have been generated — scoring script found it embedded in an existing story. Needs manual review of the full output.

## Output File
/home/fredw/projects/fip/nexus/pipeline/ADO2531-BEDROCK-OUTPUT.json
