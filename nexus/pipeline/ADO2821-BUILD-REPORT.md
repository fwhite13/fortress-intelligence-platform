# Build Report — ADO#2821
**NEXUS Decomp Tree Editor — inline hierarchy editor**
**Build Cycle:** 1 | **Agent:** Tony Stark | **Date:** 2026-05-06

---

## CC Invocation
```bash
cat pipeline/tony-cc-brief-ado2821.md | claude --model sonnet --print --dangerously-skip-permissions
```
Working directory: `/home/fredw/projects/fip/nexus/`

---

## Commit
`5a3edc59750889f191331ede2a1380f05d11197a`  
Message: `feat(nexus#2821): add Decomp Tree Editor with inline Edit Mode`

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs` | Added `AcceptanceCriteria` property (`string?`, newline-delimited) |
| `src/FortressNexus.Web/Data/NexusDbContext.cs` | Mapped `acceptance_criteria` column (`text`, nullable) to `WorkItemRecord` entity |
| `src/FortressNexus.Web/Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.cs` | New migration: `AddColumn acceptance_criteria` |
| `src/FortressNexus.Web/Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.Designer.cs` | Migration snapshot with new column |
| `src/FortressNexus.Web/Services/UserContextService.cs` | Added `IsNexusEditorAsync()` = Admin OR Reviewer |
| `src/FortressNexus.Web/Controllers/NexusArtifactsController.cs` | Added 6 editor endpoints + DTOs + helper methods |
| `src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` | Added Edit Mode toggle, inline editors, Add/Move/Delete controls |

---

## What Was Built

### 1. AcceptanceCriteria Field
- Added `string? AcceptanceCriteria` to `WorkItemRecord` entity
- Mapped as `acceptance_criteria text nullable` in DbContext
- Migration `20260506000001` creates the column

### 2. IsNexusEditorAsync()
- Added to `UserContextService`
- Returns `true` if user is `NexusAdmin` OR `NexusReviewer`
- Used in Razor component for Edit Mode gating

### 3. API Endpoints (NexusArtifactsController)
All 6 endpoints implemented with:
- `User.IsInRole()` auth guard (returns 403 if not Admin/Reviewer)
- Submission ownership validation
- ArtifactSet → SpecDocument → Submission chain verified

| Endpoint | Behavior |
|----------|----------|
| `PATCH wi/{wiId}/title` | Updates title + cascades all `ParentTitle` refs in same ArtifactSet (single transaction) |
| `PATCH wi/{wiId}/description` | Updates description |
| `PATCH wi/{wiId}/ac` | Replaces full AC block (newline-delimited) |
| `PATCH wi/{wiId}/parent` | Reparents WI, validates target exists in ArtifactSet |
| `POST wi` | Creates new WI with correct type/parent/defaults |
| `DELETE wi/{wiId}` | Recursive cascade delete via `CollectDescendants()` (walks `ParentTitle`, no FK) |

### 4. NexusArtifacts.razor — Edit Mode
Extended (NOT rewritten) from 367 lines. Key additions:
- **Header:** Edit / Done Editing / Post to ADO buttons; EDIT MODE chip; Post to ADO disabled in Edit Mode
- **Epic rows:** Inline title field + Delete button (with stopPropagation so expansion doesn't trigger)
- **Feature rows:** Inline title field + Move ▾ dropdown + Delete button + Description panel
- **Story rows:** Inline title field + Move ▾ dropdown + Delete button + Description + AC panels
- **Task rows:** Inline title field + Move ▾ dropdown + Delete button
- **TC rows:** Inline title field + Move ▾ dropdown + Delete button
- **Add buttons:** At end of each group (+ Add Feature / + Add Story / + Add Task / + Add Test Case)
- **AC section (stories only):** Split/rejoin on `\n`, per-item `MudTextField` + remove button + Add AC button
- **Save pattern:** `IDbContextFactory<NexusDbContext>` (same pattern as existing init code) — no HttpClient/cookie complications
- **Revert on failure:** All saves snapshot originals, revert + show snackbar on error
- **Cascade title update:** SaveTitle walks `_workItems` to update local `ParentTitle` refs after DB save
- **Delete confirmation:** `DialogService.ShowMessageBox` with child count, cascade remove from `_workItems`
- **Move:** Updates `ParentTitle` in DB + locally, triggers `StateHasChanged()`

---

## Parallelization Used
No — all tasks were sequential (entity → DbContext → migration → service → controller → Razor).

---

## Acceptance Criteria Check

| # | Criterion | Status |
|---|-----------|--------|
| 1 | Edit Mode toggle visible to NexusAdmin/NexusReviewer | ✅ PASS — `_isEditor` set from `IsNexusEditorAsync()`, Edit button conditional |
| 2 | Title editable, persists on blur | ✅ PASS — `MudTextField @bind-Value @onblur="SaveTitle"` with transaction + cascade |
| 3 | Description/AC collapsible, persists on blur | ✅ PASS — `MudExpansionPanel` with `@onblur="SaveDescription"` / `SaveAc` |
| 4 | Add WI at any level, appears immediately | ✅ PASS — `AddWi()` inserts to DB, adds to `_workItems`, calls `StateHasChanged()` |
| 5 | Delete with cascade confirmation, all descendants removed | ✅ PASS — `ConfirmDelete` + `GetAllDescendants` (walks by ParentTitle) + dialog |
| 6 | Move dropdown, type-compatible targets only | ✅ PASS — `GetValidParents()` switch by type |
| 7 | All badges preserved in Edit Mode | ✅ PASS — `RenderTemplateBadge` / `RenderPredecessorBadges` unchanged, called in both modes |
| 8 | Post to ADO disabled while Edit Mode active | ✅ PASS — `Disabled="@_editMode"` + tooltip |
| 9 | Non-editor cannot see Edit button, API returns 403 | ✅ PASS — `@if (_isEditor && !_editMode)` gate; controller `Forbid()` if not Admin/Reviewer |
| 10 | Save failure shows snackbar, field reverts | ✅ PASS — all save methods catch, revert from `_original*` dicts, `Snackbar.Add(...)` |
| 11 | Tree re-renders after add/delete/move without full page reload | ✅ PASS — `_workItems` + `_epics` updated in-place, `StateHasChanged()` called |

---

## Build Self-Review Checklist

- [x] ParentTitle cascade on title rename — transaction in `SaveTitle()` + controller `PatchTitle` 
- [x] AcceptanceCriteria column verified/migrated — was absent; migration `20260506000001` adds it
- [x] `IsNexusEditorAsync()` added to UserContextService
- [x] All 6 PATCH/POST/DELETE endpoints added to controller
- [x] Auth guard (`User.IsInRole` → `Forbid()`) on all write endpoints
- [x] Submission ownership validation on all endpoints
- [x] Save-failure snackbar + field revert wired in all save methods
- [x] No `ParentId` FK added — `ParentTitle` string pattern throughout
- [x] `GetAllDescendants` walks by `ParentTitle`, not FK
- [x] Post to ADO button stub present (disabled in Edit Mode)

---

## Build Result
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Things Clint Should Scrutinize

1. **Epic TitleContent stopPropagation** — Epic title field uses `@onclick:stopPropagation="true"` to prevent expansion panel toggle. Worth verifying blur fires correctly when the field loses focus via keyboard (Tab vs click).

2. **AC list loop variable capture** — The `@for (int acIdx = 0; ...)` loop uses `var capturedIdx = acIdx` pattern. Clint should confirm no closure-capture bug on `RemoveAt(capturedIdx)`.

3. **TC section visibility in Edit Mode** — Changed `@if (testCases.Any())` to `@if (testCases.Any() || _editMode)` so the "+ Add Test Case" button appears even when no TCs exist. Intentional.

4. **DbContextFactory dispose pattern** — Each save op opens/disposes its own context via `await using var db`. No shared state with the page's loaded `_workItems`. This is correct for Blazor Server but means optimistic concurrency isn't checked (last-write-wins per spec §5.4).

5. **Controller VerifySubmissionAccessAsync** — Reviewers (`NexusReviewer`) can edit any submission they have Role access to (not restricted to "their own"), since the ownership check only applies when the user is NOT an Admin. This may be intentional — Reviewers are typically cross-submission in NEXUS.

---

## How to Test Locally

```bash
cd /home/fredw/projects/fip/nexus
dotnet run --project src/FortressNexus.Web

# Apply migration
dotnet ef database update --project src/FortressNexus.Web

# Navigate to /nexus/{submissionId}/artifacts
# Log in as NexusAdmin or NexusReviewer
# Click "Edit" → verify Edit Mode activates
# Edit a title → blur → verify DB update
# Add a Feature/Story/Task/TC → verify appears immediately
# Delete a WI with children → confirm dialog shows child count
# Move a Feature to another Epic → verify tree re-renders
```
