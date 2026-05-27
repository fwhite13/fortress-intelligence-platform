# ADO#4248 — CC Agent Avatar — Adversarial Review Brief

## Task
Adversarial review of commit fa1a953a in `/home/fredw/projects/fip`.

Files changed:
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`

## What Was Built
CC agent avatar (Icons.Material.Filled.SmartToy) for task execution. Two changes:
1. Header badge (`.chat-task-indicator`): `_ccTaskActive` → SmartToy+pulse, else fa-tasks
2. Tool-call chip loop: `tc.Server == "task"` → MudIcon SmartToy, else old emoji span
3. CSS: `.cc-agent-icon`, `.cc-agent-icon--pulse` (references existing `pulse` keyframe in fortress.css), `.chat-task-indicator__cc-icon`

## Files to Read
Read these in full:
1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor`

Also check:
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/wwwroot/css/fortress.css` — look for `@keyframes pulse` definition

## Specific Questions — Answer Each Explicitly

### Q1: `_ccTaskActive` lifecycle — is it correctly set/unset?
- Set to `true`: when `task_progress` event with `Step == "start"` is received (line ~1139)
- Set to `false`:
  - On `task_progress` with `isFinal` (Step == "done" or Status == "done") — line ~1155
  - On `HandleSend` (new turn start) — line ~815
  - On `CancelTask` — line ~457
  - On conversation switch (`OnParametersSetAsync`) — line ~656
  - On turn end (the finally/cleanup block) — line ~1297
  - In text-event fade logic — line ~1033

**Verify:** Is there any code path where `_ccTaskActive` stays `true` indefinitely without a corresponding clear? Specifically:
- What if the SSE stream ends without a `task_progress` "done" event? (e.g., timeout, network drop, exception mid-stream)
- Does the turn-end cleanup correctly clear `_ccTaskActive` even on exception/cancellation?

### Q2: Orphaned pulse animation — can it get stuck?
If `_ccTaskActive = true` is set in `task_progress` start, and then the stream ends abnormally (no "done" step), does the header badge keep pulsing indefinitely?

Look at the finally block / stream end cleanup. Does it clear `_ccTaskActive`?

### Q3: Header badge conditional — `_ccTaskActive` vs. `_taskModeActive`
The header badge is gated on `_taskModeActive` (outer if), then `_ccTaskActive` (inner if).
- Are these truly distinct flags? What drives each?
- `_taskModeActive` = task mode is active (the UI mode, set by mode_switch event or user toggle)
- `_ccTaskActive` = CC is actively running a task (set by task_progress start/done events)
- **Verify:** Could `_ccTaskActive` ever be true while `_taskModeActive` is false? If so, the SmartToy badge would never show because the outer if would be false.

### Q4: CSS keyframe — does `.cc-agent-icon--pulse` reference an existing keyframe?
Check `fortress.css` for `@keyframes pulse`. If it exists, the CSS is valid. If it doesn't, the pulse animation silently fails (no error, just no animation).

Also: `.cc-agent-icon--pulse` class is used on both:
1. The header badge (via `chat-task-indicator__cc-icon cc-agent-icon--pulse`)
2. Tool-call chips (via `cc-agent-icon cc-agent-icon--pulse`)

Does the `.cc-agent-icon--pulse` CSS rule apply `animation: pulse`? Does `.cc-agent-icon` have the base styles (font-size, width, height, color)?

**Also check:** The `.chat-task-indicator__cc-icon` class — does it include the `cc-agent-icon--pulse` animation styles, or does it rely on inheriting from `.cc-agent-icon--pulse`? The header badge uses `Class="chat-task-indicator__cc-icon cc-agent-icon--pulse"` — so it applies both classes. That should be fine. Verify both classes exist.

### Q5: Non-CC task chip regression
Before this change, ALL tool-call chips used `<span class="tool-call-emoji ...">@GetToolEmoji(...)`. After the change, only `tc.Server != "task"` entries use that. 

**Verify:** Is there any server value other than "task" that previously showed a specific emoji that might now be broken? The condition is `tc.Server == "task"` — any non-task server still gets the emoji span. This looks fine, but confirm `GetToolEmoji` is unchanged and non-task entries are unaffected.

### Q6: `.cc-agent-icon` sizing — MudIcon vs icon font
MudBlazor's `MudIcon` renders as an `<svg>` element. The CSS sets `width: 1rem; height: 1rem`. Does MudBlazor's default sizing conflict with these explicit dimensions? MudIcon uses `font-size` for sizing SVG icons, not width/height directly. 

**Verify:** The CSS sets `font-size: 0.875rem` on `.cc-agent-icon`. For MudIcon SVG, does font-size control size? Or should it use the `Size` prop on the MudIcon component?

Note: The header badge uses `chat-task-indicator__cc-icon` class which sets `width: 1rem; height: 1rem` but NOT `font-size`. This might render at default MudBlazor size (24px) unless the existing container styling constrains it.

### Q7: Build cleanliness
Confirm no new errors introduced. The pre-existing error in Memory.razor (`OpenImportDialog`) is unrelated. Are there any new compilation warnings or errors in ChatView.razor?

## Pass/Fail Criteria

**PASS** if:
- `_ccTaskActive` is always cleared on error/timeout/completion (no orphan scenario)
- Header badge conditional logic is sound (distinct flags, correct nesting)
- `@keyframes pulse` exists in fortress.css
- Non-CC chips unaffected
- CSS renders at correct size (or note if sizing may be imprecise but non-breaking)

**NEEDS-CHANGES** if:
- `.cc-agent-icon` sizing may not control MudIcon correctly (cosmetic, non-breaking)
- Minor CSS specificity issues

**FAIL** if:
- `_ccTaskActive` can get stuck `true` (pulsing badge stuck on screen)
- Header badge never shows SmartToy due to flag logic error
- `@keyframes pulse` doesn't exist (animation silently broken)
- Non-CC chips broken

## Output Format
For each question Q1-Q7: state the finding clearly. End with overall verdict: PASS / NEEDS-CHANGES / FAIL and specific issues if any.
