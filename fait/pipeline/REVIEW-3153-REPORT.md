# Review Report — ADO#3153

## Verdict: NEEDS-CHANGES

One Important fix required (AccessDenied rollback gap). All 15 acceptance criteria pass. Fix is a 5-line addition — no redesign needed.

---

## Spec Compliance Check

**§2 Files changed:**
- `src/FortressAI.Web/Services/UserProvisioningService.cs` — ✅ new service, 230 lines
- `src/FortressAI.Web/Program.cs` — ✅ `AddScoped<UserProvisioningService>()` at line 97
- `src/FortressAI.Web/Components/Pages/AssistantSetup.razor` — ✅ inject at line 10, call at line 374

**§6 Out of Scope:**
✅ No out-of-scope changes. No DB migrations, no schema changes, no unrelated files touched.

**§7 Acceptance Criteria:**

| # | Criterion | Result | Notes |
|---|-----------|--------|-------|
| 1 | Idempotency | ✅ PASS | `OnboardingCompletedAt.HasValue` guard at line 39, early return |
| 2 | S3 keys match harness | ✅ PASS | See S3 alignment analysis below |
| 3 | Rollback (writtenKeys) | ✅ PASS | `Add()` after `WriteS3Async` — PutObject is atomic, ordering is correct |
| 4 | AccessDenied guard | ⚠️ PARTIAL | Halts DB write + rethrows ✅ — but does NOT rollback prior writes ❌ |
| 5 | DB write order | ✅ PASS | `OnboardingCompletedAt` set only after all S3 writes succeed |
| 6 | SOUL.md content | ✅ PASS | AssistantName, PersonalityPreset description, CommunicationStyle, ResponseFormat |
| 7 | USER.md content | ✅ PASS | DisplayName, Role, Responsibilities, PreferredName, CommStyle, RespFormat, UseCases, AdditionalContext |
| 8 | MEMORY.md content | ✅ PASS | Timestamp + UseCases section if available |
| 9 | AGENTS.md static | ✅ PASS | `const string`, not user-specific |
| 10 | UseCasesJson parsing | ✅ PASS | `Deserialize<List<string>>` with try/catch at lines 156–163 and 192–199 |
| 11 | DI registration | ✅ PASS | Program.cs line 97, scoped |
| 12 | Razor call placement | ✅ PASS | After `SaveChangesAsync` (line 371), before `Nav.NavigateTo` (line 376) |
| 13 | Error blocks navigation | ✅ PASS | Exception propagates, HandleSubmit catch prevents nav |
| 14 | No DB changes | ✅ PASS | No entities, no migrations, no schema changes |
| 15 | dotnet build | ✅ PASS | 0 errors, 32 pre-existing warnings |

**Spec compliance verdict:** ✅ COMPLIANT on 14/15 criteria — AC#4 partially met (see C1 below)

---

## Consistency Audit

**S3 Key Alignment — Deep Analysis**

The critical question: do the keys written by `UserProvisioningService` match the keys the harness reads?

**Web app writes** (`WORKSPACE_S3_PREFIX` is NOT set in the web app — only in harness ECS tasks):
```
S3Prefix = ""
prefix   = "" + "workspaces/{userId}/" = "workspaces/{userId}/"
Keys written:
  workspaces/{userId}/assistants/SOUL.md   ✅
  workspaces/{userId}/assistants/USER.md   ✅
  workspaces/{userId}/assistants/AGENTS.md ✅
  workspaces/{userId}/memory/MEMORY.md     ✅
```

**Harness reads** (`harness-server.js` lines 1162 and 1292):
```js
const prefix = S3_PREFIX || `workspaces/${userId}/`;
fetchS3File(`${prefix}assistants/SOUL.md`)    // → workspaces/{userId}/assistants/SOUL.md
fetchS3File(`${prefix}assistants/USER.md`)    // → workspaces/{userId}/assistants/USER.md
fetchS3File(`${prefix}memory/MEMORY.md`)      // → workspaces/{userId}/memory/MEMORY.md
```

ECS task mode (`WORKSPACE_S3_PREFIX=workspaces/{userId}/` per FargateUserAgentRuntime.cs:218):
- `S3_PREFIX` = `workspaces/{userId}/` → `prefix` = `workspaces/{userId}/` (truthy, uses directly)
- SOUL.md key = `workspaces/{userId}/assistants/SOUL.md` ✅

Local/dev mode (`S3_PREFIX` = `""`):
- Falls back to `workspaces/${userId}/` → same result ✅

**Keys match in both harness modes.** ✅

Note: Harness does **not** read `AGENTS.md` (no usage in harness-server.js). Written but not consumed by harness turn handler — this is acceptable (future-use documentation).

**Model fields cross-referenced:**
- `UserAssistantConfig`: `AssistantName`, `PersonalityPreset`, `CommunicationStyle`, `ResponseFormat`, `Role`, `Responsibilities`, `PreferredName`, `AdditionalContext`, `UseCasesJson` — all ✅ present in model
- `AppUser`: `DisplayName`, `OnboardingCompletedAt` — both ✅ present in model

---

## Issues

### C1 — Important: AccessDenied Catch Does Not Rollback Prior Writes

**File:** `UserProvisioningService.cs` lines 76–79  
**Category:** Correctness  

```csharp
catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "AccessDenied")
{
    _logger.LogError(ex, "[Provision] AccessDeniedException writing S3 ...");
    throw; // ← no rollback of writtenKeys
}
```

The generic `catch (Exception)` handler (lines 81–91) rolls back via `writtenKeys`. The `AccessDenied` handler does not. If `AccessDenied` fires on write 2, 3, or 4 — after prior files were already successfully written — those files are orphaned.

In practice, IAM per-bucket policies mean all-or-nothing access, making partial AccessDenied rare. But per-prefix SCPs or unusual policy configurations can trigger this. The fix is trivial — reuse the same rollback pattern:

```diff
 catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "AccessDenied")
 {
     _logger.LogError(ex, "[Provision] AccessDeniedException writing S3 for user {UserId} — halting", userId);
+    foreach (var key in writtenKeys)
+    {
+        try { await _s3.DeleteObjectAsync(BucketName, key); }
+        catch (Exception delEx) { _logger.LogWarning(delEx, "[Provision] Rollback: failed to delete {Key}", key); }
+    }
     throw;
 }
```

---

### N1 — Nitpick: AGENTS.md Contains Literal `{userId}` Template String

**File:** `UserProvisioningService.cs` lines 224, 227  
**Category:** Quality  

```csharp
User memory is stored in workspaces/{userId}/memory/MEMORY.md.
User workspace files are stored in workspaces/{userId}/artifacts/.
```

The harness never reads `AGENTS.md`, so no functional impact. But if AGENTS.md is ever read by an agent, it will see the literal string `{userId}` not the actual UUID. Consider interpolating the real userId value when building the file content. Non-blocking.

---

## Informational: Pre-existing Harness Bug (Out of Scope)

**File:** `harness-server.js` line 1101  

```js
const memKey = `${S3_PREFIX}workspaces/${userId}/memory/MEMORY.md`;
```

When `S3_PREFIX = "workspaces/{userId}/"` (ECS task mode), this produces a double-prefixed key: `workspaces/{userId}/workspaces/{userId}/memory/MEMORY.md`. This path is used only for reading the `LastModified` timestamp in the resumption brief feature — not the main turn context fetch (which uses the correct `prefix` variable). Not introduced by this PR. Flagged for tracking.

---

## What Tony Needs to Fix

**Only one change required:**

In `UserProvisioningService.cs`, add rollback to the `AccessDenied` catch handler (lines 76–79). Copy the foreach rollback loop from the generic catch handler (lines 84–90) and insert it before the `throw`. See diff above in C1.

That's it. One block of ~5 lines. No redesign needed.

---

_Review performed by Hawkeye (Clint Barton) | CC Sonnet | 2026-05-09_
