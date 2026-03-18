# CC Brief: WI814 — FfE Sprint 2: Close Excel read/write gaps

## Working Directory
/home/fredw/projects/fait-for-excel

## Overview
Three focused gaps to close across 5 files. Most of Sprint 2 is already built; this adds missing functions and improves UX for edge cases.

## CRITICAL CONSTRAINTS
- DO NOT modify existing functions in excelWriter.ts or excelReader.ts — ADD ONLY after existing exports
- DO NOT change contextFormatter.ts, useChat.ts, useWriteBack.ts, useExcelContext.ts
- DO NOT add any new npm packages
- Touch ONLY the 5 files listed below
- CC must write all changes — no manual file edits

---

## File 1: src/taskpane/services/excelWriter.ts

CURRENT FILE CONTENT:
```typescript
import type { CellSuggestion } from '../components/WriteSuggestionsDialog';

/* global Excel */

export async function applySuggestions(suggestions: CellSuggestion[]): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    for (const s of suggestions) {
      const range = sheet.getRange(s.address);

      if (s.formula) {
        range.formulas = [[s.formula]];
      } else if (s.value !== null && s.value !== undefined) {
        range.values = [[s.value]];
      }

      range.format.fill.color = '#FFFF00';

      // Add comment via worksheet (Range has no .comments — must use Worksheet.comments.add)
      try {
        sheet.comments.add(s.address, `AI suggestion: ${s.explanation}`);
      } catch {
        /* ignore comment failures */
      }
    }

    await ctx.sync();
  });
}

export async function applySingleSuggestion(suggestion: CellSuggestion): Promise<void> {
  await applySuggestions([suggestion]);
}
```

ACTION: ADD the following code AFTER the last line of the file (after `applySingleSuggestion`). Do NOT modify any existing code.

ADD THIS EXACTLY after the existing exports:

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

---

## File 2: src/taskpane/services/excelReader.ts

CURRENT FILE CONTENT:
```typescript
/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Excel: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

export interface SpreadsheetContext {
  address: string;
  rows: number;
  cols: number;
  values: unknown[][];
  formulas: string[][];
}

export async function getSelectedRange(): Promise<SpreadsheetContext> {
  return Excel.run(async (ctx: any) => {
    const range = ctx.workbook.getSelectedRange();
    range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);
    await ctx.sync();
    return {
      address: range.address as string,
      rows: range.rowCount as number,
      cols: range.columnCount as number,
      values: range.values as unknown[][],
      formulas: range.formulas as string[][],
    };
  });
}

export async function getFullWorksheet(): Promise<SpreadsheetContext> {
  return Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const range = sheet.getUsedRange();
    range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);
    await ctx.sync();

    // Hard cap: 500 rows × 50 cols
    const vals = (range.values as unknown[][]).slice(0, 500).map((r: unknown[]) => r.slice(0, 50));
    const fmls = (range.formulas as string[][]).slice(0, 500).map((r: string[]) => r.slice(0, 50));

    return {
      address: range.address as string,
      rows: Math.min(range.rowCount as number, 500),
      cols: Math.min(range.columnCount as number, 50),
      values: vals,
      formulas: fmls,
    };
  });
}
```

ACTION: ADD the following code AFTER the last line of the file (after `getFullWorksheet`). Do NOT modify any existing code.

ADD THIS EXACTLY after the existing exports:

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

---

## File 3: src/taskpane/components/ContextIndicator.tsx

CURRENT FILE CONTENT:
```typescript
import React from 'react';

interface ContextIndicatorProps {
  address: string | null;
  rows: number;
  cols: number;
  visible: boolean;
}

const ContextIndicator: React.FC<ContextIndicatorProps> = ({ address, rows, cols, visible }) => {
  if (!visible || !address) return null;

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

export default ContextIndicator;
```

ACTION: REPLACE the entire file content with the following. The only change is splitting the `if (!visible || !address) return null` into two separate checks — first check `!visible`, then when address is null show a grey empty state instead of returning null.

NEW FILE CONTENT:
```typescript
import React from 'react';

interface ContextIndicatorProps {
  address: string | null;
  rows: number;
  cols: number;
  visible: boolean;
}

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

export default ContextIndicator;
```

---

## File 4: src/taskpane/components/ChatPanel.tsx

TWO focused changes only. Do NOT change any other part of this file.

### Change A: Add import for writeRangeData and WriteRangeError

Find this line near the top of the file:
```typescript
import { getSelectedRange } from '../services/excelReader';
```

REPLACE with:
```typescript
import { getSelectedRange } from '../services/excelReader';
import { writeRangeData, WriteRangeError } from '../services/excelWriter';
```

### Change B: Update Context indicator bar rendering condition

Find this EXACT block in the file:
```typescript
      {/* Context indicator bar */}
      {includeSelection && selectionInfo && (
        <div
          style={{
            padding: '4px 8px',
            borderBottom: '1px solid #2e3f54',
            background: '#1a2332',
            flexShrink: 0,
          }}
        >
          <ContextIndicator
            address={selectionInfo.address}
            rows={selectionInfo.rows}
            cols={selectionInfo.cols}
            visible={includeSelection}
          />
        </div>
      )}
```

REPLACE with:
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

---

## File 5: src/taskpane/components/WriteSuggestionsDialog.tsx

TWO focused changes to error catch blocks only. Do NOT change any other part of this file.

### Change A: handleAcceptAll catch block

Find this EXACT block:
```typescript
    } catch (e) {
      setError('Failed to apply suggestions — check the active sheet and try again.');
    } finally {
```

REPLACE with:
```typescript
    } catch (e) {
      const msg = e instanceof Error ? e.message : '';
      if (msg.includes('dimension') || msg.includes('mismatch') || msg.includes('does not fit')) {
        setError('Range mismatch — the selected cells don\'t fit the suggested data. Try accepting each suggestion individually.');
      } else {
        setError('Failed to apply — check that the correct sheet is active and try again.');
      }
    } finally {
```

### Change B: handleAcceptCurrent catch block

Find this EXACT block:
```typescript
    } catch (e) {
      setError(`Failed to apply cell ${s.address} — skipping.`);
      if (currentIndex < suggestions.length - 1) {
        setCurrentIndex((i) => i + 1);
      }
    } finally {
```

REPLACE with:
```typescript
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
    } finally {
```

---

## Summary of All Changes
1. excelWriter.ts: ADD writeRangeData() function and WriteRangeError class after existing exports
2. excelReader.ts: ADD getSelectionState() function after existing exports
3. ContextIndicator.tsx: Split !visible||!address check — show grey empty state when visible but no address
4. ChatPanel.tsx: (A) add writeRangeData/WriteRangeError import; (B) render ContextIndicator when includeSelection=true even if selectionInfo=null
5. WriteSuggestionsDialog.tsx: Improve error messaging in both catch blocks to detect dimension mismatches

## Key correctness notes:
- getResizedRange(rows-1, cols-1) — the -1 is intentional, it's a DELTA not total size
- WriteRangeError must be EXPORTED (export class WriteRangeError)
- ContextIndicator grey state: color '#556677', background '#1e2b3a' — visually distinct from gold '#d4af37'
- ChatPanel condition changes from `{includeSelection && selectionInfo && (` to `{includeSelection && (`
- writeRangeData/WriteRangeError import in ChatPanel is for future use (Sprint 3) — no UI wired yet
