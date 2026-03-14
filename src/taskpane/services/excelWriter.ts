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

      // Add comment — may fail if the cell already has a comment in some Excel versions
      try {
        range.comments.add(ctx.workbook, `AI suggestion: ${s.explanation}`);
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
