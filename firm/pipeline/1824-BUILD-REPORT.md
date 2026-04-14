# Build Report — ADO #1824 — RetranscribeAsync HttpClient timeout fix

**Date:** 2026-04-14  
**Engineer:** Tony Stark  
**Commit:** `b5beaf2`  
**Branch:** `origin/main`

---

## What was built

Replaced `PostAsync` with `SendAsync(HttpCompletionOption.ResponseHeadersRead)` + a 10s `CancellationTokenSource` in `MeetingService.RetranscribeAsync`. This prevents firm-web from blocking while vpbot runs its async transcription pipeline (which never sends response headers until pipeline completion — potentially 5+ minutes).

---

## Files changed

- `src/FortressIntelligenceRM.Web/Services/MeetingService.cs` — `RetranscribeAsync` method (~line 285):
  - Removed `http.DefaultRequestHeaders.Add("X-Bot-Secret", botSecret)`
  - Added `HttpRequestMessage` with `request.Headers.Add("X-Bot-Secret", botSecret)`
  - Replaced `PostAsync(url, content)` with `SendAsync(request, ResponseHeadersRead, acceptCts.Token)`
  - Added 10s `CancellationTokenSource` (`acceptCts`)
  - `OperationCanceledException` → logs warning, sets `acceptedOrTimeout = true`, proceeds to status reset
  - Non-2xx response → returns `(false, "VpBot error {status}: {body}")` immediately, no status reset
  - 2xx or timeout → proceeds to status reset (`MeetingStatus.Transcribing`) and returns `(true, null)`

---

## Parallelization used

No — single targeted file change.

---

## CC sessions run

1 CC session (Sonnet). Briefed with exact before/after code blocks. No iterations needed.

---

## Acceptance criteria verification

- [x] `dotnet build` — **0 errors, 18 warnings** (pre-existing MudBlazor analyzer warnings, unrelated)
- [x] `SendAsync` + `HttpCompletionOption.ResponseHeadersRead` used
- [x] 10s `CancellationTokenSource` timeout
- [x] Timeout path logs warning and proceeds to status reset (fire-and-forget pattern)
- [x] Non-2xx path returns error immediately without status reset
- [x] `X-Bot-Secret` header on `HttpRequestMessage`, not `DefaultRequestHeaders`
- [x] Outer `catch (Exception ex)` block unchanged

---

## Known edge cases / things Clint should scrutinize

- **`acceptedOrTimeout` flag**: Used instead of `goto` — the compiler-safe approach. The `if (acceptedOrTimeout)` block always executes after the inner try/catch since both paths set it to `true`; the trailing `return (false, "Unexpected state")` is dead code added only to satisfy the compiler. Could be simplified further but is readable as-is.
- **Timeout = accepted**: The design decision here is that a 10s timeout means vpbot received the request and is processing — no indication of failure. If vpbot is actually down, it would reject connection immediately (not timeout). The `LogWarning` makes this visible in CloudWatch.
- **Response body on errors**: Wrapped in its own try/catch since `ResponseHeadersRead` doesn't guarantee body availability.

---

## How to test locally

1. Trigger retranscribe on a meeting in firm-web
2. Verify the UI returns quickly (no 5-minute hang)
3. Watch CloudWatch logs for either "RetranscribeAsync triggered" (200 from vpbot) or "vpbot retranscribe accept timeout (10s)" (timeout-as-accepted path)
4. Verify meeting status transitions to `Transcribing` in either case
