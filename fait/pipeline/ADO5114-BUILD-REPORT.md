# Build Report — ADO #5114

## What was built
Fixed three ephemeral chip UX defects: raw bash commands in chip text, chips positioned above assistant message, vertical squeeze at bottom of chat.

## Files changed
- `fait/agent-harness/harness-server.js` — Expanded `Bash` (capital B) case in `resolveProgressLabel()` with full pattern matching
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — Moved chip block to render after the streaming MessageBubble
- `fait/src/FortressAI.Web/wwwroot/css/fortress.css` — Added `min-height: 28px; flex-shrink: 0` to `.tool-call-indicator`; `min-height: 28px` to `.tool-call-indicator-list`

## Parallelization used
No — all three files modified sequentially by CC.

## CC sessions run
1 CC run (11 turns, goal met). Brief was under 4000 chars after trimming verbose code samples.

## Bug 1: Raw bash commands
**Root cause:** `resolveProgressLabel()` has two code paths for shell commands — lowercase `bash` (Bedrock native) with rich pattern matching, and uppercase `Bash` (CC native) with only a `pip install` check. CC always uses `Bash` (capital B), so the rich patterns never fired.

**Fix:** Expanded the `Bash` case to include: `openpyxl/xlsx/xlsxwriter`, `pptx/python-pptx`, `docx/python-docx`, `generate.*report`, `create.*excel/make.*xlsx`, `python3 *generate*`, `python3 *.py`, `^cat `, `^ls /^find `, output redirection `>`/`>>`.

## Bug 2: Chips above message
**Root cause:** Chips rendered before the `@if (isStreaming)` MessageBubble block in the Razor template — positional order in Blazor is rendering order.

**Fix:** Moved the chip `@if (_activeToolCalls.Any() || _toolCallsFading)` block to after the `isStreaming` MessageBubble.

## Bug 3: Vertical squeeze
**Root cause:** `.tool-call-indicator` had no `min-height` and `flex-shrink` defaulting to 1. In a flex parent with viewport height constraints, chips were compressible.

**Fix:** `min-height: 28px; flex-shrink: 0` on `.tool-call-indicator`; `min-height: 28px` on `.tool-call-indicator-list`.

## Acceptance criteria verification
- [x] `resolveProgressLabel('Bash', {command: 'python3 gen.py'})` → "Running Python script..." (not raw command)
- [x] `resolveProgressLabel('Bash', {command: 'pip install openpyxl'})` → "Installing dependencies..."
- [x] Chip block is after streaming MessageBubble in ChatView.razor (line 217 vs 213)
- [x] CSS has `min-height: 28px; flex-shrink: 0` on indicator
- [x] Commit `0b555f31` on fred-dev

## Known edge cases / things Clint should scrutinize
- The `>|>>` redirection check (`Writing output file...`) could false-positive on `grep -v 'error' file` or similar. Acceptable tradeoff.
- The `Bash` and `bash` handlers are now duplicated logic — could be refactored into a shared function, but deferred to avoid churn on a working system.

## How to test locally
Run a CC task that uses bash commands — chips should show "Running Python script..." etc. instead of raw command text.
