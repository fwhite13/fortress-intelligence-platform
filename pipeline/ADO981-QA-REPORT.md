# QA Report — ADO #981: Pipeline Side Drawer
**Agent:** Black Widow (Natasha Romanoff)  
**Environment:** famos-dev (https://famos.dev.fortressam.ai)  
**Date:** 2026-03-20  
**Verdict:** ⚠️ WARN

---

## Test Results

| # | Test | Result | Notes |
|---|------|--------|-------|
| T1 | Health check | ✅ PASS | HTTP 200 |
| T2 | Pipeline page loads | ✅ PASS | Kanban board visible, 66 active opportunities, all columns rendered |
| T3 | Click card opens drawer (no nav) | ✅ PASS | URL stays at `/pipeline`; side panel slides in from right |
| T4 | Drawer shows key fields | ✅ PASS | See field detail below |
| T5 | Close button (X) dismisses drawer | ✅ PASS | X click closes drawer, board returns to full view |
| T6 | Escape key closes drawer | ❌ FAIL | Escape does NOT close the drawer — bug |
| T7 | "View Full Details" navigates | ✅ PASS | URL changed to `/opportunity/edce09d1-8102-4fee-852d-910910c3dd48` |
| T8 | Avatar color (TIG red) | ✅ PASS | `rgb(192, 39, 45)` confirmed via DOM — TIG red, not sky-blue |

---

## T4 — Drawer Field Detail

Tested on **RIOS TRUCKING LLC** (Intake) and **MITCH CHESTER TRUCKING INC** (Intake w/ assigned AM):

| Field | Shown | Value (MITCH CHESTER) |
|-------|-------|----------------------|
| Opportunity name | ✅ | MITCH CHESTER TRUCKING INC |
| Stage | ✅ | Intake |
| Est. Premium | ✅ | $20,700 |
| Effective Date | ✅ | Apr 24, 2026 |
| Account Manager | ✅ | fred.white@fortressam.ai |
| Status chip | ✅ | "Waiting on Client" (famos-signal-chip styled) |
| Last Stage Transition | ⚠️ Missing | Not present in drawer HTML |

**Note:** The spec mentioned "last stage transition" as a drawer field — this field is not rendered. Minor gap, logged as WARN finding.

---

## T6 — Escape Key Bug Detail

- Opened drawer via card click
- Pressed Escape via `page.keyboard.press('Escape')` — no effect
- Dispatched `new KeyboardEvent('keydown', {key: 'Escape', bubbles: true})` on `document` — no effect  
- Dispatched same event on `.famos-side-drawer` element directly — no effect
- DOM confirms `mud-drawer--open` class persists after all attempts
- **Root cause:** No `keydown` listener for Escape is wired up on the drawer or document level

---

## T8 — Avatar Color Detail

- Found "FW" avatar circles on 9 kanban cards (MITCH CHESTER, SILVER DOLLAR, WMS TRUCKING LLC, AGROJAM LLC, PACE MOTOR LINES, MA ELENA LLC, etc.)
- Computed `backgroundColor`: `rgb(192, 39, 45)` — TIG red ✅
- Previous behavior would have been sky-blue (`rgb(14, 165, 233)` or similar)

---

## Issues Found

### 🐛 BUG — T6: Escape key does not close drawer
- **Severity:** Medium
- **Repro:** Open any kanban card drawer → Press Escape
- **Expected:** Drawer closes
- **Actual:** Drawer remains open
- **Fix:** Add `@keydown` handler or MudBlazor drawer keyboard event binding to listen for `Escape` and set drawer open state to false

### ⚠️ WARN — "Last Stage Transition" field missing from drawer
- **Severity:** Low
- **Notes:** The ADO spec lists "last stage transition" as a drawer field, but it's not rendered. May require API data or was deferred.

---

## Verdict Summary

**WARN** — Core drawer functionality works (T1–T5, T7–T8 all pass). Two issues:
1. Escape key closure broken (T6 FAIL) — medium severity UX bug
2. "Last stage transition" field not shown — minor gap vs. spec

The drawer correctly opens without navigation, shows key opportunity data, X-closes properly, and "View Full Details" navigates correctly. Avatar color is TIG red. Recommending WARN (not FAIL) since the drawer is functional and usable.
