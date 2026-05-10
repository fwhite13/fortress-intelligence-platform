# Review Report — ADO#3160 (Avatar Upload Implementation)

**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-09  
**Commit:** `9f402032`  
**Files reviewed:**
- `src/FortressAI.Web/Components/Pages/AssistantSettings.razor`
- `src/FortressAI.Web/Components/Chat/MessageBubble.razor`

---

## Verdict: NEEDS-CHANGES

Two issues found: one blocking logic bug (S3 prefix guard) and one missing env var that would silently fail in production. CSS nitpick also noted.

---

## Spec Compliance Check

**§ Acceptance Criteria:**
- [x] File upload picker after Accent Color card ✅
- [x] 2MB size limit enforced with error message ✅
- [x] Format validation (jpg/jpeg/png/gif/webp) ✅
- [x] S3 upload to `workspaces/{userId}/avatar/{guid}{ext}` ✅ (conditional — see I1 below)
- [x] Old avatar deleted from S3 on re-upload ✅ (conditional — see I1 below)
- [x] AvatarUrl persisted to DB via DbFactory ✅
- [x] Preview image shown when AvatarUrl is set ✅
- [x] MessageBubble shows avatar image when AvatarUrl set ✅
- [x] CSS classes using CSS vars only ✅ (one nitpick — see N1)
- [x] Build: 0 errors ✅
- [x] Migration applied ✅

**Spec compliance verdict:** ✅ COMPLIANT (with fixes required for I1 + I2)

---

## Consistency Audit

**Files Cross-Referenced:**
- `AssistantSettings.razor` ↔ `MessageBubble.razor` — ✅ AvatarUrl written/read consistently
- `SaveSettings` method ↔ `AvatarUrl` field — ✅ `SaveSettings` does NOT touch `AvatarUrl`; EF preserves existing DB value on settings save. No accidental wipe-on-save risk.
- `HandleAvatarUpload` key construction ↔ `ExtractS3Key` parsing — ✅ Consistent: prefix is baked into the key and URL, and `ExtractS3Key` strips only the `https://{bucket}.s3.amazonaws.com/` prefix, returning the full key (including prefix). Delete logic is internally consistent — BUT only if the prefix has a trailing slash when set. See I1.

**Undocumented Dependencies Found:**
- `WORKSPACE_S3_BUCKET` read from `IConfiguration` with fallback `"fortress-user-workspaces"` — **NOT SET in ECS task definition `fred-dev:150`**. See I2.
- `WORKSPACE_S3_PREFIX` read from `IConfiguration` with fallback `""` — also not in ECS (empty fallback is acceptable behavior but see I1 for why the value format matters).

---

## Issues Found

| # | Severity | File | Area | Issue |
|---|----------|------|------|-------|
| I1 | **Important** | `AssistantSettings.razor` | S3 key construction | `WORKSPACE_S3_PREFIX` has no trailing-slash guard |
| I2 | **Important** | ECS Task Def `fred-dev:150` | Infrastructure | `WORKSPACE_S3_BUCKET` missing from env vars |
| N1 | Nitpick | `AssistantSettings.razor` | CSS | Hardcoded `2px` in `.avatar-preview-img` border |

---

## Critical Issues: None

---

## Important Issues

### I1: `WORKSPACE_S3_PREFIX` missing trailing-slash guard

**File:** `AssistantSettings.razor` — `HandleAvatarUpload` method  
**Category:** Correctness / Logic

**Issue:**
```csharp
var s3Prefix = AppConfig["WORKSPACE_S3_PREFIX"] ?? "";
var key = $"{s3Prefix}workspaces/{Session.UserId}/avatar/{fileName}";
```

If `WORKSPACE_S3_PREFIX` is set to `"myprefix"` (no trailing slash), the key becomes `myprefixworkspaces/userId/avatar/file.jpg` instead of `myprefix/workspaces/userId/avatar/file.jpg`. The S3 object would land at the wrong key. Worse: since the key is baked into the stored URL and `ExtractS3Key` parses it back out, the delete-old-avatar logic would also attempt to delete the wrong key — meaning old avatars accumulate and are never cleaned up.

If the fallback `""` is always used in practice, this is currently a latent bug. But the config key exists for a reason.

**Fix:**
```diff
  var s3Prefix = AppConfig["WORKSPACE_S3_PREFIX"] ?? "";
+ if (!string.IsNullOrEmpty(s3Prefix) && !s3Prefix.EndsWith('/'))
+     s3Prefix += '/';
  var fileName = $"{Guid.NewGuid()}{ext}";
```

---

### I2: `WORKSPACE_S3_BUCKET` missing from ECS task definition `fred-dev:150`

**Infrastructure — not a code issue, but a blocking deployment blocker**

Verified with:
```bash
aws ecs describe-task-definition --task-definition fred-dev:150 --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition.containerDefinitions[0].environment[?starts_with(name,`WORKSPACE`)]'
# Returns: []
```

No `WORKSPACE_*` vars are set on the running task definition. The code has a fallback:
```csharp
var bucket = AppConfig["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
```

So it will fall back to `"fortress-user-workspaces"` — this may or may not be the correct bucket name. If the actual bucket is named differently, the S3 upload will silently use the wrong bucket (or fail with AccessDenied/NoSuchBucket). This needs to be explicitly confirmed and the env var added to the task definition before production deploy.

**Fix:** Add `WORKSPACE_S3_BUCKET=<actual-bucket-name>` to the ECS task definition environment. Verify the bucket name matches the actual S3 bucket used for workspace files.

---

## Nitpick

### N1: Hardcoded `2px` in avatar preview CSS

**File:** `AssistantSettings.razor` — `<style>` block  
```css
.avatar-preview-img {
    border: 2px solid var(--color-accent, #d4af37);  /* ← 2px is hardcoded */
}
```

The `2px` should use a CSS variable per the team's CSS compliance standard. The `var(--color-accent)` is correct; the thickness is not.

**Fix:** Replace `2px` with `var(--border-width-thin, 2px)` or whatever the team's thin-border token is.

---

## What Tony Needs to Fix

1. **I1 — `WORKSPACE_S3_PREFIX` trailing-slash guard** (code fix, AssistantSettings.razor)
   Add the two-line guard after reading the config value. See diff above.

2. **I2 — `WORKSPACE_S3_BUCKET` ECS env var** (infrastructure fix)
   Add `WORKSPACE_S3_BUCKET` to the ECS task definition. Confirm the actual bucket name before setting it.

3. **N1 — hardcoded `2px`** (optional — fix with I1 pass)
   Replace `2px` with the appropriate CSS token.

---

## CC Review Summary

CC (Claude Sonnet) ran the full adversarial review. All 18 acceptance criteria items were verified explicitly. CC confirmed:
- All validation logic is correct and appropriately ordered
- `_avatarUploading` state management is sound
- `StateHasChanged()` placement is correct (MudBlazor EventCallback triggers rerender on early returns automatically)
- `finally` block behavior after AccessDenied re-throw is intentional and correct
- `SaveSettings` does not overwrite `AvatarUrl` (EF preserves it)
- MessageBubble null-safety is correct

Two genuine findings surfaced: the S3 prefix trailing-slash gap (Important, logic bug) and the missing ECS env var (Important, deployment blocker). One CSS nitpick.

No false positives to note.

---

_Hawkeye — you see what others miss._
