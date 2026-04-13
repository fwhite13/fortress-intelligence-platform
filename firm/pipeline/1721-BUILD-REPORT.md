# Build Report — ADO #1721
**Fix:** Remove meeting fails HTTP 414 — replace HTTP self-call with direct service injection
**Date:** 2026-04-13
**Commit:** e56d03d
**Risk:** Low

---

## What Was Built

Replaced `RemoveMeeting()` HTTP self-call in `Meetings.razor` with direct `MeetingService` injection.
Added `RemoveMeetingAsync(long id, Guid userId)` to `MeetingService` replicating the controller's ownership check and in-progress status guard.

---

## Files Changed

| File | Change |
|------|--------|
| `Services/MeetingService.cs` | Added `public async Task<(bool success, string? error)> RemoveMeetingAsync(long id, Guid userId)` — ownership check via `GetMeetingAsync`, in-progress status guard (Pending/Joining/Recording/WaitingTranscript/Transcribing/Summarizing), raw DELETE |
| `Components/Pages/Meetings.razor` | Replaced `RemoveMeeting()` body — removed `HttpClientFactory.CreateClient("local")` + `http.DeleteAsync(...)`, now calls `MeetingService.RemoveMeetingAsync(meetingId, Guid.Parse(_userId!))` |

---

## Scope Decision

`@inject IHttpClientFactory HttpClientFactory` was **retained** — `StopRecording()` at line ~647 still uses `HttpClientFactory.CreateClient("local")` for `/api/vp/stop/{id}`. That's a separate VpBotService concern and out of scope for this WI. No other `CreateClient("local")` calls exist in `RemoveMeeting()`.

---

## Acceptance Criteria

- [x] `RemoveMeeting()` uses direct service injection — no HTTP self-call
- [x] `MeetingService.RemoveMeetingAsync()` added with ownership + status guards
- [x] No `HttpClientFactory.CreateClient("local")` in `RemoveMeeting()` method
- [x] `dotnet build` — **0 errors, 12 warnings (pre-existing)**

---

## Build Result

```
Build succeeded.
    0 Error(s)
    12 Warning(s) — all pre-existing
```

---

## Known Edge Cases / Things to Scrutinize

- `_userId` in `Meetings.razor` is typed as `string?` — `Guid.Parse(_userId!)` is used (same pattern as other service calls in the page). If `_userId` is null at call time, this will throw — but that's the same pre-existing risk as other methods on the page.
- `StopRecording()` still uses the HTTP self-call pattern — recommend a follow-up WI if that also exhibits issues.

---

## How to Test Locally

1. Start FIRM locally
2. Navigate to Meetings page
3. Create a test meeting in a terminal/completed state
4. Click Remove — should succeed without HTTP 414 error
5. Verify meeting disappears from list and success snackbar appears
