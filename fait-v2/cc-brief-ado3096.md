# CC Brief — ADO#3096: Scheduled Task Email Notifications on Completion and Failure

## Context
Working in `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`.

The `ScheduledTaskBackgroundService` runs scheduled tasks and updates their status. After each task run completes (success or failure), we need to send the user an email notification — fire-and-forget, never blocking the background loop.

Email is sent via MS Graph API. The existing `MicrosoftTokenService` (`Services/MicrosoftTokenService.cs`) handles OAuth token retrieval using `IMicrosoftTokenService`. Look at how it works — specifically `GetValidAccessTokenAsync(string entraOid)` which takes the Entra OID (NOT the internal user ID).

The `User` model (`Data/Models/User.cs`) has:
- `Id` (internal GUID-style string — this is what `ScheduledTask.UserId` stores)
- `EntraOid` (Entra OID — needed for MS Graph token lookup)
- `Email` (user's email address)

The `ScheduledTask` model (`Data/Models/ScheduledTask.cs`) has:
- `AlertOnCompletion` (bool) — send email on success
- `AlertOnFailure` (bool, defaults true) — send email on failure
- `Name` (string)

The `ScheduledTaskRun` model (`Data/Models/ScheduledTaskRun.cs`) has:
- `Id`, `Status`, `OutputText`, `ErrorMessage`, `CompletedAt`

The DB context is `FaitV2DbContext` (`Data/FaitV2DbContext.cs`), which has `DbSet<User> Users`.

The Graph API base URL is `https://graph.microsoft.com/v1.0`. To send email via Graph, POST to `/me/sendMail` with a Bearer token. The endpoint accepts:
```json
{
  "message": {
    "subject": "...",
    "body": { "contentType": "HTML", "content": "..." },
    "toRecipients": [{ "emailAddress": { "address": "user@example.com" } }]
  }
}
```

---

## Task 1: Create `IScheduledTaskNotificationService` and `ScheduledTaskNotificationService`

Create file: `Services/ScheduledTaskNotificationService.cs`

```csharp
namespace FortressAI.V2.Web.Services;

public interface IScheduledTaskNotificationService
{
    Task SendCompletionEmailAsync(
        ScheduledTask task,
        ScheduledTaskRun run,
        string userId,
        CancellationToken ct = default);
}
```

Implementation `ScheduledTaskNotificationService`:
- Constructor inject: `IDbContextFactory<FaitV2DbContext>`, `IMicrosoftTokenService`, `IHttpClientFactory`, `ILogger<ScheduledTaskNotificationService>`
- `SendCompletionEmailAsync`:
  1. Look up the user in DB: `await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)` 
  2. If user is null → log warning, return (don't throw)
  3. Check AlertOnCompletion / AlertOnFailure flags:
     - If `run.Status == "success"` and `!task.AlertOnCompletion` → return (user doesn't want success emails)
     - If `run.Status != "success"` (i.e. failed) and `!task.AlertOnFailure` → return
  4. Get Graph token via `IMicrosoftTokenService.GetValidAccessTokenAsync(user.EntraOid)`
  5. If token is null → log warning "MS Graph not configured or token unavailable for userId={userId} — skipping notification", return
  6. Build subject: `[FAIT] Task '{task.Name}' {statusLabel}` where statusLabel is "completed successfully" or "failed"
  7. Build HTML body:
     ```html
     <h3>[FAIT] Scheduled Task Notification</h3>
     <p><strong>Task:</strong> {task.Name}</p>
     <p><strong>Run ID:</strong> {run.Id}</p>
     <p><strong>Status:</strong> {statusLabel}</p>
     <p><strong>Completed:</strong> {run.CompletedAt:u}</p>
     {if run.OutputText != null: <p><strong>Output Preview:</strong></p><pre>{first 500 chars of run.OutputText}</pre>}
     {if run.ErrorMessage != null && status != success: <p><strong>Error:</strong> {run.ErrorMessage}</p>}
     ```
  8. POST to `https://graph.microsoft.com/v1.0/me/sendMail` with the user's token
     - Use `IHttpClientFactory.CreateClient("MicrosoftGraphClient")` (already registered)
     - Content-Type: application/json
     - Authorization: Bearer {token}
     - Body: the sendMail payload (ContentType: "HTML")
  9. If the Graph call fails (non-2xx or exception) → log warning with status code, return (never throw)
  10. Log info: "Scheduled task notification sent for task={taskId} run={runId} status={status}"

Use `CancellationToken.None` for DB and HTTP calls inside this service (fire-and-forget callers won't pass a usable token).

---

## Task 2: Register the service in `Program.cs`

In `Program.cs`, find the block of `AddScoped` registrations (near line 188 where `IScheduledTaskService` is registered).

Add after `IScheduledTaskService` registration:
```csharp
builder.Services.AddScoped<IScheduledTaskNotificationService, ScheduledTaskNotificationService>();
```

---

## Task 3: Wire into `ScheduledTaskBackgroundService.cs`

In `ScheduledTaskBackgroundService.cs`:

1. Constructor: add `IServiceProvider _services` — it's already there. Good.

2. In `ProcessTaskAsync`, there are two branches (TaskMode=true and TaskMode=false), each ending with a final `await db.SaveChangesAsync(ct)` after updating the task status.

   After EACH `await db.SaveChangesAsync(ct)` (the final save in both branches AND in the catch block), add fire-and-forget notification:

   ```csharp
   // Fire-and-forget notification — never blocks the background loop
   _ = Task.Run(async () =>
   {
       try
       {
           using var notifyScope = _services.CreateScope();
           var notifySvc = notifyScope.ServiceProvider.GetRequiredService<IScheduledTaskNotificationService>();
           await notifySvc.SendCompletionEmailAsync(task, run, task.UserId);
       }
       catch (Exception ex)
       {
           _logger.LogWarning(ex, "Failed to send task notification for task {TaskId}", task.Id);
       }
   });
   ```

   Add this after the final `await db.SaveChangesAsync(ct)` in:
   - The TaskMode=true success/failure branch (after the `dbTask2` save)
   - The TaskMode=false (CC) success/failure branch (after the `dbTask` save)
   - The catch block (after the exception-path save)

   IMPORTANT: In the TaskMode path, `run` and `task` variables are in scope. In the catch block, `task` and `run` are also in scope. Make sure the fire-and-forget lambda captures them by value as needed (just reference them directly — they're local variables that won't be modified after the fire-and-forget is started).

---

## Files to modify:
- CREATE: `Services/ScheduledTaskNotificationService.cs`
- MODIFY: `Program.cs` — add scoped registration
- MODIFY: `Services/ScheduledTaskBackgroundService.cs` — add fire-and-forget calls

## Files to read first:
- `Services/ScheduledTaskBackgroundService.cs` (understand all branches)
- `Services/MicrosoftTokenService.cs` (understand Graph token pattern)
- `Data/Models/ScheduledTask.cs`
- `Data/Models/ScheduledTaskRun.cs`
- `Data/Models/User.cs`
- `Data/FaitV2DbContext.cs`
- `Program.cs` (find registration location)

## Acceptance criteria:
- New interface + implementation in `Services/ScheduledTaskNotificationService.cs`
- Registered as scoped in `Program.cs`
- `ScheduledTaskBackgroundService` calls fire-and-forget after each run completion (success, failure, exception)
- AlertOnCompletion / AlertOnFailure flags are respected
- If Graph token unavailable → log warning, return (no throw, no crash)
- `dotnet build` 0 errors

## After completing:
Run `cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet build 2>&1 | tail -20` and include the output in your response.
