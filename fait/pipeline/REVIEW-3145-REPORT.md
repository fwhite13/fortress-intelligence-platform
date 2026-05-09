# Review Report — ADO#3145

### Verdict: PASS

---

### Spec Compliance Check

**Files changed as reported:**
- `src/FortressAI.Web/Services/IUserAgentRuntime.cs` — ✅ `Payload` field added to `HarnessEvent`, type comment updated
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ all 9 claimed changes present

**Out of Scope:** ✅ No changes to `ChatInput.razor`, `FargateUserAgentRuntime.cs`, or any other file

**Acceptance Criteria:**
- [x] `HarnessEvent.Payload` as final nullable string with default — ✅ line 68, `string? Payload = null`
- [x] `_taskMode` / `_taskModeActive` fields — ✅ lines 309–310, both `private bool`, initialized false
- [x] `_taskModeActive = false` reset at START of `HandleSend` — ✅ line 480, before streaming begins
- [x] `TaskMode: _taskMode` in TurnRequest — ✅ line 781, inside try block before `finally` resets it
- [x] `mode_switch` SSE handler — ✅ line 807–810, sets `_taskModeActive = true; StateHasChanged()`
- [x] `_taskMode = false` in finally — ✅ line 841
- [x] Toggle button with conditional active class, `@onclick`, `disabled="@isStreaming"` — ✅ lines 234–241
- [x] `chat-task-indicator` conditional on `_taskModeActive` in chat-header — ✅ lines 50–54
- [x] CSS vars only — ✅ confirmed (see CSS audit below)

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**HarnessEvent callers cross-checked:**
- `FargateUserAgentRuntime.cs:492` — sole constructor call, uses named parameter `ErrorMessage:` only. `Payload` is last with default null — no positional break. ✅
- No other `HarnessEvent(` calls found in the codebase. ✅

**JSON deserialization:**
- `FargateUserAgentRuntime.cs` uses `PropertyNameCaseInsensitive = true` — will correctly deserialize `"payload"` from wire JSON into `Payload`. ✅

**TurnRequest field — ordering check:**
- `TurnRequest` is constructed at line 777–783 (inside `try` block), well before the `finally` at line 838 that resets `_taskMode = false`. `TaskMode: _taskMode` captures the correct user-toggled value. ✅

**StateHasChanged() pattern inside await foreach:**
- All three SSE branches (`text`, `error`, `mode_switch`) call `StateHasChanged()` directly inside the `await foreach`. This is the established pattern in this file (line 793, 805, 810). The `await foreach` runs on the Blazor sync context (HandleSend is invoked from UI events), so direct `StateHasChanged()` is safe here. Consistent with existing code. ✅

---

### CSS Audit

New `.btn-task-mode`, `.btn-task-mode--active`, `.btn-task-mode:hover:not(...)`, `.chat-task-indicator` rules:

```css
/* Every property inspected */
background: transparent                          ✅ keyword
border: 1px solid var(--color-border)           ✅ 1px is unitless structural, var for color
border-radius: var(--radius-md)                 ✅
color: var(--color-text-secondary)              ✅
padding: var(--space-1, 0.25rem) var(--space-2, 0.5rem)  ✅ rem fallbacks inside var() — acceptable
font-size: var(--text-sm, 0.875rem)             ✅
transition: all 0.15s ease                      ✅ animation timing, not design token
background: var(--color-accent)                 ✅
border-color: var(--color-accent)               ✅
color: var(--color-text-on-accent, #fff)        ✅ #fff fallback inside var() — acceptable
display: flex                                   ✅
align-items: center                             ✅
gap: var(--space-1, 0.25rem)                    ✅
color: var(--color-accent)                      ✅
font-size: var(--text-sm, 0.875rem)             ✅
font-family: var(--font-primary)                ✅
padding: var(--space-1, 0.25rem) var(--space-2, 0.5rem)  ✅
background: var(--color-accent-light, rgba(212,175,55,0.1)) ✅ rgba fallback inside var()
border-radius: var(--radius-sm)                 ✅
```

**Result:** Zero hardcoded design values outside of CSS var() fallbacks. ✅

---

### Issues Found

| Severity | File | Line | Issue | Disposition |
|----------|------|------|-------|-------------|
| — | — | — | No issues found | N/A |

---

### Notable Behavior (by design, not bugs)

1. **`_taskModeActive` persists after turn completes** — The indicator stays visible after a turn with `mode_switch` until the NEXT `HandleSend` call resets it. This is correct per spec: the indicator reflects active task mode status, not toggle state.

2. **`_taskMode` resets after every send** — User must re-toggle for each message. Spec-correct; persistence is a future WI.

3. **`btn-task-mode` is HTML `<button>`, not MudIconButton** — Consistent with other KB toggle buttons already in this file. `disabled="@isStreaming"` works correctly for HTML elements with Blazor bool-attribute binding.

4. **`1px` in `.btn-task-mode` border** — `1px solid var(--color-border)` — structural border width, not a design token. This is the established pattern in the codebase and doesn't require a CSS variable.

---

### Build

```
dotnet build src/FortressAI.Web
→ 0 Errors | 32 Warnings (all pre-existing — MUD0002, CS8602, CS0649; none from ADO#3145 changes)
```

---

### Summary

Clean implementation. All 9 changes verified against the spec. HarnessEvent positional parameter safety confirmed. TurnRequest captures `_taskMode` before finally resets it. SSE `mode_switch` handler pattern matches existing `text`/`error` handlers. CSS is fully var-compliant. Build passes.

**PASS — ships.**
