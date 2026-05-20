# CC Review Brief — ADO#3884: Name Moderation Hookup

## Task
Adversarial code review of commit 88bfb6c0.
ContentModerationService.CheckNameAsync wired into AssistantSetup.razor and Settings.razor.

## Files to Review
- src/FortressAI.Web/Components/Pages/AssistantSetup.razor
- src/FortressAI.Web/Components/Pages/Settings.razor

## What to Read
Read the FULL AssistantSetup.razor and Settings.razor files, focusing on:
1. The @inject directives at the top
2. The HandleSubmit() method in AssistantSetup.razor
3. The SaveSettings() method in Settings.razor
4. The private field declarations
5. The HTML markup for the name input fields

## Acceptance Criteria to Verify

### AssistantSetup.razor
1. `@inject ContentModerationService ContentModerationService` directive present (exact Blazor @inject pattern)
2. `_assistantNameError` and `_preferredNameError` private string? fields declared
3. In HandleSubmit(): moderation checks run BEFORE any DB write (before `await using var db = ...`)
4. Both fields checked INDEPENDENTLY (not short-circuited — if _assistantName fails, still check _preferredName)
5. Empty/whitespace strings skip the moderation call (IsNullOrWhiteSpace guard)
6. Early return happens ONLY after BOTH checks are done (both errors set, then single return)
7. Exact error string for assistant name: "That assistant name is not appropriate for a workplace app. Please choose another."
8. Exact error string for preferred name: "That display name is not appropriate for a workplace app. Please choose another."
9. Inline error divs with class "setup-field-error" shown after each input when error != null
10. Both error vars cleared to null at start of HandleSubmit()

### Settings.razor
1. `@inject ContentModerationService ContentModerationService` directive present
2. In SaveSettings(): moderation checks run BEFORE SaveConfigAsync call
3. Snackbar shown on fail (not inline error)
4. Early return on fail — but MUST be inside the try block so the finally { _saving = false; } still fires
5. Same exact error strings as above
6. Empty/whitespace guard present
7. No short-circuiting — wait, Settings.razor CAN short-circuit (return immediately after first fail) unlike AssistantSetup — verify this matches the spec
8. _assistantName checked first, then _preferredName

### Both files
- No DB changes (no migration files, no DbContext changes)
- No new DbSet registrations
- await used correctly on CheckNameAsync
- No extra try/catch wrapping the moderation calls (fail-open is handled in ContentModerationService)

## Key Logic Check — Settings.razor finally block
The `return` statements on moderation failure MUST be inside the `try` block.
If they are BEFORE the `try` or outside it, `_saving = false` in the `finally` will NOT run, leaving the Save button permanently disabled.
Verify: the try block opens BEFORE the moderation checks, and the returns are inside that try.

## Key Logic Check — AssistantSetup.razor independence
Both moderation checks must run even if the first one fails.
Pattern to verify:
```
check _assistantName → set _assistantNameError if fail
check _preferredName → set _preferredNameError if fail  
if (either error != null) → return   ← single combined return
```
NOT:
```
check _assistantName → if fail, return immediately  ← WRONG, skips preferredName check
```

## Adversarial Checks
- Does the early return in AssistantSetup.razor set _isSubmitting = false before returning?
- Is StateHasChanged() called after setting error variables so they render?
- In Settings.razor: does the early return leave _saving = true forever? (Check finally block)
- Are both fields' error strings the correct variant ("assistant name" vs "display name")?
- Is CheckNameAsync awaited (not fire-and-forget)?

## Report Format
For each acceptance criterion: ✅ PASS or ❌ FAIL with evidence from the code.
Final verdict: PASS / NEEDS-CHANGES / FAIL.
