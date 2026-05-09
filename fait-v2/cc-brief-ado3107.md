# CC Brief — ADO#3107: G7 Scheduled Task Approval Gate for External Write Actions

## Context
Working in `/home/fredw/projects/fip/fait-v2/`.

G2 (ADO#3103) added pre-send approval for interactive chat via SignalR — when CC calls `requireApproval()` in the harness, it POSTs to `/api/intervention/request` which pushes a SignalR event to the active browser session and waits for user response.

G7 extends this concept to **scheduled tasks** — which run in the background with no active browser session. The flow is different: instead of waiting for real-time SignalR approval, we:
1. Store a pending approval record in DB
2. Send the user an email with approve/deny links
3. Immediately return `{ approved: false, reason: 'Approval required — email notification sent' }` to CC
4. Separately, the user can POST to an endpoint to approve/deny later

---

## Part 1: New DB Model + EF Migration

### 1a. Create Model `Data/Models/ScheduledTaskApproval.cs`

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("scheduled_task_approvals")]
public class ScheduledTaskApproval
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("scheduled_task_id")]
    [MaxLength(36)]
    [Required]
    public string ScheduledTaskId { get; set; } = string.Empty;

    [Column("intervention_id")]
    [MaxLength(36)]
    [Required]
    public string InterventionId { get; set; } = string.Empty;

    [Column("action_type")]
    [MaxLength(100)]
    public string ActionType { get; set; } = string.Empty;

    [Column("action_summary")]
    [MaxLength(2000)]
    public string ActionSummary { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending | approved | denied | expired

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }
}
```

### 1b. Add DbSet to `Data/FaitV2DbContext.cs`

Add after the existing `ScheduledTaskRuns` DbSet:
```csharp
public DbSet<ScheduledTaskApproval> ScheduledTaskApprovals => Set<ScheduledTaskApproval>();
```

### 1c. Create EF Migration

Run this command to create the migration:
```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet ef migrations add AddScheduledTaskApprovals
```

The migration should create table `scheduled_task_approvals` with columns:
- `id` VARCHAR(36) PK
- `scheduled_task_id` VARCHAR(36) NOT NULL
- `intervention_id` VARCHAR(36) NOT NULL
- `action_type` VARCHAR(100)
- `action_summary` VARCHAR(2000)
- `status` VARCHAR(20) DEFAULT 'pending'
- `created_at` DATETIME
- `expires_at` DATETIME
- `resolved_at` DATETIME NULL

---

## Part 2: Two new API endpoints in `Program.cs`

### 2a. `POST /api/scheduled-tasks/approval/request`

Auth: `X-Internal-Token` header check (same pattern as `/api/intervention/request`).

Request body (add a record near the bottom of Program.cs with other request body records):
```csharp
public record ScheduledTaskApprovalRequestBody(
    string ScheduledTaskId,
    string InterventionId,
    string ActionType,
    string ActionSummary,
    string? UserId
);
```

Handler:
1. Check `X-Internal-Token` header against `config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token"` — return 401 if invalid
2. Validate required fields (ScheduledTaskId, InterventionId, ActionType, ActionSummary)
3. Create and save `ScheduledTaskApproval` record to DB
4. If `request.UserId` is provided:
   - Look up the user in DB to get their email
   - Use `IScheduledTaskNotificationService` to send a notification email (see below)
   - Actually, since we don't have a dedicated "approval email" method on that service, just use `IDbContextFactory<FaitV2DbContext>` to find the user, then `IMicrosoftTokenService` + HttpClient to send a custom email
   - Subject: `[FAIT] Approval Required: {request.ActionType}`
   - Body (HTML): 
     ```
     <h3>Scheduled Task Approval Required</h3>
     <p>Your scheduled task wants to perform the following action:</p>
     <p><strong>Action:</strong> {request.ActionSummary}</p>
     <p>This approval request expires in 24 hours.</p>
     <p><em>To approve or deny, visit your FAIT dashboard and check the scheduled tasks page.</em></p>
     ```
   - Send via Graph API (same pattern as ScheduledTaskNotificationService — get token via `IMicrosoftTokenService.GetValidAccessTokenAsync(user.EntraOid)`, then POST to `/me/sendMail`)
   - If email fails → log warning, continue (never throw)
5. Return 200 `{ ok = true, approvalId = approval.Id }`

The endpoint should be:
```csharp
app.MapPost("/api/scheduled-tasks/approval/request", async (...) => { ... })
    .AllowAnonymous(); // guarded by X-Internal-Token
```

### 2b. `POST /api/scheduled-tasks/approval/respond`

Auth: Standard cookie auth (`.RequireAuthorization()`).

Request body (add a record):
```csharp
public record ScheduledTaskApprovalRespondBody(
    string ApprovalId,
    string Response  // "approved" | "denied"
);
```

Handler:
1. Get current user's ID from claims (pattern: `httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value` or the existing pattern used in other endpoints — check how other authed endpoints get the userId)
2. Look up the `ScheduledTaskApproval` by `ApprovalId`
3. If not found → 404
4. If `Status != "pending"` → 400 with message "Approval already resolved"
5. If `ExpiresAt < DateTime.UtcNow` → update status to "expired", save, return 400 "Approval expired"
6. Set `Status` = request.Response (validate it's "approved" or "denied"), `ResolvedAt` = DateTime.UtcNow
7. Save
8. Return 200 `{ ok = true }`

Look at existing authenticated endpoints in Program.cs to match the auth pattern. The app uses Entra cookie auth.

---

## Part 3: Harness modification (`agent-harness/harness-server.js`)

In `harness-server.js`, the `/turn` endpoint receives a request body. Currently, the `requireApproval()` function always does the real-time SignalR path (POSTs to `/api/intervention/request`).

### 3a. Add `isScheduledTask` flag to turn parsing

In the `/turn` handler body parsing block (where `sessionId`, `userId`, `message` etc. are destructured from `rawBody`):

Add:
```javascript
const isScheduledTask = rawBody.IsScheduledTask ?? rawBody.isScheduledTask ?? false;
```

### 3b. Modify `requireApproval()` to check scheduled task context

The current `requireApproval` function signature is:
```javascript
async function requireApproval(userId, actionType, actionSummary, actionDetails)
```

This function is called inside tool handlers like `graph_send_email` and `ado_update_work_item`. The problem: these tool handlers don't have access to `isScheduledTask` from the turn context.

Solution: Create a **per-turn context object** that the tool handlers can access. Add at the module level:

```javascript
// Per-turn context — set at the start of each /turn request, cleared after
const activeTurnContext = new Map(); // sessionId → { isScheduledTask, userId }
```

In the `/turn` handler, near where `sessionId` and `userId` are extracted, add:
```javascript
const turnCtxKey = sessionId || userId; // fallback to userId if no sessionId
if (turnCtxKey) {
    activeTurnContext.set(turnCtxKey, { isScheduledTask: isScheduledTask === true, userId });
}
```

And at the end of the `/turn` handler (both SSE-end paths), clean up:
```javascript
if (turnCtxKey) activeTurnContext.delete(turnCtxKey);
```

But this context approach is complex. **Simpler approach**: 

Since `requireApproval` is called from tool handlers that only receive `userId`, we can store the `isScheduledTask` flag in a simple module-level Map keyed by userId:

```javascript
// Track which userIds are currently in a scheduled task turn
const scheduledTaskUsers = new Set();
```

In the `/turn` handler body, after extracting `isScheduledTask`:
```javascript
if (isScheduledTask === true) {
    scheduledTaskUsers.add(userId);
} else {
    scheduledTaskUsers.delete(userId);
}
```

Clean up after the turn completes (both CC and Bedrock paths should clean up). For the CC path, it's in the `ccProcess.on('close', ...)` callback. For the Bedrock path, at the end. Add `scheduledTaskUsers.delete(userId)` in both places.

### 3c. Modify `requireApproval()` to take the async-safe path for scheduled tasks

Replace the current `requireApproval` function with this updated version:

```javascript
async function requireApproval(userId, actionType, actionSummary, actionDetails) {
    const interventionId = crypto.randomUUID();

    // G7: If this user is in a scheduled task context, use async-safe path
    if (scheduledTaskUsers.has(userId)) {
        try {
            const headers = { 'Content-Type': 'application/json' };
            if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
            await fetch(`${FAIT_BASE_URL}/api/scheduled-tasks/approval/request`, {
                method: 'POST',
                headers,
                body: JSON.stringify({
                    ScheduledTaskId: '', // not available at harness level — blank
                    InterventionId: interventionId,
                    ActionType: actionType,
                    ActionSummary: actionSummary,
                    UserId: userId
                })
            });
        } catch (err) {
            console.error('[harness] G7 requireApproval: failed to store approval request:', err.message);
        }
        // Immediately return denied — CC continues without waiting
        return false;
    }

    // G2: Real-time SignalR path (interactive turns)
    try {
        const headers = { 'Content-Type': 'application/json' };
        if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
        await fetch(`${FAIT_BASE_URL}/api/intervention/request`, {
            method: 'POST',
            headers,
            body: JSON.stringify({ userId, interventionId, actionType, actionSummary, actionDetails })
        });
    } catch (err) {
        console.error('[harness] requireApproval: failed to send intervention request:', err.message);
        throw new Error('Could not reach Blazor to request approval — action cancelled');
    }

    // Wait for user response (timeout: 5 minutes)
    return new Promise((resolve, reject) => {
        pendingInterventions.set(interventionId, { resolve, reject });
        setTimeout(() => {
            if (pendingInterventions.has(interventionId)) {
                pendingInterventions.delete(interventionId);
                reject(new Error('Intervention timed out after 5 minutes — action cancelled'));
            }
        }, 5 * 60 * 1000);
    });
}
```

Also update the `/turn` handler to pass `isScheduledTask` context — in `TurnRequest` if using the TaskMode path. For the harness, the `isScheduledTask` flag comes from the request body. Update the harness to also pass it forward in the CC process invocation (it's already handled via the `scheduledTaskUsers` Set).

### 3d. Pass `isScheduledTask` from .NET to harness

In `FargateUserAgentRuntime.cs` (or wherever `TurnRequest` is serialized to send to the harness `/turn` endpoint), check if `TaskMode = true` from a scheduled task context. 

Actually: the harness `/turn` endpoint needs `IsScheduledTask` in the body. Look at `TurnRequest` record in `IUserAgentRuntime.cs` — add a new property:

In `Services/IUserAgentRuntime.cs`, find the `TurnRequest` record and add:
```csharp
bool IsScheduledTask = false      // §G7 — signals harness to use async-safe approval path
```

In `Services/ScheduledTaskBackgroundService.cs`, where `TurnRequest` is constructed for `TaskMode = true` runs:
```csharp
var turnRequest = new TurnRequest(
    UserId: task.UserId,
    Message: task.Prompt,
    SystemPrompt: "You are a scheduled task executor. Complete the requested task and provide a concise response.",
    TaskMode: true,
    IsScheduledTask: true   // §G7
);
```

---

## Files to create:
- `src/FortressAI.V2.Web/Data/Models/ScheduledTaskApproval.cs`

## Files to modify:
- `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs` — add DbSet
- `src/FortressAI.V2.Web/Program.cs` — add two endpoints + request body records
- `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` — add `IsScheduledTask` to TurnRequest
- `src/FortressAI.V2.Web/Services/ScheduledTaskBackgroundService.cs` — pass `IsScheduledTask: true` in TurnRequest
- `agent-harness/harness-server.js` — add scheduled task approval path to `requireApproval`, add `scheduledTaskUsers` Set

## Files to read first:
- `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs`
- `src/FortressAI.V2.Web/Program.cs` (around line 550 for intervention endpoint pattern, and look for how authenticated endpoints get userId from claims)
- `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` (TurnRequest record)
- `src/FortressAI.V2.Web/Services/ScheduledTaskBackgroundService.cs` (TurnRequest construction)
- `agent-harness/harness-server.js` (requireApproval function and /turn handler)
- Latest migration file to understand migration pattern

## Commands to run:
After making all changes:
1. `cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet ef migrations add AddScheduledTaskApprovals 2>&1`
2. `cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet build 2>&1 | tail -20`
3. `node --check /home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js 2>&1`

## Acceptance criteria:
- `Data/Models/ScheduledTaskApproval.cs` created with correct table attributes
- `FaitV2DbContext` has `ScheduledTaskApprovals` DbSet
- EF migration `AddScheduledTaskApprovals` created
- `POST /api/scheduled-tasks/approval/request` endpoint in Program.cs — X-Internal-Token guarded
- `POST /api/scheduled-tasks/approval/respond` endpoint in Program.cs — requires auth cookie
- `IUserAgentRuntime.TurnRequest` has `IsScheduledTask = false` parameter
- `ScheduledTaskBackgroundService` passes `IsScheduledTask: true` in TaskMode path
- `harness-server.js` has `scheduledTaskUsers` Set and modified `requireApproval` with G7 path
- `dotnet build` 0 errors
- `node --check` passes
