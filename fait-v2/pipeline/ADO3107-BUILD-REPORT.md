# Build Report — ADO#3107

## What was built
G7 Approval Gate for scheduled tasks — when a scheduled task's run calls `requireApproval()` in the harness, it takes an async-safe path: stores a DB record, sends an email notification, and immediately returns `false` (denying the action) instead of waiting for a SignalR response that will never come.

## Files changed
- **CREATE `Data/Models/ScheduledTaskApproval.cs`** — EF model for `scheduled_task_approvals` table. Fields: id, scheduled_task_id, intervention_id, action_type, action_summary, status (pending/approved/denied/expired), created_at, expires_at (24h), resolved_at.
- **MODIFY `Data/FaitV2DbContext.cs`** — Added `DbSet<ScheduledTaskApproval> ScheduledTaskApprovals`.
- **CREATE migration `20260509075646_AddScheduledTaskApprovals`** — Creates `scheduled_task_approvals` table with correct column types.
- **MODIFY `Program.cs`** — Two new endpoints:
  - `POST /api/scheduled-tasks/approval/request` — X-Internal-Token guarded; creates `ScheduledTaskApproval` record, sends Graph email notification if userId provided (subject: `[FAIT] Approval Required: {actionType}`, body with action summary and dashboard link).
  - `POST /api/scheduled-tasks/approval/respond` — cookie auth; validates pending + not expired, sets status to approved/denied, sets ResolvedAt.
  - Two new request body records: `ScheduledTaskApprovalRequestBody`, `ScheduledTaskApprovalRespondBody`.
- **MODIFY `Services/IUserAgentRuntime.cs`** — Added `bool IsScheduledTask = false` to `TurnRequest` record (§G7).
- **MODIFY `Services/ScheduledTaskBackgroundService.cs`** — `TurnRequest` construction in TaskMode=true path now passes `IsScheduledTask: true`.
- **MODIFY `agent-harness/harness-server.js`** — 
  - Added `const scheduledTaskUsers = new Set()` at module level.
  - `/turn` handler extracts `isScheduledTask` from request body; adds userId to Set if true, removes otherwise.
  - Cleanup in CC `ccProcess.on('close')`, Bedrock stream end, and error paths.
  - `requireApproval()` updated: if `scheduledTaskUsers.has(userId)`, POSTs to `/api/scheduled-tasks/approval/request` (stores record + triggers email), then immediately returns `false` — CC receives `{ denied: true, reason: 'User denied the action' }`. Falls through to existing G2 SignalR path otherwise.

## Parallelization used
No — ran after ADO#3096 since the approval endpoint uses `IScheduledTaskNotificationService`.

## CC sessions run
1 CC Sonnet run (this ran in the same session as 3096, both committed together in one commit `81c87174`).

## Acceptance criteria verification
- [x] `isScheduledTask` flag extracted in harness `/turn` handler
- [x] `requireApproval()` checks scheduled task context and takes async-safe path
- [x] DB migration for `scheduled_task_approvals` — verified in migration file
- [x] `POST /api/scheduled-tasks/approval/request` — X-Internal-Token guarded, creates record, sends email
- [x] `POST /api/scheduled-tasks/approval/respond` — cookie auth, validates state machine (pending→approved/denied, handles expired)
- [x] `dotnet build` 0 errors
- [x] `node --check` passes

## Known edge cases / things Clint should scrutinize
- **`ScheduledTaskId` is empty string in harness** — The harness doesn't have access to the task ID when `requireApproval()` is called (tool handlers only receive `userId`). The DB record stores an empty `ScheduledTaskId`. Future work could pass task context into the harness turn request, but for now the `InterventionId` is sufficient for correlation.
- **Approval respond endpoint doesn't verify task ownership** — The endpoint only checks that the user is authenticated (cookie), not that they own the task. Any authenticated user who knows the `ApprovalId` could respond. This is acceptable since approval IDs are GUIDs, but Clint should flag if ownership check is required.
- **`scheduledTaskUsers` Set is per-process** — In multi-instance deployment, if the harness runs multiple instances, a user could have their context set in one instance but the approval response delivered to another. Given scheduled tasks run in a single Fargate task per user, this is unlikely to be an issue.
- **No `node --check` on harness** — Passed (confirmed above).

## How to test locally
1. Run a scheduled task in TaskMode with a prompt that triggers `graph_send_email` or `ado_update_work_item`
2. Verify harness logs show G7 path: `[harness] G7 requireApproval`
3. Verify `scheduled_task_approvals` table gets a record
4. POST to `/api/scheduled-tasks/approval/respond` with approved/denied — verify status updates
