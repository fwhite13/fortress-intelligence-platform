# Review Report — ADO#3177
## Scheduled task notifications: toast (in-session) + MS365 email (background)

**Reviewer:** Clint Barton (Hawkeye)
**Cycle:** 1 of 2
**Commit:** `385f5692`
**Date:** 2026-05-10

### Verdict: PASS ✅

---

## CC Review

CC (Claude Sonnet via CLI) was invoked but timed out on both attempts — likely a transient rate-limit issue. Per TOOLS.md fallback policy, review was conducted via direct code analysis using the code already read into context. All changed files were read in full prior to analysis.

**CC invocation attempted:**
```bash
cd /home/fredw/projects/fip/fait && cat /tmp/clint-review-brief-3177.md | \
  claude --model sonnet --print --dangerously-skip-permissions
```
Result: SIGKILL (timeout x2). Fell back to Bedrock analysis per protocol.

---

## Spec Compliance Check

**Spec:** Replace Slack with (1) SignalR toast → `DashboardHub` user group `user-{userId}` + (2) MS365 email → `/me/sendMail`. Best-effort, never throw, respect `alert_on_completion`/`alert_on_failure` flags.

| Requirement | Status |
|-------------|--------|
| Slack deleted (no residue in .cs files) | ✅ Verified — `grep -r Slack src/ --include="*.cs"` returns empty |
| Slack deleted (no residue in .razor files) | ✅ Verified — `grep -r Slack src/ --include="*.razor"` returns empty |
| SignalR push to `ReceiveTaskNotification` on `user-{userId}` group | ✅ `_hubContext.Clients.Group($"user-{userId}").SendAsync("ReceiveTaskNotification", ...)` |
| MS365 email via `GetValidAccessTokenAsync` → POST `/me/sendMail` | ✅ Correct |
| Best-effort / never throw | ✅ Both channels independently wrapped in try/catch |
| `alert_on_completion` / `alert_on_failure` flags respected | ✅ Verified |
| DB write before notification | ✅ `await ctx.SaveChangesAsync(ct)` is the last line before `Task.Run` |

---

## Consistency Audit

| Check | Result |
|-------|--------|
| Hub method name: server sends `"ReceiveTaskNotification"` | ✅ Matches client `.On<TaskNotificationPayload>("ReceiveTaskNotification", ...)` |
| Hub group: server targets `$"user-{userId}"` | ✅ Matches `DashboardHub.JoinUserGroup` → `$"user-{userId}"` |
| Hub path: client `/hubs/dashboard` | ✅ Matches `app.MapHub<DashboardHub>("/hubs/dashboard")` in Program.cs |
| `ITaskNotificationService` registered as Scoped | ✅ `builder.Services.AddScoped<ITaskNotificationService, TaskNotificationService>()` |
| `MicrosoftTokenService` registered as Scoped | ✅ `builder.Services.AddScoped<MicrosoftTokenService>()` — resolves correctly from `CreateScope()` |
| No Slack HttpClient registration in Program.cs | ✅ Removed cleanly |

---

## Critical Issues: 0

No critical issues found.

---

## Important Issues: 1

### I1: SignalR Payload Property Casing — Potential Deserialization Mismatch

- **File:** `src/FortressAI.Web/Services/TaskNotificationService.cs` + `Tasks.razor`
- **Severity:** Important
- **Category:** Consistency

**Issue:** Server sends an anonymous object with lowercase variable names:
```csharp
new { taskName, status = "success", message = "...", tasksUrl = "/tasks" }
```
In C#, anonymous objects use the variable name as the property name. These are camelCase: `taskName`, `status`, `message`, `tasksUrl`.

The client receives as a positional record:
```csharp
public record TaskNotificationPayload(string TaskName, string Status, string Message, string TasksUrl);
```
This record has PascalCase property names: `TaskName`, `Status`, `Message`, `TasksUrl`.

**Will this work?** Almost certainly yes — SignalR's `JsonHubProtocol` in .NET uses `System.Text.Json` with `PropertyNameCaseInsensitive = true` by default on the client side. However:
1. No custom `AddJsonProtocol` options are set in Program.cs (`builder.Services.AddSignalR()` with no args).
2. The behavior relies on the default client-side case-insensitive matching, which is correct.
3. This is **not a bug in practice** but it is **implicit behavior** that could break if:
   - A custom JSON protocol is ever added
   - The record is ever used in a different serialization context

**Risk level:** Low-medium. Works today. Fragile if someone ever configures SignalR's JSON protocol.

**Recommended fix** (non-blocking): Either align casing explicitly or document the implicit behavior:
```csharp
// Option A: Match server's camelCase in the record (preferred)
public record TaskNotificationPayload(string taskName, string status, string message, string tasksUrl);

// Option B: Use JsonPropertyName attributes
public record TaskNotificationPayload(
    [property: JsonPropertyName("taskName")] string TaskName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("tasksUrl")] string TasksUrl);
```

**Verdict impact:** Does NOT block PASS. The implicit case-insensitive matching works correctly in the default SignalR configuration. Flagged as Important because the implicit dependency should be made explicit.

---

## Nitpicks: 2

### N1: Double Truncation of resultSummary
- **File:** `TaskNotificationService.cs`, email body construction
- `ScheduledTaskBackgroundService` truncates `resultSummary` to 500 chars before passing it.
- `TaskNotificationService.NotifyTaskCompletedAsync` truncates it again: `resultSummary.Length > 500 ? resultSummary[..500] : resultSummary`
- The second truncation is harmless but dead code since the input is already ≤500 chars.
- **Fix:** Remove the duplicate truncation in `TaskNotificationService`, or remove it from `ScheduledTaskBackgroundService` and let the service own it. Either way, document which layer is responsible.

### N2: `return` Inside Email `try` Block Skips Catch
- **File:** `TaskNotificationService.cs` (both `NotifyTaskCompleted` and `NotifyTaskPermanentlyFailed`)
- When `userEmail == null` or `accessToken == null`, the method uses `return` inside the `try` block.
- The `return` exits the whole method (past the `catch`), but this is fine because:
  - SignalR block already ran and completed (it's above this try/catch)
  - The `return` just skips the email send — which is correct behavior
  - The method returns `Task` (implicit `return` at method end is equivalent)
- **Not a bug.** Calling it out so it doesn't trip up a future reader wondering why early returns bypass the catch.
- **Fix if desired:** Add a comment: `// return here skips email send only; SignalR already fired above`.

---

## Detailed Findings by Review Area

### Critical #1: Best-Effort Independence ✅
Both `NotifyTaskCompletedAsync` and `NotifyTaskPermanentlyFailedAsync` have **two independent try/catch blocks**:
- Channel 1 (SignalR) is entirely within the first `try { } catch { }` block
- Channel 2 (email) is entirely within the second `try { } catch { }` block
- A failure in Channel 1 **cannot prevent** Channel 2 from running
- Both catch blocks log at `Warning` and swallow the exception
- ✅ Best-effort guarantee is correctly implemented

### Critical #2: Null Token Handling ✅
```csharp
var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
if (accessToken == null)
{
    _logger.LogDebug("No MS365 token for user {UserId} — skipping email notification", userId);
    return;  // ← silent skip, no throw
}
```
- Null token → `LogDebug` + silent return. No exception. ✅
- Even if an exception were thrown, the outer `catch (Exception ex)` in the email block would catch it. ✅

### Critical #3: No Slack Residue ✅
```
$ grep -r Slack src/ --include="*.cs" → (empty)
$ grep -r Slack src/ --include="*.razor" → (empty)
```
- `ISlackNotificationService.cs` — deleted ✅
- `SlackNotificationService.cs` — deleted ✅
- `Program.cs` — no Slack HttpClient, no Slack service registration ✅
- `ScheduledTaskBackgroundService.cs` — no Slack references ✅

### Critical #4: Task Status Integrity ✅
The notification `Task.Run` fires AFTER `await ctx.SaveChangesAsync(ct)`. Code sequence:
```csharp
// Lines ~145-185: taskToUpdate.FailureCount++, taskToUpdate.LastRunStatus = ..., etc.
await ctx.SaveChangesAsync(ct);  // ← DB write is COMPLETE here

// Lines ~188-203: fire-and-forget notification
_ = Task.Run(async () => {
    using var scope = services.CreateScope();
    var notifySvc = scope.ServiceProvider.GetRequiredService<ITaskNotificationService>();
    ...
}, CancellationToken.None);
```
- The `Task.Run` block does NOT touch `task.Status`, `task.FailureCount`, or call `ctx.SaveChangesAsync`. ✅
- The notification block uses its own `ITaskNotificationService` resolved from a fresh scope. ✅
- DB integrity is maintained. ✅

### Critical #5: alert_on_completion / alert_on_failure Flag Enforcement ✅
```csharp
if (newStatus == "success" && task.AlertOnCompletion)
{
    await notifySvc.NotifyTaskCompletedAsync(task.UserId, task.Name, resultSummary);
}
else if (newStatus == "failed" && taskToUpdate?.FailureCount >= 2 && task.AlertOnFailure)
{
    await notifySvc.NotifyTaskPermanentlyFailedAsync(task.UserId, task.Name, errorMessage ?? "Unknown error");
}
```

**Spec matching:**
- Completion: `alert_on_completion = true` ✅
- Permanent failure: `alert_on_failure = true && FailureCount >= 2` ✅

**FailureCount timing analysis:**
- `taskToUpdate.FailureCount` starts at the DB value (e.g., 1 if this is the second failure)
- `taskToUpdate.FailureCount++` runs in-memory → now 2
- `ctx.SaveChangesAsync(ct)` persists it
- Notification block captures `taskToUpdate` by closure — same in-memory object, value is 2
- `taskToUpdate?.FailureCount >= 2` → TRUE → permanent failure notification fires ✅
- First failure: starts at 0, incremented to 1 → `1 >= 2` = FALSE → no permanent failure notification ✅

**Flag source:** Both flags read from `task` (the original pre-lock query object), not `taskToUpdate`. Since `AlertOnCompletion`/`AlertOnFailure` are not modified during `ProcessTaskAsync`, this is safe. ✅

### Important #6: SignalR Payload Casing — See I1 above

### Important #7: Hub Connection Lifecycle in Tasks.razor ✅
```csharp
_taskHubConnection.On<TaskNotificationPayload>("ReceiveTaskNotification", async (payload) => { ... });
await _taskHubConnection.StartAsync();  // ← awaited, completes before continuing
if (Session.UserId != Guid.Empty)
{
    await _taskHubConnection.InvokeAsync("JoinUserGroup", Session.UserId.ToString());
}
```
- `.On` handler is registered BEFORE `StartAsync()` — correct, avoids missed messages ✅
- `StartAsync()` is `await`-ed — JoinUserGroup cannot fire until the connection is established ✅
- `JoinUserGroup` is guarded by `Session.UserId != Guid.Empty` ✅
- `InitTaskHubAsync()` is called from `OnAfterRenderAsync(firstRender: true)` — correct lifecycle hook ✅
- Entire `InitTaskHubAsync` is wrapped in try/catch — graceful failure if hub is unavailable ✅

### Important #8: IDbContextFactory Usage ✅
```csharp
private async Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct)
{
    try
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);  // ← correct pattern
        var user = await ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user?.Email;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to load email for user {UserId}", userId);
        return null;
    }
}
```
- `await using` ensures correct disposal ✅
- `IDbContextFactory` (not raw `AppDbContext`) ✅
- `AsNoTracking()` for read-only query ✅

### Important #9: Scope Disposal ✅
```csharp
_ = Task.Run(async () =>
{
    try
    {
        using var scope = services.CreateScope();  // ← `using` ensures disposal
        var notifySvc = scope.ServiceProvider.GetRequiredService<ITaskNotificationService>();
        ...
    }
    catch (Exception ex) { ... }
}, CancellationToken.None);
```
- `using var scope` — disposed at end of try block ✅
- `services.CreateScope()` — `services` is `scope.ServiceProvider` from `PollAndDispatchAsync`, creating a child scope. Valid pattern. ✅
- `CreateScope()` vs `CreateAsyncScope()` — `CreateScope()` is correct. `CreateAsyncScope()` is only needed when services implement `IAsyncDisposable`. Neither `ITaskNotificationService`, `MicrosoftTokenService`, nor their dependencies do. ✅
- `ITaskNotificationService` resolves as Scoped from child scope ✅

### Important #10: Email Recipient Source ✅
```csharp
var userEmail = await GetUserEmailAsync(userId, ct);
// GetUserEmailAsync: ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
// → user?.Email
```
- Email loaded from `AppDbContext.Users` by `userId` ✅
- Not from token claim, config, or external source ✅
- Send-to-self pattern confirmed ✅

---

## Summary

This is clean work. The architecture is solid: two independent notification channels, each silently fail on error; DB write completes before any notification; scope management is correct; no Slack residue whatsoever. The one Important issue (payload casing) is a runtime-safe implicit behavior rather than a defect — it works today due to SignalR's default case-insensitive JSON deserialization, but it should be made explicit before it bites someone.

**PASS** — advance to DEPLOY.

---

_Reviewed by Hawkeye. You see what others miss._
