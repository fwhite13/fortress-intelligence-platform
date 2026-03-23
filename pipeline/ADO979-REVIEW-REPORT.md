# Review Report — ADO #979 + #980
## General Tasks + Assign-To
**Commit:** `ba2c93c`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Verdict:** ✅ PASS

---

## Checklist Results

### Entity + DB

| # | Check | Result |
|---|-------|--------|
| 1 | `FamOsTask.OpportunityId` is `Guid?` (nullable) | ✅ |
| 2 | `FamOsDbContext` marks task→opportunity FK as `.IsRequired(false)` | ✅ |
| 3 | `Program.cs` has MODIFY COLUMN migration to make `OpportunityId` nullable | ✅ |
| 4 | Migration uses try/catch (not `ADD COLUMN IF NOT EXISTS`) | ✅ |

**Notes:**
- `FamOsTask.cs`: `OpportunityId` changed from `Guid` → `Guid?`; nav property changed from `Opportunity` (required) → `Opportunity?`. Correct.
- `FamOsDbContext.cs`: `.HasForeignKey(x => x.OpportunityId).IsRequired(false)` — correct.
- `Program.cs`: `ALTER TABLE tasks MODIFY COLUMN OpportunityId CHAR(36) ... NULL` wrapped in try/catch — idempotent and safe. ✅

---

### TaskService.cs

| # | Check | Result |
|---|-------|--------|
| 5 | `CreateTaskAsync` signature takes `Guid? opportunityId` | ✅ |
| 6 | General tasks (null OpportunityId) visible in task queries — not filtered out | ✅ |
| 7 | No date arithmetic inside EF `.Select()` projections | ✅ |
| 8 | No N+1 query risks introduced | ✅ |

**Notes:**
- All three user-facing query methods (`GetOpenTasksForUserAsync`, `GetOpenTasksPagedAsync`, `GetOpenTaskCountForUserAsync`) updated with `|| (t.OpportunityId == null && t.AssignedToUserId == userId)` — general tasks are visible. ✅
- `GetAllOpenTasksAsync` uses `(t.OpportunityId == null || !t.Opportunity!.IsClosed)` — correct, passes-through general tasks. ✅
- `.OrderBy(t => t.DueAt.HasValue ? 0 : 1)` is a constant expression translatable by EF — no date arithmetic. ✅
- All projections use `.Select(t => new TaskWithOpportunity(t, t.Opportunity))` on materialized results — no N+1. ✅
- `GetOpenTaskCountForUserAsync` uses no explicit `.Include()` but accesses `t.Opportunity.OwnerUserId` in WHERE — EF Core translates to LEFT JOIN in SQL, correctly supporting the null-OpportunityId short-circuit. Safe. ✅
- `TaskWithOpportunity` record updated to `Opportunity?` — correct. ✅

---

### AddTaskDialog.razor

| # | Check | Result |
|---|-------|--------|
| 9 | `_isGeneral` toggle exists; when true, opportunity autocomplete is hidden | ✅ |
| 10 | Opportunity field label says "optional" (not required) | ✅ |
| 11 | Submit only disabled when title is empty (not when opportunity is null) | ✅ |
| 12 | Assign-to dropdown populated from `AffinityConfig.Users` | ✅ |
| 13 | Defaults to current user (`_currentUserId`) | ✅ |
| 14 | `CreateTaskAsync` called with `_assignToUserId` | ✅ |
| 15 | No `@onclick="() => AsyncMethod()"` without async/await | ✅ |

**Notes:**
- `_isGeneral` MudSwitch gates the opportunity autocomplete via `@if (!_isGeneral)`. ✅
- Opportunity label changed from `"Opportunity *"` → `"Opportunity (optional)"`. ✅
- `Disabled="@(string.IsNullOrWhiteSpace(_title))"` — correctly title-only. ✅
- Assign-to MudSelect: "Myself" item + foreach over `_otherUsers` (filtered from `AffinityOptions.Value.Users`). ✅
- `_assignToUserId = _currentUserId` on init. ✅
- `Submit()` passes `_assignToUserId` as 4th arg. ✅
- `OnClick="@(async () => await Submit())"` — lambda is `async` and properly awaits `Submit()`. Not a fire-and-forget. ✅
- Submit guard: `if (string.IsNullOrWhiteSpace(_title)) return;` — no opportunity check. ✅

---

### TaskCenter.razor

| # | Check | Result |
|---|-------|--------|
| 16 | Filter: All / Account-linked / General chips/buttons | ✅ |
| 17 | General tasks shown with "General" label (not account name) | ✅ |
| 18 | Filter uses `Value` + `ValueChanged` pattern (or equivalent — MudChips with OnClick are fine) | ✅ |
| 19 | No Blazor computed property getter re-render bug (use explicit field + rebuild, not computed getter) | ⚠️ NITPICK |

**Notes:**
- Three MudChip filter buttons render and gate the `FilteredTasks` result — All / Account-linked / General. ✅
- General tasks section uses `"General Tasks"` header + "General" chip, not account name. ✅
- Chips use `OnClick="@(() => _taskFilter = "...")"` — synchronous lambdas on MudChip `EventCallback`, Blazor will trigger re-render automatically. Equivalent to the pattern. ✅
- **Item 19 — NITPICK:** `FilteredTasks` is a computed property getter and is called **three times** in the template per render cycle (`.Any()` check, account-linked `foreach`, `generalTasks` local var). The getter is pure (no side effects, no `StateHasChanged()`), so there is **no re-render loop**. However, it triple-evaluates the LINQ chain unnecessarily. The ideal pattern is to cache the result in a `_filteredTasks` field rebuilt on state changes. This is a nitpick — correctness is not impacted for in-memory lists of this scale, but worth a follow-up ticket.
- Subtitle counter `_tasks.Where(t => t.Opportunity != null).Select(t => t.Opportunity!.Id).Distinct().Count()` correctly handles nulls now. ✅

---

## Issues Summary

| Severity | # | Description |
|----------|---|-------------|
| Nitpick | 1 | `FilteredTasks` computed getter called 3× per render cycle — pure, not a bug, but should be cached field |

---

## Verdict: PASS

No blocking issues. One nitpick (triple evaluation of `FilteredTasks`) that is safe to ship and can be cleaned up in a follow-on. All entity, DB, service, dialog, and page concerns are correctly addressed.

**ADO #979** (General Tasks): Entity, DB, migration, service queries, and UI all implemented correctly. ✅  
**ADO #980** (Assign-To): Dialog populated from `AffinityConfig.Users`, defaults to current user, passes `_assignToUserId` to service. ✅
