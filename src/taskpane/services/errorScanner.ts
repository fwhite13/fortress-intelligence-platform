/* global Excel */

export interface CellIssue {
  address: string;
  type: 'error' | 'hardcoded';
  detail: string;
}

const ERROR_VALUES = ['#REF!', '#VALUE!', '#NAME?', '#DIV/0!', '#N/A', '#NULL!', '#NUM!'];

export async function scanRangeForIssues(): Promise<CellIssue[]> {
  return Excel.run(async (ctx: any) => {
    const range = ctx.workbook.getSelectedRange();
    range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);
    await ctx.sync();

    const issues: CellIssue[] = [];

    // Build column formula-presence map (is this column predominantly formula-based?)
    const colHasFormulas: boolean[] = new Array(range.columnCount).fill(false);
    for (let r = 0; r < range.rowCount; r++) {
      for (let c = 0; c < range.columnCount; c++) {
        if ((range.formulas[r][c] as string).startsWith('=')) {
          colHasFormulas[c] = true;
        }
      }
    }

    for (let r = 0; r < range.rowCount; r++) {
      for (let c = 0; c < range.columnCount; c++) {
        const val = range.values[r][c];
        const formula = range.formulas[r][c] as string;
        const cellAddr = getCellAddress(range.address, r, c);

        // Error value
        if (typeof val === 'string' && ERROR_VALUES.includes(val)) {
          issues.push({ address: cellAddr, type: 'error', detail: val });
          continue;
        }

        // Hardcoded number in a formula-heavy column (skip row 0 — likely header)
        if (
          r > 0 &&
          colHasFormulas[c] &&
          !formula.startsWith('=') &&
          typeof val === 'number'
        ) {
          issues.push({
            address: cellAddr,
            type: 'hardcoded',
            detail: `Hardcoded ${val} in formula column`,
          });
        }
      }
    }

    return issues;
  });
}

function getCellAddress(rangeAddress: string, rowOffset: number, colOffset: number): string {
  // Parse base cell from range address (e.g. "Sheet1!A1:D20" → A1)
  const match = rangeAddress.match(/[A-Z]+\d+/);
  if (!match) return `R${rowOffset}C${colOffset}`;

  const base = match[0];
  const baseCol = base.replace(/\d+/, '');
  const baseRow = parseInt(base.replace(/[A-Z]+/, ''), 10);

  const newCol = columnIndexToLetter(colLetterToIndex(baseCol) + colOffset);
  return `${newCol}${baseRow + rowOffset}`;
}

function colLetterToIndex(col: string): number {
  let idx = 0;
  for (let i = 0; i < col.length; i++) {
    idx = idx * 26 + col.charCodeAt(i) - 64;
  }
  return idx - 1;
}

function columnIndexToLetter(idx: number): string {
  let col = '';
  let n = idx + 1;
  while (n > 0) {
    const rem = (n - 1) % 26;
    col = String.fromCharCode(65 + rem) + col;
    n = Math.floor((n - 1) / 26);
  }
  return col;
}
