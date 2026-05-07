# Code Review Report — ADO#2861
**Reviewer:** Hawkeye (REVIEW cycle 1)
**Commit:** `2681804`
**Verdict:** NEEDS-CHANGES

---

## Checklist Results

### Data Access

| # | Check | Result |
|---|-------|--------|
| 1 | `GetUserProjectsAsync` filters by `entraOid` — no cross-user leakage | PASS |
| 2 | No raw SQL — EF Core LINQ throughout | PASS |
| 3 | `GuidFormat=MySqlGuidFormat.None` on `FaitV2DbContext` connection string (`Program.cs:102`) | PASS |
| 4 | `CreateProjectAsync` generates `Guid.NewGuid().ToString()` — varchar(36) compatible | PASS |

### Service Layer

| # | Check | Result |
|---|-------|--------|
| 5 | `ProjectStateService` registered as `Scoped` (`Program.cs:150`) | PASS |
| 6 | `IProjectService` registered as `Scoped` (`Program.cs:149`) | PASS |
| 7 | `GetProjectContextAsync` returns `"# Active Project: {name}\n{description}"` — reasonable context string | PASS |

### UI / Razor

| # | Check | Result |
|---|-------|--------|
| 8 | `@foreach` with `var isActive` local — valid Razor syntax, no collection mutation, closure captures `project` correctly | PASS |
| 9 | `ProjectState.SetActiveProject` is called on selection, but `ActiveProjectContext` has **no consumer** | FAIL |
| 10 | New project CSS uses `var(--space-N)` / `var(--font-size-N)` for spacing/type; rgba transparency values are consistent with pre-existing drawer palette | PASS |
| 11 | New project button is present; `OpenNewProjectDialog()` is an **empty stub** — dialog not implemented | FAIL |

### Build

| # | Check | Result |
|---|-------|--------|
| 12 | `dotnet build` 0 errors (confirmed in commit) | PASS |
| 13 | No Cognito references introduced | PASS |
| 14 | No S3 references introduced | PASS |

---

## Findings

### FINDING 1 — FAIL: `ProjectStateService.ActiveProjectContext` is written but never consumed
**File:** `Services/ProjectStateService.cs`, `Components/Layout/MainLayout.razor:149`

`SelectProjectAsync` calls `ProjectState.SetActiveProject(project.Id, context)`, which stores the context in the scoped `ProjectStateService`. However, no component in the codebase reads `ActiveProjectContext` or `ActiveProjectId`. There is no `ChatView.razor`, and `Dashboard.razor` contains zero project references.

The review checklist explicitly requires confirming the state "flows to ChatView or the dashboard prompt builder." It does not.

**Required fix:** Either:
- (a) Add `@inject ProjectStateService ProjectState` to `Dashboard.razor` and display the active project name in the dashboard prompt builder / welcome area, OR
- (b) Add a `ChatView.razor` stub that injects `ProjectStateService` and passes `ActiveProjectContext` into the system prompt, OR
- (c) If consumption is intentionally deferred to a follow-on WI, update the `ProjectStateService` summary comment and add a `// TODO(ADO#XXXX)` marker at the call site so reviewers know the gap is tracked.

At minimum, option (c) is required for this cycle to close cleanly. Options (a) or (b) are preferred.

---

### FINDING 2 — FAIL: `OpenNewProjectDialog()` is an empty no-op stub
**File:** `Components/Layout/MainLayout.razor:153-156`

```csharp
private void OpenNewProjectDialog()
{
    // Future WI: open new project dialog
}
```

The WI description explicitly includes "new project dialog" as a deliverable. The button is wired, but clicking it does nothing. No `MudDialog`, no inline form, no navigation — nothing.

**Required fix:** Implement a minimal `MudDialog` containing:
- `MudTextField` for project name (required, max 200 chars)
- `MudTextField` for description (optional, multiline)
- Submit calls `ProjectService.CreateProjectAsync(_entraOid, name, description)`
- On success: append the new `ProjectSummary` to `_projects`, select it via `SelectProjectAsync`, close the dialog

---

## Minor Observations (non-blocking)

- **Extra DB round-trip in `SelectProjectAsync`:** `GetProjectContextAsync` calls `FindAsync` on the project that was already loaded into `_projects` as a `ProjectSummary`. The name and description are already in memory; a local format of the context string from the summary would avoid a second DB hit. Not a blocker — `FindAsync` hits the EF identity cache if the entity is tracked, but with `AddDbContextFactory` and a scoped context it may not be tracked. Worth a note for a future pass.

- **`Project.Id` default initializer:** `Project.cs:12` sets `Id = Guid.NewGuid().ToString()` as a property default AND `CreateProjectAsync` sets it again. No bug — the service override wins — but the double-initialization is slightly misleading. Non-blocking.

---

## Summary

Two checklist items fail:

| Finding | File | Severity |
|---------|------|----------|
| `ProjectStateService` context has no consumer | `MainLayout.razor:149` / `ProjectStateService.cs` | BLOCKER |
| `OpenNewProjectDialog()` is a no-op stub | `MainLayout.razor:153-156` | BLOCKER |

All data-access, service-registration, build, and security checks pass.

**Verdict: NEEDS-CHANGES** — resolve both findings and re-submit for cycle 2.
