# Prompt Validation Report v3: ADO#2555

**Date:** 2026-04-29
**Prompt source:** nexus-prompt-v3-candidate.md (Full prompt text section)
**Predecessor runs:** v1=7/10 (ADO#2531), v2=3/10 regression (ADO#2543)

## Bedrock Call Details
- Model: us.anthropic.claude-sonnet-4-20250514-v1:0 (cross-region inference profile)
- Max tokens: 32768
- anthropic_beta: output-128k-2025-02-19
- Input tokens used: 11983
- Output tokens used: 30078
- JSON parse: PASS (no fence stripping needed)

## WI Count Summary
| Type | Count |
|------|-------|
| Epic | 2 |
| Feature | 8 |
| User Story | 20 |
| Task | 42 |
| Test Case | 15 |
| **Total** | **87** |

## S11 Checklist Results

| # | Criterion | v1 | v2 | v3 | Notes |
|---|-----------|----|----|----|----|
| 1 | Infrastructure WIs wiTemplate=infrastructure | PASS | PASS | PASS | 4 infra WIs: ECR, IAM, ECS task def, ALB target group |
| 2 | Rob CF: isExternalDep + blocked-external + owner-rob-nethery tags | FAIL | FAIL | PASS | tags=["blocked-external","owner-rob-nethery"] present |
| 3 | IAM WI: isExternalDep + owner-aws-iam | PASS | PASS | PASS | tags=["blocked-external","owner-aws-iam"] present |
| 4 | search_kb: >=4 TCs for scoping | PASS | FAIL | PASS | 4 TCs: Personal KB auto-scope, Team KB membership, invalid KB/entitlement, filter merge |
| 5 | add_to_kb: >=2 TCs for write entitlement | PASS | FAIL | PASS | 4 TCs: write entitlement, no entitlement 403, missing metadata 400, Project KB project_id |
| 6 | get_job_status: >=1 TC for polling | PASS | FAIL | FAIL | 0 TCs. Story has 4 ACs but no auth/scoping keywords triggered TC generation |
| 7 | FAIT v2 DB cross-Epic predecessors | FAIL | FAIL | FAIL | No FAIT v2 DB Epic exists. Model created "FORGE KB Migration" Epic instead |
| 8 | Exactly 3 external deps (no dupes) | FAIL | FAIL | FAIL | 4 found: Rob(1) + IAM(1) + Fred(2). Extra Fred dep for FAIT v2 DB confirmation |
| 9 | External dep entries = 3 | PASS | PASS | FAIL | 4 entries. Two Fred deps instead of one consolidated entry |
| 10 | FIRM migration WI present | PASS | FAIL | PASS | wiTemplate=migration, Before/After/Validation all present |

**Score: 6/10**

## Additional Checks
- Epic count: 2 (expected 2) -- "fip-mcp Gateway Service: Core Infrastructure & KB Tools" + "FORGE KB Migration: Service Integration Updates"
  - NOTE: Epic 2 is WRONG -- expected "FAIT v2 DB: Teams, Entitlements & KB Access", got migrations Epic instead
- All User Stories have specReference: PASS (0 missing)
- All Test Cases have rationale: PASS (0 missing)
- Every User Story has >=2 Tasks: PASS (0 violations)
- JSON parseable without stripping: PASS

## Previously Failing Items -- Detailed Verification

### Item 2 -- Rob CF tags
WI found: "As a DevOps engineer, I want CloudFlare routing configured so that external requests reach the fip-mcp service"
Tags array: ["auto-generated", "needs-review", "spec-section-5", "blocked-external", "owner-rob-nethery"]
isExternalDependency: true
externalOwner: "Rob Nethery"
Result: PASS (fixed in v3)

### Item 7 -- 2 Epics + cross-Epic predecessors
Epics found:
1. "fip-mcp Gateway Service: Core Infrastructure & KB Tools"
2. "FORGE KB Migration: Service Integration Updates"

Expected Epic 2: "FAIT v2 DB: Teams, Entitlements & KB Access" with stories for teams/team_memberships tables and kb_entitlements table, carrying predecessorTitles referencing forge-kb tool group stories.

Actual Epic 2: Migration Epic containing FIRM, NEXUS, and FAIT v1 migration stories. These migration stories DO have cross-Epic predecessors (pointing to add_to_kb and search_kb stories in Epic 1), but this is the wrong Epic structure.

FAIT v2 DB work (teams table, team_memberships table, kb_entitlements table) is NOT present as separate stories in a dedicated Epic. Instead, entitlement work is folded into Epic 1 under "Implement KB Entitlement System with Fallback Configuration" feature. Two Fred external dep stories exist for DB confirmation but no actual schema creation stories.

Root cause: The prompt's Epic boundary rule says "schema changes to a SEPARATE EXISTING application's database ALWAYS get their own Epic." The model correctly created 2 Epics but chose migrations (which are also separate codebases) rather than the FAIT v2 DB schema work as the second Epic. The FAIT v2 DB schema work (teams, team_memberships, kb_entitlements tables) was absorbed into Epic 1 as part of the entitlement system, rather than recognized as changes to a separate existing app's database.

Result: FAIL (3rd consecutive failure)

### Item 8 -- External dep count and deduplication
All isExternalDependency=true WIs:
1. "As a platform deployer, I want IAM role with Bedrock permissions so that fip-mcp can access AWS Bedrock services" | owner=AWS IAM | tags=["blocked-external","owner-aws-iam"]
2. "As a DevOps engineer, I want CloudFlare routing configured so that external requests reach the fip-mcp service" | owner=Rob Nethery | tags=["blocked-external","owner-rob-nethery"]
3. "As an admin, I want to confirm FAIT v2 database requirements so that entitlement tables can be created" | owner=Fred | tags=["blocked-external","owner-fred"]
4. "As an admin, I want to confirm Project KB data source ID so that Project KB operations can work" | owner=Fred | tags=["blocked-external","owner-fred"]

Count: 4 (expected 3)

Root cause: The prompt's Fred signals fire on both Open Question #1 (FAIT v2 DB confirmation) and Open Question #6 (Project KB data source ID). The dedup rule says "same owner performing multiple instances of the same class of action" should be consolidated. The model treated these as different classes of action (DB requirements vs data source ID confirmation) and generated separate WIs. The expected behavior is one consolidated Fred external dep WI covering both open questions, OR only the Project KB data source ID one (per the spec's expected output).

Result: FAIL (3rd consecutive failure)

### Item 9 -- External dep entries
4 entries found (expected 3). See item 8 above.
Result: FAIL (new regression from v1/v2 which passed)

### Items 4/5/6 -- Test Case parenting (v2 regression)

**search_kb TCs (4 -- PASS):**
All parented under: "As a FAIT agent, I want search_kb MCP tool with auto-scoping so that I can retrieve KB content securely"
- TC: Given Personal KB access, when searching, then user_id auto-injected from token
- TC: Given Team KB access, when searching with team_id, then membership validated
- TC: Given invalid KB or no entitlement, when searching, then proper error returned
- TC: Given caller filters, when security filters exist, then both merged with AND

**add_to_kb TCs (4 -- PASS):**
All parented under: "As a content creator, I want add_to_kb MCP tool so that I can ingest content into KBs asynchronously"
- TC: Given write entitlement, when adding content with metadata, then job_id returned
- TC: Given no write entitlement, when adding content, then 403 WRITE_NOT_ENTITLED
- TC: Given missing required metadata, when adding content, then 400 METADATA_REQUIRED
- TC: Given Project KB without project_id, when adding, then 400 PROJECT_ID_REQUIRED

**get_job_status TCs (0 -- FAIL):**
Parent story: "As a client application, I want get_job_status MCP tool so that I can poll async operation progress"
The story has 4 acceptance criteria and wiTemplate=standard, but none of the auth/scoping trigger keywords appear in its title or AC text. The TC generation rule requires EITHER auth keywords OR >=4 ACs. The story has 4 ACs but they are phrased neutrally ("valid job_id", "completed job", "failed job", "invalid job_id") without any auth/scoping language, so the >=4 AC rule should have triggered but did not.

Root cause: The model may have counted the 4 ACs as a list but decided the story was not security-critical enough to warrant TCs. The prompt's TC rule says "has 4 or more distinct acceptance criteria items" should trigger TC generation regardless of auth keywords -- this condition was met but not acted on.

### Item 10 -- FIRM migration WI
WI found: "As FIRM transcriber, I want to migrate from bedrock-agent-runtime to add_to_kb so that KB ingestion is centralized"
wiTemplate: migration
predecessorTitles: ["As a content creator, I want add_to_kb MCP tool so that I can ingest content into KBs asynchronously"]
Before section present: YES
After section present: YES
Validation section present: YES
Result: PASS (fixed in v3)

## Overall Verdict
NEEDS FURTHER REFINEMENT

## Remaining Issues (4 items failing)

### Issue 1: get_job_status TCs (item 6)
**Found:** 0 TCs. **Expected:** >=1 TC.
**Root cause:** Model did not fire TC generation despite 4 ACs on a standard story. The prompt's rule 2b ("4 or more distinct acceptance criteria") should have triggered but was ignored.
**Fix hypothesis:** Add get_job_status as an explicit example in the TC generation section, or add "polling" / "contract" to the auth/scoping keyword trigger list since the spec calls it "the FIP MCP async contract."

### Issue 2: No FAIT v2 DB Epic (item 7)
**Found:** Epic 2 = "FORGE KB Migration" (3 migration stories). **Expected:** Epic 2 = "FAIT v2 DB: Teams, Entitlements & KB Access" (teams table, kb_entitlements table, Project KB confirmation).
**Root cause:** The model recognized migrations as cross-codebase work (FIRM, NEXUS, FAIT v1 are separate services) and created a migration Epic. It did NOT recognize FAIT v2 DB schema work as belonging to a separate existing app's database. The entitlement/teams work was absorbed into Epic 1.
**Fix hypothesis:** Add a specific signal rule: "If the spec references tables in an existing application's DB (e.g., 'FAIT v2 DB', 'team_memberships table') that are NOT part of the new service being built, those MUST go into a separate Epic named after the existing app's DB, not under the new service Epic." Also consider adding an explicit anti-pattern: "Migration WIs (future work in other codebases) belong under the service Epic they depend on, NOT in a separate migration Epic."

### Issue 3: External dep count (items 8 + 9)
**Found:** 4 external deps (2x Fred). **Expected:** 3 (Rob + IAM + Fred consolidated).
**Root cause:** Two separate Fred open questions (FAIT v2 DB confirmation + Project KB data source ID) each generated their own WI. The dedup rule should have consolidated them.
**Fix hypothesis:** Strengthen the Fred dedup rule: "Multiple open questions assigned to Fred → consolidate into ONE external dependency WI listing all items Fred must confirm. Title: 'Confirm open questions with Fred' or equivalent."
