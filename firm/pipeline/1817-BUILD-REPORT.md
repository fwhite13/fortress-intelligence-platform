# Build Report — ADO #1817 — Retranscribe Button (Admin-Only)

## What was built
Added an admin-only "Retranscribe" button to `MeetingDetail.razor`. Button is visible only when the current user is an admin (checked via `Firm:AdminEntraOid` config) AND the meeting has an `AudioS3Key`. Added `RetranscribeAsync` to `MeetingService` to handle the vpbot call (no HttpClientFactory in the Razor component).

## Files changed
- `Components/Pages/MeetingDetail.razor`
  - Added `@inject IConfiguration Configuration`
  - Added `_isAdmin`, `_retranscribing`, `_retranscribeError` fields
  - Admin check in `OnInitializedAsync` after `_user = user` — matches OrgContext.razor pattern
  - Retranscribe `MudButton` (Color.Warning, Variant.Outlined, Refresh icon) inside `@if (_isAdmin && !string.IsNullOrEmpty(_meeting.AudioS3Key))`
  - `MudAlert` error display below action buttons on failure
  - `RetranscribeAsync()` method: JS confirm → service call → snackbar + 10-min background poll

- `Services/MeetingService.cs`
  - Added `RetranscribeAsync(long meetingId, Guid userId)` public method
  - Replicates `MeetingsApiController.Retranscribe` logic inline
  - Uses existing `_httpClientFactory`, `_config`, `_dbFactory`, `_logger`
  - Returns `(bool success, string? error)` tuple
  - On success: posts to `{vpbotUrl}/api/meetings/retranscribe`, resets status to `Transcribing`

## Parallelization used
No — single CC session, single file context (both changes in one pass).

## CC sessions run
1 CC session — `claude --model sonnet --print --dangerously-skip-permissions`

## Acceptance criteria verification
- [x] No `IHttpClientFactory` or `IDialogService` injection in Razor — only `IConfiguration` added
- [x] Button visible only when `_isAdmin && AudioS3Key != null` — double-guarded
- [x] Button disabled (`Disabled="_retranscribing"`) during active retranscription
- [x] JS confirm dialog before triggering
- [x] Success: snackbar + background poll refreshes meeting on Complete/Failed
- [x] Failure: `_retranscribeError` displayed in `MudAlert`
- [x] `MeetingService.RetranscribeAsync` added — same logic as controller endpoint
- [x] `dotnet build` — 0 errors, 18 pre-existing warnings

## Known edge cases / things Clint should scrutinize
1. **Background poll threading:** `Task.Run` accesses `_meeting` and `_user` from a background thread. These are Razor component fields (not thread-safe by default). The captures happen at start-of-lambda — should be safe for reading, but `_meeting = updated` write is on the background thread. Consider adding a local capture: `var meetingId = _meeting.Id; var userId = _user.Id;` and replacing `_meeting = updated` with `await InvokeAsync(() => _meeting = updated)`. Low risk in practice but worth hardening.
2. **Poll does not re-fetch summary/KB status:** After retranscription completes, `_meeting` is refreshed via `GetMeetingAsync` which includes `Summary` and `Transcripts`. The KB push status (`_transcriptPushedTo`, `_summaryPushedTo`) is NOT reloaded — user would need to refresh manually if they want to re-push. Acceptable for now.
3. **Admin check depends on `Firm:AdminEntraOid` being set in config.** If missing, `_isAdmin` will be `false` for all users — button simply won't appear. Safe default.

## How to test locally
1. Set `Firm:AdminEntraOid` in `appsettings.Development.json` to your Entra OID
2. Navigate to a completed meeting with audio (AudioS3Key non-null)
3. Verify "Retranscribe" button appears (Warning/orange color)
4. Click → JS confirm → should trigger retranscription
5. Log into non-admin account → button should NOT appear

## Commit
`c0dd086` — `feat(firm#1817): add admin-only Retranscribe button in MeetingDetail + RetranscribeAsync in MeetingService`

---

## Cycle 2 — Thread-Safety + Snackbar Fix

### What was built
Two surgical fixes to `RetranscribeAsync`'s `Task.Run` poll loop in `MeetingDetail.razor`:
1. **C1:** All background-thread field mutations wrapped in `InvokeAsync`
2. **A1:** Failed status uses `Snackbar.Add(Severity.Error)` instead of setting `_retranscribeError` (which is inside `@if (MeetingStatus.Complete)` and never renders on Failed)

### Files changed
- `Components/Pages/MeetingDetail.razor`
  - `_meeting = updated` moved inside `InvokeAsync` lambda (was missing InvokeAsync wrapper)
  - `_retranscribing = false` moved inside `InvokeAsync` lambda (was on background thread — race condition)
  - Removed separate `await InvokeAsync(StateHasChanged)` — merged into same lambda
  - Failed path: `await InvokeAsync(() => Snackbar.Add("Retranscription failed. Check logs.", Severity.Error))` — replaces `_retranscribeError` assignment
  - Loop-timeout path: `await InvokeAsync(() => { _retranscribing = false; StateHasChanged(); })` — was two separate calls with race condition

### Acceptance criteria verification
- [x] All background thread mutations in `Task.Run` block wrapped in `InvokeAsync`
- [x] `_retranscribeError` NOT set in poll loop (only in sync failure path — fine, runs on UI thread)
- [x] Failed status uses `Snackbar.Add` — visible regardless of meeting status
- [x] `dotnet build` — 0 errors, 18 pre-existing warnings

### CC sessions run
1 CC session — `claude --model sonnet --print --dangerously-skip-permissions`

### Commit
`34a0ba4` — fixes bundled into nexus#1819 commit (same working tree, same push)
