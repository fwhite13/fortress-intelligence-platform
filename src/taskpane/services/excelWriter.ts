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
