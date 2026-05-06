# Review Report — ADO#2834 Cycle 2

**WI:** ADO#2834 — list_kb_files personal KB prefix: Entra OID → FAIT GUID
**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 2 (targeted re-review of 3 C2 changed files)
**Commit:** `ee21c6d9c0f803b7fb9dbbada28e00123e628246`

---

### Verdict: ✅ PASS

---

### CC Review Summary

Ran CC adversarial review against all 3 changed files. All functional criteria pass. One pre-existing flag in `GetUserTeams` (no EntraOid filter) — identical pattern to `meeting-complete`, not introduced by this PR, out of scope for ADO#2834.

---

### Spec Compliance

**Acceptance Criteria:**
- [x] `list_kb_files` for PERSONAL KB uses FAIT internal GUID in S3 prefix — ✅ Verified
- [x] Resolution failure returns clear 500 error (not silent 0-file list) — ✅ `USER_RESOLUTION_FAILED` thrown
- [x] TEAM and CORP KB paths unchanged — ✅ Verified, logic untouched
- [x] `ResolveUser` does exact EntraOid match first, single-user fallback second — ✅ Verified with `Count == 1` guard
- [x] No changes to `KnowledgeBaseService.cs`, `server.js`, or any other files — ✅ Confirmed

---

### File-by-File Results

#### `FirmIntegrationController.cs` — `ResolveUser`

| Check | Result | Evidence |
|-------|--------|----------|
| Auth (`X-Firm-Secret`) validated before DB access | ✅ | L67–73: null/empty secret check + 401 return before DB opens |
| Primary lookup: `WHERE EntraOid == entraOid && IsActive` | ✅ | L80–82 |
| Fallback: `IsEntraUser && IsActive`, only if exactly 1 result | ✅ | L86–89: `entraUsers.Count == 1 ? entraUsers[0] : null` |
| 404 if both lookups null | ✅ | L92–93 |
| Returns `{ userId: user.Id.ToString() }` | ✅ | L96 |
| `calendar-events` still has its own correct EntraOid lookup | ✅ | L349–351 |
| No other methods broken | ✅ | Confirmed |

**⚠️ Pre-existing flag (not blocking, not in scope):** `GetUserTeams` (L116–119) still uses old broken lookup (no EntraOid filter). Same as `meeting-complete`. This predates ADO#2834 and is not a C2 regression.

---

#### `fait-user-resolver.js` (NEW FILE)

| Check | Result | Evidence |
|-------|--------|----------|
| Missing secret → `return null` (not throw) | ✅ | L5–8 |
| `AbortSignal.timeout(5000)` | ✅ | L13 |
| Non-OK response → `return null` | ✅ | L15–18 |
| All exceptions caught → `return null` | ✅ | L9–24 try/catch wraps entire fetch |
| `data.userId ?? null` | ✅ | L20 |
| Secret from `process.env` only — no hardcoded values | ✅ | L2 |
| `encodeURIComponent(entraOid)` in URL | ✅ | L10 |

---

#### `list_kb_files.js`

| Check | Result | Evidence |
|-------|--------|----------|
| `getFaitUserId` imported from `../utils/fait-user-resolver.js` | ✅ | L4 |
| `getFaitUserId` called only for `KB_TYPE.PERSONAL` | ✅ | L48–51 |
| `faitUserId` passed in `getS3Prefix` args | ✅ | L52: `{ team_id, faitUserId }` |
| PERSONAL case uses `args.faitUserId` (not `user.user_id`) | ✅ | L21–25 in `getS3Prefix` |
| Throws `USER_RESOLUTION_FAILED` (500) if null | ✅ | L21–24 |
| TEAM path uses `args.team_id` — unaffected | ✅ | L26–28 |
| CORP path returns `kb-docs/fortress/` — unaffected | ✅ | L29–30 |
| No silent fallback to `user.user_id` in PERSONAL path | ✅ | Confirmed — no such reference |

**Nitpick (non-blocking):** JSDoc comment in `getS3Prefix` (L14) still says `{user.user_id}` — stale after this change. Logic is correct.

---

### Issues

| Severity | File | Issue |
|----------|------|-------|
| Nitpick | `list_kb_files.js:14` | JSDoc comment says `{user.user_id}` but code now uses `faitUserId` — stale comment, not a bug |
| Pre-existing (out of scope) | `FirmIntegrationController.cs:116–119` | `GetUserTeams` has no EntraOid filter — same pattern as `meeting-complete`, predates this PR |

---

### Notes for Rhodey (DevOps)

Per Tony's build report: **ECS env vars required before personal KB fix is live:**
- `FAIT_INTERNAL_SECRET` — must match FAIT's `Firm__SharedSecret`
- `FAIT_BASE_URL` — `https://fait.fortressam.ai`
- `KB_BUCKET` — `fortress-tools` (verify already set)

Without `FAIT_INTERNAL_SECRET`, all personal KB listing will return `USER_RESOLUTION_FAILED`.
