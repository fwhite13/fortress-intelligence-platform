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
