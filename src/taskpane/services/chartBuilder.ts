/* global Excel */

export interface ChartSpec {
  type: 'bar' | 'line' | 'pie' | 'scatter' | 'column';
  title: string;
  dataRange: string;        // e.g. "A1:D5"
  hasHeaders: boolean;
  seriesBy: 'rows' | 'columns';
  xAxis?: { title: string };
  yAxis?: { title: string };
  position?: { top: number; left: number; width: number; height: number };
}

export async function insertChart(spec: ChartSpec, sheetName?: string): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const sheet = sheetName
      ? ctx.workbook.worksheets.getItem(sheetName)
      : ctx.workbook.worksheets.getActiveWorksheet();
    const dataRange = sheet.getRange(spec.dataRange);

    // Map spec type to Excel ChartType
    const chartTypeMap: Record<string, string> = {
      bar:     'BarClustered',
      column:  'ColumnClustered',
      line:    'Line',
      pie:     'Pie',
      scatter: 'XYScatter',
    };
    const excelType = chartTypeMap[spec.type] ?? 'ColumnClustered';

    const chart = sheet.charts.add(
      excelType,
      dataRange,
      spec.seriesBy === 'rows' ? 'Rows' : 'Columns'
    );

    chart.title.text = spec.title;
    chart.title.visible = true;

    if (spec.xAxis?.title) {
      chart.axes.categoryAxis.title.text = spec.xAxis.title;
      chart.axes.categoryAxis.title.visible = true;
    }
    if (spec.yAxis?.title) {
      chart.axes.valueAxis.title.text = spec.yAxis.title;
      chart.axes.valueAxis.title.visible = true;
    }

    if (spec.position) {
      chart.top    = spec.position.top;
      chart.left   = spec.position.left;
      chart.width  = spec.position.width ?? 400;
      chart.height = spec.position.height ?? 300;
    } else {
      chart.width  = 400;
      chart.height = 300;
    }

    await ctx.sync();
  });
}
