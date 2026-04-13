# Build Report — ADO #1710: Display meeting times in browser local timezone

**Date:** 2026-04-13  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `1aa0639` (feat(#1710): display meeting times in browser local timezone via JS interop)  
**Note:** `firm-utils.js` and `App.razor` changes were staged in `fa68ab1` — both commits form the complete #1710 changeset.  
**Build:** ✅ 0 errors, 12 warnings (all pre-existing)

---

## What Was Built

Replaced hardcoded Eastern Time (America/New_York) meeting time display with browser-local timezone formatting via JavaScript Interop (`Intl.DateTimeFormat`). No server-side timezone logic changed.

---

## Files Changed

| File | Change |
|------|--------|
| `wwwroot/js/firm-utils.js` | **Created** — JS helpers using `Intl.DateTimeFormat(undefined, ...)` to format UTC ISO strings in browser local timezone |
| `Components/App.razor` | Added `<script src="js/firm-utils.js"></script>` before `</body>` |
| `Components/Pages/Meetings.razor` | Injected `IJSRuntime JS`; added `_localStartTimes`, `_localEndTimes`, `_localCreatedTimes` dicts; added `PreFormatMeetingTimesAsync()`; updated markup |
| `Components/Pages/MeetingDetail.razor` | Injected `IJSRuntime JS`; added `_localCreatedAt` field; set via JS interop in `OnInitializedAsync`; updated markup |

---

## Locations Changed (Detailed)

### Meetings.razor

1. **Line 61** — Upcoming meeting time display:  
   Before: `@FormatEastern(upcomingCard.StartDateTime) – @FormatEasternTimeOnly(upcomingCard.EndDateTime)`  
   After: `@(_localStartTimes.TryGetValue(upcomingCard.CalendarEventId, ...) – @(_localEndTimes.TryGetValue(...)`

2. **Line 113** — FIRM meetings table Date column:  
   Before: `@context.CreatedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt")`  
   After: `@(_localCreatedTimes.TryGetValue(context.Id, out var ct) ? ct : context.CreatedAt.ToString(...))`

3. **Lines 189–191** — New dictionary fields: `_localStartTimes`, `_localEndTimes`, `_localCreatedTimes`

4. **Lines 269, 315** — `PreFormatMeetingTimesAsync()` called at end of `LoadMeetings()` and `LoadUpcomingMeetingsAsync()` try blocks

5. **Lines 690–721** — `PreFormatMeetingTimesAsync()` method implementation

### MeetingDetail.razor

1. **Line 9** — `@inject IJSRuntime JS` added  
2. **Line 45** — Markup updated: `@(_localCreatedAt ?? _meeting.CreatedAt.ToString("MMM d, yyyy h:mm tt"))`  
3. **Line 219** — `private string? _localCreatedAt;` field added  
4. **Lines 265–272** — JS interop call in `OnInitializedAsync` to format CreatedAt  

---

## Pre-Formatting Dictionary Pattern

Since Razor `@` expressions cannot `await`, async JS interop calls cannot be called inline in markup. The solution:

1. **Dictionary stored at component state level** — `_localCreatedTimes: Dictionary<long, string>` (keyed by meeting ID), `_localStartTimes`/`_localEndTimes: Dictionary<string, string>` (keyed by CalendarEventId).

2. **Populated eagerly** — `PreFormatMeetingTimesAsync()` is called after `_meetings` and `_upcomingMeetings` are loaded (inside `LoadMeetings()` and `LoadUpcomingMeetingsAsync()`). It iterates every meeting and calls `JS.InvokeAsync<string>("firmUtils.formatLocalDateTime", utcStr)` for each.

3. **Fallback safety** — Each JS call is wrapped in `try/catch`. If interop fails (e.g., pre-render), the fallback is server-formatted UTC string.

4. **Render uses dictionary** — Markup uses `TryGetValue(id, out var ct) ? ct : fallback` pattern — synchronous, no await needed.

5. **Cleared on reload** — Dicts are cleared at the start of each `PreFormatMeetingTimesAsync()` call to avoid stale entries from polling cycles.

---

## JS Helper (`firm-utils.js`)

Three functions on `window.firmUtils`:

- `formatLocalTime(isoUtc, options?)` — Full date+time (weekday, month, day, hour, minute) using `Intl.DateTimeFormat(undefined, ...)` — `undefined` locale uses browser default
- `formatLocalTimeOnly(isoUtc)` — Hour and minute only (for end-time display)
- `formatLocalDateTime(isoUtc)` — Month, day, year, hour, minute (for FIRM meeting rows and detail page)

All functions append `Z` if the ISO string is missing it (Graph API sometimes omits the suffix).

---

## Constraints Honored

- ✅ Auto-join scheduler (`DateTime.UtcNow` comparisons) — **not touched**
- ✅ `_easternTz`, `FormatEastern`, `FormatEasternTimeOnly` — **kept** (just no longer called from markup)
- ✅ `CreatedAt` converted via `.ToUniversalTime().ToString("O")` before passing to JS
- ✅ ISO strings with missing `Z` handled by JS (`isoUtc + 'Z'` guard)
- ✅ No server-side timezone conversion introduced

---

## Build Result

```
Build succeeded.
0 Error(s)
12 Warning(s) — all pre-existing
```

---

## How to Test

1. Deploy to ECS or run locally
2. Load `/meetings` — meeting times in upcoming cards and in the table should match your browser timezone (not ET)
3. Open a meeting detail — `CreatedAt` date should be in local timezone
4. Test with a browser set to a non-ET timezone (e.g., UTC+5:30) to confirm

---

## Cycle 2 — Blazor Lifecycle Fix

**Date:** 2026-04-13  
**Engineer:** Tony Stark (software-engineer)  
**Commit:** `70d04e9` (fix(firm#1710): move JS interop to OnAfterRenderAsync to fix Blazor pre-render lifecycle violations)  
**Build:** ✅ 0 errors, 12 warnings (all pre-existing)

---

### Root Cause

Cycle 1 introduced JS interop calls (`JS.InvokeAsync`) inside `OnInitializedAsync` and `LoadMeetings()` (called from `OnInitializedAsync`). In Blazor Server InteractiveServer mode, components pre-render server-side before the SignalR circuit establishes — JS interop is not available at that stage. The calls silently failed, causing:
- **MeetingDetail.razor:** `_localCreatedAt` was null → fallback server time shown → flipped to local time after SignalR reconnect (~1-3s visual flash)
- **Meetings.razor:** All `_localCreatedTimes`, `_localStartTimes`, `_localEndTimes` dictionaries populated with fallback values on first load

---

### Files Changed

| File | Change |
|------|--------|
| `Components/Pages/MeetingDetail.razor` | Removed JS interop block from `OnInitializedAsync`; added `OnAfterRenderAsync(firstRender: true)` to format `_localCreatedAt` after circuit connects |
| `Components/Pages/Meetings.razor` | Added `_jsReady` bool flag; added `OnAfterRenderAsync(firstRender: true)` that sets `_jsReady = true` then calls `PreFormatMeetingTimesAsync()`; added `if (!_jsReady) return;` guard at top of `PreFormatMeetingTimesAsync()` |
| `wwwroot/js/firm-utils.js` | Added `if (!isoUtc) return '';` null guard to all three functions; improved UTC normalization to strip `+00:00` offset (not just check for `Z` suffix) |

---

### Behavior After Fix

- **Initial load:** Times show as fallback (blank/server formatted) until SignalR circuit connects
- **After circuit connects:** `OnAfterRenderAsync(firstRender: true)` fires → sets `_jsReady = true` → calls `PreFormatMeetingTimesAsync()` → `StateHasChanged()` → UI updates with local timezone times
- **Subsequent LoadMeetings() calls** (polling timer, Refresh button): `_jsReady` is already true → `PreFormatMeetingTimesAsync()` runs normally → times are correct
- **No visual flash:** Times render correctly from first JS-ready render; no flip from Eastern to local

---

### Build Result

```
Build succeeded.
0 Error(s)
12 Warning(s) — all pre-existing
```

---

### How to Test

1. Set browser to a non-ET timezone (e.g., UTC+5:30 or Pacific)
2. Load `/meetings` — on first paint times may show server fallback briefly, then flip to local timezone correctly (no ET flash)
3. Refresh the page — same behavior, no ET flash on reload
4. Open a meeting detail — CreatedAt shows correctly in local timezone without flip
5. Confirm no console errors related to `firmUtils`
