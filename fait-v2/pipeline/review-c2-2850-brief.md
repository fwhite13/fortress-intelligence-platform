# Cycle 2 Review Brief — ADO#2850

## Task
Verify two targeted fixes from NEEDS-CHANGES cycle 1. No full re-review needed — only confirm the two items below are correct.

## Fix 1 — C7: CSS variable for max-height
File: `src/FortressAI.V2.Web/wwwroot/css/app.css`

Verify:
1. Line 618: `max-height:` uses `var(--chat-input-max-height, 200px)` — NOT bare `200px`
2. No other hardcoded px values were introduced in this commit

## Fix 2 — I2: BuildSystemPrompt null return
File: `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

Verify:
1. `BuildSystemPrompt()` signature is `private string? BuildSystemPrompt()`
2. When both KB flags are false, it returns `null` (not `""`)
3. When one or both KB flags are true, it returns the joined string

## Build
Confirm build is still 0 errors, 0 warnings.

## What to report
- C7 PASS or FAIL with evidence
- I2 PASS or FAIL with evidence  
- Build status
- Any NEW issues introduced by the fix commit (unlikely but check)
- Overall verdict: PASS or NEEDS-CHANGES
