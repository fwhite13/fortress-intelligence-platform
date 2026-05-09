# Review Report — ADO#3096
## Scheduled Task Email Notifications
**Reviewer:** Hawkeye (Clint Barton) — Cycle 1
**Review Tool:** Claude Code CLI (sonnet)
**Verdict: ✅ PASS**

---

## CC Invocation
```
cat pipeline/review-brief-3096-3107-3109.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Summary
Clean implementation. All acceptance criteria met. No blocking issues.

---

## Detailed Findings

### Interface + Implementation
`ScheduledTaskNotificationService.cs:10` — `IScheduledTaskNotificationService` declared; `ScheduledTaskNotificationService` implements it at line 19. Namespace `FortressAI.V2.Web.Services` consistent with project. ✅

### DI Registration
`Program.cs:189`:
```csharp
builder.Services.AddScoped<IScheduledTaskNotificationService, ScheduledTaskNotificationService>();
```
Correctly registered before `AddHostedService<ScheduledTaskBackgroundService>()` at line 190. ✅

### AlertOnCompletion / AlertOnFailure Flags
Lines 54–57:
```csharp
if (isSuccess && !task.AlertOnCompletion)
    return;
if (!isSuccess && !task.AlertOnFailure)
    return;
```
Both flags checked with correct semantics before any email attempt. ✅

### Graph Token Null Check
Lines 59–64: If `GetValidAccessTokenAsync` returns null → logs `LogWarning` → returns. Does not throw. ✅

### Graph Email Endpoint
Line 103: `https://graph.microsoft.com/v1.0/me/sendMail` — correct. ✅

### HTML Email Body
Lines 69–85: Contains task name, run ID, status label, completion timestamp, output preview capped at 500 chars (`HtmlEncoded`), and error message on failure. All properly HTML-encoded via `System.Net.WebUtility.HtmlEncode`. ✅

### Never Throws — All Four Paths Covered
- User null: returns at line 49 ✅
- Token null: returns at line 63 ✅
- Non-2xx response: logs warning, returns at lines 113–116 ✅
- Exception: caught at line 121, logged at `LogWarning`, NOT re-thrown ✅

### Three Fire-and-Forget Placements
All placed correctly AFTER their respective `SaveChangesAsync` calls:
- **TaskMode success/fail path**: `ScheduledTaskBackgroundService.cs:173–185` (after save at line 170) ✅
- **CC path**: lines 246–258 (after save at line 243) ✅
- **Catch/exception block**: lines 291–303 (after save at line 288) ✅

### Closure Safety
`task` and `run` fields (`Status`, `CompletedAt`, `OutputText`, `ErrorMessage`) are all set before `SaveChangesAsync` and before `Task.Run` is invoked. No post-closure mutation. ✅

### CancellationToken.None
All three `Task.Run` closures call `SendCompletionEmailAsync` without forwarding the background service's `ct`. Uses default `CancellationToken.None`. ✅

---

## Critical Issues
None.

## Important Issues
None.

## Nitpick Issues
1. Fire-and-forget tasks on the thread pool may be abandoned at host shutdown (midway through Graph POST). This is a known trade-off and acceptable for a non-critical notification path.
2. `HtmlEncode(run.Id)` (line 72) — run ID is a GUID; encoding is a no-op but harmless.

## Observations
**Spec delta (AWS SES vs. MS Graph):** WI spec mentioned AWS SES, but Tony used MS Graph (consistent with existing `IMicrosoftTokenService` infrastructure). This is the correct call for codebase consistency. Fred should confirm if SES is still desired — but no action required to ship.

---

## Gate Decision
**PASS → advance to DEPLOY**
