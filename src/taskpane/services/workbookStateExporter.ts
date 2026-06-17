// WI #5209 — exports a structured [WORKBOOK STATE] block for CC task mode

const MAX_ROWS = 300;
const MAX_COLS = 26; // A-Z
const MAX_SHEETS = 5;

export interface WorkbookStateResult {
  stateBlock: string; // The full [WORKBOOK STATE]...[END WORKBOOK STATE] text
  sheetCount: number;
  totalCells: number;
}

interface TableEntry {
  sheetName: string;
  tableName: string;
  range: Excel.Range;
}

export async function exportWorkbookState(): Promise<WorkbookStateResult> {
  return Excel.run(async (ctx) => {
    // Load workbook sheets
    const sheets = ctx.workbook.worksheets;
    sheets.load('items/name');

    // Load named ranges
    const namedItems = ctx.workbook.names;
    namedItems.load('items/name,items/formula');

    // Load active sheet and selection
    const activeSheet = ctx.workbook.worksheets.getActiveWorksheet();
    activeSheet.load('name');
    const activeRange = ctx.workbook.getSelectedRange();
    activeRange.load(['address', 'values', 'formulas', 'rowCount', 'columnCount']);

    await ctx.sync();

    const allSheets = sheets.items;
    const sheetCount = allSheets.length;
    const sheetsToProcess = allSheets.slice(0, MAX_SHEETS);
    const extraSheets = sheetCount - sheetsToProcess.length;

    // Load used ranges for all sheets to process
    const sheetDataMap: Map<string, { usedRange: Excel.Range }> = new Map();
    for (const sheet of sheetsToProcess) {
      const usedRange = sheet.getUsedRangeOrNullObject();
      usedRange.load(['address', 'rowCount', 'columnCount', 'values', 'formulas']);
      sheetDataMap.set(sheet.name, { usedRange });
    }
    await ctx.sync();

    // Load table names for each sheet
    for (const sheet of sheetsToProcess) {
      sheet.tables.load('items/name');
    }
    await ctx.sync();

    // Load each table's range address via getRange()
    const tableEntries: TableEntry[] = [];
    for (const sheet of sheetsToProcess) {
      for (const table of sheet.tables.items) {
        const range = table.getRange();
        range.load('address');
        tableEntries.push({ sheetName: sheet.name, tableName: table.name, range });
      }
    }
    await ctx.sync();

    const lines: string[] = [];
    lines.push('[WORKBOOK STATE]');

    // Build sheet summaries for the header
    const sheetSummaries: string[] = [];
    for (const sheet of sheetsToProcess) {
      const { usedRange } = sheetDataMap.get(sheet.name)!;
      if (usedRange.isNullObject) continue;

      const rangeAddr = usedRange.address.includes('!')
        ? usedRange.address.split('!')[1]
        : usedRange.address;

      const sheetTables = tableEntries.filter((e) => e.sheetName === sheet.name);
      const tableDescs = sheetTables
        .map((e) => {
          const addr = e.range.address.includes('!')
            ? e.range.address.split('!')[1]
            : e.range.address;
          return `${e.tableName} ${addr}`;
        })
        .join(', ');
      const tableStr =
        sheetTables.length > 0
          ? `, ${sheetTables.length} table${sheetTables.length > 1 ? 's' : ''}: ${tableDescs}`
          : '';
      sheetSummaries.push(`${sheet.name} (${rangeAddr}${tableStr})`);
    }

    lines.push(`Sheets: ${sheetSummaries.join(', ')}`);
    if (extraSheets > 0) {
      lines.push(`[${extraSheets} more sheet${extraSheets > 1 ? 's' : ''} not shown]`);
    }

    // Named ranges
    const namedRangeLines: string[] = [];
    for (const item of namedItems.items) {
      const formula = item.formula?.replace(/^=/, '') ?? '';
      namedRangeLines.push(`${item.name}=${formula}`);
    }
    if (namedRangeLines.length > 0) {
      lines.push(`Named ranges: ${namedRangeLines.join(', ')}`);
    }

    lines.push('');

    let totalCells = 0;

    // Process each sheet
    for (const sheet of sheetsToProcess) {
      const { usedRange } = sheetDataMap.get(sheet.name)!;
      if (usedRange.isNullObject) continue;

      lines.push(`--- ${sheet.name} ---`);

      const allValues = usedRange.values as unknown[][];
      const allFormulas = usedRange.formulas as string[][];
      const totalRows = usedRange.rowCount;
      const totalCols = usedRange.columnCount;

      const addrPart = usedRange.address.includes('!')
        ? usedRange.address.split('!')[1]
        : usedRange.address;
      const startCell = addrPart.split(':')[0].replace(/\$/g, '');
      const startColLetter = startCell.match(/[A-Z]+/)?.[0] ?? 'A';
      const startRow = parseInt(startCell.match(/\d+/)?.[0] ?? '1', 10);
      const startColIndex = colLetterToIndex(startColLetter);

      const colsToRender = Math.min(totalCols, MAX_COLS);
      const rowsToRender = Math.min(totalRows, MAX_ROWS);
      const truncated = totalRows > MAX_ROWS;

      // Column header row
      const colHeaders = [''];
      for (let c = 0; c < colsToRender; c++) {
        colHeaders.push(indexToColLetter(startColIndex + c));
      }
      if (totalCols > MAX_COLS) {
        colHeaders.push(`...(${totalCols - MAX_COLS} more cols)`);
      }
      lines.push('| ' + colHeaders.join(' | ') + ' |');

      const formulaCells: string[] = [];

      for (let r = 0; r < rowsToRender; r++) {
        const rowNum = startRow + r;
        const rowCells: string[] = [String(rowNum)];

        for (let c = 0; c < colsToRender; c++) {
          const val = allValues[r]?.[c];
          const formula = allFormulas[r]?.[c];
          const colLetter = indexToColLetter(startColIndex + c);
          const cellAddr = `${colLetter}${rowNum}`;

          if (typeof formula === 'string' && formula.startsWith('=')) {
            formulaCells.push(`${cellAddr}=${formula}`);
          }

          rowCells.push(formatCellValue(val));
          totalCells++;
        }
        lines.push('| ' + rowCells.join(' | ') + ' |');
      }

      if (truncated) {
        lines.push(`[TRUNCATED: sheet has ${totalRows} rows, showing first ${MAX_ROWS}]`);
      }

      if (formulaCells.length > 0) {
        const shown = formulaCells.slice(0, 50);
        const extra = formulaCells.length - shown.length;
        let formulaLine = `[Formulas: ${shown.join(', ')}`;
        if (extra > 0) formulaLine += `, ...${extra} more`;
        formulaLine += ']';
        lines.push(formulaLine);
      }

      lines.push('');
    }

    lines.push(`Active selection: ${activeRange.address}`);
    lines.push('[END WORKBOOK STATE]');

    return {
      stateBlock: lines.join('\n'),
      sheetCount,
      totalCells,
    };
  });
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function colLetterToIndex(letter: string): number {
  let index = 0;
  for (let i = 0; i < letter.length; i++) {
    index = index * 26 + (letter.charCodeAt(i) - 64);
  }
  return index - 1;
}

function indexToColLetter(index: number): string {
  if (index < 0 || index >= 26) return '?';
  return String.fromCharCode(65 + index);
}

function formatCellValue(val: unknown): string {
  if (val === null || val === undefined || val === '') return '';
  if (typeof val === 'number') {
    if (Number.isInteger(val)) return String(val);
    return val.toPrecision(6).replace(/\.?0+$/, '');
  }
  if (typeof val === 'boolean') return val ? 'TRUE' : 'FALSE';
  const str = String(val);
  return str.length > 50 ? str.slice(0, 47) + '...' : str;
}
