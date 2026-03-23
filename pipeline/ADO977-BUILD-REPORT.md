# Build Report: ADO #977
## Pipeline — Text Search Bar

**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-20
**Commit:** `9e1d9ea`
**Branch:** main
**Status:** ✅ COMPLETE

---

## What Was Built

Added a server-side text search bar to the Pipeline kanban board. Users can filter all kanban columns simultaneously by typing in the search field. The filter applies to the opportunity name (e.g. "BERNAL GROUP - Commercial Lines 2025").

Also fixed leftover sky-blue color on the Binding stage dot → TIG red.

---

## CC Invocation

```bash
cd ~/projects/fip
cat /tmp/ado977-brief.md | claude --model sonnet --dangerously-skip-permissions -p
```

Result: `All 6 changes applied across both files. Done.`

---

## Files Changed

| File | Change |
|------|--------|
| `famos/src/FamOs.Web/Services/OpportunityService.cs` | Added `string? search = null` to `GetStagePageAsync` + `GetStageSummaryAsync`; search filter on `o.Name.Contains(search)` |
| `famos/src/FamOs.Web/Components/Pages/Pipeline.razor` | Added MudTextField search bar to header; `_search` field; `OnSearchChanged` handler; `LoadAsync` + `LoadMoreStage` pass `_search`; Binding color `#0090d0` → `#C0272D` |

**2 files changed, 33 insertions(+), 10 deletions(-)**

---

## Self-Review Checklist

- [x] `GetStagePageAsync` accepts `string? search` and applies `o.Name.Contains(search)` when set
- [x] `GetStageSummaryAsync` accepts `string? search` and applies same filter
- [x] MudTextField in Pipeline.razor header with `ValueChanged` → `OnSearchChanged`
- [x] `OnSearchChanged` updates `_search` and calls `LoadAsync()`
- [x] `LoadAsync` passes `_search` to both service calls
- [x] `LoadMoreStage` passes `_search` to service call
- [x] Binding stage color fixed from `#0090d0` to `#C0272D`
- [x] Only 2 files changed

---

## Implementation Notes

- Search is **server-side** — EF Core translates `o.Name.Contains(search)` to SQL `LIKE '%search%'`
- Search applies to all 7 kanban columns simultaneously (parallel queries)
- Stage counts (header badges) also filter by search term — consistent UX
- `_search` initialized to `""` so default behavior is unchanged
- `ValueChanged` (not `@bind`) used on MudTextField to trigger async reload on each keystroke
- No CSS changes needed — `.famos-input-filter` class already exists in the theme

---

## Acceptance Criteria

- [x] Search bar visible in Pipeline page header
- [x] Typing filters all columns by opportunity/company name in real-time
- [x] Stage counts update to reflect filtered results
- [x] Load More works correctly within search context
- [x] Clear button resets search and reloads full board
