# REVIEW Brief: ADO#2861 — FAIT v2 Projects carry-over from FAIT v1

**ADO WI:** #2861 (Fortress project)
**Review Cycle:** 1
**Build Commit:** `2681804`

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2861-REVIEW-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/fait-v2/`

---

## What Changed

**New files:**
- `Services/IProjectService.cs` — interface + `ProjectSummary` model
- `Services/ProjectService.cs` — EF Core implementation using `FaitV2DbContext`
- `Services/ProjectStateService.cs` — scoped state service carrying active project context across components

**Modified:**
- `Components/Layout/MainLayout.razor` — project sidebar with list, selection, and new project dialog
- `wwwroot/css/app.css` — sidebar project styles (CSS variables only)
- `Program.cs` — `IProjectService`, `ProjectService`, `ProjectStateService` registrations

---

## Review Checklist

### Data Access
1. `ProjectService.GetUserProjectsAsync` filters by `entraOid` — users only see their own projects (no cross-user leakage)
2. No raw SQL — EF Core LINQ throughout
3. `GuidFormat=MySqlGuidFormat.None` — check that `FaitV2DbContext` connection string includes `GuidFormat=None` (should be inherited from existing config, not added here)
4. `CreateProjectAsync` generates a new `Guid.NewGuid().ToString()` for `Id` — varchar(36) compatible

### Service Layer
5. `ProjectStateService` is registered as `Scoped` — correct for per-user Blazor circuit state
6. `IProjectService` registered as `Scoped` — correct
7. `GetProjectContextAsync` returns a reasonable string for assistant injection (project name + description)

### UI / Razor
8. Project list in sidebar renders correctly with `@foreach` — no mutation inside `@foreach` (no `@{ var x = ... }` declarations inside markup blocks)
9. `SelectProjectAsync` sets `ProjectStateService.ActiveProjectContext` — confirm the state flows to ChatView or the dashboard prompt builder
10. CSS uses only CSS variables — no hardcoded colors, font sizes, or spacing values
11. New project button/dialog present

### Build
12. `dotnet build` 0 errors, 0 warnings (confirmed in commit)
13. No Cognito references introduced
14. No S3 references (this WI is Aurora-only)

---

## ADO Tracking (MANDATORY)

After review complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2861,
  "text": "**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. Cycles: 1. {summary}"
}'
```

---

## Deliverables

1. Review Report: `/home/fredw/projects/fip/fait-v2/pipeline/ADO2861-REVIEW-REPORT-C1.md`
2. Verdict: PASS / NEEDS-CHANGES / FAIL
3. If NEEDS-CHANGES: file + line + exact fix
