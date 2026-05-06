# ADO#2834 Review Brief — Adversarial Code Review

You are performing an adversarial code review for ADO#2834. Your job is to find what is WRONG with this code.

## Context

ADO#2834 has two changes:
1. 1-liner fix in FAIT `KnowledgeBaseService.cs` — removes `GetFileNameWithoutExtension` wrapper
2. New tool `list_kb_files.js` in fip-mcp — lists S3 objects in a user's KB

The project root is `/home/fredw/projects/fip/`.

## Files to Read

Read these files in full:

1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/KnowledgeBaseService.cs` — check line 323 specifically
2. `/home/fredw/projects/fip/services/fip-mcp/src/tools/list_kb_files.js` — new file, full review
3. `/home/fredw/projects/fip/services/fip-mcp/src/server.js` — verify only import + registration added, no other changes
4. `/home/fredw/projects/fip/services/fip-mcp/package.json` — verify @aws-sdk/client-s3 added correctly
5. `/home/fredw/projects/fip/services/fip-mcp/src/auth.js` — read the user object structure (what fields are set on req.user)
6. `/home/fredw/projects/fip/fait/src/FortressAI.Shared/Models/AppUser.cs` — read AppUser model
7. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/KbDocumentService.cs` — check what userId is used for S3 prefix `kb-docs/personal/{userId}/` (lines ~54-90, ~386-400)
8. `/home/fredw/projects/fip/services/fip-mcp/src/tools/search_kb.js` — check how user.user_id is used for personal KB scoping
9. `/home/fredw/projects/fip/services/fip-mcp/src/config/kb-inventory.js` — check KB_TYPE enum values and KB_INVENTORY structure

## Critical Investigation: user_id Identity Mismatch

This is the most important thing to verify:

**In auth.js:** `user.user_id = payload.oid` (Entra Object ID — a GUID string like "a1b2c3d4-...")

**In FAIT KbDocumentService:** `UploadDocumentAsync(... Guid userId ...)` stores to `kb-docs/personal/{userId}/` where `userId` comes from `Session.UserId` which is `AppUser.Id` — the FAIT internal database GUID.

**AppUser model has TWO identity fields:**
- `AppUser.Id` — FAIT-generated internal GUID (used for S3 paths)
- `AppUser.EntraOid` — Entra OID (stored as string, added in ADO#1240)

**THE QUESTION:** Does `auth.js` `user.user_id = payload.oid` (Entra OID) match the `AppUser.Id` GUID that FAIT uses for S3 paths? Or are these different GUIDs?

Specifically:
- For Entra-provisioned users, is `AppUser.Id` set to the Entra OID, or is it a separate randomly generated GUID?
- Check how FAIT provisions Entra users — does it set `AppUser.Id = Guid.Parse(oidClaim)` or does it generate a new Guid?

Look at: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Controllers/ExcelAddinController.cs` (lines around 60-90 for Entra user provisioning) and any other controller that creates AppUser records for Entra sign-ins.

Also check: `/home/fredw/projects/fip/fait/src/FortressAI.Web/Controllers/AccountController.cs` or equivalent for how Entra users get their AppUser.Id assigned.

## Review Checklist

### 1. KnowledgeBaseService.cs line 323
- Is the fix exactly `chunk.Source.Split('/').Last()`?
- No `GetFileNameWithoutExtension` wrapper?
- Any other changes on nearby lines (should be 1-line-only change)?
- Are there other places in the same file where the same pattern might still be wrong?

### 2. list_kb_files.js — Full Review
- **Entitlement check**: Is `getEntitlements` called BEFORE the S3 call? (Must be yes — auth failure before data exposure)
- **Sidecar exclusions**: Are BOTH `.metadata.json` AND `-bda-text.txt` correctly excluded?
- **Empty filename guard**: Is `if (!filename) continue;` present?
- **Pagination**: Is the `do...while (continuationToken)` loop correct?
- **Error handling**: What happens if S3 call throws? Is it caught or does it bubble up?
- **Team KB**: Is `team_id` validation present before S3 call?
- **Corp KB prefix**: Is `kb-docs/fortress/` the correct prefix? (Verify against KbDocumentService)
- **Project KB**: Is it handled or explicitly rejected?
- **user.user_id identity**: Does `user.user_id` (Entra OID from auth.js) match what FAIT uses for S3 paths?

### 3. server.js
- Is the import line for `listKbFiles` added correctly?
- Is the tool registration following the same pattern as other tools?
- Are there ANY other changes beyond import + tool registration?
- Is the tool schema correct (kb_id required, team_id optional)?

### 4. package.json
- Is `@aws-sdk/client-s3: "^3.0.0"` present in dependencies?
- Was it already there (check other aws-sdk deps for version consistency)?

### 5. Pipeline docs (Tony note)
- Tony said CC also staged ADO2834-PLAN.md and some ADO2833 pipeline files
- Verify these are documentation only, not application code
- Check what was actually staged: look at `services/fip-mcp/pipeline/` directory

## Pass/Fail Criteria

**FAIL if:**
- `user.user_id` (Entra OID) doesn't match the userId used by FAIT for S3 paths — this means `list_kb_files` will return 0 results for all personal KBs
- Entitlement check missing or happens AFTER S3 call
- Critical security issues

**NEEDS-CHANGES if:**
- Minor issues, missing edge case handling, style problems

**PASS if:**
- user_id matches (or there's a clear mechanism making them equivalent)
- All checklist items pass
- Only docs in the extra staged files

## Output Format

Report findings in this format:
1. **user_id identity verdict** — MATCH or MISMATCH, with evidence
2. **KnowledgeBaseService.cs** — PASS or issue
3. **list_kb_files.js** — findings list
4. **server.js** — PASS or issue  
5. **package.json** — PASS or issue
6. **Pipeline docs** — docs-only confirmed or not
7. **Overall recommendation** — PASS / NEEDS-CHANGES / FAIL with reasoning
