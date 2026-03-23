# QA Report: ADO#1003 + ADO#995 (Bundled)
**Commit:** `72af7b3`
**App:** `https://famos.dev.fortressam.ai/`
**QA Tier:** Code-level + Infrastructure (Entra auth blocks post-login browser tests)
**Tester:** Black Widow (Natasha Romanoff — qa-analyst)
**Date:** 2026-03-21
**Verdict:** ✅ PASS

---

## Test Results

| Test | Description | Result |
|------|-------------|--------|
| T1 | Health endpoint (`/health`) | ✅ 200 |
| T2 | FipShared CSS (`fip-tokens.css`) | ✅ 200 |
| T3 | Status list fix — 5 new in-progress statuses in guard | ✅ Confirmed |
| T4 | Poll loop — 1800s timeout, adaptive intervals (5s / 30s) | ✅ Confirmed |
| T5 | `_showManualEntry = true` on timeout | ✅ Confirmed |
| T6 | Progress UX — `MudProgressCircular` + info alert w/ 10–30 min messaging | ✅ Confirmed |

---

## Detail

### T3 — QuoteScraperService.cs: Expanded Status Guard
Commit adds 5 new statuses to the in-progress guard:
```csharp
if (reqStatus is "Pending" or "Processing" or "Assembling" or "Queued"
                or "Submitted" or "Received" or "InProgress" or "In Progress")
    return null;  // still working
```
All 5 new statuses (`Queued`, `Submitted`, `Received`, `InProgress`, `In Progress`) confirmed present. The scraper will no longer treat these as terminal/unknown and bail early.

### T4 — QuoteScraperPanel.razor: Adaptive Poll Loop
```csharp
var shortPhaseSecs = 120;   // 5s intervals for 2 min
var maxPollSecs = 1800;     // 30 min total timeout
while (elapsed.TotalSeconds < maxPollSecs)
    var intervalMs = elapsed.TotalSeconds < shortPhaseSecs ? 5000 : 30000;
```
- Total timeout expanded from 60s → **1800s (30 min)** ✅
- Adaptive intervals: **5s for first 2 min**, then **30s** thereafter ✅

### T5 — _showManualEntry on Timeout
```csharp
if (result == null)
{
    _scrapeError = "Scraper is taking longer than expected. The PDF has been submitted — check back later or enter the premium manually.";
    _showManualEntry = true;
    return;
}
```
Manual entry form now surfaces on timeout instead of a dead-end error. ✅

### T6 — Progress UX (ADO#995)
```razor
<MudProgressCircular Indeterminate="true" Color="Color.Primary" Size="Size.Small" />
<MudAlert Severity="Severity.Info" Class="mt-3" Dense="true">
    Large carrier quotes may take 10–30 minutes. You can navigate away — the PDF has been submitted.
</MudAlert>
```
Circular spinner present. Info alert with "10–30 minutes" messaging confirmed. ✅

---

## Limitations / Fred's Manual Sign-Off Required

- **Entra auth blocks post-login browser testing.** The above verifications are code-level only.
- **Fred must test with a real carrier PDF** to confirm:
  1. The scraper actually waits for the result before showing the premium prompt
  2. The manual entry form surfaces correctly on timeout
  3. The adaptive poll intervals behave as expected in the live environment

---

## ADO Work Items
- **ADO#1003** — Fix status list + poll loop: ✅ Done
- **ADO#995** — Progress UX improvements: ✅ Done
