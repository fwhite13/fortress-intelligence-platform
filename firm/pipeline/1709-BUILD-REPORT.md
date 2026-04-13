# Build Report — FIRM ADO #1709
## "Remove" button fails with "Failed to remove meeting" toast

**Commit:** `ac9f1a5`
**Date:** 2026-04-13
**Risk:** low-medium

---

## Root Cause

Three compounding issues:

### 1. Relative route on `[HttpDelete]` (likely primary cause)
`[HttpDelete("{id}")]` used a relative route, while every other endpoint in `MeetingsApiController` uses absolute routes (e.g. `[HttpGet("/api/meetings/{id}/transcript/download")]`). While the relative route technically combines with the controller-level `[Route("api/meetings")]`, the inconsistency created a routing edge case. **Fixed to:** `[HttpDelete("/api/meetings/{id}")]`.

### 2. Status constraint too restrictive
The API blocked deleting any meeting not in `Scheduled` status — returning `409 Conflict`. This meant `Complete` and `Failed` meetings could never be removed. These are terminal states with no active operations, so the restriction was illogical. **Fixed:** Now only blocks removal of actively in-progress meetings (`Pending`, `Joining`, `Recording`, `WaitingTranscript`, `Transcribing`, `Summarizing`).

### 3. Error body never shown
`RemoveMeeting` in `Meetings.razor` showed a generic "Failed to remove meeting" toast without ever reading the response body. This made debugging impossible. **Fixed:** Now reads `response.Content.ReadAsStringAsync()` and includes the body in both the Snackbar and `Logger.LogError`.

---

## Files Changed

### `Controllers/MeetingsApiController.cs`
- `[HttpDelete("{id}")]` → `[HttpDelete("/api/meetings/{id}")]` — absolute route, consistent with all other endpoints
- `NotFound()` → `NotFound(new { error = "Meeting not found" })` — structured error body
- Status check relaxed: allow `Scheduled`, `Complete`, `Failed`; block only active in-progress states

### `Components/Pages/Meetings.razor`
- `RemoveMeeting`: Reads response body on failure, shows in Snackbar, logs via `Logger.LogError`
- `RemoveMeeting`: Adds success Snackbar on removal
- `RemoveMeeting`: Adds `Logger.LogError` on exception path
- UI: Remove button now shows for `Scheduled`, `Complete`, and `Failed` meetings (not just `Scheduled`)
- UI: Join Now button still `Scheduled`-only (no change)

---

## Build Result
```
Build succeeded.
0 errors, 12 warnings (all pre-existing)
```

---

## Acceptance Criteria Verification
- [x] Error body shown in Snackbar on failure — reads `response.Content.ReadAsStringAsync()`
- [x] Route is absolute: `[HttpDelete("/api/meetings/{id}")]`
- [x] API allows removing `Scheduled`, `Complete`, `Failed` meetings
- [x] API still blocks in-progress meetings
- [x] Remove button shows for `Scheduled`, `Complete`, `Failed` in UI
- [x] `dotnet build` — 0 errors

---

## How to Test
1. Deploy FIRM
2. Add a meeting via calendar detection
3. Confirm meeting appears in list with `Scheduled` status
4. Click Remove → meeting should disappear + green "Meeting removed." toast
5. Complete a meeting (or manually set status to `Complete`/`Failed` in DB)
6. Confirm Remove button appears for completed/failed meetings
7. Click Remove → meeting should disappear + green toast

---

## Parallelization
N/A — single sequential CC session. Both files have overlapping concerns (UI + API).

## CC Sessions Run
1 CC session (Sonnet). All three changes applied in one pass.
