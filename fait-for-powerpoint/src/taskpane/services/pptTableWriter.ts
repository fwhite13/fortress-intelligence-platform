/* global PowerPoint */
declare const PowerPoint: any;

import type { PptTableSpec } from './pptSpecParser';

export class PptTableError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'DIMENSION_MISMATCH' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptTableError';
  }
}

export async function insertTable(spec: PptTableSpec): Promise<void> {
  // Validate dimensions
  if (spec.headers.length !== spec.columnCount) {
    throw new PptTableError(
      `headers.length (${spec.headers.length}) !== columnCount (${spec.columnCount})`,
      'DIMENSION_MISMATCH'
    );
  }
  for (let i = 0; i < spec.values.length; i++) {
    if (spec.values[i].length !== spec.columnCount) {
      throw new PptTableError(
        `values[${i}].length (${spec.values[i].length}) !== columnCount (${spec.columnCount})`,
        'DIMENSION_MISMATCH'
      );
    }
  }

  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptTableError('No slide selected', 'NO_SLIDE');
    }

    const slide = selectedSlides.items[0];

    // allRows: header row + data rows
    const allRows: string[][] = [spec.headers, ...spec.values];
    const totalRows = allRows.length; // = spec.rowCount + 1 (includes header)

    const options: any = { values: allRows };

    if (spec.headerStyle !== 'none') {
      const isLight = spec.headerStyle === 'lightHeader';
      // specificCellProperties must be EXACTLY totalRows × columnCount
      options.specificCellProperties = allRows.map((row, rowIdx) =>
        row.map(() =>
          rowIdx === 0
            ? {
                fill: { color: isLight ? '#DCE6F1' : '#1F3864' },
                font: { bold: true, color: isLight ? '#1F3864' : '#FFFFFF' },
              }
            : {}
        )
      );
    }

    // totalRows includes the header row
    slide.shapes.addTable(totalRows, spec.columnCount, options);
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptTableError) throw e;
    throw new PptTableError(e?.message ?? 'Table creation failed', 'PPT_ERROR');
  });
}
