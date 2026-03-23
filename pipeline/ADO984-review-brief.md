# Code Review Brief — ADO #984: Dashboard Enhancements
# Commit: 20a1dea
# Reviewer: Hawkeye (Clint Barton)

You are a senior code reviewer. Analyze the changes in commit 20a1dea of this repository.

## Command to get the diff
Run: git diff 20a1dea^ 20a1dea

## Files changed
1. famos/src/FamOs.Web/wwwroot/css/famos.css
2. famos/src/FamOs.Web/Services/OpportunityService.cs
3. famos/src/FamOs.Web/Components/Pages/Dashboard.razor

## Your task
Read the full diff for all 3 files and check each item below. For each, output PASS ✅ or FAIL ❌ with a brief explanation if FAIL.

## Checklist

### CSS (famos.css)
1. Does `.famos-kpi-value` use `font-family: var(--font-display)` — NOT a hardcoded "Fraunces" string?
2. Do `.famos-stale-badge`, `.famos-stale-warn`, `.famos-stale-urgent` all exist with amber/red color palettes?

### Service (OpportunityService.cs)
3. Does `DashboardSummary` have both `PremiumByStage: Dictionary<LifecycleStage, decimal>` AND `StaleDeals: List<StaleOpportunity>` properties?
4. Is `StaleOpportunity` class defined with properties: Id, Name, Stage, DaysStale, IsUrgent?
5. Does the EF query for premium-by-stage use `.GroupBy(o => o.LifecycleStage)` with a `.Sum` or `.SumAsync` pattern?
6. Does the stale query use `UpdatedAt < staleThreshold` (14 days), urgent = `UpdatedAt < urgentThreshold` (21 days), and `Take(8)`?
7. Are ALL queries single DB round-trips (no N+1 risks — no lazy loading loops, no per-item queries)?
8. Are the new return properties (PremiumByStage and StaleDeals) actually populated in the return block?

### Dashboard.razor
9. Does `GetStageColor` return stage-specific hex colors (NOT `var(--sky)` or any CSS variable)?
10. Does `FormatPremium` correctly format values as M (millions), K (thousands), or raw?
11. Are bar chart rows clickable and do they navigate to `/pipeline`?
12. Does the Stale Deal Alerts card render conditionally when `_summary.StaleDeals.Any()` is true, with `xs="12"` for full width?
13. Do stale badges use `.famos-stale-badge` CSS class (not inline style attributes)?
14. Do ALL `@onclick` handlers on async methods use the `async () => await` lambda pattern (required for Blazor Server)?
15. Is there NO remaining `var(--sky)` in bar chart fill colors?
16. Confirm exactly 3 files changed (CSS, Service, Razor).

## Output format
For each item, output:
`[N]. [description] — ✅ PASS` or `[N]. [description] — ❌ FAIL: [reason]`

Then give a final VERDICT: PASS / NEEDS-CHANGES / FAIL
- PASS: all 16 items pass
- NEEDS-CHANGES: 1+ non-critical issues
- FAIL: critical structural issues

Finally, list any additional observations (bugs, anti-patterns, security issues) not covered by the checklist.
