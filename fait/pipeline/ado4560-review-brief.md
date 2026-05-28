# ADO4560 Review Brief — Adversarial Code Review

## Task
Review commit 3f1bbb0bd41df2c112959e5e0c34a8ae13def7a7 for ADO#4560.

**Claimed change:** Single constant `_importPrompt` in `Memory.razor` expanded from a one-liner to a full structured verbatim string. No other files changed.

## File to review
`/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Pages/Memory.razor`

## What to verify

### 1. Scope check
- Confirm ONLY `_importPrompt` was changed. No other fields, methods, markup, or logic.
- Read lines 205-220 (Razor UI using `_importPrompt`) and confirm nothing was touched.
- Read lines 555-570 (JS copy logic) and confirm nothing was touched.

### 2. C# verbatim string escaping (CRITICAL)
The constant uses `@"..."` verbatim string literal syntax. In C# verbatim strings:
- Double-quotes MUST be escaped as `""` (two double-quotes)
- Backslash `\` is literal (no escaping needed)
- Check every double-quote inside the string. Any `\"` would be a compile error / unexpected behavior.
- Specifically check: `""always do X""`, `""never do Y""` — these should be `""` not `\"`

### 3. Five categories present in correct order
Verify all 5 are present:
1. Instructions
2. Identity
3. Career
4. Projects
5. Preferences

### 4. Content coherence
- Does the prompt make sense as a Claude Desktop memory export request?
- Is the YYYY-MM-DD format instruction present and correct?
- Is the `[unknown]` fallback for missing dates present?
- Is the code block output wrapping instruction present?
- Is the "state whether complete" instruction present?

### 5. Razor/code block structural integrity
- The `@"..."` string must be properly closed with `";` on the last line
- No stray `@` characters inside the string that Razor could misinterpret (since this is in `@code` block, `@` inside verbatim strings is fine — but verify)
- The const declaration is inside `@code { }` where `@` prefixed verbatim strings are valid

### 6. UI rendering (lines 208-214)
- `<MudText>@_importPrompt</MudText>` with `white-space: pre-wrap` — confirm this is unchanged
- This ensures multiline prompt renders with newlines in the UI

## Pass criteria
- Only `_importPrompt` constant changed
- All double-quotes inside the verbatim string use `""` escaping
- All 5 categories present in order
- Prompt content is coherent and complete
- No structural issues in the Razor file

## Fail criteria
- Any `\"` inside the verbatim string (wrong escaping — compile error risk)
- Missing categories
- Any other file or code path modified
- Malformed string termination

Report findings with severity (Critical/Important/Nitpick) and specific line numbers.
