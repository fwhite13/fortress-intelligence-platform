# Review Report — ADO #1710 — Browser Local Timezone Display

**Reviewer:** Hawkeye (Clint Barton) — Cycle 1
**Date:** 2026-04-13
**Commits:** `fa68ab1` (firm-utils.js + App.razor) · `1aa0639` (Meetings.razor + MeetingDetail.razor)
**Risk:** Low-medium (UI-only, new JS file, IJSRuntime injection)

---

## Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**4 expected files present and accounted for:**
- `wwwroot/js/firm-utils.js` — ✅ created
- `Components/App.razor` — ✅ modified (script tag added)
- `Components/Pages/Meetings.razor` — ✅ modified (IJSRuntime, dicts, PreFormatMeetingTimesAsync)
- `Components/Pages/MeetingDetail.razor` — ✅ modified (IJSRuntime, _localCreatedAt)

**Scope:** `DatabaseInitializationService.cs` appears in `fa68ab1` alongside the JS work (that's the #1709 FK cascade fix in the same commit). Acceptable — not out of scope for this review.

**Spec compliance verdict:** ✅ COMPLIANT (files correct, intent correct, but implementation has lifecycle bugs)

---

## CC Review Summary

CC ran adversarial analysis on all 4 files. Key findings aligned with pre-identified critical checks.
CC confirmed the lifecycle hook bug in both components, confirmed the dead code, confirmed null guard gap in JS.
No false positives identified. Verdict below reflects CC findings synthesized with Hawkeye judgment.

---

## Consistency Audit

| Check | Result |
|---|---|
| `_localCreatedTimes` keyed by `m.Id` (`long`) vs `Dictionary<long, string>` | ✅ Match |
| `_localStartTimes` keyed by `c.CalendarEventId` (`string`) vs `Dictionary<string, string>` | ✅ Match |
| `_localEndTimes` keyed by `c.CalendarEventId` (`string`) vs `Dictionary<string, string>` | ✅ Match |
| Markup TryGetValue key types match declarations | ✅ Match |
| JS function names in C# interop calls match `window.firmUtils` object | ✅ Match |

No consistency mismatches found.

---

## Critical Issues [2]

### C1: JS Interop Called from `OnInitializedAsync` — MeetingDetail.razor

- **File:** `MeetingDetail.razor` (~line 268, inside `OnInitializedAsync`)
- **Category:** Correctness / Blazor lifecycle violation
- **Issue:** `JS.InvokeAsync<string>("firmUtils.formatLocalDateTime", utcStr)` is called inside `OnInitializedAsync`. In Blazor Server (InteractiveServer render mode — confirmed by `App.razor`), `OnInitializedAsync` executes **twice**:
  1. **Pre-render phase (server-side SSR):** JS runtime unavailable → `InvalidOperationException` thrown → caught by try/catch → fallback server-side datetime format used (Eastern time, no tz indicator).
  2. **After SignalR circuit connects:** JS call succeeds → correct local time rendered.

  The `try/catch` prevents a crash but the user sees an ET-formatted time that flips to their local time 1–3 seconds after page load. This is the observable "flash" and violates Blazor's documented interop contract.

- **Impact:** Wrong time shown on first render for all users not in Eastern time. Silent failure mode — no error surfaces; wrong data shown briefly.

- **Fix:** Remove the JS interop from `OnInitializedAsync`. Add `OnAfterRenderAsync` override:
  ```csharp
  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
      if (firstRender && _meeting != null)
      {
          try
          {
              var utcStr = _meeting.CreatedAt.ToUniversalTime().ToString("O");
              _localCreatedAt = await JS.InvokeAsync<string>("firmUtils.formatLocalDateTime", utcStr);
              StateHasChanged();
          }
          catch
          {
              _localCreatedAt = _meeting.CreatedAt.ToString("MMM d, yyyy h:mm tt");
              StateHasChanged();
          }
      }
  }
  ```
  Remove the JS try/catch block from `OnInitializedAsync`.

---

### C2: JS Interop Called from `OnInitializedAsync` — Meetings.razor

- **File:** `Meetings.razor` — `OnInitializedAsync` → `LoadUpcomingMeetingsAsync()` + `LoadMeetings()` → `PreFormatMeetingTimesAsync()`
- **Category:** Correctness / Blazor lifecycle violation (same root cause as C1)
- **Issue:** Both `LoadMeetings()` and `LoadUpcomingMeetingsAsync()` call `PreFormatMeetingTimesAsync()`, which fires `JS.InvokeAsync` on every meeting row and upcoming card. Called from `OnInitializedAsync` → same pre-render/circuit lifecycle issue. All meeting rows and upcoming cards flash ET before local time loads.

  Note: Execution order is correct (`LoadUpcomingMeetingsAsync` → `LoadMeetings` → second PreFormat call repopulates all dicts). The data is right — only the timing is wrong.

- **Impact:** All meeting times on the Meetings list page show wrong timezone briefly. On every refresh triggered during the first render, same flash occurs.

- **Fix:** Add a `_jsReady` flag. Set it in `OnAfterRenderAsync(firstRender: true)` and call `PreFormatMeetingTimesAsync()` from there on first render. Guard `PreFormatMeetingTimesAsync` to no-op if `!_jsReady`. After first render, `_jsReady` stays `true` and all subsequent `LoadMeetings()` calls (refresh, timer, join) work normally.

  ```csharp
  private bool _jsReady = false;

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
      if (firstRender)
      {
          _jsReady = true;
          await PreFormatMeetingTimesAsync();
          await InvokeAsync(StateHasChanged);
      }
  }

  private async Task PreFormatMeetingTimesAsync()
  {
      if (!_jsReady) return;  // Guard: no-op before circuit is ready
      // ... existing implementation unchanged
  }
  ```

---

## Important Issues [2]

### I1: Dead Code — `FormatEastern`, `FormatEasternTimeOnly`, `_easternTz`

- **File:** `Meetings.razor`
- **Issue:** Three symbols are defined but no longer referenced anywhere in markup:
  - `_easternTz` (static field, `TimeZoneInfo`)
  - `FormatEastern(string isoUtc)` (static method)
  - `FormatEasternTimeOnly(string isoUtc)` (static method)
  
  These were the pre-interop Eastern-hardcoded implementations, now superseded by the JS interop dicts. Keeping them causes confusion about which path is actually active.

- **Severity:** Non-blocking. Does not affect correctness. Clean up in follow-up ticket.
- **Fix:** Delete all three. A search confirms they are not called from markup, and the JS-based dicts (`_localStartTimes`, `_localEndTimes`, `_localCreatedTimes`) are the active path.

---

### I2: No Null Guard in `firm-utils.js`

- **File:** `wwwroot/js/firm-utils.js`
- **Issue:** All three functions begin with `isoUtc.endsWith('Z')`. If `isoUtc` is `null`, this throws `TypeError: Cannot read properties of null`. The C# try/catch around the interop call catches this, but the JS could be more defensive.

  Additionally, if Graph API ever returns an offset-qualified datetime like `2026-04-13T14:00:00+00:00`, the guard appends Z → `...+00:00Z` → invalid ISO 8601 → `new Date()` returns `Invalid Date` → `RangeError` on format. Low practical risk (Graph returns offset-free datetimes), but a correctness gap if the API format changes.

- **Severity:** Non-blocking given existing try/catch. Recommended improvement.
- **Fix:**
  ```js
  formatLocalTime: function(isoUtc, options) {
      if (!isoUtc) return '';
      // Handle offset suffix — strip +HH:MM or -HH:MM before Z-guard
      const normalized = isoUtc.match(/[+-]\d{2}:\d{2}$/) ? isoUtc : (isoUtc.endsWith('Z') ? isoUtc : isoUtc + 'Z');
      const d = new Date(normalized);
      // ...
  }
  ```

---

## Nitpicks [1]

- **N1: 7-decimal fractional seconds** (`firm-utils.js`) — `.NET`'s `ToString("O")` outputs 7 decimal places (`0000000`). Modern browsers (V8, SpiderMonkey, WebKit ≥2020) handle this. Not an issue for FIRM's user base. No change needed.

---

## Positive Observations

- **Z guard logic is correct for the primary case** — Graph API datetimes without Z are handled properly. The core bug the feature was designed to solve (treating UTC as local) is fixed correctly.
- **Pre-formatting dict pattern is clean** — keying by meeting ID and CalendarEventId is correct, type-safe (`long` vs `string`), and the clear-then-repopulate approach avoids stale entries.
- **Fallbacks everywhere** — every JS interop call is wrapped in try/catch with a sensible server-side fallback. The app will never crash due to a JS timing issue.
- **Execution order in OnInitializedAsync is correct** — `LoadUpcomingMeetingsAsync` before `LoadMeetings` ensures `_upcomingMeetings` is populated before the second `PreFormatMeetingTimesAsync` call.
- **Script tag placement is correct** — `firm-utils.js` after `blazor.server.js` is fine because interop calls execute after circuit establishment.

---

## What to Fix (NEEDS-CHANGES)

Two mandatory changes before ship:

**1. `MeetingDetail.razor`** — Remove `JS.InvokeAsync` from `OnInitializedAsync`. Add `OnAfterRenderAsync(firstRender: true)` override to set `_localCreatedAt` and call `StateHasChanged()`. Keep the existing try/catch with server-side fallback.

**2. `Meetings.razor`** — Add `_jsReady` bool. Guard `PreFormatMeetingTimesAsync` with `if (!_jsReady) return`. Add `OnAfterRenderAsync(firstRender: true)` override to set `_jsReady = true`, call `PreFormatMeetingTimesAsync()`, and call `InvokeAsync(StateHasChanged)`. All subsequent loads (refresh, timer, join) will naturally go through `PreFormatMeetingTimesAsync` once `_jsReady` is true — no other changes needed.

Non-blocking items (defer to follow-up):
- Delete `FormatEastern`, `FormatEasternTimeOnly`, `_easternTz` dead code (I1)
- Add null guard to `firm-utils.js` functions (I2)

---

_Hawkeye — Cycle 1 complete._
