# QA Report: ADO#1485b + ADO#1486

**QA Analyst:** Natasha Romanoff (Black Widow)  
**Date:** 2026-04-01  
**Test Start:** ~12:35 EDT  
**Test Duration:** ~6 minutes  

---

### QA Verdict: ✅ PASS

---

### Environment
- firm-web:75 (commit b23cfb9) — `fortress-tools-cluster`
- firm-vpbot:latest (commit 0e59e38) — ECR `firm-vpbot:latest`

---

### Test Results

| TC | Description | Result | Details |
|----|-------------|--------|---------|
| TC1 | firm-web:75 healthy | ✅ PASS | `firm-web:75` on `fortress-tools-cluster` — status=ACTIVE, rolloutState=COMPLETED, desiredCount=1, runningCount=1 |
| TC2 | VpCallback logging present | ✅ PASS | Line 93: entry log before secret validation. Line 163: post-UpdateStatusAsync success log. Both confirmed in commit b23cfb9. |
| TC3 | vpbot self-termination changes | ✅ PASS | `meeting-bot.ts`: `_noLeaveButtonCount` (line 73), `_recordingStartTime` (line 74), `_monitorInterval` (line 72) — all confirmed. Leave button check in `_endPollInterval` at lines 342–359. `index.ts`: `process.once('SIGTERM', ...)` at line 178, `safetyTimer.unref()` at line 123. |
| TC4 | postCallback HTTP status logging | ✅ PASS | `index.ts` line 96: `HTTP ${response.status}` on success. Line 99: error log with HTTP status + body on non-200. |
| TC5 | FipShared regression | ✅ PASS | `https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css` → HTTP 302 (not 404). |
| TC6 | TG cleanup verified | ✅ PASS | `meetings-web-dev-tg`: 1 target, state=healthy (172.31.73.3). No draining targets. |

---

### Detailed Findings

#### TC1 — firm-web ECS Service Health
```json
{
  "status": "ACTIVE",
  "desiredCount": 1,
  "runningCount": 1,
  "deployment": {
    "status": "PRIMARY",
    "rolloutState": "COMPLETED",
    "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:75"
  }
}
```

#### TC2 — VpCallback Logging (MeetingsApiController.cs, commit b23cfb9)
- **Line 93 (entry log, before secret validation):**
  ```csharp
  _logger.LogInformation("FIRM: VpCallback received — meetingId={MeetingId} status={Status}", payload?.MeetingId, payload?.Status);
  ```
- **Line 163 (post-UpdateStatusAsync success log):**
  ```csharp
  _logger.LogInformation("FIRM: VpCallback processed — meetingId={MeetingId} status={Status} → {MeetingStatus}", payload.MeetingId, payload.Status, meetingStatus);
  ```
- Both log points confirmed. Entry log fires on every inbound request (even if secret validation fails). Success log fires after the retry block completes.

#### TC3 — vpbot Self-Termination Changes (commit 0e59e38)
**meeting-bot.ts class fields:**
```typescript
private _endPollInterval: ReturnType<typeof setInterval> | null = null;  // line 71
private _monitorInterval: ReturnType<typeof setInterval> | null = null;  // line 72
private _noLeaveButtonCount: number = 0;                                  // line 73
private _recordingStartTime: number = 0;                                  // line 74
```
**Leave button detection in `_endPollInterval`** (lines 342–359): 60s grace period after recording start; 2 consecutive polls without Leave button → meeting ended.

**index.ts:**
- `process.once('SIGTERM', ...)` at line 178 — graceful stop handler
- `safetyTimer.unref()` at line 123 — safety net setTimeout doesn't block exit

#### TC4 — postCallback HTTP Logging (index.ts)
```typescript
// Line 96 — success path:
console.log(`[Pipeline] FIRM callback sent: ${status} — HTTP ${response.status}`);

// Line 99 — non-200 path:
console.error(`[Pipeline] FIRM callback FAILED: ${status} — HTTP ${response.status} — ${body}`);
```
Also confirmed at lines 280/283 for the inline `postFirmCallback` (API server mode).

#### TC5 — FipShared
```
curl → HTTP 302 ✅ (not 404)
URL: https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css
```

#### TC6 — Target Group Health
```json
[{ "ip": "172.31.73.3", "state": "healthy" }]
```
Single healthy target. All stale draining entries from earlier deploys cleared.

---

### fip repo HEAD
```
b23cfb9  fix(ADO#1485): add VpCallback entry logging + postCallback HTTP status logging
44a4990  fix(ADO#1486): vpbot self-termination — leave button detection + interval cleanup + exit safety net
```
Both commits present and verified.

---

### Test Summary
- Total tests: 6
- Passed: 6
- Failed: 0
- Warnings: 0

---

### Notes
- vpbot is on-demand Fargate (RunTask per meeting) — no persistent service to health-check. Image verification done via source (commit 0e59e38 at ECR push time per deploy report).
- Awaiting Fred live-meeting re-test to validate end-to-end callback flow in production conditions.

---

*Report by Natasha Romanoff — QA Analyst — 2026-04-01*
