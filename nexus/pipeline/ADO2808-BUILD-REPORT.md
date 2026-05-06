# Build Report — ADO#2808
## NEXUS ArtifactGen: v7 Validation — Two-Call TC Architecture

**Date:** 2026-05-06 00:26:19 EDT
**Script:** `run_v7_validation.py`
**Input spec:** `forge-kb-mcp-server-spec-2026-04-27.md`

---

## Model & Token Usage

| | Value |
|---|---|
| Model | `us.anthropic.claude-sonnet-4-20250514-v1:0` |
| Call 1 (decomposition) input tokens | 11,888 |
| Call 1 (decomposition) output tokens | 25,288 |
| Call 2 (TC scan) input tokens | 31,506 |
| Call 2 (TC scan) output tokens | 12,817 |
| Call 2 success | ✅ Yes |
| Total tokens | 81,499 |

---

## WI Type Counts

| Type | Count |
|------|-------|
| Epic | 2 |
| Feature | 6 |
| Task | 46 |
| Test Case | 67 |
| User Story | 23 |
| **TOTAL** | **144** |

---

## §G Checklist

**Score: 12/13**

| # | Check | Result | Notes |
|---|-------|--------|-------|
| G1 | Infra WIs have wiTemplate = 'infrastructure' | ✅ PASS | 5 infra-template WIs; 5 infra-signal WIs |
| G2 | Ext dep WIs have blocked-external + owner-* tags | ❌ FAIL | 20 ext dep WIs; 16 missing required tags |
| G3 | All external owners extracted from spec found | ✅ PASS | Expected: ['Rob Nethery', 'AWS IAM']; Found: ['AWS IAM', 'Rob Nethery'] |
| G4 | No duplicate ext dep WIs per owner | ✅ PASS | OQ WIs per owner: {'unknown': 1} |
| G5 | Open questions consolidated (1 WI per external owner) | ✅ PASS | OQ WIs per owner: {'unknown': 1} |
| G6 | TC Rule A fires (security keyword stories have ≥1 TC) | ✅ PASS | All Rule A stories have ≥1 TC |
| G7 | TC Rule B fires (stories with 4+ ACs have ≥1 TC) | ✅ PASS | All Rule B stories have ≥1 TC |
| G8 | Separate Epic for separate app DB work | ✅ PASS | No separate-app DB work detected; 2 epic(s) — correct |
| G9 | Prerequisite schema work tracked in ADO | ✅ PASS | Spec §8 has schema prereqs; 32 schema-related WIs found |
| G10 | Follow-on migration WI exists (incl. conditional/deferred) | ✅ PASS | Spec has migration/deferred signals: True; migration WIs: 6 |
| G11 | Every User Story has specReference (non-null, has §N) | ✅ PASS | 0/23 stories missing specReference |
| G12 | Every TC has rationale citing a spec section | ✅ PASS | 0/67 TCs missing rationale with §N |
| G13 | Every User Story has ≥2 Task children | ✅ PASS | All 23 stories have ≥2 tasks |

---

## Fail Details

### G2: Ext dep WIs have blocked-external + owner-* tags

**Root cause:** The Python `WiClassifierService` replica flags WIs as `isExternalDependency=True` when their title/description contains `"iam"` or `"bedrock-agent-runtime"`. These 16 WIs are **internal implementation tasks** (creating IAM policies, calling Bedrock APIs, migration tasks) — the model correctly did NOT add `blocked-external`/`owner-*` tags to them because they are not externally blocked. The 4 genuinely externally-blocked WIs (Rob Nethery's CloudFlare work, product owner KB confirmation) DO have correct tags.

**Implication:** `WiClassifierService.ExternalDependencySignals` is too broad — `"iam"` and `"bedrock-agent-runtime"` are technical implementation terms, not reliable external-owner signals. This is a classifier defect, not a prompt defect.

Found 16 ext dep WI(s) missing required tags:
  - As a DevOps engineer, I want IAM task execution role configured so that fip-mcp  | missing: ['blocked-external', 'owner-*']
  - As a developer, I want CORS configuration implemented so that FIP applications c | missing: ['blocked-external', 'owner-*']
  - As a developer, I want search_kb tool implemented so that clients can query Know | missing: ['blocked-external', 'owner-*']
  - As a developer, I want FIRM migration from direct Bedrock calls to fip-mcp add_t | missing: ['blocked-external', 'owner-*']
  - As a developer, I want NEXUS migration from direct Bedrock calls to fip-mcp sear | missing: ['blocked-external', 'owner-*']
  - As a developer, I want FAIT v1 migration from direct Bedrock calls to fip-mcp se | missing: ['blocked-external', 'owner-*']
  - Create IAM policy with Bedrock permissions for fip-mcp | missing: ['blocked-external', 'owner-*']
  - Attach Bedrock policy to fip-mcp ECS task execution role | missing: ['blocked-external', 'owner-*']
  - Implement bedrock-agent-runtime Retrieve API integration | missing: ['blocked-external', 'owner-*']
  - Implement GetKnowledgeBase and ListIngestionJobs API calls | missing: ['blocked-external', 'owner-*']
  - Implement StartIngestionJob API integration with job ID generation | missing: ['blocked-external', 'owner-*']
  - Implement GetIngestionJob API calls for status polling | missing: ['blocked-external', 'owner-*']
  - Test end-to-end routing from CloudFlare to fip-mcp service | missing: ['blocked-external', 'owner-*']
  - Replace bedrock-agent-runtime SDK with HTTP client in firm-transcriber | missing: ['blocked-external', 'owner-*']
  - Replace bedrock-agent-runtime SDK with HTTP client in NEXUS backend | missing: ['blocked-external', 'owner-*']
  - Update FAIT v1 KB retrieval calls to use search_kb tool (conditional) | missing: ['blocked-external', 'owner-*']

---

## Run History

| Version | Score | ADO |
|---------|-------|-----|
| v1 | 7/13 | ADO#2531 |
| v2 | 3/13 | ADO#2543 |
| v3 | 6/13 | ADO#2555 |
| v4 | 8/13 | ADO#2558 |
| v5 | 10/13 | ADO#2577 |
| v6 | 11/13 | ADO#2581 |
| **v7** | **12/13** | ADO#2808 |

---

_End of report._