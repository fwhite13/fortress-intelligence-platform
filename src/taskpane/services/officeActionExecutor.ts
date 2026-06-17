import type { OfficeAction } from './faitApi';

export interface ActionResult {
  type: string;
  success: boolean;
  message: string;
}

export interface ExecutionSummary {
  results: ActionResult[];
  successCount: number;
  failureCount: number;
}

export async function executeOfficeActions(actions: OfficeAction[]): Promise<ExecutionSummary> {
  const results: ActionResult[] = [];

  for (const action of actions) {
    try {
      const message = await executeSingleAction(action);
      results.push({ type: action.type, success: true, message });
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      results.push({ type: action.type, success: false, message: `Failed: ${msg}` });
    }
  }

  return {
    results,
    successCount: results.filter(r => r.success).length,
    failureCount: results.filter(r => !r.success).length,
  };
}

async function executeSingleAction(action: OfficeAction): Promise<string> {
  switch (action.type) {
    case 'write_cells':
      return executeWriteCells(action);
    case 'apply_formatting':
      return executeApplyFormatting(action);
    case 'create_sheet':
      return executeCreateSheet(action);
    case 'create_chart':
      return executeCreateChart(action);
    default:
      throw new Error(`Unknown action type: ${action.type}`);
  }
}

async function executeWriteCells(action: OfficeAction): Promise<string> {
  const range = action.range as string;
  const values = action.values as unknown[][];
  if (!range) throw new Error('write_cells: missing range');
  if (!Array.isArray(values)) throw new Error('write_cells: missing values');

  await Excel.run(async (ctx) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const r = sheet.getRange(range);
    r.values = values;
    await ctx.sync();
  });

  const rows = values.length;
  const cols = values[0]?.length ?? 0;
  return `Written ${rows}×${cols} values to ${range}`;
}

async function executeApplyFormatting(action: OfficeAction): Promise<string> {
  const range = action.range as string;
  if (!range) throw new Error('apply_formatting: missing range');

  await Excel.run(async (ctx) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const r = sheet.getRange(range);

    if (typeof action.bold === 'boolean') {
      r.format.font.bold = action.bold;
    }
    if (typeof action.fillColor === 'string') {
      r.format.fill.color = action.fillColor;
    }
    if (typeof action.fontColor === 'string') {
      r.format.font.color = action.fontColor;
    }
    if (typeof action.numberFormat === 'string') {
      r.numberFormat = [[action.numberFormat]];
    }
    if (typeof action.columnWidth === 'number') {
      r.format.columnWidth = action.columnWidth;
    }
    if (typeof action.rowHeight === 'number') {
      r.format.rowHeight = action.rowHeight;
    }

    await ctx.sync();
  });

  return `Formatting applied to ${range}`;
}

async function executeCreateSheet(action: OfficeAction): Promise<string> {
  const name = action.name as string;
  if (!name) throw new Error('create_sheet: missing name');

  await Excel.run(async (ctx) => {
    ctx.workbook.worksheets.add(name);
    await ctx.sync();
  });

  return `Sheet '${name}' created`;
}

async function executeCreateChart(action: OfficeAction): Promise<string> {
  const dataRange = action.dataRange as string;
  const chartType = (action.chartType as string) ?? 'column';

  if (!dataRange) throw new Error('create_chart: missing dataRange');

  const chartTypeMap: Record<string, Excel.ChartType> = {
    bar: Excel.ChartType.barClustered,
    line: Excel.ChartType.line,
    pie: Excel.ChartType.pie,
    column: Excel.ChartType.columnClustered,
  };

  const excelChartType = chartTypeMap[chartType] ?? Excel.ChartType.columnClustered;

  await Excel.run(async (ctx) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const dr = sheet.getRange(dataRange);
    const chart = sheet.charts.add(excelChartType, dr, Excel.ChartSeriesBy.auto);

    if (typeof action.title === 'string') {
      chart.title.text = action.title;
      chart.title.visible = true;
    }

    if (typeof action.targetCell === 'string') {
      const anchor = sheet.getRange(action.targetCell);
      chart.setPosition(anchor);
    }

    await ctx.sync();
  });

  return `${chartType} chart created from ${dataRange}`;
}
