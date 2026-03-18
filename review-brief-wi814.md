# Review Brief: WI814 — FfE Sprint 2: Close Excel read/write gaps
## Cycle 1 of 2
## Reviewer: Hawkeye (Clint Barton)

You are Claude Code performing a code review for WI814. Read the 5 modified files and verify each checklist item below. For each item, report what you actually found in the code (exact lines/snippets) and whether it passes.

## Working directory
`/home/fredw/projects/fait-for-excel/`

## Commit under review
`6c8649e` — 5 files changed in `src/taskpane/`

## Files to read
1. `src/taskpane/services/excelWriter.ts`
2. `src/taskpane/services/excelReader.ts`
3. `src/taskpane/components/ContextIndicator.tsx`
4. `src/taskpane/components/ChatPanel.tsx`
5. `src/taskpane/components/WriteSuggestionsDialog.tsx`

---

## Checklist (read the code, then verify each item)

### CHECK 1 — HIGH: `getResizedRange(rows-1, cols-1)` in excelWriter.ts
`getResizedRange` takes delta args, not total size:
- `getResizedRange(0, 0)` = same single cell (1×1)
- `getResizedRange(rows-1, cols-1)` = extends to fit exactly `rows × cols` data

Read `excelWriter.ts`, find the `writeRangeData` function, and report the exact line calling `getResizedRange`. It MUST be `(rows - 1, cols - 1)`. If it's `(rows, cols)` — that's a critical bug.

### CHECK 2 — HIGH: `WriteRangeError` is exported
Callers doing `catch (e) { if (e instanceof WriteRangeError)` require the class to be exported.
Read `excelWriter.ts` and confirm the class declaration starts with `export class WriteRangeError`.

### CHECK 3 — HIGH: `WriteRangeError.code` union type
The `.code` property on `WriteRangeError` must be typed as the union `'EMPTY_DATA' | 'DIMENSION_MISMATCH' | 'EXCEL_ERROR'`.
Read the class constructor in `excelWriter.ts` and report the exact type annotation.

### CHECK 4 — MEDIUM: ContextIndicator null-address state uses muted grey, NOT gold
- Active selection state: `color: '#d4af37'` (gold)
- No-selection state: `color: '#556677'` (muted grey) — must be visually distinct

Read `ContextIndicator.tsx`, find the `if (!address)` branch, and report the `color` value used. It must be `#556677` (grey), not `#d4af37` (gold).

### CHECK 5 — MEDIUM: ChatPanel renders ContextIndicator when `selectionInfo=null`
Old condition: `{includeSelection && selectionInfo && (...)}` — hid indicator when no selection.
New condition must be: `{includeSelection && (...)}` — always shows when toggle is on.

Read `ChatPanel.tsx`, find the context indicator render block, and report:
1. The outer condition (must NOT include `&& selectionInfo`)
2. The `address` prop value passed to `ContextIndicator` (must use optional chaining: `selectionInfo?.address ?? null`)

### CHECK 6 — MEDIUM: `getSelectionState()` added without modifying existing exports
Read `excelReader.ts` and list ALL exported items. Must show:
- `getSelectedRange` (original — must still exist)
- `getFullWorksheet` (original — must still exist)
- `getSelectionState` (new addition)
- `SpreadsheetContext` (interface — original)

### CHECK 7 — MEDIUM: WriteSuggestionsDialog — BOTH catch blocks updated
Both `handleAcceptAll()` and `handleAcceptCurrent()` must have dimension mismatch detection.
Read `WriteSuggestionsDialog.tsx` and for each function, confirm the catch block checks for `dimension` / `mismatch` / `does not fit` and shows a specific error message. Report the line numbers and actual code for both.

### CHECK 8 — LOW: No unexpected imports added to ChatPanel
Read the imports at the top of `ChatPanel.tsx`. The only new import beyond what was there before should be:
`import { writeRangeData, WriteRangeError } from '../services/excelWriter';`
No new packages, no new React hooks, no unexpected additions.

### CHECK 9 — LOW: No existing functions modified (additive only)
Verify:
- `excelWriter.ts`: `applySuggestions()` and `applySingleSuggestion()` are untouched
- `excelReader.ts`: `getSelectedRange()` and `getFullWorksheet()` are untouched
- `ChatPanel.tsx`: `handleSend()`, `handleChart()`, `handlePivot()` are untouched
- No functions were deleted or renamed

### CHECK 10 — LOW: API compatibility
`getResizedRange`, `range.values`, and `ctx.sync()` are all ExcelApi 1.1. The manifest's `MinVersion="1.13"` is well above this. No new APIs used outside compatibility range.

---

## What to produce
For each check, report:
- What you found (exact code snippet or line)
- PASS or FAIL
- If FAIL: exactly what is wrong and what the fix should be

Then give an overall verdict: PASS, NEEDS-CHANGES, or FAIL.

PASS = all HIGH checks pass and all MEDIUM checks pass
NEEDS-CHANGES = any MEDIUM check fails (or LOW nitpick worth fixing)
FAIL = any HIGH check fails
