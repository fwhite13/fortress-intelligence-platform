# QA Report: ADO#3173 — On-Demand Tab, History Tab, Failed-Task Banner

**QA Analyst:** Natasha Romanoff (Black Widow)  
**Date:** 2026-05-10  
**Commit:** `e13a800b` (fix) on top of `aa7573eb` (feat)  
**Task Def:** `fred-dev:160`  
**Target URL:** https://fait.dev.fortressam.ai  

---

## ⚠️ VERDICT: QA BLOCKED — Auth Bypass Not Configured

**Live browser QA was not possible due to two blockers:**

1. **Cloudflare managed challenge** — `fait.dev.fortressam.ai` presents a Cloudflare bot-protection challenge that blocks headless Chrome (OpenClaw's browser tool). The challenge requires a human-visible browser with real fingerprinting to pass.

2. **`TestAuth:Secret` not configured in task def** — The test-session bypass endpoint (`POST /auth/test-session`) returns `{"error":"Invalid secret"}` (HTTP 401) for all inputs because the `TestAuth__Secret` env var is not set in the `fred-dev:160` task definition. The `appsettings.Development.json` has `TestAuth.Secret = ""` (empty string), and `TestAuthService.ValidateSecret` returns `false` for empty/null expected values by design.

   - **Evidence:** Direct ALB call (`fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`) with Host: `fait.dev.fortressam.ai` confirmed the endpoint responds but rejects all secrets.
   - **ECS logs confirm:** `warn: FortressAI.Web.Controllers.TestAuthController[0] TestAuth: invalid secret attempt from 99.7.135.70` (two attempts logged)

**Per pipeline policy:** Partial Pass is not an accepted verdict when auth bypass fails. This is a deployment blocker.

---

## Service Health Check

| Check | Result |
|-------|--------|
| ECS service status | ✅ ACTIVE |
| Task definition | ✅ `fred-dev:160` |
| Desired count | ✅ 1 |
| Running count | ✅ 1 |
| ASPNETCORE_ENVIRONMENT | ✅ Development |
| Application running (ALB response) | ✅ App responds to requests |

---

## Source-Level Code Review (substitute for live testing)

Since live browser testing was blocked, I performed code-level verification of the deployed commits against the acceptance criteria. This is NOT a substitute for live QA but documents what was shipped.

### Commit `aa7573eb` — Feature commit

#### /tasks page — On-Demand tab

| AC | Code Evidence | Assessment |
|----|--------------|------------|
| On-Demand tab exists | `<MudTabPanel Text="On-Demand">` present in Tasks.razor | ✅ Code present |
| Shows list (name, prompt preview ~100 chars, last run, last_run_status badge) | Prompt preview: `Prompt.Length > 100 ? Prompt.Substring(0, 100) + "…" : Prompt`. Columns: Name, Prompt Preview, Last Run, Last Result | ✅ Code correct |
| "Run Now" button exists | `<MudButton ... OnClick="@(() => RunNowAsync(localTask))" StartIcon="@Icons.Material.Filled.PlayArrow">Run Now</MudButton>` | ✅ Code present |
| Run Now creates run row + triggers execution | `RunNowAsync` creates ScheduledTaskRun with Status="running", saves to DB, fires agent dispatch via `AgentRuntime.SendTurnAsync` in background Task | ✅ Logic correct |
| Edit works | `OpenEditOnDemandModalAsync` → `TaskEditModal` with `IsOnDemand=true` | ✅ Code present |
| Delete works | `ConfirmDeleteOnDemandAsync` → `TaskSvc.DeleteTaskAsync` | ✅ Code present |
| Empty state | `@if (_onDemandTasks == null \|\| _onDemandTasks.Count == 0)` → "No saved prompts yet." | ✅ Code present |

**On-Demand tab: 7/7 AC items present in code**

#### /tasks page — History tab

| AC | Code Evidence | Assessment |
|----|--------------|------------|
| History tab exists | `<MudTabPanel Text="History">` present | ✅ Code present |
| Shows flat list (task name, schedule type badge, started, duration, status badge) | Columns: Task, Type (MudChip recurring/on-demand), Started, Duration, Status | ✅ Code correct |
| Rows expandable — failed shows error, success shows result_summary | `ToggleExpand(runId)` + `ChildRowContent` renders `localRun.Error` for failed, `localRun.ResultSummary` for success | ✅ Logic correct |
| "Load More" when more pages exist; disappears after last | `_hasMoreHistory` flag drives button visibility (fixed in e13a800b) | ✅ Code correct |
| Empty state | `@if (_runHistory == null \|\| _runHistory.Count == 0)` → "No task history yet." | ✅ Code present |

**History tab: 5/5 AC items present in code**

#### /chat page — Failed-task banner

| AC | Code Evidence | Assessment |
|----|--------------|------------|
| Banner appears when IsActive=true && FailureCount>0 | `CheckFailedTasksAsync` queries `ScheduledTasks.AnyAsync(t => t.UserId == Session.UserId && t.IsActive && t.FailureCount > 0)` → sets `_hasFailedTasks` | ✅ Logic correct |
| Banner rendered conditionally | `@if (_agentReady && _hasFailedTasks && !_failedTaskBannerDismissed)` | ✅ Code correct |
| Banner is dismissible (X hides for session) | `ShowCloseIcon="true" CloseIconClicked="@(() => _failedTaskBannerDismissed = true)"` — session-scoped bool | ✅ Code correct |
| Banner contains link to /tasks | `<MudLink Href="/tasks">View tasks →</MudLink>` | ✅ Code present |
| Banner does NOT appear when no tasks failed | Gated on `_hasFailedTasks` which is false by default | ✅ Code correct |
| Banner uses MudAlert with Warning severity | `<MudAlert Severity="Severity.Warning" Dense="true" ...>` | ✅ Code present |

**Failed-task banner: 6/6 AC items present in code**

### Commit `e13a800b` — Fix commit (two bugs fixed)

#### Bug 1: CronExpression null on on-demand edit

- **Before:** `CronExpression = cron` — would pass the dropdown's current value (e.g. "0 9 * * *") when saving an on-demand edit, corrupting the task's schedule type semantics.
- **After:** `CronExpression = IsOnDemand ? null : cron` — on-demand tasks correctly get `null` CronExpression on update.
- **Assessment:** ✅ Fix is correct and complete.

#### Bug 2: `_hasMoreHistory` pagination flag missing

- **Before:** `@if (_runHistory.Count == HistoryPageSize)` — Load More button showed based on raw count equality, which would incorrectly hide the button if the page had exactly 50 items on initial load but then more existed, and would show it incorrectly after last page load.
- **After:** `_hasMoreHistory` boolean set after each load: `items.Count == HistoryPageSize`. Both `LoadHistoryAsync` and `LoadMoreHistoryAsync` now correctly set this flag.
- **Assessment:** ✅ Fix is correct. The `_hasMoreHistory` field was declared but not set in the initial feature commit — the fix properly initializes it in both load paths.

---

## Issues Found

### P0 — Auth Bypass Not Configured in Task Def

**Severity:** P0 — Deployment Blocker  
**Type:** Infrastructure / Pre-existing  
**Description:** `TestAuth__Secret` env var is missing from `fred-dev:160` task definition. The `appsettings.Development.json` contains `"TestAuth": { "Secret": "" }` (empty string). `TestAuthService.ValidateSecret` returns false for empty expected values, making the test-session endpoint permanently non-functional.  
**Impact:** Headless QA cannot authenticate. No live browser testing possible.  
**Fix:** Add `TestAuth__Secret=<value>` to the `fred-dev:160` task definition environment. Value should be a stable shared secret that matches what QA uses.  
**Note:** This may also affect Fred's ability to test the app without going through Entra MFA on every session.

### P1 — Cloudflare Bot Challenge Blocks Headless Browser

**Severity:** P1 — QA Process Blocker  
**Type:** Infrastructure  
**Description:** `fait.dev.fortressam.ai` is behind Cloudflare with "Managed Challenge" mode. The OpenClaw headless Chrome profile fails the challenge and cannot load any page. Even the ALB direct path redirects through Cloudflare.  
**Impact:** Browser-based QA cannot proceed without a human-controlled browser or CF exemption.  
**Fix options:**
1. Add IP allowlist in Cloudflare for the SteamServer egress IP (99.7.135.70)
2. Disable managed challenge for the dev subdomain
3. Use a CF bypass header/secret for internal tooling

---

## What Could Be Verified (Without Live Auth)

| Item | Method | Result |
|------|--------|--------|
| ECS service health (`fred-dev:160` running=1) | AWS CLI | ✅ PASS |
| App responds to HTTP requests | ALB curl | ✅ PASS |
| TestAuth endpoint exists and responds | ALB curl | ✅ Returns 401 (endpoint exists, wrong config) |
| On-Demand tab code — all 7 ACs | Source inspection | ✅ All present |
| History tab code — all 5 ACs | Source inspection | ✅ All present |
| Failed-task banner code — all 6 ACs | Source inspection | ✅ All present |
| CronExpression null fix correctness | Git diff inspection | ✅ Correct |
| _hasMoreHistory pagination fix correctness | Git diff inspection | ✅ Correct |

---

## Code Quality Notes (Observations, Not Blockers)

1. **`RunNowAsync` fire-and-forget on a background Task** — the `_ = Task.Run(...)` pattern in Blazor Server has a known risk: if the server recycles while the task is in flight, the run row will be stuck at `Status="running"` with no completion. This is an acceptable trade-off for a v1 on-demand feature but should be noted for future work.

2. **`GetTasksAsync` called twice on init** — `LoadRecurringTasksAsync` and `LoadOnDemandTasksAsync` both call `TaskSvc.GetTasksAsync(Session.UserId)` separately, then filter client-side. A single call + two filter passes would be more efficient. Not a bug, just an optimization opportunity.

3. **`_hasMoreHistory` initial value = false** — correctly defaults to `false` before `LoadHistoryAsync` sets it. The empty-history state will not show "Load more" incorrectly. ✅

---

## Summary

| Category | Status |
|----------|--------|
| Service running | ✅ PASS |
| Feature code present (source) | ✅ PASS — all 18 AC items verified in code |
| Bug fixes correct (source) | ✅ PASS — both fixes verified |
| Live browser QA | ❌ BLOCKED |
| Verdict | ⛔ **QA BLOCKED** |

---

## Required Before QA Can Complete

1. **Add `TestAuth__Secret` to `fred-dev` task definition** — set to a value and provide it to QA (or commit it to `.env`).
2. **Resolve Cloudflare managed challenge** — add SteamServer IP (99.7.135.70) to CF allowlist for `*.dev.fortressam.ai`, or disable managed challenge on the dev subdomain.

Once these are resolved, QA can complete full live browser testing within ~15 minutes.

---

## Recommendations

The code itself appears correct — all acceptance criteria are implemented, and both bug fixes are sound. **The blockers are infrastructure-only, not code quality issues.** If Fred can access the app manually (via his Entra-authenticated browser), a quick manual spot-check could unblock the WI while the infrastructure issues are resolved.

---

_Trust nothing. Verify everything. — Natasha_
