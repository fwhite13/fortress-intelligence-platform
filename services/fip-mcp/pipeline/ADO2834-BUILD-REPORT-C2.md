# Build Report — ADO#2834 Cycle 2

**WI:** ADO#2834 — list_kb_files personal KB prefix: Entra OID → FAIT GUID
**Commit:** `ee21c6d9c0f803b7fb9dbbada28e00123e628246`
**Build result:** SUCCEEDED
**Agent:** Tony Stark — BUILD cycle 2

---

## What was built

Fixed the root cause of `list_kb_files` returning 0 files for personal KB: the tool was using the
Entra OID (`user.user_id`) as the S3 prefix, but FAIT stores files under `kb-docs/personal/{AppUser.Id}/`
(FAIT internal GUID). These are different identifiers.

Solution: new `fait-user-resolver.js` utility calls `/api/firm/resolve-user` on FAIT to exchange the
Entra OID for the FAIT internal GUID. `FirmIntegrationController.ResolveUser` was upgraded to do a
proper `EntraOid` DB lookup (previously it was ignoring the OID and returning the most recent Entra user).

---

## Files changed

| File | Change |
|------|--------|
| `fait/src/FortressAI.Web/Controllers/FirmIntegrationController.cs` | `ResolveUser` method: replaced flawed fallback with `WHERE EntraOid == entraOid` exact match; single-Entra-user fallback only when no exact match exists |
| `services/fip-mcp/src/utils/fait-user-resolver.js` | **NEW** — `getFaitUserId(entraOid)` calls `/api/firm/resolve-user` with `X-Firm-Secret` auth; returns FAIT GUID or null on failure |
| `services/fip-mcp/src/tools/list_kb_files.js` | Added import for `getFaitUserId`; resolves `faitUserId` before `getS3Prefix`; PERSONAL case throws `USER_RESOLUTION_FAILED` (500) if resolution returns null |

---

## Acceptance criteria

- [x] `list_kb_files` for PERSONAL KB uses FAIT internal GUID in S3 prefix
- [x] Resolution failure returns a clear 500 error (not silent 0-file list)
- [x] TEAM and CORP KB paths are unchanged
- [x] `ResolveUser` does exact EntraOid match first, single-user fallback second
- [x] No changes to `KnowledgeBaseService.cs`, `server.js`, or any other files

---

## ECS Environment Variables — ACTION REQUIRED for Rhodey (DevOps)

**fip-mcp ECS task definition** needs these env vars set before the personal KB listing fix is live:

| Variable | Value | Source |
|----------|-------|--------|
| `FAIT_INTERNAL_SECRET` | Same value as FAIT's `Firm__SharedSecret` | FAIT ECS task def or Secrets Manager |
| `FAIT_BASE_URL` | `https://fait.fortressam.ai` | Static |
| `KB_BUCKET` | `fortress-tools` | Static (already set?) |

Without `FAIT_INTERNAL_SECRET`, `getFaitUserId` will warn and return `null`, causing all personal KB
file listing to return `USER_RESOLUTION_FAILED`. The secret value must match exactly what FAIT uses
for `Firm:SharedSecret` — they share it bidirectionally.

---

## How to test locally

1. Confirm FAIT's `AppUser.EntraOid` column is populated for your test user (check DB)
2. Call `GET /api/firm/resolve-user?entraOid=<your-entra-oid>` with `X-Firm-Secret` header → should return `userId` matching FAIT internal GUID
3. With `FAIT_INTERNAL_SECRET` and `FAIT_BASE_URL` set in fip-mcp env, call `list_kb_files` for personal KB → should return files from `kb-docs/personal/{guid}/`

---

## Parallelization
Single CC session (sequential) — both repos share context; changes depend on each other.

## Notes for Clint
- `FirmIntegrationController.ResolveUser` had a large block of TODO comments admitting it was broken — all cleaned up
- The `calendar-events` endpoint already had the correct `EntraOid` lookup pattern (added in ADO#1240); `ResolveUser` now matches that pattern
- `fait-user-resolver.js` is purely additive — no existing fip-mcp logic changed
- Failure path is explicit (throws `USER_RESOLUTION_FAILED`) rather than silently returning 0 files
