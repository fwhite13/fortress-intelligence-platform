# Review Report — ADO#2834

**WI:** KB file enumeration fix: S3-authoritative listing + preserve filenames with extensions  
**Reviewer:** Hawkeye (Clint Barton)  
**Review cycle:** 1  
**Date:** 2026-05-06  

---

### Verdict: FAIL ❌

---

## Spec Compliance Check

**§ Codebase Map:**
- `fait/src/FortressAI.Web/Services/KnowledgeBaseService.cs` — ✅ modified as specified (line 323, 1-line fix)
- `services/fip-mcp/src/tools/list_kb_files.js` — ✅ created
- `services/fip-mcp/src/server.js` — ✅ import + registration only
- `services/fip-mcp/package.json` — ✅ `@aws-sdk/client-s3` added

**§ Out of Scope:**
- `services/fip-mcp/pipeline/` — ✅ docs-only (ADO2832-BUILD-REPORT.md, ADO2834-PLAN.md, ADO2834-BUILD-REPORT.md) — no application code touched

**§ Acceptance Criteria:**
- [x] `GetFileNameWithoutExtension` removed — ✅ verified line 323
- [x] `list_kb_files.js` exists with S3 listing, entitlement check, sidecar filtering — ✅ present
- [x] `@aws-sdk/client-s3` in `package.json` — ✅ confirmed
- [x] Tool imported and registered in `server.js` — ✅ confirmed
- [ ] Personal KB prefix resolves to actual files — ❌ BROKEN (see Critical C1)

**Spec compliance verdict:** ❌ NON-COMPLIANT — Personal KB listing will always return 0 files

---

## Consistency Audit

**Cross-referenced:**
- `auth.js` user object: `user.user_id = payload.oid` (Entra OID) ↔ `KbDocumentService.cs` S3 path: `kb-docs/personal/{userId}/` where `userId = AppUser.Id = Guid.NewGuid()` — ❌ **IDENTITY MISMATCH**
- `list_kb_files.js` Corp KB prefix `kb-docs/fortress/` ↔ `KbDocumentService.cs:79` — ✅ matches
- `list_kb_files.js` Team KB prefix `kb-docs/teams/{team_id}/` ↔ `KbDocumentService.cs:85` — ✅ matches (need to verify)
- `package.json` `@aws-sdk/client-s3: "^3.0.0"` ↔ existing `@aws-sdk` deps — ✅ version consistent

---

## Critical Issues [1]

### C1: Personal KB S3 prefix uses wrong user identity — will always return 0 files

**File:** `services/fip-mcp/src/tools/list_kb_files.js` (line 20)  
**Category:** Correctness / Identity mismatch  

**Issue:** `list_kb_files.js` constructs the personal KB S3 prefix using `user.user_id`, which `auth.js` sets to `payload.oid` (the Entra Object ID). But FAIT's `KbDocumentService` uploads personal KB documents using `AppUser.Id` — a **randomly generated GUID** that is entirely separate from the Entra OID.

**Evidence chain:**

`auth.js` (lines 57–58):
```javascript
return {
  user_id: payload.oid,   // ← Entra OID (e.g., "a1b2c3d4-1111-2222-...")
  ...
};
```

`list_kb_files.js` (line 20):
```javascript
return `kb-docs/personal/${user.user_id}/`;   // uses Entra OID
```

`KbDocumentService.cs` (line 87):
```csharp
_ => $"kb-docs/personal/{userId}/{safeFilename}"  // userId = AppUser.Id
```

`ExcelAddinController.cs` (line 59) — Entra user provisioning:
```csharp
user = new AppUser
{
    Id       = Guid.NewGuid(),   // ← NEW random GUID, NOT the Entra OID
    EntraOid = oidClaim,         // ← Entra OID stored separately
};
```

**`AppUser.Id` ≠ Entra OID.** They are two different GUIDs with no relation.

**Impact:** Every call to `list_kb_files` for a personal KB will look under `kb-docs/personal/{Entra-OID}/` but actual files live under `kb-docs/personal/{FAIT-DB-GUID}/`. Result: the tool will always return `{ file_count: 0, files: [] }` for all personal KBs. The tool is functionally broken as shipped.

**Fix options (pick one):**

**Option A** — Resolve Entra OID → FAIT AppUser.Id at auth time (preferred):  
Add to `auth.js` (or a new `user-resolver.js`): look up `AppUser` by `EntraOid` and inject `fait_user_id`:
```javascript
// In validateToken or a post-auth enrichment step:
const faitUser = await db.users.findOne({ entra_oid: payload.oid });
return {
  user_id: payload.oid,           // keep as-is for search_kb Bedrock metadata filter
  fait_user_id: faitUser?.id,     // FAIT internal GUID for S3 paths
  ...
};
```
Then in `list_kb_files.js`:
```javascript
case KB_TYPE.PERSONAL:
  if (!user.fait_user_id) throw { code: 'USER_NOT_PROVISIONED', status: 403, ... };
  return `kb-docs/personal/${user.fait_user_id}/`;
```

**Option B** — FAIT issues a JWT with `fait_user_id` as a custom claim (cleanest long-term, requires FAIT work).

**Option C** — Provision Entra users with `Id = Guid.Parse(oidClaim)` in FAIT, eliminating the split identity entirely (requires DB migration for all existing users — not a quick fix).

**Note:** `search_kb.js` has a related but pre-existing mismatch where it injects `user.user_id` (Entra OID) as the Bedrock metadata filter for the `user_id` field, but the `.metadata.json` companion stored during upload uses `ownerId` = `AppUser.Id`. That is a pre-existing issue; it is NOT in scope for this ADO but the same root cause. Do not bundle fixes.

---

## Important Issues [0]

None.

---

## Nitpicks [2]

- **N1:** `s3_key` field in response — exposes full internal S3 object key to the caller. Since the user owns their personal KB files this is technically their data, but it leaks the internal bucket structure. Consider omitting or gating behind a flag. Not blocking.

- **N2:** No explicit `MaxKeys` limit on `ListObjectsV2Command`. If a KB grows very large (thousands of files) the `do/while` loop will paginate fully and could return a very large payload. Consider adding a reasonable cap (e.g., 1000 files) with a `truncated: true` indicator. Not blocking for now.

---

## Positive Observations

- **KnowledgeBaseService.cs fix is clean** — exactly 1 line changed, correct fix, no scope creep.
- **Entitlement check order is correct** — auth happens before S3 call, no data exposure on unauthorized access.
- **Pagination implemented correctly** — `do...while (continuationToken)` handles multi-page S3 results properly.
- **Sidecar filtering is complete** — both `.metadata.json` and `-bda-text.txt` excluded; empty filename guard present.
- **Error handling follows existing pattern** — bubbles to `handleToolError` in `server.js` consistently with all other tools.
- **server.js registration is pattern-compliant** — identical structure to existing tools.
- **package.json version is consistent** — `^3.0.0` matches existing `@aws-sdk` deps.
- **Pipeline docs confirmed docs-only** — no application code affected.

---

## What to Fix (FAIL — send back to Tony)

### Fix required: Personal KB user identity

**File:** `services/fip-mcp/src/tools/list_kb_files.js` (line 20)  
**Also involves:** `services/fip-mcp/src/auth.js`

The personal KB S3 prefix must use `AppUser.Id` (FAIT internal GUID), not `payload.oid` (Entra OID). These are different GUIDs.

Recommended approach (Option A — least invasive):

1. In `auth.js`, after JWT validation, query FAIT DB (or call a FAIT internal API) to resolve `payload.oid` → `AppUser.Id`. Inject as `fait_user_id` on the user object.

2. In `list_kb_files.js` `getS3Prefix`, use `user.fait_user_id` for the personal KB prefix. Add guard:
   ```javascript
   case KB_TYPE.PERSONAL:
     if (!user.fait_user_id) throw { code: 'USER_NOT_PROVISIONED', status: 403, message: 'FAIT user record not found for this Entra identity' };
     return `kb-docs/personal/${user.fait_user_id}/`;
   ```

Coordinate with Reed on whether Option A, B, or C is the right architectural call. The pattern also affects `search_kb.js` (pre-existing issue, separate ADO when ready).

Everything else is clean — this is the only blocking issue.

---

_Hawkeye — Review cycle 1 complete. FAIL on C1. Resubmit after identity fix._
