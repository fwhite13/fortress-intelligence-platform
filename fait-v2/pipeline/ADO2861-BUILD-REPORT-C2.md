# Build Report C2: ADO#2861 — Projects carry-over fixes

**Agent:** Tony Stark
**Cycle:** 2
**Date:** 2026-05-07
**Commit:** `9b63c6d`

---

## Fixes Applied

### Fix 1: ProjectStateService consumer wiring — `ChatView.razor`

- Injected `ProjectStateService ProjectState` into `ChatView.razor`
- Added `BuildContextualMessage(string userInput)` helper that prepends `[ACTIVE PROJECT CONTEXT]` block when `ActiveProjectContext` is non-empty
- `SendMessage()` now passes `BuildContextualMessage(userMessage)` as the `TurnRequest.Message` — context flows into every Bedrock call when a project is active

### Fix 2: OpenNewProjectDialog — `MainLayout.razor`

- Replaced empty stub with a `MudDialog` containing:
  - `Project name` (required) + `Description` (optional) text fields
  - Cancel / Create buttons (Create disabled when name is empty)
- Added `_showNewProjectDialog`, `_newProjectName`, `_newProjectDescription` fields
- `OpenNewProjectDialog()` resets fields and opens dialog
- `CreateProject()` calls `ProjectService.CreateProjectAsync`, appends result to `_projects`, closes dialog, selects new project, triggers `StateHasChanged()`

### Bonus: Workspace.razor build fix

Pre-existing `Workspace.razor` had Razor-compiler-incompatible relational patterns (`< 1024`) in a `switch` expression (Razor misinterpreted them as HTML tags). Converted `FormatSize` to an `if/else` body to resolve compiler errors.

---

## Build Gate

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Commit

- **Hash:** `9b63c6d`
- **Branch:** `main`
- **Pushed:** Yes (`origin/main`)
