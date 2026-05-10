# Review Report: ADO#3171
## 3.2-B: Slack Notifications on Task Completion/Failure

## CC Invocation
```bash
cd /home/fredw/projects/fip/fait && cat /tmp/clint-review-brief-3171.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Verdict: PASS

---

## AC Verification

### 1. alert_on_completion=true → DM on success: ✅ PASS
**Evidence:** `ScheduledTaskBackgroundService.cs:192` — guard is `if (newStatus == "success" && task.AlertOnCompletion && userEmail != null)`. When all three are true, calls `slackSvc.SendDmAsync(userEmail, "✅ Scheduled task *{task.Name}* completed successfully.\n{summary}")` at line 196. Summary is truncated to 200 chars at line 194-195 (`resultSummary[..200]`). Task name + result summary confirmed.

### 2. alert_on_completion=false → no DM on success: ✅ PASS
**Evidence:** Same guard at line 192 — `task.AlertOnCompletion` must be true. If false, the `if` condition fails and no DM is sent. Confirmed by code structure.

### 3. alert_on_failure=true → DM on permanent failure (failure_count >= 2): ✅ PASS
**Evidence:** `ScheduledTaskBackgroundService.cs:199` — `else if (newStatus == "failed" && taskToUpdate?.FailureCount >= 2 && task.AlertOnFailure && userEmail != null)`. When conditions met, fires DM at line 201: `"⚠️ Scheduled task *{task.Name}* has stopped retrying after 2 failures and requires your attention.\nError: {errorMessage}\nReview at: https://fait.fortressam.ai/tasks"`. Message includes task name, error, `/tasks` link, and explicitly states "stopped retrying." The `>= 2` check is on the post-increment value (see AC#3 detail below).

### 4. alert_on_failure=false → no DM on failure: ✅ PASS
**Evidence:** `task.AlertOnFailure` must be true in the `else if` at line 199. If false, condition fails, no DM fires.

### 5. try/catch wrapping confirmed (notify never affects task status): ✅ PASS
**Code trace:** The notification block spans lines 187–208:
```csharp
try                                                                              // line 187
{
    var slackSvc = services.GetRequiredService<ISlackNotificationService>();     // line 189
    var userEmail = await LoadUserEmailAsync(dbFactory, task.UserId, ct);        // line 190
    if (newStatus == "success" && ...)
        await slackSvc.SendDmAsync(userEmail, ...);                              // line 196
    else if (newStatus == "failed" && ...)
        await slackSvc.SendDmAsync(userEmail, ...);                              // line 201
}
catch (Exception ex)                                                             // line 205
{
    _logger.LogWarning(ex, "Slack notification failed for task {TaskId} — task status unaffected", task.Id);
}                                                                                // line 208
```
All three operations — `GetRequiredService` (line 189), `LoadUserEmailAsync` (line 190), and both `SendDmAsync` calls (lines 196, 201) — are inside the try block. The catch only logs; it does not touch `last_run_status`, `failure_count`, or any task field. `LoadUserEmailAsync` additionally has its own internal try/catch (lines 213–222) and always returns `null` on failure rather than throwing. `SendDmAsync` also has a full catch-all internally (`SlackNotificationService.cs:70`) and never rethrows. Defense in depth: both layers swallow exceptions.

### 6. Ordering confirmed (notify after SaveChanges): ✅ PASS
**Code trace (success path):**
1. `taskToUpdate.LastRunStatus = "success"` — line 156
2. `await ctx.SaveChangesAsync(ct)` — line 184
3. Notification try block begins — line 187

**Code trace (failure path):**
1. `taskToUpdate.FailureCount++` — line 165
2. `taskToUpdate.LastRunStatus = "failed"` — line 166
3. `taskToUpdate.IsActive = false` (if count >= 2) — line 178
4. `await ctx.SaveChangesAsync(ct)` — line 184
5. Notification try block begins — line 187

In both paths, `SaveChangesAsync` is unconditionally at line 184 and the notification block begins at line 187. There is no code path that can reach notification without passing through `SaveChangesAsync` first — they are sequential in the same method with no branches between them.

### 7. No new infra beyond 2 service files: ✅ PASS
**Evidence:** Only two new files confirmed: `ISlackNotificationService.cs` and `SlackNotificationService.cs`. `ScheduledTaskBackgroundService.cs` and `Program.cs` are modified (not new). No new NuGet packages introduced — all usings reference existing BCL/ASP.NET packages. A named HttpClient `"slack"` is registered in Program.cs (lines 315–319), but this is configuration-level wiring within `Program.cs`, not a new service file.

### 8. No email notifications: ✅ PASS
**Evidence:** All references to `email`/`Email` in the four files refer to the user's email address used as a Slack lookup key for `users.lookupByEmail` API. No SMTP, mailkit, sendgrid, `IEmailService`, or any email-transport reference exists in the diff.

---

## Issues Found

### Nitpick — `AddHostedService` in Program.cs (line 109)
`builder.Services.AddHostedService<ScheduledTaskBackgroundService>()` appears in the diff. If `ScheduledTaskBackgroundService` was already registered before this WI, this would be a duplicate registration. **Not blocking** — if it's a new registration as part of this task, it's correct. Tony should confirm this was intentional and not a duplicate.

### Nitpick — `SlackNotificationService` registered as Scoped, consumed in a hosted service
`ISlackNotificationService` is registered Scoped (`AddScoped`) per Program.cs line 110. `ScheduledTaskBackgroundService` is a `BackgroundService` (singleton-lifetime hosted service) and correctly resolves `ISlackNotificationService` via a DI scope (`services.GetRequiredService<ISlackNotificationService>()` where `services` is a scoped service provider from `IServiceScopeFactory`). This is correct and safe — no captive dependency issue. Noting for visibility only.

### Nitpick — `_botToken` null behavior
If `Slack__BotToken` is not configured, `SendDmAsync` logs a warning and returns silently. This is intentional best-effort behavior per the interface contract. However, there is no startup validation warning when the config key is absent. In dev/staging, this could lead to silent Slack failures. Low priority.

---

## Notes

- **AC#3 (failure_count >= 2 is post-increment):** Confirmed. `FailureCount++` fires at line 165. The notification check at line 199 reads `taskToUpdate?.FailureCount >= 2` — this is the post-increment, post-save value. It is impossible for this notification to fire on the first failure (count would be 1 at that point, which falls into the retry branch). The deactivation branch (`else` at line 175) and the notification condition (`>= 2` at line 199) are in exact lockstep.

- **Overall code quality:** Clean, minimal, well-scoped. The fire-and-forget notification pattern is correctly implemented. The try/catch placement is textbook — all fallible operations are inside the boundary, DB state is fully committed before any notification attempt, and the catch is inert with respect to task state.

- **The `LoadUserEmailAsync` double-protection pattern** (internal catch returns null + outer catch) means even an unexpected DB error during email lookup cannot cause a notification exception to surface or affect the task record.

---

*Reviewed by Hawkeye (Clint Barton) | CC Sonnet | ADO#3171 | 2026-05-10*
