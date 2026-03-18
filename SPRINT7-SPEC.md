# FfE Sprint 7 Spec — Table Object Awareness

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** Sprint 6 (Write Table to Range) must be landed — `writeRangeData()` live, `ParsedTable` interface exists  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What the Source Actually Shows

### Current context pipeline

```
User selects cells
    ↓
ChatPanel.handleSend()
    → getSelectedRange()  [excelReader.ts]
    → formatContext(ctx)  [contextFormatter.ts]
    → send(text, context) [useChat.ts]
        → fullMessage = `${context}\n\nUser question: ${text}`
        → POST /api/haven/chat { message: fullMessage }
```

`getSelectedRange()` returns `SpreadsheetContext`:
```typescript
interface SpreadsheetContext {
  address: string;    // e.g. "Sheet1!A1:D10"
  rows: number;
  cols: number;
  values: unknown[][];
  formulas: string[][];
}
```

`formatContext(ctx)` injects this into the prompt:
```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!A1:D10 | 10 rows × 4 cols

Headers: | Region | Q1 | Q2 | Q3 |
Row 1: | North | 12 | 15 | 18 |
Row 2: | South | 8 | 10 | 11 |
...
[END SPREADSHEET CONTEXT]
```

**Current header detection is heuristic:** row 0 is assumed to be a header if all cells are non-empty strings that don't parse as numbers. This works for many tables but silently fails for:
- Tables where the first data row happens to be all strings
- Numeric column headers ("2022", "2023", "2024")
- Tables where row 0 is blank (common when users select from A2 downward)

**No table metadata anywhere in the pipeline.** FAIT doesn't know the table name, whether columns are semantically named in the ListObject, or that it's dealing with structured data vs. a plain range.

### write path

`writeRangeData(targetCell, data[][])` in `excelWriter.ts`:
- Gets the active worksheet
- Calls `sheet.getRange(targetCell).getResizedRange(rows-1, cols-1)`
- Sets `writeRange.values = data`
- No awareness of whether `targetCell` is inside a Table
- Writing to a cell inside a Table works but can mangle the Table structure (wrong dimensions, skipping header row)
- No `writeToTable()` function exists yet

### ExcelApi table APIs — what's available at 1.13 baseline

`worksheet.tables` — available since **ExcelApi 1.1** ✅  
`table.name` — 1.1 ✅  
`table.getRange()` — returns full table range including headers — 1.1 ✅  
`table.getDataBodyRange()` — data rows only (no header) — 1.1 ✅  
`table.getHeaderRowRange()` — header row only — 1.1 ✅  
`table.rows.add(index?, values?)` — append rows — 1.1 ✅  
`table.columns` — column collection — 1.1 ✅  
`table.columns.items[i].name` — column header string — 1.1 ✅  
`range.getIntersectionOrNullObject(otherRange)` — detect overlap — **1.4** ✅ (in 1.13 baseline)  
`table.showTotals` — 1.1 ✅  
`worksheet.tables.getItemAt(i)` — 1.1 ✅  

**Answer to spec question 6: Table detection requires no API bump. Everything needed is ExcelApi 1.1–1.4, well within the 1.13 baseline. manifest.xml stays at `MinVersion="1.13"`.**

---

## What Sprint 7 Delivers

When the user selects a range inside an Excel Table (ListObject), FAIT:

1. **Detects** the Table automatically during context read — no user action required
2. **Enriches** `SpreadsheetContext` with Table metadata: name, column names (authoritative), row count, bound address
3. **Upgrades** `formatContext()` to emit structured Table context — FAIT gets semantically named columns instead of heuristic header detection
4. **Updates** `ContextIndicator` to show "📋 Table: SalesData (10 rows)" when a Table is detected
5. **Adds** `writeToTable()` to `excelWriter.ts` — appends rows to a named Table cleanly, respecting Table bounds
6. **Guards** `writeRangeData()` — detects if the target cell is inside a Table and warns; the actual write proceeds, but the user sees a warning

---

## Design Decisions

### Decision 1: Where does Table detection happen?

**Option A — in `getSelectedRange()` (excelReader.ts):** Detect the Table during the range read pass. One `Excel.run()` for everything.

**Option B — separate `getTableContext()` function:** Separate async function called after `getSelectedRange()`.

**Decision: Option A — detect in `getSelectedRange()`.**

Rationale: The Table metadata (column names, table name) comes from the same worksheet context as the range values. Combining them into one `Excel.run()` pass is cleaner, cheaper (one round-trip), and avoids a second `await Excel.run()` call. The `SpreadsheetContext` interface gets an optional `tableInfo` field — callers that don't need it ignore it.

### Decision 2: How to detect if a range is inside a Table

The Excel JS API doesn't have a direct "what table contains this range?" query. The approach:

1. Load `worksheet.tables` (iterable collection)
2. For each table, load `table.getRange()` address
3. Check if the selected range intersects / is contained by a table range

Efficient pattern using `getIntersectionOrNullObject()` (ExcelApi 1.4):

```typescript
for (let i = 0; i < sheet.tables.count; i++) {
  const table = sheet.tables.getItemAt(i);
  const tableRange = table.getRange();
  const intersection = selectedRange.getIntersectionOrNullObject(tableRange);
  intersection.load('isNullObject');
  // load table metadata too
  table.load('name');
  tableRange.load('address');
}
await ctx.sync();
// After sync: if intersection.isNullObject === false → selection is inside this table
```

This is safe even with 0 tables on the sheet (the loop simply doesn't execute).

### Decision 3: What to do in `formatContext()` when Table is detected

**Replace heuristic header detection with authoritative Table column names.**

Before (heuristic):
```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!A1:D11 | 11 rows × 4 cols

Headers: | Region | Q1 | Q2 | Q3 |
Row 1: | North | 12 | 15 | 18 |
```

After (Table-aware):
```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!A1:D11 | 11 rows × 4 cols
Excel Table: SalesData | 10 data rows × 4 columns
Table columns: Region, Q1, Q2, Q3

Row 1: | North | 12 | 15 | 18 |
Row 2: | South | 8 | 10 | 11 |
```

Key differences:
- `Excel Table: SalesData | 10 data rows × 4 columns` — FAIT knows this is a named, structured object
- `Table columns: Region, Q1, Q2, Q3` — authoritative column names (from the Table's column definitions, not guessed from cell values)
- Data rows don't repeat the header row (header is already captured by `Table columns:`)
- `Row 1:` numbering still present — FAIT can reference `Row 3` in its response

### Decision 4: `writeToTable()` vs `writeRangeData()` for Table targets

Two distinct use cases:

**Use case A — write back into an existing Table (structured append):**  
User has a Table called `SalesData`. FAIT generates new rows. User wants to append them. Use `writeToTable(tableName, rows)` — appends via `table.rows.add()`. This is clean, Table-aware, and won't corrupt the structure.

**Use case B — write to a cell that happens to be inside a Table:**  
The Sprint 6 "Write to Sheet ↓" flow. User clicks the target-cell prompt and types "B3" — which happens to be inside a Table. `writeRangeData("B3", data)` would work but could overwrite existing Table data or write past bounds. Sprint 7 adds a detection guard: if the target is inside a Table, warn the user before writing.

**Decision: Implement both. `writeToTable()` for explicit Table appends; guard in `writeRangeData()` that surfaces a warning (but does not block the write).**

The guard doesn't throw — it returns a `warning` field in the result. ChatPanel surfaces it in the success/error panel.

### Decision 5: `writeToTable()` scope for Sprint 7

The `writeToTable()` function appends rows to a named Table. It does NOT:
- Create a new Table from a plain range (deferred — complex, medium effort)
- Insert rows at a specific position (deferred — low demand for now)
- Update cells within existing Table rows (use `writeRangeData()` with a cell address for that)

Sprint 7 `writeToTable()` signature:
```typescript
writeToTable(tableName: string, rows: (string | number | boolean | null)[][]): Promise<{ rowsAdded: number; tableAddress: string }>
```

The ChatPanel flow for `writeToTable()` is triggered when the user types a Table name (e.g., `SalesData` or `Sheet1!SalesData`) instead of a cell address in the target cell prompt. Detection: if input contains no `$` and no digit at any position after letter chars (i.e., doesn't look like `A1`, `B3`, `Sheet1!A1`), treat as a Table name.

---

## Data Model Changes

### `SpreadsheetContext` — new optional field

```typescript
// excelReader.ts
export interface TableInfo {
  name: string;            // e.g. "SalesData"
  columnNames: string[];   // e.g. ["Region", "Q1", "Q2", "Q3"]
  dataRowCount: number;    // number of data rows (excluding header)
  boundAddress: string;    // full table range, e.g. "Sheet1!A1:D11"
}

export interface SpreadsheetContext {
  address: string;
  rows: number;
  cols: number;
  values: unknown[][];
  formulas: string[][];
  tableInfo?: TableInfo | null;   // ← NEW (undefined = no detection attempted; null = no table found)
}
```

### `WriteRangeError` — new code

```typescript
// excelWriter.ts — extend existing WriteRangeError codes
type WriteRangeCode = 'EMPTY_DATA' | 'DIMENSION_MISMATCH' | 'EXCEL_ERROR';
// No new error code — table detection uses a warning return, not an exception
```

### `writeRangeData` return type — add `warning`

```typescript
// excelWriter.ts — extend writeRangeData return type
return Promise<{
  address: string;
  rows: number;
  cols: number;
  warning?: string;   // ← NEW: set if write target is inside a Table
}>
```

### New `WriteTableError` class

```typescript
// excelWriter.ts
export class WriteTableError extends Error {
  constructor(
    message: string,
    public readonly code: 'TABLE_NOT_FOUND' | 'EMPTY_ROWS' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'WriteTableError';
  }
}
```

### `selectionInfo` in `ChatPanel` — add `tableName`

```typescript
// ChatPanel.tsx — selectionInfo state
const [selectionInfo, setSelectionInfo] = useState<{
  address: string;
  rows: number;
  cols: number;
  tableName?: string | null;   // ← NEW
} | null>(null);
```

---

## Parallelization Map

```
Single sequential CC session — all changes in fait-for-excel/src/ only.
No shared files between tasks. 6 files total.

  Task 1: excelReader.ts         — add TableInfo interface + table detection in getSelectedRange()
                                    (one Excel.run(), load values+formulas+tableInfo together)

  Task 2: contextFormatter.ts    — update formatContext() to emit Table-aware context
                                    (reads tableInfo from SpreadsheetContext)

  Task 3: excelWriter.ts         — add writeToTable(); add table-presence warning in writeRangeData()

  Task 4: ChatPanel.tsx          — propagate tableName into selectionInfo; update handleWriteTableConfirm
                                    to route table-name inputs to writeToTable(); surface writeRangeData warning

  Task 5: ContextIndicator.tsx   — accept + display tableName prop ("📋 Table: SalesData")

  Task 6: useExcelContext.ts     — (if used independently) propagate tableInfo from getSelectedRange()
                                    Note: ChatPanel.tsx calls getSelectedRange() directly, not via useExcelContext.
                                    This task updates the standalone hook for parity. Low risk, small change.
```

---

## File-Level Spec

### Task 1: `src/taskpane/services/excelReader.ts`

**Add `TableInfo` interface** (before `SpreadsheetContext`):

```typescript
export interface TableInfo {
  name: string;
  columnNames: string[];
  dataRowCount: number;
  boundAddress: string;
}
```

**Add `tableInfo?: TableInfo | null` to `SpreadsheetContext`:**

```typescript
export interface SpreadsheetContext {
  address: string;
  rows: number;
  cols: number;
  values: unknown[][];
  formulas: string[][];
  tableInfo?: TableInfo | null;   // ← add this field
}
```

**Update `getSelectedRange()` to detect Tables in a single `Excel.run()`:**

Replace the current `getSelectedRange()` implementation with:

```typescript
export async function getSelectedRange(): Promise<SpreadsheetContext> {
  return Excel.run(async (ctx: any) => {
    const range = ctx.workbook.getSelectedRange();
    range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);

    const sheet = range.worksheet;
    sheet.load('name');

    // Pre-load table collection count
    const tables = sheet.tables;
    tables.load('count');

    await ctx.sync();

    const baseContext: SpreadsheetContext = {
      address: range.address as string,
      rows: range.rowCount as number,
      cols: range.columnCount as number,
      values: range.values as unknown[][],
      formulas: range.formulas as string[][],
      tableInfo: null,
    };

    // No tables on this sheet — skip detection
    if ((tables.count as number) === 0) {
      return baseContext;
    }

    // Check each table for intersection with the selected range
    // ExcelApi 1.4: getIntersectionOrNullObject is safe — returns isNullObject=true if no overlap
    const tableCount = tables.count as number;
    const tableItems: any[] = [];
    const intersections: any[] = [];
    const tableRanges: any[] = [];

    for (let i = 0; i < tableCount; i++) {
      const table = tables.getItemAt(i);
      table.load(['name']);

      const tRange = table.getRange();
      tRange.load(['address']);

      const headerRange = table.getHeaderRowRange();
      headerRange.load(['values']);

      const dataRange = table.getDataBodyRange();
      dataRange.load(['rowCount']);

      const intersection = range.getIntersectionOrNullObject(tRange);
      intersection.load(['isNullObject']);

      tableItems.push({ table, headerRange, dataRange });
      intersections.push(intersection);
      tableRanges.push(tRange);
    }

    await ctx.sync();

    // Find the first table that intersects the selected range
    for (let i = 0; i < tableCount; i++) {
      const intersection = intersections[i];
      if (intersection.isNullObject) continue;

      const { table, headerRange, dataRange } = tableItems[i];
      const columnNames = (headerRange.values[0] as string[]).map(String);

      baseContext.tableInfo = {
        name: table.name as string,
        columnNames,
        dataRowCount: dataRange.rowCount as number,
        boundAddress: tableRanges[i].address as string,
      };
      break; // Use the first matching table
    }

    return baseContext;
  });
}
```

**Do NOT change `getFullWorksheet()` or `getSelectionState()`.**

**Important:** `getSelectionState()` calls `getSelectedRange()` internally — it will automatically get table detection for free. No separate change needed for it.

---

### Task 2: `src/taskpane/services/contextFormatter.ts`

**Update `formatContext()` to use Table metadata when present:**

```typescript
import type { SpreadsheetContext } from './excelReader';

export function formatContext(ctx: SpreadsheetContext): string {
  const sanitize = (v: unknown): string =>
    String(v)
      .replace(/[\n\r]/g, ' ')
      .replace(/\|/g, '\\|');

  let out = `[SPREADSHEET CONTEXT]\nSheet range: ${ctx.address} | ${ctx.rows} rows × ${ctx.cols} cols\n`;

  if (ctx.tableInfo) {
    // Table-aware path: authoritative column names from the Table definition
    out += `Excel Table: ${ctx.tableInfo.name} | ${ctx.tableInfo.dataRowCount} data rows × ${ctx.tableInfo.columnNames.length} columns\n`;
    out += `Table columns: ${ctx.tableInfo.columnNames.map(sanitize).join(', ')}\n`;
    out += '\n';

    // Emit data rows (skip the header row if it's included in the selection — first row of values
    // matches the column names, meaning the user selected starting at the header row)
    const firstRow = ctx.values[0] ?? [];
    const firstRowMatchesHeaders =
      firstRow.length === ctx.tableInfo.columnNames.length &&
      firstRow.every((v, i) => String(v).trim() === ctx.tableInfo!.columnNames[i].trim());

    const dataRows = firstRowMatchesHeaders ? ctx.values.slice(1) : ctx.values;
    const fmlRows = firstRowMatchesHeaders ? ctx.formulas.slice(1) : ctx.formulas;
    const rowOffset = firstRowMatchesHeaders ? 2 : 1;

    dataRows.forEach((row, ri) => {
      out += `Row ${ri + 1}: | ${row.map(sanitize).join(' | ')} |\n`;

      const fmlRow = fmlRows[ri] ?? [];
      const fmlStr = fmlRow
        .map((f: string, ci: number) =>
          f.startsWith('=') ? `${getCellAddr(ri + rowOffset, ci)}=${f}` : ''
        )
        .filter(Boolean)
        .join(', ');
      if (fmlStr) out += `Formulas: ${fmlStr}\n`;
    });
  } else {
    // Non-table path: existing heuristic header detection (unchanged)
    out += '\n';

    const row0 = ctx.values[0] ?? [];
    const isHeader =
      row0.length > 0 &&
      row0.every((v) => typeof v === 'string' && v.trim() !== '' && isNaN(Number(v)));

    if (isHeader) {
      out += `Headers: | ${row0.map(sanitize).join(' | ')} |\n`;
    }

    const dataRows = isHeader ? ctx.values.slice(1) : ctx.values;
    const fmlRows = isHeader ? ctx.formulas.slice(1) : ctx.formulas;

    dataRows.forEach((row, ri) => {
      out += `Row ${ri + 1}: | ${row.map(sanitize).join(' | ')} |\n`;

      const fmlRow = fmlRows[ri] ?? [];
      const fmlStr = fmlRow
        .map((f: string, ci: number) =>
          f.startsWith('=') ? `${getCellAddr(ri + (isHeader ? 2 : 1), ci)}=${f}` : ''
        )
        .filter(Boolean)
        .join(', ');
      if (fmlStr) out += `Formulas: ${fmlStr}\n`;
    });
  }

  // Token cap: ~6,000 chars
  if (out.length > 6000) {
    out = out.slice(0, 5900) + '\n[... truncated for brevity]\n';
  }

  out += '[END SPREADSHEET CONTEXT]';
  return out;
}
```

**`getCellAddr()` is unchanged.** Copy it verbatim — do not alter it.

**Key point:** The non-table branch is the existing code, copied unchanged. The Table branch is new code that runs only when `ctx.tableInfo` is non-null. This ensures no regression for users who don't use Tables.

---

### Task 3: `src/taskpane/services/excelWriter.ts`

Two additions: `writeToTable()` and a table-presence warning in `writeRangeData()`.

**Add `WriteTableError` class:**

```typescript
export class WriteTableError extends Error {
  constructor(
    message: string,
    public readonly code: 'TABLE_NOT_FOUND' | 'EMPTY_ROWS' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'WriteTableError';
  }
}
```

**Update `writeRangeData()` return type to include optional `warning`:**

```typescript
// Change the return type signature:
export async function writeRangeData(
  targetCell: string,
  data: (string | number | boolean | null)[][]
): Promise<{ address: string; rows: number; cols: number; warning?: string }> {
```

Inside the `Excel.run()` block, after setting `writeRange.values = data` and loading `address`, add table detection:

```typescript
// After: writeRange.values = data;
// After: writeRange.load('address');
// ADD: detect table presence at target
const sheet = ctx.workbook.worksheets.getActiveWorksheet();
const tables = sheet.tables;
tables.load('count');

await ctx.sync();

let warning: string | undefined;

if ((tables.count as number) > 0) {
  const tableCount = tables.count as number;
  const intersections: any[] = [];
  const tableNames: string[] = [];

  for (let i = 0; i < tableCount; i++) {
    const table = tables.getItemAt(i);
    table.load('name');
    const tRange = table.getRange();
    const intersection = writeRange.getIntersectionOrNullObject(tRange);
    intersection.load('isNullObject');
    intersections.push(intersection);
    tableNames.push(''); // placeholder
  }

  await ctx.sync();

  // Reload table names after sync
  for (let i = 0; i < tableCount; i++) {
    const table = tables.getItemAt(i);
    table.load('name');
  }
  await ctx.sync();

  for (let i = 0; i < tableCount; i++) {
    if (!intersections[i].isNullObject) {
      warning = `Written inside Table — use writeToTable() for structured Table appends`;
      break;
    }
  }
}

return {
  address: writeRange.address as string,
  rows,
  cols,
  warning,
};
```

**WAIT — the above pattern is too convoluted (two syncs for names). Simplify:**

Load table names in the first pass alongside the intersection check:

```typescript
// Revised pattern inside writeRangeData — after writeRange.values = data:
const sheet = ctx.workbook.worksheets.getActiveWorksheet();
const tables = sheet.tables;
tables.load('count');
// We need this sync to know the count before iterating
writeRange.load('address');
await ctx.sync();

let warning: string | undefined;

if ((tables.count as number) > 0) {
  const tableCount = tables.count as number;
  const checkItems: { intersection: any; table: any }[] = [];

  for (let i = 0; i < tableCount; i++) {
    const table = tables.getItemAt(i);
    table.load('name');
    const tRange = table.getRange();
    const intersection = writeRange.getIntersectionOrNullObject(tRange);
    intersection.load('isNullObject');
    checkItems.push({ intersection, table });
  }

  await ctx.sync();

  for (const { intersection, table } of checkItems) {
    if (!intersection.isNullObject) {
      warning = `Target overlaps Table "${table.name as string}" — consider writeToTable() for clean row appends`;
      break;
    }
  }
}

return {
  address: writeRange.address as string,
  rows,
  cols,
  warning,
};
```

**Note on `writeRangeData` structure:** The current code ends with:
```typescript
return {
  address: writeRange.address as string,
  rows,
  cols,
};
```
This is inside the `Excel.run()` callback, which ends before the `.catch()` block. Tony must keep the `.catch()` block intact — it wraps the entire `Excel.run()`. Only the return value inside `Excel.run()` changes.

**Add `writeToTable()` function:**

```typescript
/**
 * Append rows to a named Excel Table (ListObject) on the active worksheet.
 *
 * @param tableName  Name of the Excel Table (e.g. "SalesData"). Case-sensitive.
 * @param rows       2D array of values to append. Each inner array is one row.
 *                   Column count must match the Table's column count.
 * @throws WriteTableError with .code "TABLE_NOT_FOUND" | "EMPTY_ROWS" | "EXCEL_ERROR"
 */
export async function writeToTable(
  tableName: string,
  rows: (string | number | boolean | null)[][]
): Promise<{ rowsAdded: number; tableAddress: string }> {
  if (!rows || rows.length === 0) {
    throw new WriteTableError('No rows to append', 'EMPTY_ROWS');
  }

  return Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    // getItemOrNullObject returns a proxy — load isNullObject to check existence
    const table = sheet.tables.getItemOrNullObject(tableName);
    table.load('isNullObject');
    await ctx.sync();

    if (table.isNullObject) {
      throw new WriteTableError(`Table "${tableName}" not found on active worksheet`, 'TABLE_NOT_FOUND');
    }

    // table.rows.add(index, values):
    //   index = -1 means append at the end
    //   values = 2D array
    table.rows.add(-1, rows);

    // Reload the table range address for the return value
    const tRange = table.getRange();
    tRange.load('address');
    await ctx.sync();

    return {
      rowsAdded: rows.length,
      tableAddress: tRange.address as string,
    };
  }).catch((e: any) => {
    if (e instanceof WriteTableError) throw e;
    throw new WriteTableError(
      e?.message ?? 'Excel table write failed',
      'EXCEL_ERROR'
    );
  });
}
```

**Do NOT change `applySuggestions()`, `applySingleSuggestion()`, or `WriteRangeError`.**

---

### Task 4: `src/taskpane/components/ChatPanel.tsx`

Four targeted changes. Do not restructure anything.

**Change 1: Update `selectionInfo` state type to include `tableName`:**

```typescript
const [selectionInfo, setSelectionInfo] = useState<{
  address: string;
  rows: number;
  cols: number;
  tableName?: string | null;
} | null>(null);
```

**Change 2: Update the selection polling `useEffect` to propagate `tableInfo.name`:**

```typescript
// In the setInterval callback inside the selection polling useEffect:
const ctx = await getSelectedRange();
setSelectionInfo({
  address: ctx.address,
  rows: ctx.rows,
  cols: ctx.cols,
  tableName: ctx.tableInfo?.name ?? null,   // ← add this
});
```

There are two places that call `setSelectionInfo` — one in the `useEffect` polling, one in `handleSend()`. Update both.

In `handleSend()`:
```typescript
// BEFORE
setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });

// AFTER
setSelectionInfo({
  address: ctx.address,
  rows: ctx.rows,
  cols: ctx.cols,
  tableName: ctx.tableInfo?.name ?? null,
});
```

**Change 3: Route table-name inputs to `writeToTable()` in `handleWriteTableConfirm`:**

Add import at the top of the file:
```typescript
import { writeRangeData, WriteRangeError, writeToTable, WriteTableError } from '../services/excelWriter';
```

Update `handleWriteTableConfirm()`:

```typescript
const handleWriteTableConfirm = async () => {
  if (!pendingTableData) return;
  const target = writeTableTarget.trim() || 'A1';

  setWriteTableLoading(true);
  setWriteTableError(null);
  setWriteTableSuccess(null);

  // Determine write mode: Table name vs cell address
  // A Table name: no digits in the name, or it looks like "SalesData" / "Sheet1!SalesData"
  // A cell address: ends with digits, e.g. "A1", "Sheet1!B3", "$A$1"
  const isTableTarget = /^[A-Za-z!_][A-Za-z0-9_!]*$/.test(target) && !/[0-9]$/.test(target);

  if (isTableTarget) {
    // Write to named Table — append rows only (no headers row)
    try {
      const result = await writeToTable(target, pendingTableData.rows);
      setWriteTableSuccess(
        `Appended ${result.rowsAdded} rows to Table "${target}" (${result.tableAddress})`
      );
      setPendingTableData(null);
    } catch (e) {
      if (e instanceof WriteTableError) {
        if (e.code === 'TABLE_NOT_FOUND') {
          setWriteTableError(`Table "${target}" not found on active worksheet. Use a cell address (e.g. A1) to write as a new range.`);
        } else if (e.code === 'EMPTY_ROWS') {
          setWriteTableError('No rows to append.');
        } else {
          setWriteTableError('Table write failed — Excel error.');
        }
      } else {
        setWriteTableError('Write failed.');
      }
    } finally {
      setWriteTableLoading(false);
    }
  } else {
    // Write to cell address — include headers row
    const data: (string | number | boolean | null)[][] = [
      pendingTableData.headers,
      ...pendingTableData.rows,
    ];

    try {
      const result = await writeRangeData(target, data);
      let successMsg = `Written to ${result.address} (${result.rows} rows × ${result.cols} cols)`;
      if (result.warning) {
        successMsg += ` ⚠️ ${result.warning}`;
      }
      setWriteTableSuccess(successMsg);
      setPendingTableData(null);
    } catch (e) {
      if (e instanceof WriteRangeError) {
        if (e.code === 'EMPTY_DATA') {
          setWriteTableError('No data to write.');
        } else if (e.code === 'DIMENSION_MISMATCH') {
          setWriteTableError('Rows have inconsistent column counts — cannot write.');
        } else {
          setWriteTableError('Write failed — check the target cell address and try again.');
        }
      } else {
        setWriteTableError('Write failed — check the target cell address and try again.');
      }
    } finally {
      setWriteTableLoading(false);
    }
  }
};
```

**Change 4: Update the target cell prompt label to hint at Table names:**

```typescript
// In the inline prompt panel, change the label:
// BEFORE
<div style={{ fontSize: '11px', color: '#8899aa', marginBottom: '4px' }}>
  Writing {pendingTableData.rows.length + 1} rows ×{' '}
  {pendingTableData.headers.length} cols — top-left cell:
</div>

// AFTER
<div style={{ fontSize: '11px', color: '#8899aa', marginBottom: '4px' }}>
  Writing {pendingTableData.rows.length} rows × {pendingTableData.headers.length} cols
  — cell address or Table name:
</div>
```

And update the placeholder text on the input:
```typescript
// BEFORE
placeholder="e.g. A1 or Sheet1!B3"

// AFTER
placeholder="e.g. A1, Sheet1!B3, or SalesData"
```

**Do NOT change** any other handlers or state in `ChatPanel.tsx`.

---

### Task 5: `src/taskpane/components/ContextIndicator.tsx`

Add `tableName` prop and show Table badge when present.

```typescript
import React from 'react';

interface ContextIndicatorProps {
  address: string | null;
  rows: number;
  cols: number;
  visible: boolean;
  tableName?: string | null;   // ← NEW
}

const ContextIndicator: React.FC<ContextIndicatorProps> = ({
  address,
  rows,
  cols,
  visible,
  tableName,
}) => {
  if (!visible) return null;

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

  // Table detection: show Table badge instead of plain address
  if (tableName) {
    return (
      <div
        title={`Excel Table "${tableName}" detected — ${address} (${rows}×${cols})`}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          padding: '2px 8px',
          background: '#1a3020',
          border: '1px solid #2e5040',
          borderRadius: '12px',
          fontSize: '11px',
          color: '#6fcf97',
          whiteSpace: 'nowrap',
          maxWidth: '100%',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        <span>📋</span>
        <span>Table: {tableName} ({rows}×{cols})</span>
      </div>
    );
  }

  // Plain range
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

**Update the `<ContextIndicator>` render in `ChatPanel.tsx`** to pass `tableName`:

```typescript
// BEFORE
<ContextIndicator
  address={selectionInfo?.address ?? null}
  rows={selectionInfo?.rows ?? 0}
  cols={selectionInfo?.cols ?? 0}
  visible={includeSelection}
/>

// AFTER
<ContextIndicator
  address={selectionInfo?.address ?? null}
  rows={selectionInfo?.rows ?? 0}
  cols={selectionInfo?.cols ?? 0}
  visible={includeSelection}
  tableName={selectionInfo?.tableName ?? null}
/>
```

---

### Task 6: `src/taskpane/hooks/useExcelContext.ts`

Minor parity update — propagate `tableInfo.name` from `getSelectedRange()`.

```typescript
// Update the selectionInfo state type:
const [selectionInfo, setSelectionInfo] = useState<{
  address: string;
  rows: number;
  cols: number;
  tableName?: string | null;
} | null>(null);

// In the setInterval callback:
const info = await getSelectedRange();
setSelectionInfo({
  address: info.address,
  rows: info.rows,
  cols: info.cols,
  tableName: info.tableInfo?.name ?? null,
});

// Update readSelection return type if TypeScript requires it:
// (SpreadsheetContext already includes tableInfo — no additional change needed)
```

**Note:** `ChatPanel.tsx` calls `getSelectedRange()` directly and manages its own `selectionInfo` state. This hook is a thin wrapper used independently in some contexts. Keep both in sync.

---

## Files Changed Summary

| File | Change type | Description |
|------|-------------|-------------|
| `src/taskpane/services/excelReader.ts` | Modify | Add `TableInfo` + `tableInfo` field; update `getSelectedRange()` to detect Tables |
| `src/taskpane/services/contextFormatter.ts` | Modify | Table-aware formatting path; non-table path unchanged |
| `src/taskpane/services/excelWriter.ts` | Modify | Add `writeToTable()` + `WriteTableError`; add table-presence `warning` in `writeRangeData()` |
| `src/taskpane/components/ChatPanel.tsx` | Modify | `tableName` in `selectionInfo`; route Table-name inputs to `writeToTable()`; surface warning |
| `src/taskpane/components/ContextIndicator.tsx` | Modify | Add `tableName` prop; Table badge UI |
| `src/taskpane/hooks/useExcelContext.ts` | Modify | Propagate `tableName` from `tableInfo` |

**No new files. No new npm packages. 6 files.**

---

## Prompt Context: Before vs After

### Before (no Table awareness)

User selects `A1:D11` where row 1 = headers, rows 2–11 = data in a Table called `SalesData`:

```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!A1:D11 | 11 rows × 4 cols

Headers: | Region | Q1 | Q2 | Q3 |
Row 1: | North | 12 | 15 | 18 |
Row 2: | South | 8 | 10 | 11 |
Row 3: | East | 20 | 22 | 25 |
...
[END SPREADSHEET CONTEXT]
```

FAIT sees: 11 rows of data with an assumed header row. Doesn't know this is a named structured object.

### After (Table-aware)

Same selection, same data:

```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!A1:D11 | 11 rows × 4 cols
Excel Table: SalesData | 10 data rows × 4 columns
Table columns: Region, Q1, Q2, Q3

Row 1: | North | 12 | 15 | 18 |
Row 2: | South | 8 | 10 | 11 |
Row 3: | East | 20 | 22 | 25 |
...
[END SPREADSHEET CONTEXT]
```

FAIT now knows:
- This is a named structured object called `SalesData`
- The authoritative column names are `Region, Q1, Q2, Q3` (from the Table's definition, not guessed from cells)
- There are exactly 10 data rows (not 11 rows with headers mixed in)
- It can reference the Table by name: "To append to SalesData, use `writeToTable('SalesData', rows)`"

### Edge case: User selects only a subset of the Table

User selects `B3:C7` — 5 rows, 2 columns, entirely inside `SalesData`. The Table detection still fires (intersection check passes). `tableInfo` is populated with the Table's full metadata. `formatContext()` emits the Table header for orientation but only shows the values in the selected range:

```
[SPREADSHEET CONTEXT]
Sheet range: Sheet1!B3:C7 | 5 rows × 2 cols
Excel Table: SalesData | 10 data rows × 4 columns
Table columns: Region, Q1, Q2, Q3

Row 1: | 12 | 15 |
Row 2: | 8 | 10 |
...
[END SPREADSHEET CONTEXT]
```

FAIT knows the full column schema even when only a partial slice is selected. This is a significant improvement — FAIT can now answer "what are the other columns in this Table?" without the user reselecting.

---

## ExcelApi Requirement Analysis

| API | Min version | Used in Sprint 7 |
|-----|-------------|-----------------|
| `worksheet.tables` | 1.1 | ✅ Table collection access |
| `table.name` | 1.1 | ✅ Table name |
| `table.getRange()` | 1.1 | ✅ Bound address detection |
| `table.getHeaderRowRange()` | 1.1 | ✅ Authoritative column names |
| `table.getDataBodyRange()` | 1.1 | ✅ Data row count |
| `table.rows.add(-1, values)` | 1.1 | ✅ Append rows |
| `table.load('count')` via collection | 1.1 | ✅ Table count on sheet |
| `range.getIntersectionOrNullObject()` | **1.4** | ✅ Detect range-table overlap |
| `table.tables.getItemOrNullObject()` | **1.4** | ✅ Find table by name safely |

**All APIs ≤ ExcelApi 1.4. Baseline is 1.13. No manifest change required.**

---

## UX Flow — Exact Sequences

### Flow A: User selects cells inside a Table

```
1. User clicks anywhere inside the "SalesData" Table (e.g. cell B5)
2. Selection polling fires (2s interval)
   → getSelectedRange() detects intersection with SalesData
   → selectionInfo.tableName = "SalesData"
3. ContextIndicator updates: "📋 Table: SalesData (1×1)"
4. User sends "What's the trend in Q1 column?"
5. handleSend() calls getSelectedRange() again (fresh read)
   → tableInfo populated
   → formatContext() emits Table-aware context
6. FAIT receives: "Excel Table: SalesData | 10 data rows × 4 columns\nTable columns: Region, Q1, Q2, Q3\n..."
7. FAIT answers with awareness of the Table structure and named columns
```

### Flow B: User writes to an existing Table (Sprint 6 + Sprint 7 combined)

```
1. FAIT responds with a table of data matching SalesData's schema
2. User clicks "↓ Write to Sheet"
3. Target cell input pre-fills with current selection top-left (e.g. "Sheet1!A1")
4. User CLEARS the input and types "SalesData"
5. handleWriteTableConfirm() detects "SalesData" is not a cell address
   → calls writeToTable("SalesData", pendingTableData.rows)
   → NOTE: headers are NOT included — Table already has its own header row
6. Success: "Appended 3 rows to Table 'SalesData' (Sheet1!A1:D14)"
```

### Flow C: User writes to a cell that happens to be inside a Table (S6 unchanged, S7 adds warning)

```
1. User types "B3" in the target cell input
2. handleWriteTableConfirm() — "B3" looks like a cell address
   → calls writeRangeData("B3", [headers, ...rows])
3. writeRangeData detects B3 is inside "SalesData" Table
   → returns { address: "Sheet1!B3:E6", rows: 4, cols: 4, warning: "Target overlaps Table \"SalesData\"..." }
4. ChatPanel shows: "✓ Written to Sheet1!B3:E6 (4 rows × 4 cols) ⚠️ Target overlaps Table..."
5. Write succeeded — data is in the sheet. Warning is advisory only.
```

---

## Acceptance Criteria

1. **Table detection:** Selecting any cell inside an Excel Table causes `getSelectedRange()` to return a `SpreadsheetContext` with `tableInfo` populated (name, columnNames, dataRowCount, boundAddress)
2. **Context injection:** When `tableInfo` is non-null, `formatContext()` emits `Excel Table: <name>` and `Table columns: <col1>, <col2>, ...` header lines
3. **Heuristic path unchanged:** When `tableInfo` is null (plain range), `formatContext()` behaves exactly as before — no regression for non-Table users
4. **ContextIndicator:** Shows green `📋 Table: SalesData (10×4)` badge when Table detected; falls back to existing gold badge for plain ranges
5. **`writeToTable()`:** Appends rows to a named Table; throws `WriteTableError` with `TABLE_NOT_FOUND` if table doesn't exist on active sheet
6. **`writeRangeData()` warning:** Returns `warning` field if write target overlaps a Table; write still succeeds
7. **Target input routing:** Input "SalesData" routes to `writeToTable()`; input "A1" or "Sheet1!B3" routes to `writeRangeData()`
8. **Table-name error UX:** If `writeToTable()` throws `TABLE_NOT_FOUND`, user sees actionable error with fallback suggestion to use a cell address
9. **No ExcelApi version bump:** manifest.xml stays at `MinVersion="1.13"`
10. **No regression:** All Sprint 1–6 features work identically for users not using Excel Tables

---

## Constraints for CC

- Touch only the 6 files listed above
- Do NOT rewrite `getSelectedRange()` from scratch — update it surgically: add the Table detection block after the initial `await ctx.sync()` that loads range values
- Do NOT change `applySuggestions()`, `applySingleSuggestion()`, `WriteRangeError`, or any existing function signature (only `writeRangeData` return type gains an optional `warning` field — this is backward-compatible)
- `writeToTable()` appends rows only — do NOT add insert/update/delete row logic in this sprint
- `TableInfo` and `WriteTableError` must be exported from their respective files (other files import them)
- The non-table path in `contextFormatter.ts` must be the exact existing code — do not optimize or alter it
- Do NOT call `getSelectedRange()` twice in a single handler — table info is already on the returned `SpreadsheetContext`, use it directly
- Headers row must NOT be included when calling `writeToTable()` — `table.rows.add()` inserts data rows only; the Table manages its own header

---

## Clint Review Priorities

```
⚠️  HIGH: Verify getIntersectionOrNullObject().isNullObject is checked AFTER ctx.sync().
          Accessing isNullObject before sync produces wrong results. This is the most
          common ExcelJS bug pattern. Check every sync boundary in the new Table detection code.

⚠️  HIGH: Confirm writeToTable() does NOT include headers in the rows parameter.
          table.rows.add(-1, rows) where rows = pendingTableData.rows (not [headers, ...rows]).
          The ChatPanel routing code sets this — verify the isTableTarget branch passes
          pendingTableData.rows, not the full [headers, ...rows] array.

⚠️  HIGH: Verify getSelectedRange() doesn't hang when tables collection is empty.
          The guard: if (tables.count === 0) return baseContext; must be AFTER the
          first ctx.sync() that loads tables.count. If count is checked before sync,
          it's always 0 — silent bug where Table detection never fires.

⚠️  MEDIUM: Confirm table.tables.getItemOrNullObject() is used in writeToTable(),
            not table.tables.getItem() which throws on missing table (hard to catch).

⚠️  MEDIUM: Table name routing regex in handleWriteTableConfirm — verify edge cases:
            "Sheet1!SalesData" (cross-sheet table name) — does the regex handle it?
            "A1" — correctly identified as cell address (has trailing digit)?
            "SalesData2023" — contains digit but not trailing → treated as Table name?
            Review the regex: /^[A-Za-z!_][A-Za-z0-9_!]*$/.test(target) && !/[0-9]$/.test(target)
            "SalesData2023" would fail !/[0-9]$/ → treated as CELL ADDRESS → wrong.
            Fix: Use a stricter cell-address pattern instead: /^[A-Z$][A-Z$0-9]*\d+$/i.test(target).
            If it matches cell address pattern → cell address. Otherwise → Table name.

⚠️  LOW: Confirm ContextIndicator green badge color (#1a3020 background, #6fcf97 text)
         matches the existing dark theme palette. Check against other success states in the app.

⚠️  LOW: Confirm the success message for writeToTable() displays the table address correctly.
         result.tableAddress is loaded from tRange.address after sync — verify it's not
         undefined (it would be if the load/sync order is wrong).
```

---

## Architectural Note: Why Table-Name Routing Belongs in ChatPanel

The decision of whether to call `writeToTable()` vs `writeRangeData()` is a UX routing decision — not a service-layer concern. The services are pure operations: `writeRangeData` writes to a range, `writeToTable` appends to a Table. The ChatPanel determines which to call based on what the user typed.

This keeps the services clean and testable in isolation. `writeRangeData("SalesData", data)` would fail with an `EXCEL_ERROR` because "SalesData" is not a valid range address — but we never let it get that far. The routing happens in `handleWriteTableConfirm()` before any Excel API call is made.

---

_Spec by Reed Richards | Sprint 7 is 6 files. The core insight: Excel Table column names are authoritative metadata that dramatically improves FAIT's contextual understanding. The write-back Table routing is a bonus that completes the read/write loop for structured data._
