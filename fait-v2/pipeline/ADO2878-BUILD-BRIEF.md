# ADO#2878 — FAIT v2 Scheduled Tasks UI (/tasks route) — BUILD Brief

## Spec
`memory/projects/fait-v2-spec-2026-04-27.md §5.2 (Scheduled Tasks screen)`
Feature: Epic F (Scheduled Tasks)
Sprint: FAIT v2 Sprint 5
**Depends on:** ADO#2877 (PASS — commit `3132b9f`) — IScheduledTaskService is available

## Context
Current HEAD: `3132b9f` on `main`. fait-v2 repo: `/home/fredw/projects/fip/fait-v2/`
`IScheduledTaskService` is already registered and available for injection.

## What to Build

### 1. `/tasks` Route — `Components/Pages/Tasks.razor`

Route: `@page "/tasks"`
Authorization: `@attribute [Authorize]`

Three-tab layout (MudTabs):
- **Recurring** — tasks where `ScheduleType == "recurring"`
- **On-Demand** — tasks where `ScheduleType == "on_demand"`
- **History** — recent task runs across all tasks (last 50 runs)

**Table columns for Recurring and On-Demand tabs:**
| Column | Source |
|--------|--------|
| Task Name | `ScheduledTask.Name` |
| Schedule | `ScheduledTask.CronExpression` (for recurring) or "On demand" |
| Next Run | `ScheduledTask.NextRunAt` (formatted as relative time, e.g. "in 2 hours") |
| Last Run Status | `ScheduledTask.LastRunStatus` (MudChip color: success=green, failed=red, null=grey) |
| Actions | Edit (MudIconButton), Pause/Resume toggle, Run Now, Delete |

**History tab columns:**
| Column | Source |
|--------|--------|
| Task Name | Via navigation to `ScheduledTask.Name` |
| Started | `ScheduledTaskRun.StartedAt` |
| Duration | `CompletedAt - StartedAt` (or "Running..." if null) |
| Status | `ScheduledTaskRun.Status` (colored chip) |

Responsive: at < 768px, collapse table to card layout (MudCard per row).

### 2. Create/Edit Task Dialog — `Components/Shared/TaskEditDialog.razor`

MudDialog with:
- **Name** (MudTextField, required, max 200)
- **Prompt** (MudTextField multiline, required — what CC should do when this runs)
- **Schedule Type** (MudToggleGroup or MudRadioGroup): "Recurring" | "On Demand"
- **Cron Expression** (MudTextField, shown only when Recurring selected) — with helper text showing common examples: "0 9 * * 1-5" (weekdays 9am), "0 * * * *" (hourly), "0 8 * * *" (daily 8am)
- **Alert on completion** (MudSwitch)
- **Alert on failure** (MudSwitch, default on)
- Save / Cancel buttons

Validation: Name required, Prompt required, CronExpression required when Recurring.

On save: call `IScheduledTaskService.CreateTaskAsync()` or `UpdateTaskAsync()` depending on mode.

### 3. Delete Confirmation

MudDialog confirmation before deleting. On confirm: call `DeleteTaskAsync()`.

### 4. Sidebar Link

In `Components/Layout/MainLayout.razor`, add "Scheduled Tasks" navigation link to the left sidebar pointing to `/tasks`, with an appropriate MudBlazor icon (e.g., `Icons.Material.Outlined.Schedule`). Place it below the existing nav items (Projects, Workspace, etc.).

### 5. Dashboard Summary Widget

On the dashboard/main page (if there's a `Components/Pages/Home.razor` or similar), add a small "Scheduled Tasks" summary section showing:
- Count of active recurring tasks
- Next scheduled run (soonest `NextRunAt`)
- Link to `/tasks`

If no dashboard page exists yet, skip this step — don't create one.

## CSS Rules (MANDATORY)
- ALL colors must use CSS variables from `fortress.css` (e.g., `--color-primary`, `--color-success`, `--color-danger`)
- No hardcoded hex values, rgb(), or named colors in .razor files
- Spacing and font sizes may use rem values (existing codebase pattern)
- Status chip colors: use MudBlazor `Color` enum values (Color.Success, Color.Error, Color.Default) — not custom CSS colors

## Acceptance Criteria
- [ ] `/tasks` route renders with Recurring / On-Demand / History tabs
- [ ] Tasks load from `IScheduledTaskService` filtered to current user
- [ ] Create task dialog: name, prompt, schedule type, cron expression, alert flags
- [ ] Edit task dialog pre-populated with existing values
- [ ] Pause/Resume toggle calls UpdateTaskAsync with IsActive toggled
- [ ] Run Now calls TriggerNowAsync
- [ ] Delete shows confirmation dialog
- [ ] History tab shows last 50 runs with status
- [ ] Sidebar navigation link added
- [ ] Responsive card layout at < 768px
- [ ] CSS variables only — no hardcoded colors
- [ ] dotnet build 0 errors

## Rules
- CSS variable rule MANDATORY
- No Cognito references
- UserId always from Entra OID claim — never from request/form body
- MudBlazor v7 API (`MudDialogInstance`, not `IMudDialogInstance`)

## MANDATORY: Use Claude Code CLI
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2878-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

## ADO Comment (add after build)
Project: Fortress, ID: 2878
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: Tasks.razor (/tasks route, 3-tab layout), TaskEditDialog.razor, sidebar nav link. Build: SUCCEEDED.
```
