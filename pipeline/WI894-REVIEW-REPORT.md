# Review Report: WI894 — FAM OS Sprint 4 (Intake Form + Task Center)

**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`
**Commit:** `a3654d4`
**Review Cycle:** 1
**Date:** 2026-03-19
**Files Reviewed:** 11 (3 new, 8 modified)
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Executive Summary

Solid sprint overall. Architecture is clean, the task auto-generation pattern is well-executed, and all key risks (CreateExecutionStrategy wrapper, SaveChangesAsync discipline in CreateTasksForStageAsync, MudDialogInstance concrete type) pass review. However there are **2 issues** that must be fixed before this ships:

1. **CRITICAL: `TaskService.cs` missing `using FamOs.Web.Domain`** — compile error, `NotFoundException` is unreachable
2. **Important: Redundant `@using FamOs.Web.Domain` in `IntakePanel.razor` and `TaskCenter.razor`** — already in `_Imports.razor`, violates project convention

---

## File-by-File Findings

### ✅ 1. `StageTaskTemplates.cs` (NEW)
- Namespace: `FamOs.Web.Domain` ✅
- Static class with `ForStage(LifecycleStage)` switch returning `IReadOnlyList<string>` ✅
- All 6 stages covered: UnderwritingPrep, Marketed, QuotesReceived, ClientDecision, Binding, Bound ✅
- Fallback `_ => Array.Empty<string>()` ✅
- Clean, no issues. Well-documented with XML summary.

---

### ❌ 2. `TaskService.cs` (NEW) — **CRITICAL**
- Namespace: `FamOs.Web.Services` ✅
- All 5 methods present: `GetOpenTasksForUserAsync`, `GetAllOpenTasksAsync`, `CompleteTaskAsync`, `CreateTaskAsync`, `GetOpenTaskCountForUserAsync` ✅
- `TaskWithOpportunity` record at bottom ✅
- Uses `IDbContextFactory<FamOsDbContext>` with `await using var db = await _dbFactory.CreateDbContextAsync()` ✅
- Query ordering (due-first, then by date) ✅

**🔴 CRITICAL ISSUE:**
`CompleteTaskAsync` (line 55) throws `new NotFoundException(...)`. `NotFoundException` is defined in `FamOs.Web.Domain` namespace (in `LifecycleCommandService.cs`). However, `TaskService.cs` only has:
```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
```
**`using FamOs.Web.Domain;` is absent.** This is a compile error — `NotFoundException` will not resolve.

**Fix:** Add `using FamOs.Web.Domain;` to the using block at the top of `TaskService.cs`.

---

### ✅ 3. `AddTaskDialog.razor` (NEW)
- `[CascadingParameter] MudDialogInstance MudDialog` (concrete, NOT `IMudDialogInstance`) ✅
- No `@using FamOs.Web.Domain` (already in `_Imports`) ✅
- Has `@using FamOs.Web.Services` and `@using FamOs.Web.Data.Entities` ✅
- `MudAutocomplete` for opportunity selection with `SearchFunc` and `ToStringFunc` ✅
- Cancel + Submit buttons with `Disabled="@(_selectedOpp == null || string.IsNullOrWhiteSpace(_title))"` guard ✅
- `OnInitializedAsync` loads opportunities from `OppService.GetPipelineAsync()` ✅
- Clean. No issues.

---

### ✅ 4. `Opportunity.cs` (MODIFIED)
- `IntakeResponsesJson` property present as `public string? IntakeResponsesJson { get; set; }` ✅
- Positioned after `EffectiveDateTarget` ✅
- XML doc comment explaining structure ✅
- Nullable string ✅

---

### ✅ 5. `FamOsDbContext.cs` (MODIFIED)
- `intake_responses_json` column mapped with:
  ```csharp
  e.Property(x => x.IntakeResponsesJson)
      .HasColumnName("intake_responses_json")
      .HasColumnType("mediumtext");
  ```
- `mediumtext` type ✅
- Clean addition to existing Opportunity entity builder block ✅

---

### ✅ 6. `Program.cs` (MODIFIED)
- `builder.Services.AddScoped<TaskService>()` present ✅
- `ALTER TABLE opportunities ADD COLUMN IF NOT EXISTS intake_responses_json MEDIUMTEXT NULL` present ✅
- `using FamOs.Web` still present (WI893 fix preserved) ✅
- Idempotent schema migration in the Sprint 4 comment block ✅

---

### ✅ 7. `LifecycleCommandService.cs` (MODIFIED)
- `SaveIntakeResponsesAsync` method present ✅
- **Uses `CreateExecutionStrategy` wrapper correctly:**
  ```csharp
  await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
  {
      await using var tx = await _db.Database.BeginTransactionAsync();
      ...
  });
  ```
  ✅ (all other methods use bare `BeginTransactionAsync` without retry strategy — consistent with existing pattern; only intake as a new addition uses the wrapper, which is slightly inconsistent but not a blocker for this sprint)

- `CreateTasksForStageAsync` private helper present ✅
- `CreateTasksForStageAsync` does NOT call `SaveChangesAsync` internally — verified: comment explicitly states "Tasks are saved in the calling method's SaveChangesAsync() — do not call SaveChanges here." ✅
- Stage methods calling `CreateTasksForStageAsync`:
  - `PursueOpportunityAsync` → UnderwritingPrep ✅
  - `RouteToMarketAsync` → Marketed ✅
  - `RecordQuoteAsync` (conditional, first quote) → QuotesReceived ✅
  - `SendProposalAsync` → ClientDecision ✅
  - `RequestBindAsync` → Binding ✅
  - `RecordBinderReceivedAsync` → Bound ✅
  - All 6 stages covered (exceeds minimum requirement of 4) ✅

---

### ⚠️ 8. `IntakePanel.razor` (MODIFIED) — **Important**
- `@namespace FamOs.Web.Components.Panels` at top ✅
- 4 sections: Account Information, Fleet Information, Coverage Requirements, Loss History ✅
- SaveDraft + PursueOpportunity buttons ✅
- Validation: `contactName`, `fleetSize`, `dotNumber`, `stateOfDomicile`, `lossRunsAvailable` all required ✅
- `@using System.Text.Json` present ✅
- Draft reload from existing `IntakeResponsesJson` on `OnInitialized` ✅
- `BuildResponseDict()` serializes all 17 field keys ✅

**⚠️ Important Issue:**
Line 6: `@using FamOs.Web.Domain` is present. This namespace is already declared in `_Imports.razor`. Per project convention (and the brief's explicit requirement), new Razor files should NOT re-declare namespaces already in `_Imports`. This creates redundancy and is inconsistent with all other Panels.

**Fix:** Remove `@using FamOs.Web.Domain` from `IntakePanel.razor` (line 6).

Note: `@using FamOs.Web.Data.Entities` (line 5) is acceptable — that's not in `_Imports.razor`.

---

### ⚠️ 9. `TaskCenter.razor` (MODIFIED) — **Important**
- `@page "/tasks"` and `@attribute [Authorize]` ✅
- `TaskService` injected ✅
- Grouped by opportunity, filter by text, `CompleteTask`, `OpenAddTaskDialog` ✅
- `@using FamOs.Web.Services` present ✅
- After-dialog-close reload pattern (re-queries with userId) ✅
- Overdue task highlighting (red color + "Overdue ·" prefix) ✅
- `GetStageLabel` switch for display names ✅
- Click-through to opportunity workspace via `Nav.NavigateTo` ✅

**⚠️ Important Issue:**
Line 9: `@using FamOs.Web.Domain` is present. Same as above — already in `_Imports.razor`, redundant, violates project convention.

**Fix:** Remove `@using FamOs.Web.Domain` from `TaskCenter.razor` (line 9).

Note: `@using FamOs.Web.Data.Entities` (line 8) is acceptable — not in `_Imports`.

---

### ✅ 10. `NavMenu.razor` (MODIFIED)
- Task count badge present with conditional `@if (_openTaskCount > 0)` ✅
- Badge hidden when count is 0 ✅
- Non-fatal `try/catch` around task count fetch in `OnInitializedAsync` ✅
- No new namespace collision issues ✅
- `@inject TaskService TaskSvc` and `@inject UserSessionService UserSession` ✅
- Clean integration — existing nav items untouched ✅

---

### ✅ 11. `wwwroot/css/famos.css` (MODIFIED)
- `.task-row:hover { background: var(--cream); }` present ✅
- `.task-row:last-child { border-bottom: none; }` present ✅
- Added at the end of the file, no conflicts with existing rules ✅

---

## Regression Checks

| Check | Status | Notes |
|-------|--------|-------|
| `@namespace` on `OpportunityWorkspace.razor` (WI870) | ✅ INTACT | `@namespace FamOs.Web.Components.Pages` line 1 |
| `@namespace` on all 7 Panels (WI870) | ✅ ALL INTACT | Verified all 7: Binding, Bound, ClientDecision, Intake, Marketed, QuotesReceived, UnderwritingPrep |
| `_Imports.razor`: Dialogs/Panels/Shared/Services/Domain usings | ✅ INTACT | All present: `.Dialogs`, `.Panels`, `.Shared`, `.Services`, `.Domain`, `FipShared.Components`, `FipShared.Models` |
| `FipTheme.cs`: `Shadows.Elevation` absent (WI890 hotfix) | ✅ INTACT | Comment confirms intentional omission: "Shadows.Elevation not overridden — MudBlazor v7 requires exactly 25 entries" |
| `GoToPipeline()` in `Dashboard.razor` (WI872) | ✅ INTACT | Line 17 button + line 51 method both present |
| `DrawerVariant.Persistent` in `MainLayout.razor` (WI893) | ✅ INTACT | Line 13 confirmed |

---

## Key Risk Verification

| Risk | Result |
|------|--------|
| `CreateTasksForStageAsync` does NOT call `SaveChangesAsync` | ✅ CONFIRMED — tasks added via `_db.Tasks.Add()`, save deferred to caller |
| `SaveIntakeResponsesAsync` uses `CreateExecutionStrategy` wrapper | ✅ CONFIRMED — only method in the class that uses it |
| `AddTaskDialog` uses `MudDialogInstance` (concrete, not `IMudDialogInstance`) | ✅ CONFIRMED |
| No `@using FamOs.Web.Domain` in new Razor files | ❌ VIOLATION — present in `IntakePanel.razor` and `TaskCenter.razor` |

---

## Issues Summary

| # | Severity | File | Issue | Fix |
|---|----------|------|-------|-----|
| 1 | 🔴 Critical | `Services/TaskService.cs` | Missing `using FamOs.Web.Domain;` — `NotFoundException` will not compile | Add `using FamOs.Web.Domain;` to using block |
| 2 | ⚠️ Important | `Panels/IntakePanel.razor` | Redundant `@using FamOs.Web.Domain` (already in `_Imports`) | Remove line 6 |
| 3 | ⚠️ Important | `Pages/TaskCenter.razor` | Redundant `@using FamOs.Web.Domain` (already in `_Imports`) | Remove line 9 |

---

## What Passes

Everything else is solid:
- Task auto-generation architecture is clean and well-separated (templates → service helper → caller saves)
- IntakePanel draft/restore pattern using JSON is correct
- Validation covers all 5 required fields
- AddTaskDialog is clean: correct cascading parameter type, proper disabled guard
- DB schema migration is idempotent (`ADD COLUMN IF NOT EXISTS`)
- NavMenu badge is resilient (non-fatal try/catch)
- All 6 lifecycle stages generate tasks (full coverage, not just minimum 4)
- CSS additions are minimal and well-placed

---

## Verdict: ⚠️ NEEDS-CHANGES

**3 issues — 1 critical (compile error), 2 cosmetic cleanup.**

Tony: Fix the `using FamOs.Web.Domain` missing from `TaskService.cs` (compile blocker), then remove the two redundant `@using FamOs.Web.Domain` lines from `IntakePanel.razor` and `TaskCenter.razor`. Scope is surgical — no other changes.
