# Review Report — ADO#3200

**Task:** 5.1-A: user_workspace_files table, S3 storage, artifact SSE event + chat card  
**Reviewer:** Clint Barton (Hawkeye)  
**Cycle:** 1 of 2  
**Commit:** `9fba6c72`

---

### Verdict: NEEDS-CHANGES

---

## CC Review Summary

CC invoked via:
```bash
cat /tmp/clint-review-brief-3200.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC read all 7 files in the diff plus targeted ChatView.razor sections. It confirmed 10 of 12 critical checks passed cleanly and identified the same stale-artifacts issue I caught in manual review. Two nitpicks surfaced that are logged below but don't block ship.

CC false positives: none. All findings confirmed as real.

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Files | Result |
|-------|-------|--------|
| Bucket name | `WorkspaceFileService.cs` ↔ `MemoryFileService.cs` | ✅ Same key (`WORKSPACE_S3_BUCKET`), same default (`fortress-user-workspaces`) |
| CHAR(36) entity ↔ migration | `AppDbContext.cs` ↔ `20260510174001_AddWorkspaceFiles.cs` | ✅ All 4 Guid columns: `id`, `user_id`, `conversation_id`, `task_run_id` — CHAR(36) in both |
| Migration additive | `20260510174001_AddWorkspaceFiles.cs` | ✅ Only creates `user_workspace_files` + 2 indexes. Zero changes to existing tables. |
| GuidFormat | `Program.cs` (line 45) | ✅ `GuidFormat = MySqlGuidFormat.None` present in the `MySqlConnectionStringBuilder` block consumed by `AddDbContextFactory<AppDbContext>` |
| DI registration | `Program.cs` (line 112) | ✅ `AddScoped<IWorkspaceFileService, WorkspaceFileService>()` — correct lifetime |
| S3 key exposure | `ArtifactCard.razor` HTML vs `@code { }` | ✅ `Artifact.S3Key` only appears in `@code { }` as a method parameter, never in the rendered HTML |

---

## Critical Issues [2]

### C1: `_conversationArtifacts` not reset when navigating to new conversation

**File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (OnParametersSetAsync `else if (conversation == null)` block, ~line 448)  
**Category:** Correctness / Stale UI State  
**Issue:** When the user navigates to a new (unsaved) conversation, `OnParametersSetAsync` enters the `else if (conversation == null)` branch. This branch resets `messages`, `_selectedTeamIds`, `_fortressKbEnabled`, and `_personalKbEnabled`, but does NOT reset `_conversationArtifacts`. A user who chatted in a prior conversation (which generated artifacts) and then opens a fresh chat will see those prior artifacts rendered above the empty chat input.

**Impact:** Stale artifact cards from conversation A appear in conversation B. This is a data display correctness bug — no data is leaked to unauthorized users, but the wrong data is shown to the correct user.

**Fix:**
```diff
else if (conversation == null)
{
    messages = new List<ChatMessage>();
    wasSummarized = false;
    _selectedTeamIds = new HashSet<int>();
    _fortressKbEnabled = false;
    _personalKbEnabled = false;
+   _conversationArtifacts = new();
}
```

---

### C2: `_conversationArtifacts` not reset when `ConversationId` resolves to null

**File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (OnParametersSetAsync inner null-conversation branch, ~line 437)  
**Category:** Correctness / Stale UI State  
**Issue:** When `ConversationId.HasValue` is true but `GetConversationAsync` returns null (conversation deleted, or belongs to a different user — the service returns null for authorization failures), the `if (conversation != null)` block is skipped. The outer `if (ConversationId.HasValue)` matched, so the `else if (conversation == null)` branch is also skipped. `_conversationArtifacts` is never reset. Stale artifacts persist.

**Impact:** Same class of stale-display bug as C1. Also: if a user somehow navigates to another user's conversation ID (gets null back from auth check), they won't see the other user's artifacts (good), but they will continue seeing their own stale artifacts (misleading UX).

**Fix:**
```diff
if (ConversationId.HasValue)
{
    conversation = await ChatSvc.GetConversationAsync(ConversationId.Value, Session.UserId);
    if (conversation != null)
    {
        messages = conversation.Messages.OrderBy(m => m.CreatedAt).ToList();
        currentModel = conversation.Model;
        _selectedTeamIds = conversation.TeamKbs.Select(t => t.TeamId).ToHashSet();
        _fortressKbEnabled = conversation.EnableFortressKb;
        _personalKbEnabled = conversation.EnablePersonalKb;
        _conversationArtifacts = await WorkspaceFileSvc.GetConversationArtifactsAsync(conversation.Id);
    }
+   else
+   {
+       _conversationArtifacts = new();
+   }
}
```

---

## Important Issues [0]

None. All eight Important checks passed.

---

## Nitpick Issues [2]

### N1: `DownloadAsync` catch swallows exception without logging

**File:** `ArtifactCard.razor` (~line 54)  
**Issue:** `catch (Exception ex)` shows a Snackbar but never logs `ex`. No server-side trace if downloads start failing in production. Also generates a compiler warning (`ex` assigned but not used in the log call if Snackbar doesn't include it).  
**Fix:** Add `@inject ILogger<ArtifactCard> Logger` and `Logger.LogWarning(ex, "[ArtifactCard] Download failed")` in the catch block.

### N2: `CancellationToken ct` in `GetPresignedDownloadUrlAsync` is accepted but silently ignored

**File:** `WorkspaceFileService.cs` (~line 73)  
**Issue:** The method signature includes `CancellationToken ct` but `GetPreSignedURL` is a local computation — no cancellation path exists. The parameter is accepted but unused.  
**Note:** This is not a bug — `GetPreSignedURL` is synchronous and local. But the parameter is misleading. Either remove it from the interface or document that it's a no-op.  
**Verdict:** Accept as-is for v1. Worth cleaning up when the interface is next touched.

---

## Spec Compliance

All critical spec requirements checked:

| Check | Result |
|-------|--------|
| GuidFormat = MySqlGuidFormat.None in Program.cs | ✅ PASS (line 45) |
| IDbContextFactory pattern in WorkspaceFileService | ✅ PASS — `await using var db = await _dbFactory.CreateDbContextAsync(ct)` in all 3 methods |
| No raw S3 key in rendered HTML | ✅ PASS — S3Key only in `@code { }`, never in template |
| Presigned URL via `GetPreSignedUrlRequest` SDK pattern | ✅ PASS — BucketName, Key, Verb, Expires all correct; returns `_s3.GetPreSignedURL(request)` |
| Migration additive only | ✅ PASS — creates only `user_workspace_files`, zero ALTER/DROP |
| CHAR(36) for all Guid columns in OnModelCreating | ✅ PASS — Id, UserId, ConversationId, TaskRunId all CHAR(36) |
| Artifact SSE handler try/catch | ✅ PASS — fully wrapped; bad JSON logs warning and continues |
| `_conversationArtifacts` cleared on conversation switch | ❌ FAIL — two code paths (C1, C2) both miss the reset |
| Preview button: `Disabled="true"` + tooltip "Preview coming soon" | ✅ PASS — both present |
| Bucket name matches MemoryFileService (`WORKSPACE_S3_BUCKET`) | ✅ PASS — same key, same default |
| `@inject IJSRuntime JSRuntime` in ArtifactCard.razor | ✅ PASS (line 4) |
| `AddScoped<IWorkspaceFileService, WorkspaceFileService>()` | ✅ PASS (Program.cs line 112) |

---

## What to Fix (NEEDS-CHANGES)

Tony — two surgical fixes needed, both in `ChatView.razor`, `OnParametersSetAsync`:

**Fix 1** — `else if (conversation == null)` block (~line 448):  
Add `_conversationArtifacts = new();` alongside the other field resets.

**Fix 2** — Inside `if (ConversationId.HasValue)`, after the `if (conversation != null)` block (~line 437):  
Add an `else { _conversationArtifacts = new(); }` to handle the case where GetConversationAsync returns null.

These are the only two changes required. Everything else is clean — good work on the service layer and migration.

---

*Hawkeye out.*
