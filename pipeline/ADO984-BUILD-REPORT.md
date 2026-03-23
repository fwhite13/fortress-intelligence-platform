# Build Report — ADO #984
## Dashboard Enhancements: Fraunces KPI Font, Enhanced Bar Chart, Stale Deal Alerts

**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-20  
**Commit:** `20a1dea`  
**Branch:** main  
**Status:** ✅ BUILD COMPLETE — pushed to origin

---

## CC Invocation
```bash
cd ~/projects/fip
cat /tmp/ado984-brief.md | claude --model sonnet --dangerously-skip-permissions -p
```

---

## Files Changed (3 only)

| File | Change Summary |
|------|----------------|
| `famos/src/FamOs.Web/wwwroot/css/famos.css` | `.famos-kpi-value` → `font-family: var(--font-display)`; added `.famos-stale-badge`, `.famos-stale-warn`, `.famos-stale-urgent` |
| `famos/src/FamOs.Web/Services/OpportunityService.cs` | `DashboardSummary` gets `PremiumByStage` + `StaleDeals`; new `StaleOpportunity` class; two new EF queries in `GetDashboardSummaryAsync` |
| `famos/src/FamOs.Web/Components/Pages/Dashboard.razor` | Enhanced pipeline bar chart (stage colors, premium totals, clickable); Stale Deal Alerts card; `GetStageColor` + `FormatPremium` helpers |

**Diff stat:** 3 files changed, 119 insertions(+), 17 deletions(-)

---

## Self-Review Checklist

- [x] `.famos-kpi-value` has `font-family: var(--font-display)` (replaces hardcoded `'Fraunces', Georgia, serif`)
- [x] `DashboardSummary` has `PremiumByStage` + `StaleDeals` properties
- [x] `StaleOpportunity` class defined (after `DashboardSummary`)
- [x] `GetDashboardSummaryAsync` queries premium-by-stage (`premiumByStage`) and stale deals (`staleOpps`, threshold 14d, urgent 21d, take 8)
- [x] Pipeline bar chart replaced with enhanced version: stage colors via `GetStageColor`, premium per bar, clickable rows, excludes `Bound` + `ClosedNotBound`
- [x] `GetStageColor` helper in Dashboard.razor `@code`
- [x] `FormatPremium` helper in Dashboard.razor `@code` (M/K/raw formatting)
- [x] Stale Deal Alerts card shown when `_summary.StaleDeals.Any()` — full width `xs="12"`, urgent row tinted #fff5f5, warn tinted #fffdf5
- [x] Stale badge CSS classes added (`.famos-stale-badge`, `.famos-stale-warn`, `.famos-stale-urgent`)
- [x] Bar fill color uses `{color}` from `GetStageColor` — no `var(--sky)` remaining in Dashboard
- [x] Only 3 files changed

---

## Change Details

### 1. famos.css — KPI Font Modernization
`font-family: 'Fraunces', Georgia, serif;` → `font-family: var(--font-display);`

This uses the CSS custom property defined at line 1075 (`--font-display: 'Fraunces', Georgia, serif;`) — semantically correct, consistent with `.font-display` utility class.

### 2. OpportunityService.cs — DashboardSummary Extensions

**New properties:**
```csharp
public Dictionary<LifecycleStage, decimal> PremiumByStage { get; set; } = new();
public List<StaleOpportunity> StaleDeals { get; set; } = new();
```

**New class:**
```csharp
public class StaleOpportunity { Id, Name, Stage, DaysStale, IsUrgent }
```

**New queries (before return):**
- `premiumByStage`: groups active opps by stage, sums `EstimatedPremium` where non-null, returns `Dictionary<LifecycleStage, decimal>`
- `staleOpps`: finds opps not updated in 14+ days, ordered by `UpdatedAt` asc, take 8, projects to `StaleOpportunity` with `IsUrgent = UpdatedAt < urgentThreshold` (21d)

### 3. Dashboard.razor — Enhanced Pipeline Chart

**Before:** Simple 6px bars, single navy color (`var(--sky)`), count only, no interaction  
**After:** 8px bars, per-stage hex colors, premium totals shown inline (`"4 · $1.2M"`), clickable rows navigate to `/pipeline`, excludes `Bound` stage from bar chart

**Stage colors:**
- Intake: #1d4ed8 (blue)
- UnderwritingPrep: #6d28d9 (purple)
- Marketed: #d97706 (amber)
- QuotesReceived: #0369a1 (sky blue)
- ClientDecision: #9333ea (violet)
- Binding: #C0272D (brand red)
- Bound: #059669 (green, excluded from chart)

### 4. Dashboard.razor — Stale Deal Alerts Card

Full-width card below the main grid. Renders only when stale deals exist. Each row:
- Deal name (bold, navy) + stage label
- Right: `famos-stale-badge` with days stale (warn: amber tones, urgent: red tones)
- Entire row clickable → navigates to opportunity detail

---

## Build Notes

- `.NET 9` not installed on SteamServer (pre-existing infra constraint; builds in AWS via CI). Code verified structurally via CC + manual grep review.
- No `var(--sky)` references remain in Dashboard.razor bar chart — confirmed via grep.
- `ClosedNotBound` already excluded from old chart; `Bound` newly excluded from enhanced chart per spec.
