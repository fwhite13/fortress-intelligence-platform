# Review Report — ADO#4248

**Verdict: NEEDS-CHANGES**

---

## CC Review Summary

CC performed a full adversarial review of `ChatView.razor` commit `fa1a953a`. Seven targeted questions were answered. One real issue found (sizing), six checks passed clean. No false positives dismissed — the single finding is confirmed by direct inspection of the MudBlazor rendering model.

---

## Spec Compliance Check

**AC1** — CC agent icon shown during task execution: ✅ `tc.Server == "task"` branch renders SmartToy in chip loop  
**AC2** — Distinct from generic spinner: ✅ MudIcon SmartToy replaces emoji+spin; fa-tasks unchanged for non-CC mode  
**AC3** — Visible spawn → completion: ✅ `calling` = SmartToy + pulse, `done` = SmartToy static; header badge tied to `_ccTaskActive`  
**AC4** — Consistent with FAIT design language: ✅ MudBlazor `MudIcon`, `var(--color-accent)`, class-driven CSS, no inline styles  

**Spec compliance verdict:** ✅ COMPLIANT (with fix required for precise sizing)

---

## Consistency Audit

**Files cross-referenced:**
- `ChatView.razor` ↔ `fortress.css` — `@keyframes pulse` ✅ confirmed at fortress.css:2035 (`0%/100% opacity:1`, `50% opacity:0.4`)
- `_ccTaskActive` flag ↔ all lifecycle clear paths ✅ (CancelTask, HandleSend, OnParametersSetAsync, task_progress isFinal, text-event fade, finally block)
- `_ccTaskActive` ↔ `_taskModeActive` — ✅ distinct flags, correct nesting

**Undocumented dependencies:** None found.

---

## Issues Found

| Severity | File | Lines | Issue | Fix |
|----------|------|-------|-------|-----|
| Important | `ChatView.razor` | 2092–2096 | `.chat-task-indicator__cc-icon` missing `font-size` — MudIcon renders at MudBlazor default 24px instead of intended ~14px, visible size jump vs fa-tasks | Add `font-size: 0.875rem;` to `.chat-task-indicator__cc-icon` |

---

## Full Findings

### ✅ `_ccTaskActive` Lifecycle — No Orphan Scenario
All five clear paths are present. Critically, the `finally` block at line 1297 covers every abnormal exit path (network drop, timeout, exception, cancellation). The fire-and-forget fade callback at line 1033 is already gated on `!_ccTaskActive` so its redundant clear is harmless.

### ✅ Pulse Animation — Cannot Get Stuck
`finally` block clears both `_ccTaskActive` AND `_taskModeActive`. Even without a `task_progress "done"` event, both flags go false and the outer `@if (_taskModeActive)` removes the entire indicator div from DOM. Pulse animation cannot orphan.

### ✅ Header Badge Conditional Logic — Sound
`_taskModeActive` and `_ccTaskActive` are genuinely distinct flags with different event sources. The outer/inner nesting is correct. The theoretical race (task_progress "start" before mode_switch) is a sub-second window, not a correctness bug — both flags resolve correctly once both SSE events arrive.

### ✅ `@keyframes pulse` — Confirmed in fortress.css
```css
@keyframes pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
}
```
`.cc-agent-icon--pulse { animation: pulse 1.5s ease-in-out infinite; }` — references a valid keyframe.

### ✅ Non-CC Chips — No Regression
`else` branch preserves original `<span class="tool-call-emoji">@GetToolEmoji(...)` for all `tc.Server != "task"`. `GetToolEmoji` is unchanged. Non-task chips are unaffected.

### ⚠️ `.chat-task-indicator__cc-icon` — Missing `font-size` (Important)
`.cc-agent-icon` (used on chips) has `font-size: 0.875rem` ✅ — correct.  
`.chat-task-indicator__cc-icon` (used on header badge) sets `width: 1rem; height: 1rem` but **no `font-size`**.

MudBlazor's `MudIcon` SVG is sized by `font-size` on the wrapper, not by `width/height`. Without an explicit override, MudBlazor's `mud-icon-size-medium` class wins at `font-size: 1.5rem` → 24px icon. The `<i class="fas fa-tasks">` it replaces renders at the container's `~14px`. This produces a visible jump when `_ccTaskActive` toggles.

### ✅ Build Cleanliness
No new errors or warnings. `Icons.Material.Filled.SmartToy` is a valid MudBlazor constant. All Razor expressions are well-formed.

---

## What to Fix

**1 change, 1 line:**

```diff
 .chat-task-indicator__cc-icon {
     width: 1rem;
     height: 1rem;
     color: var(--color-accent);
+    font-size: 0.875rem;
 }
```

Location: `ChatView.razor` ~line 2093 (inside the CSS block near bottom of file).

Alternatively, add `Size="Size.Small"` to the MudIcon at line 76 — either approach resolves the sizing.

---

_Reviewed by Hawkeye — 1 cycle. 1 issue. Fix it and resubmit._
