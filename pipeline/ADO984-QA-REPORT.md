# QA Report — ADO #984
## Dashboard Enhancements: KPI Fraunces Font, Enhanced Bar Chart, Stale Deal Alerts

**Analyst:** Black Widow (Natasha Romanoff)  
**Date:** 2026-03-21  
**Environment:** https://famos.dev.fortressam.ai  
**Verdict:** ❌ FAIL

---

## Executive Summary

Dashboard is **crashing on load** with a Blazor server-side runtime exception. The page never renders — all UI tests (T2–T9) are blocked. The error is consistent across multiple page loads.

---

## Test Results

| Test | Result | Notes |
|------|--------|-------|
| T1 — Health endpoint | ✅ PASS | HTTP 200 |
| T2 — Dashboard loads | ❌ FAIL | Blazor crash — page does not render |
| T3 — KPI Fraunces font | ❌ BLOCKED | Dashboard did not load |
| T4 — Bar chart stage colors | ❌ BLOCKED | Dashboard did not load |
| T5 — Bar chart premium totals | ❌ BLOCKED | Dashboard did not load |
| T6 — Bar chart rows clickable | ❌ BLOCKED | Dashboard did not load |
| T7 — "Full View →" button | ❌ BLOCKED | Dashboard did not load |
| T8 — Stale Deal Alerts card | ❌ BLOCKED | Dashboard did not load |
| T9 — Stale badge styling | ❌ BLOCKED | Dashboard did not load |

---

## Error Details

**Error Message:**
```
No coercion operator is defined between types 'System.DateTime' and 'System.Nullable`1[System.TimeSpan]'.
```

**Error Type:** Blazor server-side circuit crash (unhandled exception during component rendering)

**Behavior:** The Blazor WebSocket connects successfully (confirmed via console), but the server circuit immediately throws and the error boundary catches it, rendering "Something went wrong."

**Root Cause (suspected):** Type mismatch in the new stale deal alert logic. Most likely scenario: the code computing staleness (e.g., `DateTime.Now - opportunity.UpdatedAt`) produces a `TimeSpan`, but it's being compared or assigned to a `Nullable<TimeSpan>` (or vice versa) without an explicit cast. The `DateTime` and `Nullable<TimeSpan>` types mentioned in the error suggest the subtraction operation or a comparison expression is hitting a coercion boundary in compiled Razor/Blazor.

**Likely file:** Stale Deal Alerts component or the Dashboard component's `OnInitializedAsync`/`OnParametersSetAsync` where stale deal filtering logic was added.

**Fix direction:**
- Look for expressions like `(DateTime.Now - opp.UpdatedAt)` where `UpdatedAt` might be `DateTime?`
- Ensure null-safe subtraction: `(opp.UpdatedAt.HasValue ? DateTime.Now - opp.UpdatedAt.Value : (TimeSpan?)null)`
- Or check for a `TimeSpan?` property being directly compared to a `DateTime` expression

---

## Screenshots

- Dashboard crash: `2ea967ce-e61d-4f33-8c6d-565ac6dd1f6b.png` (error boundary displayed)

---

## Console Log

Blazor WebSocket connects normally — error is entirely server-side:
```
[INFO] WebSocket connected to wss://famos.dev.fortressam.ai/_blazor?id=...
```
No client-side JS errors. This is a .NET runtime exception in the Blazor circuit.

---

## Recommendation

**ROLLBACK or HOTFIX required before any further QA.** The dashboard is completely non-functional. The stale deal alert feature introduced a type coercion bug that crashes the entire page.

Priority fix: resolve the `DateTime` / `Nullable<TimeSpan>` type mismatch in the stale deal computation logic, then re-deploy and re-run QA.

---

*Pipeline stage: VERIFY → FAIL → return to BUILD*
