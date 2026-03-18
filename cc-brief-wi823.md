# CC Brief: WI823 — FfE S7 Table Object Awareness — Fix Remaining Issues

## Context

Sprint 7 (Table Object Awareness) is ~95% implemented. There are exactly 2 remaining issues to fix. Do NOT touch any other files or any other code — only the 2 fixes described below.

**Working directory:** `/home/fredw/projects/fait-for-excel`

---

## Issue 1: excelWriter.ts — Duplicate `const sheet` declaration (COMPILE ERROR)

**File:** `src/taskpane/services/excelWriter.ts`

Inside the `writeRangeData` function, `const sheet` is declared twice within the same `Excel.run()` callback scope. This causes TypeScript error TS2451 and the build fails.

**Current broken code** (inside `Excel.run(async (ctx: any) => {` in `writeRangeData`):

```typescript
  return Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    // Resize the range from the target cell to fit the data exactly
    const startRange = sheet.getRange(targetCell);
    const writeRange = startRange.getResizedRange(rows - 1, cols - 1);

    writeRange.values = data;

    // Load the final address so we can return it
    writeRange.load('address');

    // Detect if write target overlaps a Table (advisory warning — write still proceeds)
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const tables = sheet.tables;
```

**Fix:** Remove the second `const sheet = ctx.workbook.worksheets.getActiveWorksheet();` line (the one that appears after `writeRange.load('address')`). The first `const sheet` at the top of the `Excel.run()` callback is correct and already used by `sheet.getRange(targetCell)`. The second duplicate is the bug.

After fix, the `tables` line should reference the already-declared `sheet`:
```typescript
    // Detect if write target overlaps a Table (advisory warning — write still proceeds)
    const tables = sheet.tables;
    tables.load('count');
```

**This is a one-line deletion fix.** Remove only the duplicate `const sheet = ctx.workbook.worksheets.getActiveWorksheet();` line inside `writeRangeData`. Do not change anything else in this file.

---

## Issue 2: useExcelContext.ts — Add `tableName` to `selectionInfo` state

**File:** `src/taskpane/hooks/useExcelContext.ts`

**Current code:**
```typescript
const [selectionInfo, setSelectionInfo] = useState<{ address: string; rows: number; cols: number } | null>(null);
```

And in the setInterval callback:
```typescript
        const info = await getSelectedRange();
        setSelectionInfo({ address: info.address, rows: info.rows, cols: info.cols });
```

**Required fix — update the state type to include `tableName`:**
```typescript
const [selectionInfo, setSelectionInfo] = useState<{ address: string; rows: number; cols: number; tableName?: string | null } | null>(null);
```

**Required fix — update the setSelectionInfo call in the interval:**
```typescript
        const info = await getSelectedRange();
        setSelectionInfo({ address: info.address, rows: info.rows, cols: info.cols, tableName: info.tableInfo?.name ?? null });
```

**That's it for this file.** Do not change `readSelection()`, the return statement, the imports, or anything else.

---

## Verification

After making both fixes, the file should compile cleanly:
- `excelWriter.ts`: no duplicate variable declaration
- `useExcelContext.ts`: `selectionInfo` includes `tableName?: string | null`

**Do NOT touch any other files.** The following files are already correctly implemented and must not be modified:
- `src/taskpane/services/excelReader.ts` — complete
- `src/taskpane/services/contextFormatter.ts` — complete
- `src/taskpane/components/ChatPanel.tsx` — complete
- `src/taskpane/components/ContextIndicator.tsx` — complete

Only fix the 2 issues described above in the 2 files listed.
