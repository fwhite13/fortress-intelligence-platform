# Review Report — ADO#1817 — Retranscribe Button in MeetingDetail.razor

**Verdict: NEEDS-CHANGES**
**Cycle:** 1
**Reviewer:** Hawkeye (code-reviewer)
**Commit:** c0dd086
**Risk:** Medium — Blazor Server UI + background thread pattern

---

## Spec Compliance Check

**What was asked:**
- Admin-only Retranscribe button in `MeetingDetail.razor` (`_isAdmin && AudioS3Key != null`)
- JS confirm dialog before triggering
- Calls `MeetingService.RetranscribeAsync` (no self-HTTP in component)
- Background poll via `Task.Run` for completion
- `MeetingService.RetranscribeAsync(long meetingId, Guid userId)` — calls external VpBot, no `CreateClient("local")`

**Files modified:**
- ✅ `Components/Pages/MeetingDetail.razor` — modified as specified
- ✅ `Services/MeetingService.cs` — new `RetranscribeAsync` method added

**Spec compliance verdict:** ✅ Structurally compliant — implementation is correct in scope and approach. One critical thread-safety fix required before merge.

---

## CC Review Summary

Ran adversarial CC review against both changed files plus OrgContext.razor for admin-check comparison. CC confirmed one Critical issue (C2 — thread safety), found one additional Minor issue (A1 — poll-failure error not visible after status transition), and confirmed all other checklist items clean.

No false positives dismissed.

---

## Consistency Audit

| Check | Result |
|-------|--------|
| `_isAdmin` logic vs OrgContext.razor | ✅ Functionally identical — same dual-claim OID lookup, same `IsInRole`/`HasClaim` fallbacks |
| `MeetingService.RetranscribeAsync` target URL | ✅ External VpBot only (`Firm:VpBotUrl` / `FIRM_VPBOT_URL`) |
| `CreateClient("local")` anti-pattern | ✅ Zero occurrences in component or service |
| `IHttpClientFactory`/`HttpClient` in component | ✅ Zero — component calls service directly |
| `_isAdmin` default value | ✅ `false` |

---

## Critical Issues (1)

### C1: Thread Safety — `_meeting` and `_retranscribing` Assigned Outside `InvokeAsync`

- **File:** `Components/Pages/MeetingDetail.razor`
- **Lines:** ~377–388 (inside `Task.Run` poll loop)
- **Category:** Correctness — Blazor threading
- **Issue:** Component fields `_meeting` and `_retranscribing` are assigned directly from the background thread without going through `InvokeAsync`. The Blazor component synchronization context is not held at this point. The renderer can be reading these fields concurrently during a re-render triggered by another event.

**Current (buggy):**
```csharp
// On completion/failure — inside Task.Run:
_meeting = updated;         // ← raw background thread write — RACE
_retranscribing = false;    // ← raw background thread write — RACE
if (updated.Status == MeetingStatus.Complete)
    await InvokeAsync(() => Snackbar.Add("Retranscription complete!", Severity.Success));
else
    await InvokeAsync(() => { _retranscribeError = "Retranscription failed."; });
await InvokeAsync(StateHasChanged);  // ← separate dispatch

// On timeout — inside Task.Run:
_retranscribing = false;    // ← raw background thread write — RACE
await InvokeAsync(StateHasChanged);
```

**Required fix — consolidate all UI mutations into a single `InvokeAsync`:**
```csharp
// On completion/failure:
await InvokeAsync(() =>
{
    _meeting = updated;
    _retranscribing = false;
    _retranscribeError = updated.Status == MeetingStatus.Failed ? "Retranscription failed." : null;
    StateHasChanged();
});
if (updated.Status == MeetingStatus.Complete)
    await InvokeAsync(() => Snackbar.Add("Retranscription complete!", Severity.Success));
return;

// On timeout:
await InvokeAsync(() =>
{
    _retranscribing = false;
    StateHasChanged();
});
```

- **Impact:** Race condition — UI state corruption on the completion/timeout paths. Reproducible when another event (e.g., user scrolls or another timer fires) triggers a render concurrently with the background thread assignment.

---

## Important Issues (1)

### I1: No CancellationToken — Background Poll Continues After Component Disposal (Tech Debt)

- **File:** `Components/Pages/MeetingDetail.razor`
- **Lines:** `Task.Run` poll loop — no `IDisposable` implementation
- **Category:** Reliability
- **Issue:** If the user navigates away while retranscription is in progress, the `Task.Run` loop runs for the full 10-minute window (~60 × 10s), calling `InvokeAsync` on a disposed component. The framework swallows the `ObjectDisposedException` silently, but generates log noise and wastes a DB polling thread.
- **Recommendation:** Do NOT block merge on this. Track as tech debt. Fix: implement `IDisposable` on the component, create a `CancellationTokenSource _pollCts`, pass the token to `Task.Delay` and `GetMeetingAsync`, cancel in `Dispose()`.
- **Priority:** Track in follow-up WI. Acceptable for this cycle.

---

## Minor Issues (1)

### A1: Poll-Detected Failure Error Not Visible After Status Transition

- **File:** `Components/Pages/MeetingDetail.razor`
- **Issue:** The `_retranscribeError` `MudAlert` is inside the `@if (_meeting.Status == MeetingStatus.Complete)` block. After the background poll sets status to `MeetingStatus.Failed` (via C1 fix), that block no longer renders — so the error alert is invisible. Only the Snackbar-based approach works for poll-detected failures.
- **Recommendation:** When applying the C1 fix, also handle the failure Snackbar:
  ```csharp
  // In the InvokeAsync for failure:
  await InvokeAsync(() =>
  {
      _meeting = updated;
      _retranscribing = false;
      StateHasChanged();
  });
  await InvokeAsync(() => Snackbar.Add("Retranscription failed.", Severity.Error));
  ```
  Drop the `_retranscribeError` assignment for the poll path (it's invisible in `Failed` status anyway). The `_retranscribeError` path in the `else` branch (sync failure, before status changes) is fine since status is still `Complete` then.
- **Priority:** Bundle with C1 fix — same location, minimal extra work.

---

## Confirmed Clean

| Check | Status |
|-------|--------|
| No `HttpClientFactory`/`HttpClient` in component | ✅ Clean |
| No `CreateClient("local")` in service | ✅ Clean — uses `CreateClient()` (unnamed) to external VpBot |
| Admin check matches OrgContext pattern exactly | ✅ Clean |
| `_isAdmin` default is `false` | ✅ Clean |
| Error handling: `_retranscribeError` set in `else`/`catch`, rendered in `MudAlert` | ✅ Clean |
| `Snackbar.Add("started...")` on main thread (not in Task.Run) | ✅ Clean |
| `Snackbar.Add("complete!")` inside `InvokeAsync` | ✅ Clean |
| `_retranscribeError = "Retranscription failed."` inside `InvokeAsync` | ✅ Clean |
| JS confirm dialog wired to `RetranscribeAsync` | ✅ Clean |
| Guard: `if (_meeting == null || _user == null || _retranscribing) return` | ✅ Clean |
| Service: OID guard via `GetMeetingAsync(meetingId, userId)` | ✅ Clean — user ownership enforced |
| Service: Status reset to `Transcribing` after VpBot call | ✅ Clean |
| Service: Error handling returns `(false, message)` tuple | ✅ Clean |

---

## What Tony Needs to Fix

### Fix C1 (required before merge):

In `RetranscribeAsync()` in `MeetingDetail.razor`, inside the `Task.Run` lambda:

**Replace the completion/failure block:**
```csharp
// BEFORE (buggy):
_meeting = updated;
_retranscribing = false;
if (updated.Status == MeetingStatus.Complete)
    await InvokeAsync(() => Snackbar.Add("Retranscription complete!", Severity.Success));
else
    await InvokeAsync(() => { _retranscribeError = "Retranscription failed."; });
await InvokeAsync(StateHasChanged);
return;

// AFTER (fixed):
await InvokeAsync(() =>
{
    _meeting = updated;
    _retranscribing = false;
    StateHasChanged();
});
if (updated.Status == MeetingStatus.Complete)
    await InvokeAsync(() => Snackbar.Add("Retranscription complete!", Severity.Success));
else
    await InvokeAsync(() => Snackbar.Add("Retranscription failed.", Severity.Error));
return;
```

**Replace the timeout block:**
```csharp
// BEFORE (buggy):
_retranscribing = false;
await InvokeAsync(StateHasChanged);

// AFTER (fixed):
await InvokeAsync(() =>
{
    _retranscribing = false;
    StateHasChanged();
});
```

Two surgical edits. No other changes needed.

---

## Positive Observations

- ✅ Clean service pattern — `MeetingService.RetranscribeAsync` correctly uses `IDbContextFactory` (not injected DbContext), correct `await using var db = ...` scope.
- ✅ Admin check is a carbon-copy of the OrgContext pattern — no drift.
- ✅ Guard clause at top of `RetranscribeAsync()` prevents double-fire elegantly.
- ✅ `finally { StateHasChanged(); }` correctly handles all early-return paths in the synchronous portion.
- ✅ No self-HTTP anti-pattern anywhere. Tony got this right.
- ✅ VpBot URL config key has dual-key fallback (`Firm:VpBotUrl` / `FIRM_VPBOT_URL`) — consistent with the rest of the service.
