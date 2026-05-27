# Build Brief: ADO#4248 — CC Agent Avatar During Task Execution

## Objective
Replace the generic spinning gear/emoji with a distinct CC agent avatar (SmartToy robot icon with pulse animation) in the tool-call indicator list when a CC `task_progress` event is active.

## File to modify
`/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor`

---

## Change 1: Tool-call indicator markup (around line 170-178)

### Current markup:
```razor
                    @foreach (var tc in _activeToolCalls)
                    {
                        <div class="tool-call-indicator @(tc.Status == "calling" ? "tool-call-active" : tc.Status == "error" ? "tool-call-error" : "tool-call-done")">
                            <span class="tool-call-emoji @(tc.Status == "calling" ? "tool-call-emoji-spin" : "")">@GetToolEmoji(tc.ToolName, tc.Server)</span>
                            <span class="tool-call-summary">@GetToolLabel(tc.ToolName, tc.Server, tc.Summary)</span>
                        </div>
                    }
```

### New markup — replace with:
```razor
                    @foreach (var tc in _activeToolCalls)
                    {
                        <div class="tool-call-indicator @(tc.Status == "calling" ? "tool-call-active" : tc.Status == "error" ? "tool-call-error" : "tool-call-done")">
                            @if (tc.Server == "task")
                            {
                                <MudIcon Icon="@Icons.Material.Filled.SmartToy"
                                         Class="cc-agent-icon @(tc.Status == "calling" ? "cc-agent-icon--pulse" : "")" />
                            }
                            else
                            {
                                <span class="tool-call-emoji @(tc.Status == "calling" ? "tool-call-emoji-spin" : "")">@GetToolEmoji(tc.ToolName, tc.Server)</span>
                            }
                            <span class="tool-call-summary">@GetToolLabel(tc.ToolName, tc.Server, tc.Summary)</span>
                        </div>
                    }
```

---

## Change 2: Update the `chat-task-indicator` header (around line 71-81)

### Current markup:
```razor
            @if (_taskModeActive)
            {
                <div class="chat-task-indicator">
                    <i class="fas fa-tasks"></i>
                    <span>⚡ Task Mode · <span id="task-timer-display">00:00</span></span>
                    <button class="chat-task-indicator__cancel" @onclick="CancelTask" title="Cancel task">
                        <i class="fas fa-times"></i>
                    </button>
                </div>
            }
```

### New markup — replace with:
```razor
            @if (_taskModeActive)
            {
                <div class="chat-task-indicator">
                    @if (_ccTaskActive)
                    {
                        <MudIcon Icon="@Icons.Material.Filled.SmartToy"
                                 Class="chat-task-indicator__cc-icon cc-agent-icon--pulse" />
                    }
                    else
                    {
                        <i class="fas fa-tasks"></i>
                    }
                    <span>⚡ Task Mode · <span id="task-timer-display">00:00</span></span>
                    <button class="chat-task-indicator__cancel" @onclick="CancelTask" title="Cancel task">
                        <i class="fas fa-times"></i>
                    </button>
                </div>
            }
```

---

## Change 3: Add CSS classes for CC agent icon (add after the existing `.tool-call-emoji-spin` block)

Find the block:
```css
.tool-call-emoji-spin {
    display: inline-block;
    animation: spin 1.5s linear infinite;
}
```

Add immediately after it:
```css
.cc-agent-icon {
    font-size: 0.875rem;
    width: 1rem;
    height: 1rem;
    color: var(--color-accent);
    flex-shrink: 0;
}
.cc-agent-icon--pulse {
    animation: pulse 1.5s ease-in-out infinite;
}
.chat-task-indicator__cc-icon {
    width: 1rem;
    height: 1rem;
    color: var(--color-accent);
}
```

The `pulse` keyframe already exists in `/home/fredw/projects/fip/fait/src/FortressAI.Web/wwwroot/css/fortress.css`:
```css
@keyframes pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
}
```
Do NOT add a duplicate `@keyframes pulse` in ChatView.razor — just reference the class. The class `.cc-agent-icon--pulse` uses `animation: pulse 1.5s ease-in-out infinite;` which references the existing keyframe.

---

## Constraints
- Do NOT touch any C# logic, state variables, or backend code
- Do NOT touch `_ccTaskActive`, `_taskModeActive`, or any other state fields
- Do NOT modify `GetToolEmoji` or `GetToolLabel` — the emoji function is no longer called for `server == "task"` items (they get MudIcon instead), so no changes needed there
- Do NOT add `@keyframes pulse` to ChatView.razor — the keyframe is already in fortress.css and is globally available
- Do NOT add inline styles — use CSS classes only
- Do NOT change any other components

## Acceptance Criteria
1. When a `task_progress` SSE event arrives (step="start"), the tool-call chip shows `Icons.Material.Filled.SmartToy` with pulse animation instead of the spinning gear emoji
2. The icon is visible from CC spawn (step="start") through completion (step="done") — done state shows the icon without pulse
3. The `chat-task-indicator` header badge shows SmartToy icon (pulsing) when `_ccTaskActive == true`, and falls back to `fa-tasks` when task mode is active but CC is not running
4. All CSS uses class names (no inline styles)
5. FAIT design language: accent color (`var(--color-accent)`) for the icon, consistent sizing with other chip elements

## Output
When complete, confirm what lines were changed in ChatView.razor.
