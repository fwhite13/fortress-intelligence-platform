import type { SpreadsheetContext } from './excelReader';

export function formatContext(ctx: SpreadsheetContext): string {
  const sanitize = (v: unknown): string =>
    String(v)
      .replace(/[\n\r]/g, ' ')       // prevent prompt injection via newlines
      .replace(/\|/g, '\\|');        // escape pipe chars to avoid breaking markdown table

  let out = `[SPREADSHEET CONTEXT]\nSheet range: ${ctx.address} | ${ctx.rows} rows × ${ctx.cols} cols\n\n`;

  // Detect headers: row 0 is all strings, no numerics
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

    // Include non-trivial formulas alongside each row
    const fmlRow = fmlRows[ri] ?? [];
    const fmlStr = fmlRow
      .map((f: string, ci: number) =>
        f.startsWith('=') ? `${getCellAddr(ri + (isHeader ? 2 : 1), ci)}=${f}` : ''
      )
      .filter(Boolean)
      .join(', ');
    if (fmlStr) out += `Formulas: ${fmlStr}\n`;
  });

  // Token cap: ~6,000 chars
  if (out.length > 6000) {
    out = out.slice(0, 5900) + '\n[... truncated for brevity]\n';
  }

  out += '[END SPREADSHEET CONTEXT]';
  return out;
}

function getCellAddr(row: number, col: number): string {
  // Multi-letter column (A, B, ..., Z, AA, AB, ...) — handles up to 50 cols (AX)
  let colNum = col + 1;
  let colLetter = '';
  while (colNum > 0) {
    const rem = (colNum - 1) % 26;
    colLetter = String.fromCharCode(65 + rem) + colLetter;
    colNum = Math.floor((colNum - 1) / 26);
  }
  return `${colLetter}${row + 1}`;
}
