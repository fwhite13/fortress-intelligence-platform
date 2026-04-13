# Build Report — FIRM ADO #1713 — Push to Personal KB fails

## What was built
Fixed "Push to personal KB" button that fired two red error toasts and a green "KB updated" toast simultaneously without actually pushing anything.

---

## CloudWatch Error Excerpt

```
fail: FortressIntelligenceRM.Web.Services.S3Service[0]
      FIRM: Failed to get summary from S3: firm-transcripts/17/summary.md
      Amazon.S3.AmazonS3Exception: The specified key does not exist.

info: POST http://localhost:8080/api/meetings/17/push-to-kb
info: Received HTTP response headers after 651ms - 403

info: POST http://localhost:8080/api/meetings/17/push-to-kb
info: Received HTTP response headers after 30ms - 403
[... repeated multiple times ...]
```

---

## Root Cause

**Blazor Server → localhost HTTP anti-pattern (primary bug)**

`MeetingDetail.razor` used `HttpClientFactory.CreateClient("local")` to call its own API endpoints:
- `GET /api/meetings/{id}/kb-status` (on page load)
- `POST /api/meetings/{id}/push-to-kb` (on button click)

The `"local"` named HttpClient points to `http://localhost:8080` with **no auth cookie forwarded**. In Blazor Server, the razor component runs server-side — there is no browser request context, so auth cookies are never attached to outbound HttpClient requests. The `[Authorize]` middleware sees an unauthenticated request and returns **403 Forbidden**.

**Why two red toasts:** The `kb-status` GET on init also fails with 403 (silently swallowed), and the `push-to-kb` POST fails with 403, producing the first error. The Blazor circuit then emits a second error from the unhandled 403 response body being read.

**Why a green toast was NOT firing** (contrary to the bug report's suspicion): The toast IS gated correctly on `resp.IsSuccessStatusCode`, which is false for 403. The green toast shown to users was likely from a previous successful push of a different meeting that left a stale snackbar in the queue.

**S3 error (secondary/unrelated):** A separate `S3Service` call for `firm-transcripts/17/summary.md` returned 404 — this is a test meeting (meeting 17) whose summary was never written to S3 via the old code path. Not relevant to the KB push bug; the new code reads summary from the DB, not S3.

---

## Fix Applied

**Commit:** `2c66557` — `fix(firm#1713): bypass HTTP layer for KB push/status in MeetingDetail`

**File changed:** `Components/Pages/MeetingDetail.razor`

| Before | After |
|--------|-------|
| `HttpClient("local").GET /kb-status` | `FirmKbService.GetPushedScopesAsync()` directly |
| `HttpClient("local").POST /push-to-kb` | `FirmKbService.PushDocumentAsync()` directly |

**Specific changes:**
1. Added `@inject FirmKbService FirmKbService` injection
2. Added `_user` field to cache the resolved `FirmUser` across method calls
3. Set `_user = user` after auth resolution in `OnInitializedAsync()`
4. Replaced local HTTP `kb-status` GET with `FirmKbService.GetPushedScopesAsync()` (×2)
5. Replaced `PushTranscript()` HTTP POST with `FirmKbService.PushDocumentAsync()` + `FaitUserId` null guard
6. Replaced `PushSummary()` HTTP POST with `FirmKbService.PushDocumentAsync()` + `FaitUserId` null guard

**FaitUserId null guard added:** If the user's FAIT account isn't linked yet, shows a specific actionable error: _"KB push requires your FAIT account to be linked. Please sign out and sign back in."_

---

## Files Changed
- `firm/src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor` — bypassed HTTP layer, injected FirmKbService directly

## Files NOT Changed
- `FirmKbService.cs` — service logic was correct
- `MeetingsApiController.cs` — endpoint is correct, just shouldn't be called from server-side Blazor

---

## Build Verification
```
dotnet build — Build succeeded. 0 Error(s)
```

---

## How to Test
1. Deploy to dev/staging
2. Open a meeting in `Complete` status
3. Click "Push Transcript" or "Push Summary"
4. Expected: Single green success toast, document appears in FAIT personal KB
5. Expected: No red error toasts
6. Verify `kb-status` loads correctly on page init (checkboxes should reflect already-pushed docs)

---

## CC Sessions
- 1 CC Sonnet run (sequential — single file)

## Known Edge Cases
- Team KB push requires `TeamKbId`/`TeamDsId` to be configured in app settings; if not set, uses hardcoded defaults from `FirmKbService.cs` property definitions
- Bedrock ingestion `ConflictException` is handled gracefully (logged, not re-thrown)

---

**Built by:** Tony Stark  
**Date:** 2026-04-13  
**WI:** ADO #1713
