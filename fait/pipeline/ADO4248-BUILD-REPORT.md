# Build Report: ADO#4248

## Summary
Replaced the generic spinning gear/emoji with a distinct CC agent avatar (`Icons.Material.Filled.SmartToy`) in two places: (1) the tool-call chip indicator list when `server == "task"`, and (2) the `chat-task-indicator` header badge when `_ccTaskActive` is true. The icon pulses while CC is running and renders statically when done.

## CC Invocation
Single CC Sonnet run via:
```bash
cat /home/fredw/projects/fip/fait/pipeline/ADO4248-build-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

## Files Modified
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`
  - **~line 71-88** (task indicator header): Added `@if (_ccTaskActive)` branch inside `.chat-task-indicator` — shows pulsing `SmartToy` icon when CC is active, falls back to `fa-tasks` otherwise
  - **~line 182-192** (tool-call chip loop): Added `@if (tc.Server == "task")` branch — renders `<MudIcon SmartToy>` with `.cc-agent-icon--pulse` when calling, `.cc-agent-icon` (no pulse) when done. Non-task entries keep existing emoji span unchanged.
  - **~line 2080-2095** (CSS): Added `.cc-agent-icon`, `.cc-agent-icon--pulse`, and `.chat-task-indicator__cc-icon` classes. `pulse` animation references existing `@keyframes pulse` in `fortress.css` (no duplicate keyframe added).

## Self-Review Checklist
- [x] AC1: CC agent icon (`SmartToy`) shown during task execution — `_activeToolCalls` with `server == "task"` render the MudIcon
- [x] AC2: Distinct from wrench spinner — no gear/wrench emoji for task server items; `fa-tasks` only when CC not active
- [x] AC3: Visible spawn → completion — icon shown for `calling` (with pulse) and `done` (static) states
- [x] AC4: Consistent with FAIT design language — MudBlazor `MudIcon` component, `var(--color-accent)` color, class-driven styling, no inline styles
- [x] Build compiles clean — only pre-existing error in `Memory.razor` (unrelated `OpenImportDialog` reference), no new errors

## ADO Comment
Posted to ADO#4248 — commit `fa1a953a`.
