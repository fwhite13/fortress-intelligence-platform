# FfE Sprint 2 Spec — Excel Read/Write Integration

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** WI#813 (build refactor) must be landed first  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Critical Finding: Most of Sprint 2 Is Already Built

Before speccing what to build, I read the source. Here is the actual state:

| Feature | Status | Files |
|---------|--------|-------|
| Read selected range | ✅ **DONE** | `excelReader.ts → getSelectedRange()` |
| Format context as markdown table | ✅ **DONE** | `contextFormatter.ts → formatContext()` |
| Inject selection into chat prompt | ✅ **DONE** | `ChatPanel.tsx → handleSend()` |
| Selection toggle (include/exclude) | ✅ **DONE** | `ChatInput.tsx` checkbox |
| Selection indicator bar | ✅ **DONE** | `ContextIndicator.tsx` |
| Selection polling (2s interval) | ✅ **DONE** | `useExcelContext.ts` |
| Write cell values from AI suggestions | ✅ **DONE** | `excelWriter.ts → applySuggestions()` |
| Write-back confirmation dialog | ✅ **DONE** | `WriteSuggestionsDialog.tsx` |
| Review-each mode for suggestions | ✅ **DONE** | `WriteSuggestionsDialog.tsx` |
| Write full dataset to range | ❌ **MISSING** | See §Gaps below |
| Graceful "no selection" handling | ⚠️ **PARTIAL** | See §Gaps below |
| Write dimension mismatch error | ⚠️ **PARTIAL** | See §Gaps below |

**Conclusion:** The read/inject and write-suggestion flows are complete. Sprint 2 work is three focused gaps, not a full build.

---

## What Actually Needs Building (The Three Gaps)

### Gap 1 — `excelWriter.ts`: `writeRangeData()` function (missing)

`applySuggestions()` writes individual cells from AI-parsed `CellSuggestion` objects. There is no function to write a **2D dataset** (e.g., a table of results FAIT generates in its response) to a contiguous range starting at a target cell.

This is needed for: "FAIT generates a table, user says write it to A1."

### Gap 2 — `ContextIndicator` / `ChatInput`: Empty selection feedback (partial)

When no range is selected (e.g., user opens the add-in before clicking any cell), `selectionInfo` is null and `ContextIndicator` returns null — the indicator simply disappears. There's no message to the user explaining why context won't be included. This causes silent failures.

### Gap 3 — Write dimension validation + user-facing error (partial)

`applySuggestions()` has a try/catch that swallows errors with a generic message. When a write fails due to dimension mismatch (writing a 3×2 table to a 1×1 range), the user gets no actionable feedback. Need dimension pre-validation before calling `ctx.sync()`.

---

## Parallelization Map

```
Single sequential CC session — all changes are in fait-for-excel/src only.
No shared files between tasks. Small scope.

  Task 1: excelWriter.ts — add writeRangeData() function
  Task 2: excelReader.ts — add getSelectionState() helper
  Task 3: ContextIndicator.tsx — add "no selection" empty state
  Task 4: ChatPanel.tsx — wire up empty selection warning + writeRangeData trigger
  Task 5: WriteSuggestionsDialog.tsx — improve error messaging for dimension mismatch
```

---

## File-Level Spec

### Task 1: `src/taskpane/services/excelWriter.ts`

Add one new exported function after the existing `applySingleSuggestion`:

```typescript
/**
 * Write a 2D array of values to a contiguous range starting at targetCell
 * on the active worksheet.
 *
 * @param targetCell  Excel address of the top-left cell, e.g. "A1" or "Sheet1!B3"
 * @param data        2D array: data[row][col]. Must be non-empty.
 * @throws WriteRangeError with .code "EMPTY_DATA" | "DIMENSION_MISMATCH" | "EXCEL_ERROR"
 */
export async function writeRangeData(
  targetCell: string,
  data: (string | number | boolean | null)[][]
): Promise<{ address: string; rows: number; cols: number }> {
  if (!data || data.length === 0 || data[0].length === 0) {
    throw new WriteRangeError('No data to write', 'EMPTY_DATA');
  }

  const rows = data.length;
  const cols = data[0].length;

  // Validate all rows have same column count
  if (data.some((row) => row.length !== cols)) {
    throw new WriteRangeError(
      'Data rows have inconsistent column counts',
      'DIMENSION_MISMATCH'
    );
  }

  return Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    // Resize the range from the target cell to fit the data exactly
    const startRange = sheet.getRange(targetCell);
    const writeRange = startRange.getResizedRange(rows - 1, cols - 1);

    writeRange.values = data;

    // Load the final address so we can return it
    writeRange.load('address');
    await ctx.sync();

    return {
      address: writeRange.address as string,
      rows,
      cols,
    };
  }).catch((e: any) => {
    if (e instanceof WriteRangeError) throw e;
    throw new WriteRangeError(
      e?.message ?? 'Excel write failed',
      'EXCEL_ERROR'
    );
  });
}

export class WriteRangeError extends Error {
  constructor(
    message: string,
    public readonly code: 'EMPTY_DATA' | 'DIMENSION_MISMATCH' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'WriteRangeError';
  }
}
```

**Do NOT modify** `applySuggestions()` or `applySingleSuggestion()` — they work correctly.

---

### Task 2: `src/taskpane/services/excelReader.ts`

Add one new exported function after `getFullWorksheet()`:

```typescript
/**
 * Returns whether the user currently has a non-empty selection.
 * Safe to call at any time — returns false if Excel is unavailable.
 */
export async function getSelectionState(): Promise<{
  hasSelection: boolean;
  address: string | null;
  rows: number;
  cols: number;
}> {
  try {
    const ctx = await getSelectedRange();
    // A "no selection" in Excel often returns a single cell — treat 1×1 as valid
    return {
      hasSelection: ctx.rows > 0 && ctx.cols > 0,
      address: ctx.address,
      rows: ctx.rows,
      cols: ctx.cols,
    };
  } catch {
    return { hasSelection: false, address: null, rows: 0, cols: 0 };
  }
}
```

**Do NOT modify** `getSelectedRange()` or `getFullWorksheet()`.

---

### Task 3: `src/taskpane/components/ContextIndicator.tsx`

Update to show an explicit "no selection" state when `visible` is true but `address` is null:

```typescript
const ContextIndicator: React.FC<ContextIndicatorProps> = ({ address, rows, cols, visible }) => {
  if (!visible) return null;

  // No selection — show an informational empty state instead of nothing
  if (!address) {
    return (
      <div
        title="No range selected — click a cell or range in Excel to include context"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          padding: '2px 8px',
          background: '#1e2b3a',
          border: '1px solid #2e3f54',
          borderRadius: '12px',
          fontSize: '11px',
          color: '#556677',
          whiteSpace: 'nowrap',
        }}
      >
        <span>📊</span>
        <span>No selection — click a cell to include context</span>
      </div>
    );
  }

  // Has selection — existing display
  return (
    <div
      title={`Spreadsheet context will be included: ${address}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        padding: '2px 8px',
        background: '#243447',
        border: '1px solid #2e3f54',
        borderRadius: '12px',
        fontSize: '11px',
        color: '#d4af37',
        whiteSpace: 'nowrap',
        maxWidth: '100%',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
      }}
    >
      <span>📊</span>
      <span>Using: {address} ({rows}×{cols})</span>
    </div>
  );
};
```

Style distinction: grey/muted for no-selection state, gold for active selection state.

---

### Task 4: `src/taskpane/components/ChatPanel.tsx`

Two focused changes only:

**Change A — expose `writeRangeData` for future use (import only, no UI yet)**

Add to the import block at the top:
```typescript
import { writeRangeData, WriteRangeError } from '../services/excelWriter';
```

This import enables Tony or subsequent sprints to wire up a "write table to range" trigger. No UI component change needed in Sprint 2 — the write-back dialog already handles AI-suggested cell writes. Direct table writes are triggered by the user explicitly (Sprint 3 scope), not by automatic detection.

**Change B — pass null address to ContextIndicator when no selection**

The existing code already polls selection and sets `selectionInfo` to null on error. The ContextIndicator is only rendered when `includeSelection && selectionInfo` — which hides it entirely when there's no selection. Change the render condition to always show the indicator when `includeSelection` is true:

Find this block:
```typescript
{/* Context indicator bar */}
{includeSelection && selectionInfo && (
  <div ...>
    <ContextIndicator
      address={selectionInfo.address}
      rows={selectionInfo.rows}
      cols={selectionInfo.cols}
      visible={includeSelection}
    />
  </div>
)}
```

Replace with:
```typescript
{/* Context indicator bar — always show when include toggle is on */}
{includeSelection && (
  <div
    style={{
      padding: '4px 8px',
      borderBottom: '1px solid #2e3f54',
      background: '#1a2332',
      flexShrink: 0,
    }}
  >
    <ContextIndicator
      address={selectionInfo?.address ?? null}
      rows={selectionInfo?.rows ?? 0}
      cols={selectionInfo?.cols ?? 0}
      visible={true}
    />
  </div>
)}
```

**Do NOT change** `handleSend()`, `handleChart()`, `handlePivot()`, or any other handler. The read/inject flow is correct.

---

### Task 5: `src/taskpane/components/WriteSuggestionsDialog.tsx`

Improve error messaging to distinguish dimension mismatch from other write failures.

In `handleAcceptAll()`, replace the generic catch:
```typescript
// BEFORE
} catch (e) {
  setError('Failed to apply suggestions — check the active sheet and try again.');
}

// AFTER
} catch (e) {
  const msg = e instanceof Error ? e.message : '';
  if (msg.includes('dimension') || msg.includes('mismatch') || msg.includes('does not fit')) {
    setError('Range mismatch — the selected cells don\'t fit the suggested data. Try accepting each suggestion individually.');
  } else {
    setError('Failed to apply — check that the correct sheet is active and try again.');
  }
}
```

Apply the same improvement to `handleAcceptCurrent()`:
```typescript
// AFTER
} catch (e) {
  const msg = e instanceof Error ? e.message : '';
  const cellAddr = s.address;
  if (msg.includes('dimension') || msg.includes('mismatch')) {
    setError(`Cell ${cellAddr}: range doesn't fit — skipping.`);
  } else {
    setError(`Failed to apply cell ${cellAddr} — skipping.`);
  }
  if (currentIndex < suggestions.length - 1) {
    setCurrentIndex((i) => i + 1);
  }
}
```

---

## Context Format Reference (Already Correct — No Changes)

`contextFormatter.ts` produces this format (already working):

```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!A1:D5 | 5 rows × 4 cols

Headers: | Date | Product | Units | Revenue |
Row 1: | 2024-01-01 | Widget A | 100 | 5000 |
Row 2: | 2024-01-02 | Widget B | 85 | 3825 |
...
[END SPREADSHEET CONTEXT]

User question: What is the total revenue?
```

This format is correct and tested. Do not change `contextFormatter.ts`.

---

## Write Flow — How It Works (Already Implemented)

The complete write flow is already working via the suggestion system:

1. User sends a prompt with selection context
2. FAIT responds with structured data containing `cell_suggestions` JSON block
3. `parseSuggestions()` extracts `CellSuggestion[]` from the response
4. `useChat` sets `pendingSuggestions` 
5. `ChatPanel` detects this and calls `offerSuggestions()` via `useWriteBack`
6. `WriteSuggestionsDialog` renders with Accept All / Review Each / Reject All
7. On acceptance, `applySuggestions()` writes values/formulas to Excel, adds yellow highlight + AI comment

This flow is complete. Sprint 2 does not add a new write trigger — it improves the existing one (better error messages, Gap 3) and adds the infrastructure for bulk range writes (Gap 1).

---

## Error Handling Matrix

| Scenario | Current behavior | After Sprint 2 |
|----------|-----------------|----------------|
| No range selected, `includeSelection` on | Indicator disappears silently | Shows grey "No selection" message |
| Send message with no selection | Proceeds without context (non-fatal) | Same — non-fatal, no context injected |
| Write fails (dimension mismatch) | Generic "check your sheet" error | Specific "range doesn't fit" message |
| Write fails (sheet not active) | Generic error | Generic error (unchanged) |
| Empty data passed to writeRangeData | Not applicable (function doesn't exist) | Throws `WriteRangeError` with code `EMPTY_DATA` |
| Excel JS timeout | Existing timeout handling | Unchanged |

---

## No New Dependencies

Sprint 2 requires zero new npm packages. All Excel JS APIs used (`getResizedRange`, `range.values`, `ctx.sync`) are available in ExcelApi 1.1 — well within the manifest's `MinVersion="1.13"`.

---

## Acceptance Criteria

1. **No selection state:** When `includeSelection` is checked and no Excel range is selected, a grey "No selection — click a cell to include context" indicator is visible (not an empty gap)
2. **Active selection state:** When a range is selected, the gold "Using: A1:D5 (5×4)" indicator is visible — unchanged from current behavior
3. **`writeRangeData()` exists** and is exported from `excelWriter.ts`
4. **`writeRangeData()` builds cleanly** — TypeScript compiles with no errors
5. **Dimension mismatch error** in `WriteSuggestionsDialog` shows the specific "range doesn't fit" message, not the generic one
6. **All existing Sprint 1–5 features work unchanged**: chat, suggestion write-back, chart, pivot, CF, sort/filter, session persistence, FORGE search, error scanner, slash commands

---

## Constraints for CC

- Touch only the 5 files listed in the task breakdown
- Do NOT rewrite `excelWriter.ts` — only ADD `writeRangeData` and `WriteRangeError` after the existing exports
- Do NOT rewrite `excelReader.ts` — only ADD `getSelectionState` after the existing exports
- Do NOT change `contextFormatter.ts` — it is correct
- Do NOT change `useChat.ts`, `useWriteBack.ts`, `useExcelContext.ts` — they are correct
- Do NOT add any new React components
- Do NOT add any new npm packages

---

## Clint Review Priorities

```
⚠️  HIGH: Verify writeRangeData() uses getResizedRange() correctly — the resize args
          are (rowDelta, colDelta) from the start cell, i.e. (rows-1, cols-1), not (rows, cols).
          Off-by-one here writes to a range one row/col larger than the data.

⚠️  HIGH: Verify WriteRangeError is exported (not just defined) — it's needed by any
          caller that wants to catch and inspect the .code property.

⚠️  MEDIUM: Confirm ContextIndicator null-address state uses muted grey styling,
            not the gold active-selection styling — these must be visually distinct.

⚠️  MEDIUM: Confirm ChatPanel change renders ContextIndicator when includeSelection=true
            AND selectionInfo=null — the old condition (includeSelection && selectionInfo)
            would still hide it. The new condition must be (includeSelection) only.

⚠️  LOW: Confirm WriteSuggestionsDialog error message change doesn't break the
         existing review-each flow — error state is per-dialog not global.
```

---

## What Sprint 3 Should Add (Out of Scope Here)

For completeness — don't implement these now:

- **"Write to range" button** in the chat UI — user selects a target cell, clicks a button, FAIT's last table response is written there using `writeRangeData()`
- **Auto-detect structured tables** in FAIT responses and offer a one-click "write to sheet" action on the message bubble
- **Named range support** — writing to a named range rather than a cell address

These all depend on `writeRangeData()` (built in Sprint 2) and the existing suggestion flow.

---

_Spec by Reed Richards | Sprint 2 is 5 small file changes. Most of the feature already exists._
