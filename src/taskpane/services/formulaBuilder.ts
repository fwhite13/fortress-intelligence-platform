/* global Excel */

import { setFaitWriting } from './watchMode';

export interface FormulaSpec {
  formula: string;
  explanation: string;
  functionNames: string[];
  targetCell: string;   // "__SELECTED__" | A1-notation address
  previewable: boolean;
}

export interface FormulaPreviewResult {
  value: string | number | boolean | null;
  valueType: string;
  isError: boolean;
  errorMessage?: string;
}

const SCRATCH_SHEET_NAME = '__FAIT_SCRATCH__';

async function ensureScratchSheet(): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const existing = ctx.workbook.worksheets.getItemOrNullObject(SCRATCH_SHEET_NAME);
    existing.load('isNullObject');
    await ctx.sync();

    if (existing.isNullObject) {
      const scratch = ctx.workbook.worksheets.add(SCRATCH_SHEET_NAME);
      (scratch as any).visibility = "VeryHidden";
      await ctx.sync();
    }
  });
}

export function prefixFormulaRefs(formula: string, sheetName: string): string {
  const escapedSheet = sheetName.includes(' ') ? `'${sheetName}'` : sheetName;
  return formula.replace(
    /(?<![!'A-Za-z])([A-Z]+\d+(?::[A-Z]+\d+)?)/g,
    (match) => `${escapedSheet}!${match}`
  );
}

export async function previewFormula(
  formula: string,
  activeSheet: string
): Promise<FormulaPreviewResult> {
  await ensureScratchSheet();

  const prefixedFormula = prefixFormulaRefs(formula, activeSheet);

  return Excel.run(async (ctx: any) => {
    const scratch = ctx.workbook.worksheets.getItem(SCRATCH_SHEET_NAME);
    const cell = scratch.getRange('A1');

    cell.formulas = [[prefixedFormula]];
    cell.load(['values', 'valueTypes']);
    await ctx.sync();

    const rawValue = (cell.values as any[][])[0][0];
    const valueType = (cell.valueTypes as string[][])[0][0];

    // CRITICAL: clear ALWAYS runs — even if the value is an error
    cell.clear(Excel.ClearApplyTo.contents);
    await ctx.sync();

    const isError = valueType === 'Error' || (typeof rawValue === 'string' && rawValue.startsWith('#'));

    return {
      value: isError ? null : rawValue,
      valueType,
      isError,
      errorMessage: isError ? String(rawValue) : undefined,
    };
  }).catch((e: any) => {
    return {
      value: null,
      valueType: 'Error',
      isError: true,
      errorMessage: e?.message ?? 'Formula evaluation failed',
    };
  });
}

export async function writeFormula(
  formula: string,
  address: string,
  explanation?: string
): Promise<void> {
  setFaitWriting(true);
  try {
    await Excel.run(async (ctx: any) => {
      const sheet = ctx.workbook.worksheets.getActiveWorksheet();
      const cell = sheet.getRange(address);

      cell.formulas = [[formula]];

      if (explanation) {
        try {
          sheet.comments.add(address, `FAIT formula: ${explanation}`);
        } catch {
          // non-fatal
        }
      }

      await ctx.sync();
    });
  } finally {
    setFaitWriting(false);
  }
}

export function formatPreviewValue(result: FormulaPreviewResult): string {
  if (result.isError) {
    return `→ ${result.errorMessage ?? '#ERROR'}`;
  }
  if (result.value === null || result.value === undefined) {
    return '→ (empty)';
  }
  if (typeof result.value === 'number') {
    if (Math.abs(result.value) >= 1000) {
      return `→ ${result.value.toLocaleString('en-US', { maximumSignificantDigits: 6 })}`;
    }
    return `→ ${result.value}`;
  }
  return `→ ${String(result.value)}`;
}
