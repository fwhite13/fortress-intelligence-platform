# Build Report — ADO#976
## Accounts: Filter Bar + Column Sort

**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-20
**Commit:** `5b10f9a`
**Branch:** `main`
**Status:** ✅ COMPLETE — pushed to origin

---

## CC Invocation
```
cat /tmp/ado976-brief.md | claude --model sonnet --dangerously-skip-permissions -p
```
Result: SUCCESS — all changes applied in single CC run.

---

## Files Changed
1. `famos/src/FamOs.Web/Components/Pages/Accounts.razor` — +131 / -19
2. `famos/src/FamOs.Web/wwwroot/css/famos.css` — +19 / -0

**No other files changed.**

---

## What Was Built

### 1. Filter Bar (collapsible)
- Added `_showFilters` bool toggled by "Filters" button (placed before Sync button in header)
- Filter panel uses `.famos-filter-panel` CSS class (light gray bg, padding, rounded)
- **Text search** (`_search`) — moved from header row into filter panel; binds `oninput` for reactive filtering by CompanyName, City, State
- **State dropdown** (`_filterState`) — `MudSelect` populated from `_distinctStates` computed property (distinct non-null states from `_accounts`, sorted)
- **Active Opps toggle** (`_filterOpps`) — `MudSelect` with three options: All / Has Opps / No Opps
- **Apply** button — no-op (filters are reactive via computed property)
- **Clear** button — resets `_search = ""`, `_filterState = ""`, `_filterOpps = "All"`

### 2. Column Sort
- State: `_sortColumn = "Company"` (default), `_sortAsc = true`
- `SortBy(string column)` — toggles direction if same column, else resets to asc
- `SortIcon(string column)` — `RenderFragment` rendering ▲/▼ on active sort column
- All 4 headers (`famos-account-col-name/location/opps/sync`) get `famos-sort-header` class + `@onclick` calling `SortBy(...)`
- Sort keys: `"Company"` → `CompanyName`, `"Location"` → `State` then `City`, `"ActiveOpps"` → `ActiveOppCount`, `"LastSynced"` → `LastSyncedAt`

### 3. `_filteredAccounts` — Full Replacement
Old: simple `=>` expression (text search only)
New: `get {}` property applying:
1. Text search (CompanyName / City / State)
2. State filter (exact match)
3. Opps filter (HasOpps: `> 0`, NoOpps: `== 0`, All: no filter)
4. Sort (switch on `_sortColumn` + `_sortAsc`)

### 4. Active Opps Chip Color Fix
- `rgba(0,144,208,0.1); color:var(--sky)` → `rgba(192,39,45,0.1); color:#C0272D`

### 5. CSS Additions (`famos.css`)
- `.famos-filter-panel` — flex panel, `#f2f4f7` bg, border, border-radius 10px
- `.famos-sort-header` — cursor pointer, user-select none, flex
- `.famos-sort-header:hover` — color `#C0272D`

---

## Self-Review Checklist
- [x] Filter panel shows/hides on Filters button click (`_showFilters` toggle)
- [x] Text search still works — moved into filter panel, same `oninput` binding
- [x] State dropdown populates from `_distinctStates` (distinct + sorted from `_accounts`)
- [x] Has Active Opps toggle works (All / Has Opps / No Opps logic in `_filteredAccounts`)
- [x] Clear button resets `_search`, `_filterState`, `_filterOpps`
- [x] All 4 column headers clickable via `SortBy("Company"|"Location"|"ActiveOpps"|"LastSynced")`
- [x] Sort indicator ▲/▼ rendered by `SortIcon()` on active column
- [x] `_filteredAccounts` applies text search + state filter + opps filter + sort
- [x] Active Opps chip color fixed to TIG red `rgba(192,39,45,0.1)` / `#C0272D`
- [x] No other files changed

---

## Ready for Clint (code-reviewer)
