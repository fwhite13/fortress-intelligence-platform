# Build Report — ADO#2827

**Task:** fip-mcp: S3 write + metadata sidecar in add_to_kb, fix search_kb filter keys  
**Commit:** `5457c22`  
**Branch:** `main`  
**Date:** 2026-05-07  
**Engineer:** Tony Stark (software-engineer)

---

## What Was Built

Two correctness fixes required for fip-mcp KB operations to function end-to-end:

1. **add_to_kb.js**: Implemented S3 content write + metadata sidecar before calling `StartIngestionJob`. Previously the ingestion job fired with no data in S3 — content never entered the KB.

2. **search_kb.js + kb-inventory.js**: Fixed metadata filter keys from snake_case (`user_id`, `team_id`, `project_id`) to camelCase (`ownerId`, `teamId`, `projectId`) to match what FAIT v1 writes in `.metadata.json` sidecars. Search was returning 0 results due to the key mismatch.

---

## Files Changed

- `src/config/kb-inventory.js` — SCOPING_RULE constants updated; S3 config fields added to all 9 KB entries; `A5U1GKN0TS` data_source_id populated
- `src/tools/add_to_kb.js` — S3Client import + client; `getScopingId()` and `getExtension()` helpers; S3 write (content + conditional sidecar) before `StartIngestionJob`; Team KB `team_id` validation added
- `src/tools/search_kb.js` — Filter keys in `buildRetrievalFilter` now use `SCOPING_RULE.*` constants (resolving to correct camelCase); `RESERVED_KEYS` updated

---

## Parallelization

None — all changes had dependencies (kb-inventory.js changes are prerequisites for both add_to_kb.js and search_kb.js).

---

## CC Sessions

1 CC run (sonnet) — all 3 files in a single session

---

## Acceptance Criteria Verification

- [x] **add_to_kb Personal KB**: writes `s3://fortress-tools/kb-docs/personal/{ownerId}/{filename}` + sidecar `{ ownerId: user.user_id }` — implemented, config-driven from inventory
- [x] **add_to_kb Personal Dev KB**: writes `s3://fortress-tools/kb-docs/dev/personal/{ownerId}/{filename}` — dev KB carries its own `s3_prefix`
- [x] **add_to_kb Team KB**: writes `kb-docs/teams/{teamId}/` + sidecar `{ teamId: team_id }` — `metadata.team_id` validated before write
- [x] **add_to_kb Corp KB**: writes `kb-docs/fortress/` with no sidecar — `kb.metadata_key` is undefined for Corp, sidecar block skipped
- [x] **search_kb Personal KB**: filter key is `ownerId` — `SCOPING_RULE.USER_ID === 'ownerId'`
- [x] **search_kb Team KB**: filter key is `teamId` — `SCOPING_RULE.TEAM_ID === 'teamId'`
- [x] **search_kb Project KB**: filter key is `projectId` — `SCOPING_RULE.PROJECT_ID === 'projectId'`
- [x] **No hardcoded S3 paths in add_to_kb.js** — all S3 config (`s3_bucket`, `s3_prefix`, `metadata_key`) read from `kb` inventory object

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **S3 key for Corp KB**: `scoping_id` is empty string → `s3Key = kb-docs/fortress/{filename}` (no trailing slash issue — the ternary checks `scopingId` truthiness). Verify this is correct for Corp structure.

2. **NEXUS KB**: marked `writable: false` — the write path will still 403 before reaching S3 via the `forge-kb-admin` role check. `kb.metadata_key` is undefined for NEXUS so no sidecar would be written either. This is consistent.

3. **`metadata.team_id` vs `callerFilters.team_id`**: `add_to_kb` takes `team_id` from `metadata.team_id` (new validation added). `search_kb` takes `team_id` from `callerFilters.team_id`. These are different call signatures — intentional per the tool design.

4. **Project KB `data_source_id`**: was `null`, now `'QAP3QMUD5N'` per WI spec. This unblocks Project KB writes that previously would have thrown `DATA_SOURCE_UNAVAILABLE`.

5. **File naming**: `safeSource` strips non-alphanumeric chars to prevent S3 key injection. Extension defaults to `.txt` unless `metadata.content_type` is provided.

---

## How to Test Locally

```bash
# 1. Start fip-mcp dev server
cd /home/fredw/projects/fip/services/fip-mcp
npm run dev

# 2. Test add_to_kb to Personal Dev KB (PBKCTCPNUU)
# Requires valid Entra token with user_id
# Expected: S3 object at kb-docs/dev/personal/{oid}/test-{ts}.txt
#           S3 sidecar at ...txt.metadata.json with { ownerId: oid }
#           StartIngestionJob called, job_id returned

# 3. Test search_kb on Personal Dev KB
# Expected: filter key = 'ownerId' in Bedrock retrieve call
# Previously would use 'user_id' → 0 results
```

Natasha QA end-to-end: add_to_kb → get_job_status → search_kb on Personal Dev KB.

---

## Build Status

✅ **SUCCEEDED** — commit `5457c22`, preflight passed, 3 files, 107 insertions / 13 deletions
