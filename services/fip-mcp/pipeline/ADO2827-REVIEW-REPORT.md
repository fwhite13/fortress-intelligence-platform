# Review Report — ADO#2827

**Task:** fip-mcp S3 write + metadata sidecar in add_to_kb, fix search_kb metadata keys  
**Commit:** `5457c22`  
**Cycle:** 1 of 2  
**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-07

---

## Verdict: ✅ PASS

No Critical bugs. No security vulnerabilities. All acceptance criteria met. Two advisory findings — neither blocks shipment.

---

## CC Review

**CC invocation:**
```bash
cd /home/fredw/projects/fip/services/fip-mcp && \
CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
claude --model sonnet --print --dangerously-skip-permissions < /tmp/review-2827-brief.md
```

CC surfaced 2 real advisory findings and 1 false positive on team_id empty-string handling (corrected below). All other CC findings verified real or dismissed with justification.

---

## Spec Compliance Check

**Acceptance Criteria:**

- [x] `add_to_kb` Personal KB writes `s3://fortress-tools/kb-docs/personal/{ownerId}/{filename}` + sidecar `{ ownerId: oid }` — ✅ Verified: `scopingId = user.user_id`, key = `${s3_prefix}/${scopingId}/${filename}`, sidecar `{ metadataAttributes: { ownerId: scopingValue } }`
- [x] `add_to_kb` Corp KB writes `s3://fortress-tools/kb-docs/fortress/{filename}` (no double slash, no sidecar) — ✅ Verified: `scopingId = ''` (falsy), ternary takes false branch → `${kb.s3_prefix}/${filename}` = `kb-docs/fortress/{filename}`. No `metadata_key` on Corp KB → no sidecar.
- [x] `add_to_kb` Team KB writes `kb-docs/teams/{teamId}/` + sidecar `{ teamId: team_id }` — ✅ Verified: `scopingId = metadata.team_id`, key = `kb-docs/teams/{teamId}/{filename}`, sidecar `{ metadataAttributes: { teamId: scopingValue } }`
- [x] `search_kb` Personal filter key is `ownerId` — ✅ Verified: `SCOPING_RULE.USER_ID = 'ownerId'`, used directly as Bedrock filter key
- [x] `search_kb` Team filter key is `teamId` — ✅ Verified: `SCOPING_RULE.TEAM_ID = 'teamId'`
- [x] `search_kb` Project filter key is `projectId` — ✅ Verified: `SCOPING_RULE.PROJECT_ID = 'projectId'`
- [x] No hardcoded S3 paths in `add_to_kb.js` — ✅ Verified: all S3 coordinates (`s3_bucket`, `s3_prefix`) come from `kb-inventory.js` via `getKb()`
- [x] `RESERVED_KEYS` blocks `ownerId`, `teamId`, `projectId` from caller override — ✅ Verified
- [x] ESM throughout — ✅ Verified: all `import`/`export`, no `require()` anywhere

---

## Consistency Audit

| Cross-Reference | Result |
|----------------|--------|
| `SCOPING_RULE.USER_ID` (`'ownerId'`) ↔ `metadata_key` in Personal KB (`'ownerId'`) ↔ `RESERVED_KEYS[0]` (`'ownerId'`) | ✅ All match |
| `SCOPING_RULE.TEAM_ID` (`'teamId'`) ↔ `metadata_key` in Team KB (`'teamId'`) ↔ `RESERVED_KEYS[1]` (`'teamId'`) | ✅ All match |
| `SCOPING_RULE.PROJECT_ID` (`'projectId'`) ↔ `metadata_key` in Project KB (`'projectId'`) ↔ `RESERVED_KEYS[2]` (`'projectId'`) | ✅ All match |
| All 9 KB entries have `s3_bucket` + `s3_prefix` | ✅ Verified via runtime import check |
| Corp + NEXUS KBs (`writable: false`) have no `metadata_key` | ✅ Verified — 3 entries (WYSKBKWHPL, AOFDTSHGNT, WHB6WU9CVW) correctly omit `metadata_key` |
| Sidecar format `{ metadataAttributes: { [key]: value } }` matches FAIT v1 KbDocumentService | ✅ Verified |

---

## Issues Found

### Advisory Findings (non-blocking)

#### A1: Missing guard on `user.user_id` for Personal KB — Important (Advisory)
- **File:** `src/tools/add_to_kb.js`
- **Category:** Correctness / data integrity
- **Issue:** For Personal KB, `getScopingId()` returns `user.user_id` without a null check. If auth middleware ever produces a user with falsy/undefined `user_id`:
  1. `scopingId = undefined` → falsy → S3 key becomes `kb-docs/personal/{filename}` (root of prefix, visible to all personal KB users)
  2. Sidecar: `JSON.stringify({ metadataAttributes: { ownerId: undefined } })` → `{"metadataAttributes":{}}` — `ownerId` silently dropped by JSON.stringify
  3. Document lands in wrong path with no ownership metadata → orphaned, potentially visible to all

  Confirmed via: `JSON.stringify({ metadataAttributes: { ownerId: undefined } })` → `{"metadataAttributes":{}}`

- **Severity:** Advisory — auth likely prevents this in practice, but silent data corruption if it slips through is serious
- **Fix:**
  ```diff
  // After the Team KB team_id check, before S3 write
  + if (kb.kb_type === 'personal' && !user.user_id) {
  +   throw { code: 'USER_ID_MISSING', status: 401, message: 'user_id is required for Personal KB write' };
  + }
  ```

#### A2: Millisecond timestamp collision risk — Nitpick (Advisory)
- **File:** `src/tools/add_to_kb.js`
- **Category:** Data integrity
- **Issue:** `Date.now()` ms precision. Two concurrent writes from same user with same `source` value within the same millisecond produce identical S3 key → silent overwrite. Low probability but silent loss.
- **Fix:** `uuidv4` is already imported — append 6 hex chars:
  ```diff
  - const filename = `${safeSource}-${timestamp}.${ext}`;
  + const filename = `${safeSource}-${timestamp}-${uuidv4().slice(0, 6)}.${ext}`;
  ```

---

## Nitpicks (non-blocking, informational)

- **N1:** `RESERVED_KEYS` in `search_kb.js` blocks `teamId` / `projectId` (camelCase) but not `team_id` / `project_id` (snake_case). If a caller passes `filters: { team_id: 'x' }`, the extra filter is applied against a non-existent Bedrock metadata field → empty results but no security bypass (the enforced camelCase security filter still applies). Consider blocking snake_case variants too for cleaner error surface.

---

## False Positives Dismissed

| Issue | Dismissal |
|-------|-----------|
| Corp KB double slash | `scopingId = ''` is falsy → ternary takes false branch → `${kb.s3_prefix}/${filename}` = no double slash ✅ |
| Team KB empty-string team_id bypass | `!''` is `true` → validation throws for empty string. CC's brief analysis in task was incorrect; confirmed via `node -e "console.log(!'')"` = `true` ✅ |
| RESERVED_KEYS snake_case as security bypass | Not a security issue — camelCase security filter still fires; snake_case extra filter hits no metadata field → empty results only ✅ |
| Sidecar format mismatch | `{ metadataAttributes: { [kb.metadata_key]: scopingValue } }` matches FAIT v1 exactly ✅ |
| Dev KB entries missing fields | All 9 entries verified via runtime import — all have `s3_bucket` + `s3_prefix` ✅ |

---

## Positive Observations

- The `metadata_key` → sidecar → `SCOPING_RULE` constants chain is clean and self-consistent. Adding a new KB type requires changes in only one place (kb-inventory.js) and the rest of the system follows.
- Corp/NEXUS 403 guard fires **before** any S3 touch — correct order.
- Team KB `team_id` validation is symmetric with Project KB `project_id` validation.
- ESM throughout, no legacy `require()` patterns.

---

_Reviewed by Hawkeye — commit 5457c22 — 2026-05-07_
