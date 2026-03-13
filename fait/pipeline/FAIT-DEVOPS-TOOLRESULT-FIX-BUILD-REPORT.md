# Build Report: FAIT DevOps tool_result Mismatch Fix

**Task:** FAIT-DEVOPS-TOOLRESULT-FIX  
**Date:** 2026-03-12  
**Builder:** Tony Stark (software-engineer)  
**Commit:** `81f2827`  
**Branch:** `main`

---

## Summary

Fixed a production Bedrock API error:
> `"The number of toolResult blocks at messages.10.content exceeds the number of toolUse blocks of previous turn."`

Root cause: `BuildConverseMessages` was passing `tool_result` user messages to Bedrock without verifying that a matching `tool_use` assistant message preceded them. Added an orphan guard to drop unmatched tool_result blocks before submission.

---

## Fix 1: `BedrockService.cs` — Orphaned tool_result Guard

### Approach: De-staticized to instance method (preferred)

`BuildConverseMessages` was `private static`. Since `BedrockService` already has an `_logger` field (`ILogger<BedrockService>`, injected via constructor at line 25), the method was de-staticized to `private` (non-static) so it can use `_logger.LogWarning(...)` directly. No `Console.WriteLine` fallback needed.

### Changes

| Line | Change |
|------|--------|
| **580** | `private static List<Message> BuildConverseMessages(...)` → `private List<Message> BuildConverseMessages(...)` (removed `static`) |
| **625–659** | Inserted orphaned tool_result guard block (38 lines inserted, replacing the old bare `if (contentBlocks.Count > 0)` check) |

### Guard Logic (lines 625–663)

1. **Check if any tool_result blocks were parsed** — only applies the guard when the message actually contains tool_result content
2. **No preceding assistant message** → `LogWarning` + `continue` (drop the message)
3. **Preceding assistant message has no ToolUse blocks** → `LogWarning` + `continue` (drop the message)
4. **Filter to only matching tool_use_id blocks** — removes individual unmatched tool_result entries while keeping any valid ones
5. **No valid tool_result blocks remain after filter** → `LogWarning` + `continue` (drop the entire message)
6. **Valid blocks exist** → `contentBlocks = validBlocks` (proceed with filtered set)

The existing `if (contentBlocks.Count > 0)` check at line 663 (post-guard) remains as the final gate before adding to `result`.

---

## Fix 2: `DatabaseInitializationService.cs` — DevOps Seed Verification

**File:** `src/FortressAI.Web/Services/DatabaseInitializationService.cs`

No code changes required. Verified correct as of commit `ce830be`:

### `requires_user_auth` in INSERT

```sql
VALUES ({0}, 'Azure DevOps', 'devops', 'Work items, repos, and pipelines via Azure DevOps REST API',
    'http', {1}, 'devops_pat', 0, 1, {2},   -- ← requires_user_auth = 0 ✅
```

✅ `requires_user_auth = 0` confirmed in INSERT values (position in column list: `auth_type, requires_user_auth`)

### `ON DUPLICATE KEY UPDATE` clause

```sql
ON DUPLICATE KEY UPDATE
    endpoint_url = VALUES(endpoint_url),
    auth_type = VALUES(auth_type),              -- ✅ present
    requires_user_auth = VALUES(requires_user_auth),  -- ✅ present
    tool_manifest = VALUES(tool_manifest),
    updated_at = NOW(6)
```

✅ `auth_type` included  
✅ `requires_user_auth` included  
✅ Both fields will be updated on duplicate key, ensuring existing rows get corrected values

**Auth path confirmation:** DevOps auth is handled via `user_devops_connections` + `IsConnectedAsync`. `requires_user_auth = 0` means the server uses the `devops_pat` auth type path (PAT resolved by `DevOpsConnectionService`), NOT the `user_mcp_tokens` path. Correct.

---

## Build Result

```
cd ~/projects/fip/fait/src/FortressAI.Web && dotnet build

    30 Warning(s)
    0 Error(s)

Time Elapsed 00:00:11.35
```

✅ **0 Errors**  
⚠️ 30 warnings (pre-existing, unrelated to this change — MudBlazor analyzer warnings)

---

## Commit

```
commit 81f2827
fix(devops): guard orphaned tool_result blocks in BuildConverseMessages; verify DevOps seed requires_user_auth=0

 1 file changed, 38 insertions(+), 1 deletion(-)
```

**Push:** `ce830be..81f2827  main → main`

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/BedrockService.cs` | Removed `static` from `BuildConverseMessages`; inserted orphan guard (38 lines) |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | No changes — verified correct |

---

## Self-Review Checklist

- [x] Guard only activates when tool_result blocks are present in the message
- [x] Logging uses `_logger.LogWarning` (not Console.WriteLine) — consistent with rest of service
- [x] `continue` correctly skips the `result.Add(...)` call for dropped messages
- [x] Partial match scenario handled: valid tool_result blocks kept, unmatched ones filtered out
- [x] The existing `if (contentBlocks.Count > 0)` check still acts as final gate
- [x] De-staticizing `BuildConverseMessages` has no side effects — it was always called via `this` (no `BedrockService.BuildConverseMessages(...)` static call sites)
- [x] Build: 0 errors
- [x] `requires_user_auth = 0` confirmed in both INSERT and ON DUPLICATE KEY UPDATE
- [x] `auth_type` and `requires_user_auth` both present in ON DUPLICATE KEY UPDATE
