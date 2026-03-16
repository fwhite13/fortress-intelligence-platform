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
