# Review Report: ADO#1484
## FIRM: Stop Recording Button

### Review Verdict: NEEDS-CHANGES

---

### CC Invocation Used

```bash
cd /home/fredw/projects/fip/firm && cat /tmp/review-brief-1484.md | claude --model sonnet --print --dangerously-skip-permissions
```

Brief written to `/tmp/review-brief-1484.md` — 35-item adversarial checklist covering all four modified files plus cross-file consistency. CC read all files in full and returned per-item verdicts. Exit code 0.

---

### Consistency Audit

| Check | Result |
|-------|--------|
| Controller route `/api/vp/stop/{meetingId}` ↔ Razor `PostAsync($"/api/vp/stop/{meetingId}", null)` | ✅ Exact match |
| `MeetingStatus.Recording` — controller status check ↔ Razor render condition | ✅ Both use `MeetingStatus.Recording` |
| `meetingId` type — controller `long` ↔ `HashSet<long>` ↔ `context.Id` | ✅ All `long`, consistent |
| `BotTaskArn` on `FirmMeeting` model | ✅ `string? BotTaskArn` — correct nullable, `string.IsNullOrEmpty()` guard appropriate |
| `process.once` (not `process.on`) in index.ts | ✅ Confirmed `process.once` |
| Stop button sibling vs. nested in `<MudTooltip>` | ✅ Sibling — outside the tooltip |

All consistency checks pass. No cross-file contract violations.

---

### Critical Issues

None.

---

### Important Issues

#### I1: `VpBotService.StopBotAsync` — Silent return on unconfigured cluster produces false success

**File:** `VpBotService.cs` lines 94–98  
**Severity:** Important (not critical because it's a misconfiguration path, but it produces a user-visible lie)

**The bug:**

```csharp
var cluster = _config["Firm:EcsCluster"];
if (string.IsNullOrEmpty(cluster) || string.IsNullOrEmpty(taskArn))
{
    _logger.LogWarning("FIRM: StopBotAsync called with empty cluster or taskArn");
    return;   // ← silent return, no exception
}
```

When `Firm:EcsCluster` is missing from config, `StopBotAsync` logs a warning and returns. The controller's `catch` block never fires:

```csharp
await _vpBotService.StopBotAsync(meeting.BotTaskArn);
return Ok(new { status = "stop_signal_sent", ... });  // ← reached even though nothing was sent
```

**User experience:** "Stop signal sent to bot. Recording will complete shortly." — but no ECS stop was sent. The recording continues indefinitely.

**Note on the `taskArn` guard:** The controller already guards `BotTaskArn` empty before calling this method (returns `no_bot`). So `string.IsNullOrEmpty(taskArn)` in the service guard is a belt-and-suspenders check that's fine to keep — but the `cluster` check is the real exposure.

**Fix:**

```diff
- _logger.LogWarning("FIRM: StopBotAsync called with empty cluster or taskArn");
- return;
+ _logger.LogError("FIRM: StopBotAsync — ECS cluster not configured (Firm:EcsCluster missing)");
+ throw new InvalidOperationException("ECS cluster not configured — cannot stop ECS task");
```

This ensures the controller's catch block returns `bot_unreachable` instead of falsely reporting success.

---

#### I2: ECS SIGTERM→SIGKILL window vs. post-processing pipeline (operational risk, not a code bug)

**File:** `index.ts` + ECS task definition (out of scope for this PR but worth flagging)

After SIGTERM, ECS waits `stopTimeout` (default: **30 seconds**) before sending SIGKILL. The post-SIGTERM pipeline is: `bot.stop()` → upload audio → transcribe → summarize → callback. For any recording over a few minutes, SIGKILL will fire before the pipeline completes, losing the transcript and summary (though the raw audio upload to S3 is step 2, so the file survives).

The code is correct — the handler is fire-and-forget with `.catch()`, no unhandled rejection risk. The operational fix is to set `stopTimeout` in the ECS task definition to 900 seconds. Tony should create a follow-up task or add a note to the deployment runbook.

---

### Nitpicks

- **N1** `index.ts:164` — `if (bot && bot.isCurrentlyRecording())` — the `bot &&` guard is unnecessary since `bot` is always defined by line 161 within the same function scope. Harmless noise.
- **N2** `MeetingsApiController.cs:648–649` — The `BadRequest` error response has a different shape (`{ error }`) than all other responses (`{ status, message }`). Unreachable from the UI (button only renders when Recording) but inconsistent for API consumers.
- **N3** `Meetings.razor:151–152` — `_stoppingMeetingIds.Contains(context.Id)` called twice in the same template expression (once for `Disabled`, once for the ternary label). Not a bug, just a minor redundancy in the render path. A local var `var isStopping = _stoppingMeetingIds.Contains(context.Id)` would be cleaner, but Razor templates don't support inline locals easily — not worth the ceremony.
- **N4** Route namespace: the stop endpoint lives under `/api/vp/` but conceptually belongs under `/api/meetings/{id}/stop`. Works fine, won't block, but the mixed namespace will surprise API consumers navigating the route surface.

---

### Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| SIGTERM handler registered AFTER `new MeetingBot(...)`, BEFORE `bot.join()` | ✅ Lines 161 → 164 → 207 in index.ts |
| Handler checks `bot.isCurrentlyRecording()` before `bot.stop()` | ✅ Confirmed |
| Not recording → `process.exit(0)` | ✅ Confirmed |
| `process.once` (not `process.on`) | ✅ Confirmed |
| `[Authorize]` on stop endpoint | ✅ Confirmed |
| `ResolveOwnedMeeting` ownership validation | ✅ Confirmed |
| Status check: `MeetingStatus.Recording` before proceeding | ✅ Returns 400 if not Recording |
| Returns `no_bot` if `BotTaskArn` null/empty | ✅ `string.IsNullOrEmpty` guard returns `ok/no_bot` |
| Calls `_vpBotService.StopBotAsync(meeting.BotTaskArn)` | ✅ Confirmed |
| Returns `stop_signal_sent` on success | ✅ Confirmed |
| Catches exception → `bot_unreachable` (never 500) | ✅ Confirmed — but see I1 (silent-return bypasses catch entirely) |
| Does NOT change meeting status directly | ✅ Confirmed — no status mutation |
| `StopBotAsync` uses `System.Threading.Tasks.Task` return type | ✅ Fully qualified, no Amazon.ECS.Model.Task ambiguity |
| `StopTaskRequest` has Cluster + Task + Reason | ✅ All three fields set |
| Re-throws on ECS failure | ✅ `throw;` on catch — but I1 covers the silent-return path |
| `_stoppingMeetingIds` field `HashSet<long>` | ✅ Declared on line 184 |
| Stop button only rendered for `MeetingStatus.Recording` | ✅ Confirmed |
| Button OUTSIDE `<MudTooltip>` (sibling) | ✅ Confirmed |
| `Disabled="@_stoppingMeetingIds.Contains(context.Id)"` | ✅ Confirmed |
| Adds to set → `InvokeAsync(StateHasChanged)` → HTTP call → removes in `finally` | ✅ Confirmed |
| No `Dense="true"` on new elements | ✅ Confirmed |
| No `page` as foreach variable | ✅ Confirmed (`_page` field, `upcomingCard` in foreach) |
| No bare `GetValue<T>()` calls | ✅ Confirmed |
| Route `/api/vp/stop/{meetingId}` ↔ Razor URL — exact match | ✅ Confirmed |

**22 of 23 criteria verified clean. 1 criterion partially passes (I1 is a related-but-distinct path).**

---

### Positive Observations

- **Async handler done right** — `bot.stop()` is called as a fire-and-forget promise with `.catch()` inline. No `async` on the SIGTERM handler, no unhandled rejection risk, no accidental process hang.
- **`process.once` discipline** — Double-fire prevention was explicitly considered and correctly implemented. Good instinct.
- **False-success immunity where covered** — The null `BotTaskArn` path is correctly handled by the controller (not the service), and the ECS exception path correctly re-throws. The `System.Threading.Tasks.Task` disambiguation was a nice catch by Tony.
- **UI state management** — The `_stoppingMeetingIds` + `finally` pattern is the correct idiomatic Blazor approach. No state leak on error.
- **No status short-circuit** — Correctly delegating status transition to the bot's existing callback pipeline rather than setting `Stopped` directly in the controller. This preserves the single source of truth for recording state.

---

### What Tony Needs to Fix

**I1 — VpBotService.cs, line 98:**

Change:
```csharp
_logger.LogWarning("FIRM: StopBotAsync called with empty cluster or taskArn");
return;
```

To:
```csharp
_logger.LogError("FIRM: StopBotAsync — ECS cluster not configured (Firm:EcsCluster missing)");
throw new InvalidOperationException("ECS cluster not configured — cannot stop ECS task");
```

This is the only blocking fix. Everything else is nitpick-level.

**I2 — Optional follow-up:**
Create a task or runbook note to set ECS `stopTimeout` to 900s on the bot task definition.

---

*Review by Hawkeye (Clint Barton) — Cycle 1 of 2*
