import type { CellSuggestion } from '../components/WriteSuggestionsDialog';
import { isFaitWriting } from './watchMode';

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
): Promise<{ address: string; rows: number; cols: number; warning?: string }> {
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
    // Sprint 9: Defense-in-depth loop prevention when watch mode is active
    if (isFaitWriting()) {
      ctx.runtime.enableEvents = false;
    }

    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    // Resize the range from the target cell to fit the data exactly
    const startRange = sheet.getRange(targetCell);
    const writeRange = startRange.getResizedRange(rows - 1, cols - 1);

    writeRange.values = data;

    // Load the final address so we can return it
    writeRange.load('address');

    // Detect if write target overlaps a Table (advisory warning — write still proceeds)
    const tables = sheet.tables;
    tables.load('count');

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
  }).catch((e: any) => {
    if (e instanceof WriteRangeError) throw e;
    throw new WriteRangeError(
      e?.message ?? 'Excel write failed',
      'EXCEL_ERROR'
    );
  });
}

export class WriteTableError extends Error {
  constructor(
    message: string,
    public readonly code: 'TABLE_NOT_FOUND' | 'EMPTY_ROWS' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'WriteTableError';
  }
}

/**
 * Append rows to a named Excel Table (ListObject) on the active worksheet.
 *
 * @param tableName  Name of the Excel Table (e.g. "SalesData"). Case-sensitive.
 * @param rows       2D array of values to append. Each inner array is one row.
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
    // Sprint 9: Defense-in-depth loop prevention when watch mode is active
    if (isFaitWriting()) {
      ctx.runtime.enableEvents = false;
    }

    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    // getItemOrNullObject returns a proxy — load isNullObject to check existence
    const table = sheet.tables.getItemOrNullObject(tableName);
    table.load('isNullObject');
    await ctx.sync();

    if (table.isNullObject) {
      throw new WriteTableError(`Table "${tableName}" not found on active worksheet`, 'TABLE_NOT_FOUND');
    }

    // Append rows at end of table (index -1)
    // rows is data only — do NOT include headers; Table manages its own header row
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

export class WriteRangeError extends Error {
  constructor(
    message: string,
    public readonly code: 'EMPTY_DATA' | 'DIMENSION_MISMATCH' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'WriteRangeError';
  }
}

// ── Sprint 8: Named Range operations ──────────────────────────────────────

export class NamedRangeError extends Error {
  constructor(
    message: string,
    public readonly code: 'DUPLICATE_NAME' | 'INVALID_NAME' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'NamedRangeError';
  }
}

/**
 * Create a workbook-scoped named range pointing to the given address.
 *
 * @param name      Name for the range (e.g. "FAIT_output_20260316_143022").
 * @param address   Address as returned by writeRangeData (e.g. "Sheet1!A1:D11").
 *                  Automatically converted to absolute format ("=Sheet1!$A$1:$D$11").
 * @param comment   Optional comment to attach to the named range.
 * @throws NamedRangeError with .code "DUPLICATE_NAME" | "INVALID_NAME" | "EXCEL_ERROR"
 */
export async function createNamedRange(
  name: string,
  address: string,
  comment?: string
): Promise<void> {
  // Convert address to absolute reference: "Sheet1!A1:D11" → "=Sheet1!$A$1:$D$11"
  // Step 1: make columns and rows absolute by prepending $
  // Regex ([A-Z]+)(\d+) captures multi-letter columns (AA, BC, XFD) and row numbers
  const absAddr = address.replace(/([A-Z]+)(\d+)/g, '$$$1$$$2');
  // Step 2: prepend the required = prefix
  const formula = `=${absAddr}`;

  return Excel.run(async (ctx: any) => {
    // CRITICAL: Check for duplicate BEFORE calling names.add()
    // names.add() on a duplicate throws a runtime error that's hard to distinguish
    const existing = ctx.workbook.names.getItemOrNullObject(name);
    existing.load('isNullObject');
    await ctx.sync();

    if (!existing.isNullObject) {
      throw new NamedRangeError(
        `Name "${name}" already exists in this workbook`,
        'DUPLICATE_NAME'
      );
    }

    // Now safe to add
    ctx.workbook.names.add(name, formula);

    if (comment) {
      // Re-fetch the item to set comment after creation
      const item = ctx.workbook.names.getItem(name);
      item.comment = comment;
    }

    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof NamedRangeError) throw e;
    // Excel throws "InvalidArgument" if name contains invalid chars
    const msg: string = e?.message ?? '';
    if (msg.includes('InvalidArgument') || msg.includes('invalid') || msg.includes('name')) {
      throw new NamedRangeError(
        `Invalid range name "${name}" — names cannot contain spaces, start with a digit, or duplicate existing names`,
        'INVALID_NAME'
      );
    }
    throw new NamedRangeError(e?.message ?? 'Named range creation failed', 'EXCEL_ERROR');
  });
}

/**
 * Delete a workbook named range by name. Silent if the name doesn't exist.
 */
export async function deleteNamedRange(name: string): Promise<void> {
  return Excel.run(async (ctx: any) => {
    const item = ctx.workbook.names.getItemOrNullObject(name);
    item.load('isNullObject');
    await ctx.sync();
    if (!item.isNullObject) {
      item.delete();
      await ctx.sync();
    }
  }).catch((e: any) => {
    // Non-fatal — if deletion fails, log and continue
    console.warn('FAIT: deleteNamedRange failed:', e?.message);
  });
}

/**
 * Rename a workbook named range. Deletes the old name and recreates with new name + same address.
 * item.value is the formula string (e.g. "=Sheet1!$A$1:$D$11") — loaded before delete.
 */
export async function renameWorkbookNamedRange(
  oldName: string,
  newName: string
): Promise<void> {
  return Excel.run(async (ctx: any) => {
    const item = ctx.workbook.names.getItemOrNullObject(oldName);
    item.load(['isNullObject', 'value']);
    await ctx.sync();

    if (item.isNullObject) {
      throw new NamedRangeError(`Name "${oldName}" not found`, 'EXCEL_ERROR');
    }

    // item.value is the formula string (e.g. "=Sheet1!$A$1:$D$11")
    const formula = item.value as string;
    item.delete();
    ctx.workbook.names.add(newName, formula);
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof NamedRangeError) throw e;
    throw new NamedRangeError(e?.message ?? 'Rename failed', 'EXCEL_ERROR');
  });
}

/**
 * List all workbook named range names (for registry sync validation).
 * Returns empty array on Excel.run() failure — caller must guard against this.
 */
export async function listWorkbookNamedRanges(): Promise<string[]> {
  return Excel.run(async (ctx: any) => {
    const names = ctx.workbook.names;
    names.load('items/name');
    await ctx.sync();
    return (names.items as any[]).map((item: any) => item.name as string);
  }).catch(() => [] as string[]);
}

// ── Sprint 9: Watch mode event handler registration ───────────────────────

/**
 * Register a worksheet.onChanged event handler on the active worksheet.
 * Returns the event result object — caller MUST store it to unregister later.
 *
 * @param onChange  Callback invoked when the worksheet changes.
 *                  MUST NOT be async — the event proxy is only valid synchronously.
 */
export async function registerWatchHandler(
  onChange: (args: any) => void
): Promise<any> {
  return Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const handler = sheet.onChanged.add(onChange);
    await ctx.sync();
    return handler;
  });
}

/**
 * Unregister a previously registered worksheet.onChanged handler.
 * Safe to call with null — does nothing.
 *
 * @param handlerResult  The event result object returned by registerWatchHandler().
 */
export async function unregisterWatchHandler(handlerResult: any): Promise<void> {
  if (!handlerResult) return;
  try {
    await handlerResult.context.remove(handlerResult);
  } catch {
    // Handler may already be invalid (sheet deleted, workbook closed) — silent failure is safe
  }
}
