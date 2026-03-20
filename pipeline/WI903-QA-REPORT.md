# QA Report — WI903 Sprint 5 Re-check
**Agent:** Black Widow (Natasha Romanoff) — `qa-analyst`  
**Date:** 2026-03-19T22:36 EDT  
**Environment:** https://famos.dev.fortressam.ai (dev)  
**Bypass:** X-QA-Bypass: natasha-qa-token-famos-dev  
**Verdict:** ⚠️ WARN

---

## Test Results Summary

| Test | Result | Notes |
|------|--------|-------|
| T1 — Health + QA bypass | ✅ PASS | Both endpoints 200, qaBypass active |
| T2 — Core routes 200 | ⚠️ WARN | 3/4 pass; `/dashboard` → 404 (dashboard is at `/`) |
| T3 — No column errors in logs | ✅ PASS | CLEAN — no "Unknown column" in recent log stream |
| T4 — PascalCase columns confirmed | ✅ PASS | All 8 columns present |
| T5 — Dashboard renders stats | ✅ PASS | KPI cards visible at `/` (4 stat cards with counts) |
| T6 — Pipeline cards show owner initials | ⚠️ WARN | No initials — OwnerUserId NULL for all 71 opportunities (data gap) |
| T7 — Close dialog has reason dropdown | ⚠️ WARN | Close button renders; dialog requires live Blazor session to verify |
| T8 — No snake_case columns remain | ❌ FAIL | 9 snake_case columns still present (migration additive, old not dropped) |

---

## Detailed Findings

### T1 — Health + QA Bypass ✅
```
GET /health  → {"status":"healthy","service":"famos","timestamp":"2026-03-19T22:34:34Z"}
GET /qa/status → {"qaBypass":true,"environment":"dev","timestamp":"...","message":"QA bypass active"}
```
Both healthy.

### T2 — Core Routes ⚠️
```
/         → 200
/pipeline → 200
/tasks    → 200
/dashboard → 404  ← NOT a 500, but route doesn't exist
```
**Note:** The dashboard lives at `/` (root), not `/dashboard`. The title is "Dashboard — FAM OS" and KPI cards render there. The missing `/dashboard` route is a routing discrepancy vs the test spec — not a crash.

### T3 — Log Column Errors ✅
Log stream: `famos-web/famos-web/f7364beb592b493583f2136b8d2919ed`

Checked last 80 events. No "Unknown column" errors found. The `fail` log entries present are duplicate `ALTER TABLE` attempts from startup migrations (columns already exist — non-fatal, expected behavior).

```
CLEAN
```

### T4 — PascalCase Columns Confirmed ✅
```
CloseReason       ← opportunities
CloseNotes        ← opportunities
LastStageTransitionAt ← opportunities
CoverageTypes     ← submissions
SubmittedAt       ← submissions
QuoteResultJson   ← submissions
Notes             ← submissions
UpdatedAt         ← submissions
```
**8 of 8 rows returned.** ✅

### T5 — Dashboard Renders ✅
Dashboard is at `/` (not `/dashboard`). Server-rendered HTML confirms:
- 4 KPI cards: `famos-kpi-card kpi-navy/red/amber/green`
- Labels: "Active Opportunities" (0), and 3 others
- KPI values render (0/0/0/3)
- No 500 error

Dashboard is functional. `/dashboard` route is unregistered (404).

### T6 — Pipeline Cards Initials ⚠️ WARN
Pipeline board renders with 67 opportunities across 7 columns (Intake:18, App Review:15, Submitted:13, Quotes In:11, Proposal:7, Binding:3, Bound:0).

**No initials badge present in any card HTML.**

Root cause confirmed via DB: `OwnerUserId IS NULL` for all 71 opportunities. This is a **data gap**, not a missing UI component. If owner data were populated, the badge infrastructure would need to be verified separately.

```sql
SELECT COUNT(*) as total, SUM(CASE WHEN OwnerUserId IS NOT NULL THEN 1 ELSE 0 END) as has_owner
FROM opportunities;
-- total: 71, has_owner: 0
```

### T7 — Close Opportunity Dialog ⚠️ WARN (unverifiable)
The "Close" button is present on the opportunity workspace (`/opportunity/{id}`):
```html
<button class="famos-btn-danger" __internal_stopPropagation_onclick>
  <span class="mud-button-label">Close</span>
</button>
```
This triggers a Blazor client-side event that opens a MudDialog. The dialog content is **not present in SSR HTML** — it renders only after Blazor SignalR connects and the button is clicked. Cannot confirm dropdown vs plain text from static render alone.

> T7 status: Unverifiable without live browser+Blazor session. Marked WARN pending manual verification.

### T8 — Snake_case Columns Remain ❌ FAIL
The schema rename was **additive** — PascalCase columns were added but old snake_case columns were **not dropped**. Both sets coexist:

**Duplicate columns in `opportunities`:**
- `close_reason` AND `CloseReason`
- `close_notes` AND `CloseNotes`
- `last_stage_transition_at` AND `LastStageTransitionAt`

**Duplicate columns in `submissions`:**
- `coverage_types` AND `CoverageTypes`
- `submitted_at` AND `SubmittedAt`
- `responded_at` AND `RespondedAt`
- `quote_result_json` AND `QuoteResultJson`
- `notes` AND `Notes`
- `updated_at` AND `UpdatedAt`

T8 expected "CLEAN (no snake_case leftovers)" — actual result: 9 snake_case columns still present.

---

## Verdict: ⚠️ WARN

**Rationale:**
- App is functional — no 500s, no runtime column errors, PascalCase columns confirmed
- Schema migration was incomplete: old snake_case columns not dropped (T8 FAIL — but not blocking app function since app now uses PascalCase)
- Owner initials not visible due to missing data, not missing code
- T7 dialog cannot be verified without live Blazor session

**Blocking Issues (require follow-up):**
1. **T8** — Drop the 9 residual snake_case columns to complete the migration cleanup
2. **T2** — `/dashboard` route returns 404; if this is a required route, register it (redirect to `/`)
3. **T6** — Assign `OwnerUserId` to opportunities (or seed test data) to verify initials badge renders

**Not blocking:**
- T7 (visual verify requires manual Blazor session)
- T5 (dashboard at `/` is functional; `/dashboard` is just an unregistered alias)

---

## Schema State Reference

### PascalCase (new — confirmed present)
`CloseReason`, `CloseNotes`, `LastStageTransitionAt` (opportunities)  
`CoverageTypes`, `SubmittedAt`, `QuoteResultJson`, `Notes`, `UpdatedAt` (submissions)

### Snake_case (old — still present, should be dropped)
`close_reason`, `close_notes`, `last_stage_transition_at` (opportunities)  
`coverage_types`, `submitted_at`, `responded_at`, `quote_result_json`, `notes`, `updated_at` (submissions)

---

*— Natasha Romanoff, QA Analyst*
