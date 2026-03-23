# Review Report — ADO #977: Pipeline Text Search

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `9e1d9ea`
**Cycle:** 1
**Verdict:** ✅ PASS
**Date:** 2026-03-20

---

## Scope Check

```
git diff --name-only 9e1d9ea^ 9e1d9ea
  famos/src/FamOs.Web/Services/OpportunityService.cs
  famos/src/FamOs.Web/Components/Pages/Pipeline.razor
```

✅ Exactly 2 files — clean scope.

---

## Checklist Results

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | `GetStagePageAsync` — `string? search = null` param + filter | ✅ | `public async Task<OpportunityPage> GetStagePageAsync(LifecycleStage stage, int pageIndex, string? affinityId = null, string? search = null)` → `if (!string.IsNullOrWhiteSpace(search)) query = query.Where(o => o.Name.Contains(search));` |
| 2 | `GetStageSummaryAsync` — same param + filter | ✅ | `public async Task<Dictionary<LifecycleStage, int>> GetStageSummaryAsync(string? affinityId = null, string? search = null)` → same guard + filter |
| 3 | `_search` field — `private string _search = ""` | ✅ | Line 86: `private string _search = "";` |
| 4 | `OnSearchChanged` — updates `_search`, calls `LoadAsync()` | ✅ | `_search = value ?? ""; await LoadAsync();` |
| 5 | `LoadAsync` — passes `_search` to both service methods | ✅ | `GetStageSummaryAsync(affinityId, _search)` and `GetStagePageAsync(col.Stage, 0, affinityId, _search)` |
| 6 | `LoadMoreStage` — passes `_search` to `GetStagePageAsync` | ✅ | `OppService.GetStagePageAsync(stage, current.PageIndex + 1, affinityId, _search)` |
| 7 | MudTextField — `Value` + `ValueChanged` (not `@bind-Value`) | ✅ | `Value="_search" ValueChanged="@((string v) => OnSearchChanged(v))"` |
| 8 | `LifecycleStage.Binding` color — `"#C0272D"` not `"#0090d0"` | ✅ | Line 170: `LifecycleStage.Binding => "#C0272D"` |
| 9 | Scope — only 2 files | ✅ | Confirmed above |
| 10 | No N+1 — `Task.WhenAll` parallel loading preserved | ✅ | `Task.WhenAll` on `GetStagePageAsync` calls still in place; `IDbContextFactory` used correctly for concurrent EF access |

---

## Issues Found

### Important (Non-blocking — recommend follow-up ticket)

**Case sensitivity is implicit.**
`o.Name.Contains(search)` defers case-sensitivity entirely to DB collation. On SQL Server with `_CI_` collation (typical default) this is case-insensitive — correct behavior. But if the DB ever moves to PostgreSQL default collation or a `_CS_` collation, search silently becomes case-sensitive and users get confusing empty results. Intent is not explicit in code.

**Recommendation:** Follow-up ticket to use `EF.Functions.Like(o.Name, $"%{search}%")` with explicit collation, or at minimum `.ToLower()` on both sides. Not a blocker for this WI.

### Nitpicks

1. **No debounce on search field.** Every keystroke fires `LoadAsync()` → 8 DB queries (1 summary + 7 stage pages). A 250–300 ms debounce would reduce server load. MudTextField supports `DebounceInterval` prop directly.

2. **Summary count shows filtered total.** `@_stageSummary.Values.Sum() active opportunities` will display filtered count while searching (e.g., "3 active opportunities") — could confuse users into thinking that's the total. Consider "3 matching opportunities" when `_search` is non-empty. Minor UX, not a code defect.

---

## Review Summary

All 10 checklist items pass. Core feature is correctly implemented end-to-end:

- Search param flows: `MudTextField` → `OnSearchChanged` → `_search` → `LoadAsync` / `LoadMoreStage` → both service methods
- Guards are correct: `IsNullOrWhiteSpace` handles null, empty, and whitespace
- `Task.WhenAll` parallelism intact — no N+1 introduced
- ADO#976 `@bind-Value` lesson applied correctly — `ValueChanged` pattern used
- Binding color fix (`#C0272D`) confirmed

**Verdict: PASS** — 2 nitpick-level items, 1 Important item recommended as follow-up ticket, no blockers.

---

*CC invocation: `cat review-brief-ado977.md | claude --model sonnet --print --dangerously-skip-permissions`*
