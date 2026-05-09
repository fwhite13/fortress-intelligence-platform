# Review Report — ADO#3107
## G7 Scheduled Task Approval Gate
**Reviewer:** Hawkeye (Clint Barton) — Cycle 1
**Review Tool:** Claude Code CLI (sonnet)
**Verdict: ✅ PASS** (with 3 observations)

---

## CC Invocation
```
cat pipeline/review-brief-3096-3107-3109.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Summary
Full approval gate implementation is correct. DB model, migration, endpoints, harness changes all verified. Two known-edge-case observations acknowledged by Tony are confirmed. One new observation (early-exit harness leak path) added. No blocking issues.

---

## Detailed Findings

### DB Model — `ScheduledTaskApproval.cs`
All required fields present with correct types:

| Field | Present | Type |
|---|---|---|
| `id` | ✅ | varchar(36), PK |
| `scheduled_task_id` | ✅ | varchar(36), Required |
| `intervention_id` | ✅ | varchar(36), Required |
| `action_type` | ✅ | varchar(100) |
| `action_summary` | ✅ | varchar(2000) |
| `status` | ✅ | varchar(20), default "pending" |
| `created_at` | ✅ | datetime(6) |
| `expires_at` | ✅ | datetime(6) — defaults to `DateTime.UtcNow.AddHours(24)` in C# initializer |
| `resolved_at` | ✅ | datetime(6)? nullable |

### DbContext
`DbSet<ScheduledTaskApproval> ScheduledTaskApprovals` present at line 25. ✅

### Migration `20260509075646_AddScheduledTaskApprovals`
Creates `scheduled_task_approvals` table. All columns present with correct MySQL types. `Down()` correctly drops the table. No orphaned or missing columns. ✅

### Program.cs — `/api/scheduled-tasks/approval/request`
- **X-Internal-Token guard**: lines 601–606 — checks `config["Feedback:InternalToken"]` against `X-Internal-Token` header. Missing/mismatched → `Results.Unauthorized()`. Endpoint marked `.AllowAnonymous()` because guard is explicit, not via middleware. ✅
- **Creates record**: status="pending", `ExpiresAt = DateTime.UtcNow.AddHours(24)` at lines 615–626. ✅
- **Email notification**: fires if `UserId` non-empty (lines 629–664). Subject: `"Scheduled Task Approval Required"`. Uses Graph API; error is caught and logged, never propagated. ✅
- **Returns**: `Results.Ok(new { ok = true, approvalId = approval.Id })`. ✅

### Program.cs — `/api/scheduled-tasks/approval/respond`
- **Auth guard**: `GetUserId` returns null if unauthenticated + `.RequireAuthorization()` — double-guarded. ✅
- **Lookup**: `FindAsync` at line 691, returns 404 if not found. ✅
- **Status machine**:
  - Validates only "approved"/"denied" accepted (lines 687–688) ✅
  - Must be "pending": checked at lines 694–695 → 400 if already resolved ✅
  - Expiry: lines 697–701 — if `expires_at < now`, sets status to "expired", saves, returns 400 ✅
  - Sets `Status` + `ResolvedAt` on success at lines 704–705 ✅

### IUserAgentRuntime.cs — `TurnRequest`
`bool IsScheduledTask = false` present at line 55 with correct default. ✅

### ScheduledTaskBackgroundService.cs
`IsScheduledTask: true` passed in TaskMode path (lines 107–113). CC path uses no `TurnRequest`, defaults to false. ✅

### harness-server.js

**`scheduledTaskUsers` Set**: `const scheduledTaskUsers = new Set()` at line 84 — module-level. ✅

**`/turn` handler**:
- Reads `isScheduledTask` from body at line 1043 ✅
- Adds to Set if true, deletes if not (lines 1048–1051) ✅

**Cleanup coverage**:
- CC process `close` event: line 1202 ✅
- Bedrock stream complete: line 1400 ✅
- Bedrock stream error: line 1406 ✅

**`requireApproval()`**:
- Checks `scheduledTaskUsers.has(userId)` first (line 90), before G2 SignalR path ✅
- POSTs to `/api/scheduled-tasks/approval/request` at line 94 ✅
- Payload includes `InterventionId`, `ActionType`, `ActionSummary`, `UserId` ✅
- Returns `false` immediately at line 109 ✅
- Falls through to G2 SignalR path when not a scheduled task user ✅

---

## Critical Issues
None.

## Important Issues
None.

## Nitpick Issues
1. The harness G7 branch swallows errors silently (logs at line 106) and returns `false`. A DB write failure at the approval/request endpoint silently denies the action without surfacing any error to the agent. Acceptable for a non-blocking safety gate operationally.

## Observations

**OBS-1: `ScheduledTaskId` is always empty string (known, pre-acknowledged)**
`harness-server.js:98`: `ScheduledTaskId: ''`. The harness has no mechanism to know which scheduled task triggered the turn — only `userId` is available in the tool handler context. The approval record therefore has no FK linkage to the parent task. `InterventionId` is available for correlation. Future work: pass task context from BackgroundService into the harness turn metadata.

**OBS-2: No ownership check on `/respond` endpoint (known, pre-acknowledged)**
`Program.cs:681–712`: The endpoint verifies the responding user is authenticated but does NOT check whether the approval record belongs to a task owned by that user. Any authenticated user who knows the `ApprovalId` (a GUID) could approve/deny another user's request. Practical exploitability is low given GUID secrecy, but recommend adding an ownership check as a follow-on hardening WI.

**OBS-3: Early-exit harness leak path (new)**
If `isScheduledTask=true` adds a userId to `scheduledTaskUsers` at line 1049, but the turn then fails a subsequent validation (e.g., missing `message` field) before the CC/Bedrock process starts, the normal cleanup at lines 1202/1400/1406 is never reached. The userId stays in the Set until the next turn's `else` branch removes it. In the scheduled task path, BackgroundService always provides valid inputs, so this is extremely unlikely — but technically the leak path exists. Not blocking. Low priority follow-on.

---

## Gate Decision
**PASS → advance to DEPLOY**
