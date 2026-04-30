# Prompt Validation Report v4: ADO#2558

**Date:** 2026-04-29
**Prompt source:** nexus-prompt-v4-candidate.md
**Run history:** v1=7/10 (ADO#2531), v2=3/10 (ADO#2543), v3=6/10 (ADO#2555)

## Bedrock Call Details
- Model: us.anthropic.claude-sonnet-4-20250514-v1:0 (primary, no fallback)
- Max tokens: 32768
- anthropic_beta: output-128k-2025-02-19
- Input tokens: 12610
- Output tokens: 26541
- JSON parse: PASS
- Read timeout: 300s (default 60s caused timeout on first attempt)

## WI Count Summary
| Type | Count |
|------|-------|
| Epic | 1 |
| Feature | 7 |
| User Story | 17 |
| Task | 35 |
| Test Case | 12 |
| **Total** | **72** |

## S11 Checklist Results

| # | Criterion | v1 | v2 | v3 | v4 | Notes |
|---|-----------|----|----|----|----|-------|
| 1 | Infra WIs: wiTemplate=infrastructure | PASS | PASS | PASS | PASS | 4 infra WIs (ECR, IAM, ECS, ALB) |
| 2 | Rob CF: isExtDep + blocked-external + owner-rob-nethery | FAIL | FAIL | PASS | PASS | Correct tags and owner |
| 3 | IAM: isExtDep + owner-aws-iam | PASS | PASS | PASS | PASS | Correct tags and owner |
| 4 | search_kb: >=4 TCs scoping | PASS | FAIL | PASS | PASS | 4 TCs (Personal, Team, unknown kb_id, unauthorized) |
| 5 | add_to_kb: >=2 TCs write entitlement | PASS | FAIL | PASS | PASS | 4 TCs (write entitlement, metadata, project_id, valid request) |
| 6 | get_job_status: >=1 TC polling | PASS | FAIL | FAIL | **FAIL** | **KEY TARGET** 0 TCs despite 6 ACs + polling/async keywords |
| 7 | FAIT v2 DB cross-Epic predecessors | FAIL | FAIL | FAIL | **FAIL** | **KEY TARGET** Only 1 Epic; no FAIT v2 DB Epic generated |
| 8 | Exactly 3 external deps (no dupes) | FAIL | FAIL | FAIL | **PASS** | **KEY TARGET** First pass ever! 3 exact: Rob, AWS IAM, Fred |
| 9 | External dep entries = 3 distinct | PASS | PASS | FAIL | PASS | Rob Nethery, AWS IAM, Fred |
| 10 | FIRM migration: wiTemplate=migration + Before/After/Validation | PASS | FAIL | PASS | PASS | 3 migration WIs (FIRM, NEXUS, FAIT v1) all correct shape |

**Score: 8/10**

## Additional Checks
- Epic count: 1 — "FORGE KB MCP Gateway Service: Core Infrastructure & KB Tools"
- All User Stories have specReference: PASS (all 17 have non-null specReference)
- All Test Cases have rationale: PASS (all 12 have non-null rationale citing spec sections)
- Every User Story has >=2 Tasks: PASS (all stories have 2+ tasks)

## Key Target Items -- Detailed Verification

### Item 6 -- get_job_status TCs
Story title: "As a FIP application, I want to poll async job status universally so that I can track progress of KB ingestion and future operations"
Story AC count: 6 (validates format, routes KB ingestion, returns status, includes result, includes error, percent_complete)
Story wiTemplate: standard
TCs under this story: **0**
testedByTitles on story: null

Also checked other 4+ AC stories with 0 TCs:
- "As a developer, I want to implement the HTTP/SSE MCP server foundation..." — 6 ACs, 0 TCs
- "As a web developer, I want to configure CORS headers..." — 7 ACs, 0 TCs
- "As a FIP application, I want to discover available Knowledge Bases..." — 6 ACs, 0 TCs
- "As a FIP administrator, I want to inspect Knowledge Base metadata..." — 6 ACs, 0 TCs

Result: **FAIL**
Root cause: The model is still only generating TCs for stories matching rule 2a keywords (auth/token/entitlement/scope/validate etc.) in the story's own content. The rule 2b "4+ ACs = unconditional TCs" is still being ignored for stories that don't match 2a. The "UNCONDITIONAL" label and counter-example were insufficient — the model treats rules 2a and 2b as a single evaluation block where 2a acts as a gate. The get_job_status story title contains "async" and "polling" (added to 2a keyword list in v4) but the model still didn't trigger. This suggests the keyword matching is happening against the story's security-related content, not its title.

### Item 7 -- Epic structure and cross-Epic predecessors
Epic 1 title: "FORGE KB MCP Gateway Service: Core Infrastructure & KB Tools"
Epic 2: **does not exist**
Is there a FAIT v2 DB Epic? **NO**

The model collapsed everything into a single Epic. The FAIT v2 DB schema work (teams, team_memberships, kb_entitlements tables) is referenced in descriptions of existing stories but not broken out as separate stories under a second Epic. Migration WIs are placed directly under the single Epic (correct placement per prompt rules), but the DB schema stories that the prompt's Epic boundary rules should have triggered are completely absent.

Result: **FAIL**
Root cause: The prompt's FAIT v2 DB recognition rule fires on "schema additions to a SEPARATE EXISTING application's database," but the spec describes these tables as "FAIT v2 DB — not yet created." The model likely interpreted this as future work that doesn't warrant WIs in this decomposition, since the spec's Section 8 frames them as pending prerequisites ("FAIT v2 DB must exist before kb_entitlements table can be created"). The negative signal about migrations ("do NOT create a migration Epic") may have also over-corrected — the model may be suppressing any second Epic to avoid the v2 anti-pattern.

### Item 8 -- External dependency count
All isExternalDependency=true WIs:
1. "As a platform engineer, I want to create the IAM task execution role..." | externalOwner="AWS IAM" | tags=["blocked-external", "owner-aws-iam"]
2. "Confirm open questions with Fred" | externalOwner="Fred" | tags=["blocked-external", "owner-fred"]
3. "Configure CloudFlare routing for /mcp paths with SSE support" | externalOwner="Rob Nethery" | tags=["blocked-external", "owner-rob-nethery"]

Total count: 3 (expected: 3)
Result: **PASS** -- Fred dedup rule worked perfectly. Single consolidated WI lists 8 items for Fred to confirm.

## Overall Verdict
**NEEDS FURTHER REFINEMENT** -- 8/10, up from 6/10. Item 8 (Fred dedup) is fixed. Items 6 and 7 remain.

## Remaining Issues

### Item 6 — get_job_status TCs (4th consecutive failure)
The v4 prompt changes (added keywords, UNCONDITIONAL label, counter-example) did not work. The model is treating the entire TC generation block as gated by rule 2a keyword presence in the story's security/scoping context, not as two independent OR triggers.

**Suggested v5 fix:** Restructure the TC rules to make 2a and 2b completely separate evaluation passes. Instead of listing them as sub-items under "Any one of these is true," make them two separate top-level rules with their own headers:

```
## TEST CASE RULE A — SECURITY KEYWORD TRIGGER
[current rule 2a content]

## TEST CASE RULE B — HIGH-AC-COUNT TRIGGER (UNCONDITIONAL)
If a standard User Story has 4 or more distinct acceptance criteria items,
it MUST have at least 1 Test Case child. This rule has NO preconditions.
It does not require security keywords. It fires on AC count alone.
[counter-examples]
```

Also consider adding a structured self-check step: "For EVERY standard User Story, count its ACs. If count >= 4 and Test Cases = 0, you have violated Rule B. Go back and add Test Cases."

### Item 7 — FAIT v2 DB Epic (4th consecutive failure)
The prompt's recognition rule depends on the spec explicitly describing schema work to a separate existing app. The FORGE KB spec frames the FAIT v2 DB work as prerequisites/open questions (Section 8), not as deliverable WIs in this spec. The model correctly identified that Section 8 items are unresolved prerequisites, not work to decompose.

**Suggested v5 fix:** Add an explicit rule:
```
## PREREQUISITE SCHEMA WORK = SEPARATE EPIC
If the spec's prerequisites or open questions reference schema changes to a separate
existing application's database (e.g., tables that must exist before the new service
works), generate those schema changes as User Stories under a separate Epic for that
application. The fact that they are listed as prerequisites does NOT mean they should
be omitted — it means they are BLOCKING work that must be tracked.
```

Also: the WRONG/RIGHT example should include a "WRONG (v3): Single Epic with DB work omitted because it's listed as prerequisites" entry.
