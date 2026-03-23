# Review Report — ADO #984: Dashboard Enhancements
**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `20a1dea`  
**Review Cycle:** 1  
**Date:** 2026-03-20  
**Verdict:** ✅ PASS

---

## Review Method

Review brief written and piped to Claude Code CLI:
```bash
cat pipeline/ADO984-review-brief.md | claude --permission-mode bypassPermissions --print '...'
```
CC ran `git diff 20a1dea^ 20a1dea` and inspected all 3 changed files directly.

---

## Checklist Results

### CSS — `famos.css`
| # | Item | Result |
|---|------|--------|
| 1 | `.famos-kpi-value` uses `font-family: var(--font-display)` (no hardcoded Fraunces) | ✅ PASS |
| 2 | `.famos-stale-badge`, `.famos-stale-warn`, `.famos-stale-urgent` with correct amber/red palettes | ✅ PASS |

Details:
- `.famos-stale-warn`: bg `#fef3c7` / color `#92400e` (amber) ✅
- `.famos-stale-urgent`: bg `#fee2e2` / color `#991b1b` (red) ✅

### Service — `OpportunityService.cs`
| # | Item | Result |
|---|------|--------|
| 3 | `DashboardSummary` has `PremiumByStage: Dictionary<LifecycleStage, decimal>` + `StaleDeals: List<StaleOpportunity>` | ✅ PASS |
| 4 | `StaleOpportunity` class: Id, Name, Stage, DaysStale, IsUrgent | ✅ PASS |
| 5 | EF query uses `.GroupBy(o => o.LifecycleStage)` + `.Sum(o => o.EstimatedPremium!.Value)` | ✅ PASS |
| 6 | Stale threshold 14d, urgent threshold 21d, `Take(8)` | ✅ PASS |
| 7 | No N+1 risks — all queries single DB round-trips | ✅ PASS |
| 8 | `PremiumByStage` and `StaleDeals` populated in return block | ✅ PASS |

### Razor — `Dashboard.razor`
| # | Item | Result |
|---|------|--------|
| 9 | `GetStageColor` returns stage-specific hex colors (not CSS vars) | ✅ PASS |
| 10 | `FormatPremium` formats M/K/raw correctly | ✅ PASS |
| 11 | Bar chart rows clickable → `/pipeline` | ✅ PASS |
| 12 | Stale Deal Alerts card: conditional on `StaleDeals.Any()`, `xs="12"` | ✅ PASS |
| 13 | Stale badges use `.famos-stale-badge` CSS class (not inline styles) | ✅ PASS |
| 14 | `@onclick` on async methods uses `async () => await` pattern | ✅ PASS |
| 15 | No remaining `var(--sky)` in bar chart fill | ✅ PASS |
| 16 | Exactly 3 files changed | ✅ PASS |

---

## Additional Observations (Non-blocking)

These are nitpicks / low-risk items. They do NOT affect the verdict.

1. **EF Core translation risk** — `DaysStale = (int)(DateTime.UtcNow - o.UpdatedAt).TotalDays` inside `.Select()` projected to `ToListAsync()` may fail at runtime depending on EF Core provider. SQL Server EF can struggle with `TimeSpan.TotalDays`. Safer: compute client-side after materialize, or use `EF.Functions.DateDiffDay(o.UpdatedAt, DateTime.UtcNow)`. *Low risk if already tested against target provider.*

2. **Multiple `DateTime.UtcNow` evaluations** — `staleThreshold`, `urgentThreshold`, and the `DaysStale` projection each call `DateTime.UtcNow` independently. Capture `var now = DateTime.UtcNow;` once to avoid midnight-boundary inconsistency.

3. **Null-forgiving operator** — `_summary!.StaleDeals.Any()` uses the `!` operator outside the null guard. Suppress is cosmetically fine here but hides a valid warning.

4. **Unnecessary interpolation** — `Nav.NavigateTo($"/pipeline")` has no interpolation holes; simplify to `Nav.NavigateTo("/pipeline")`.

**These are follow-up nitpicks. Pipeline proceeds.**

---

## Summary

All 16 checklist items pass. Implementation is clean and correct. Four minor nitpicks noted for awareness, none blocking. The EF translation concern (item 1) is the only one worth a quick sanity check if this hasn't been exercised against SQL Server yet.

**VERDICT: PASS — advance to SECURITY.**
