# Build Report: ADO#3244 — Task Progress Timeline

## Summary
Implemented Feature 2.4: Task Progress Timeline for CC task mode. Harness now emits `mode_switch` SSE at CC spawn, uses `--output-format stream-json` for structured CC output, and parses NDJSON to emit `task_progress` SSE events for each tool call. Blazor ChatView renders a live timeline with elapsed timer and cancel button. New `CCProgressHub` SignalR hub registered for future background task progress.

## CC Invocation
`cat /tmp/brief-3244.md | claude --model opus --print --dangerously-skip-permissions`

## Changes Made

### harness-server.js
- Emit `mode_switch` SSE event at CC spawn entry point
- Emit initial `task_progress` (step: 'start') before CC spawn
- Added `--output-format stream-json` to CC spawn args
- Replaced raw stdout handler with NDJSON line-by-line parser:
  - Buffers partial lines across data chunks
  - Parses `assistant` events → extracts text content blocks + tool_use blocks
  - Parses `tool_result` events → emits task_progress with status 'done'
  - Parses `result` event → emits final text only if no assistant content was streamed
  - Falls back to raw text on JSON parse failure

### CCProgressHub.cs (new)
- Created `src/FortressAI.Web/Hubs/CCProgressHub.cs`
- SignalR hub with `JoinUserGroup`/`LeaveUserGroup` methods using `cc-user-{userId}` groups
- Registered at `/hubs/cc-progress` in Program.cs

### IUserAgentRuntime.cs
- Added `TaskProgressPayload` record with Step, ToolName, Status, Message properties
- Updated `HarnessEvent.Type` comment to include `task_progress`

### ChatView.razor
- Added task progress state fields: `_taskProgressSteps`, `_taskStartTime`, `_taskElapsed`, `_elapsedTimer`, `_taskCancelled`
- Added `TaskProgressStep` record and `CancelTask()` method
- Added `task_progress` event handler in streaming loop (with elapsed timer on start)
- Task Progress Timeline UI: header with elapsed timer + cancel button, last 8 steps with tool/spinner icons
- Cleanup in HandleSend reset, finally block, and DisposeAsync

### Program.cs
- Added `app.MapHub<CCProgressHub>("/hubs/cc-progress")` after DashboardHub

### CSS
- Added in ChatView.razor `<style>` block (scoped)
- All styles use CSS variables only (zero hardcoded colors/sizes)
- BEM-style classes: `.task-progress-timeline`, `__header`, `__elapsed`, `__cancel`, `__steps`, `__step`, `__step--done`, `__step--error`, `__step-msg`, `__step-time`

## Acceptance Criteria Verification
- [x] mode_switch SSE emitted at CC spawn
- [x] CC stream-json parsing working (NDJSON parser with line buffering)
- [x] task_progress SSE events emitted for each CC tool call
- [x] CCProgressHub.cs created and registered
- [x] Task Progress Timeline renders in ChatView when taskModeActive
- [x] Elapsed timer works and disposes correctly
- [x] Cancel button wired to stream cancellation (streamingCts)
- [x] All CSS via variables only (zero hardcoded values)
- [x] No regressions on Bedrock path (taskMode=false path unchanged)

## Commit Hash
[pending]

## Self-Review Checklist
- [x] Blazor project builds successfully (`dotnet build`) — 0 errors, 46 warnings (pre-existing)
- [x] Harness syntax check (`node --check harness-server.js`) — passes
- [x] All CSS uses variables
- [x] No hardcoded colors/sizes
- [x] GuidFormat rule not applicable (no new MySQL connections)
- [x] IDbContextFactory pattern not applicable (no new DB contexts)
