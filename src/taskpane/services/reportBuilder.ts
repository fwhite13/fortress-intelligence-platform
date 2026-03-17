/* global Excel */

import type { ChartSpec } from './chartBuilder';
import { insertChart } from './chartBuilder';
import { setFaitWriting } from './watchMode';

export interface KeyMetric {
  label: string;
  value: string;
  note?: string;
}

export interface ReportSpec {
  title: string;
  summary: string;
  keyMetrics: KeyMetric[];
  chartSpec: ChartSpec;
}

export interface ReportResult {
  sheetName: string;
  metricsAddress: string;
  reportAddress: string;
}

/**
 * Create a FAIT Report sheet in the active workbook.
 * Writes: title header, summary, key metrics table, chart.
 * The source sheet is never touched.
 *
 * @param spec           The report specification from FAIT
 * @param sourceAddress  Source data address (e.g. "Sheet1!A1:D10") — used for positioning
 * @param overrideTitle  Optional user-edited title (overrides spec.title)
 * @param chartType      Optional user-selected chart type (overrides spec.chartSpec.type)
 */
export async function createReportSheet(
  spec: ReportSpec,
  sourceAddress: string,
  overrideTitle?: string,
  chartType?: 'column' | 'bar' | 'line' | 'pie'
): Promise<ReportResult> {
  const title = (overrideTitle ?? spec.title).slice(0, 45);
  const today = new Date().toISOString().slice(0, 10);
  // IMPORTANT: The dash between "Report" and the date is an em dash U+2014 (—), NOT a hyphen.
  // Copy this character exactly: —
  const sheetName = `FAIT Report — ${today}`;

  setFaitWriting(true);
  try {
    const result = await Excel.run(async (ctx: any) => {
      const wb = ctx.workbook;

      // ── 1. Delete any existing same-day report sheet ─────────────────────
      const existing = wb.worksheets.getItemOrNullObject(sheetName);
      existing.load('isNullObject');
      await ctx.sync();
      if (!existing.isNullObject) {
        existing.delete();
        await ctx.sync();
      }

      // ── 2. Create new report sheet ───────────────────────────────────────
      const sourceSheetName = sourceAddress.includes('!') ? sourceAddress.split('!')[0] : '';
      const newSheet = wb.worksheets.add(sheetName);
      newSheet.tabColor = '#D4AF37';

      if (sourceSheetName) {
        try {
          const sourceSheet = wb.worksheets.getItemOrNullObject(sourceSheetName);
          sourceSheet.load(['isNullObject', 'position']);
          await ctx.sync();
          if (!sourceSheet.isNullObject) {
            newSheet.position = (sourceSheet.position as number) + 1;
          }
        } catch {
          // fallback: leave at default position
        }
      }

      await ctx.sync();

      // ── 3a. Title row (A1:F1 merged) ─────────────────────────────────────
      const titleCell = newSheet.getRange('A1');
      titleCell.values = [[title]];
      titleCell.format.font.size = 16;
      titleCell.format.font.bold = true;
      titleCell.format.font.color = '#D4AF37';
      titleCell.format.fill.color = '#0F1720';
      // merge(false) = merge all cells into one (not across-rows)
      newSheet.getRange('A1:F1').merge(false);

      // ── 3b. Summary section ───────────────────────────────────────────────
      const summaryLabel = newSheet.getRange('A3');
      summaryLabel.values = [['Summary']];
      summaryLabel.format.font.bold = true;
      summaryLabel.format.font.color = '#8899AA';
      summaryLabel.format.font.size = 10;

      const summaryCell = newSheet.getRange('A4');
      summaryCell.values = [[spec.summary.slice(0, 500)]];
      summaryCell.format.wrapText = true;
      summaryCell.format.rowHeight = 60;
      newSheet.getRange('A4:F4').merge(false);

      // ── 3c. Key Metrics section ───────────────────────────────────────────
      const metricsLabel = newSheet.getRange('A6');
      metricsLabel.values = [['Key Metrics']];
      metricsLabel.format.font.bold = true;
      metricsLabel.format.font.color = '#8899AA';
      metricsLabel.format.font.size = 10;

      // Headers row at A7:C7
      const headersRange = newSheet.getRange('A7:C7');
      headersRange.values = [['Metric', 'Value', 'Note']];
      headersRange.format.font.bold = true;
      headersRange.format.font.color = '#D4AF37';
      headersRange.format.fill.color = '#1A3A5F';

      // Metrics data rows starting at row 8 (capped at 8 items)
      const metrics = spec.keyMetrics.slice(0, 8);
      const metricsStartRow = 8;
      const metricsEndRow = metricsStartRow + metrics.length - 1;

      const metricsData: string[][] = metrics.map((m) => [
        m.label,
        m.value,
        m.note ?? '',
      ]);

      const metricsRange = newSheet.getRange(`A${metricsStartRow}:C${metricsEndRow}`);
      metricsRange.values = metricsData;

      // Zebra striping
      for (let i = 0; i < metrics.length; i++) {
        const rowRange = newSheet.getRange(`A${metricsStartRow + i}:C${metricsStartRow + i}`);
        rowRange.format.fill.color = i % 2 === 0 ? '#131F2E' : '#0F1720';
      }

      // Column widths
      newSheet.getRange('A:A').format.columnWidth = 160;
      newSheet.getRange('B:B').format.columnWidth = 100;
      newSheet.getRange('C:C').format.columnWidth = 180;

      // Load used range address
      const usedRange = newSheet.getUsedRange();
      usedRange.load('address');
      await ctx.sync();

      const metricsAddress = `${sheetName}!A${metricsStartRow}:B${metricsEndRow}`;

      return {
        sheetName,
        metricsAddress,
        metricsEndRow,
        reportAddress: usedRange.address as string,
      };
    });

    // ── 3d. Insert chart on the report sheet ─────────────────────────────
    // CRITICAL: override dataRange to point to the report sheet metrics table,
    // NOT the original source sheet range from spec.chartSpec.dataRange.
    if (spec.chartSpec) {
      const chartSpecForReport: ChartSpec = {
        ...spec.chartSpec,
        type: chartType ?? spec.chartSpec.type,
        // MUST override dataRange to report-sheet-relative range before calling insertChart()
        dataRange: `A7:B${result.metricsEndRow}`,
        hasHeaders: true,
        seriesBy: 'columns',
        position: {
          top: 120,
          left: 240,
          width: 380,
          height: 240,
        },
      };
      await insertChart(chartSpecForReport, sheetName);
    }

    return {
      sheetName: result.sheetName,
      metricsAddress: result.metricsAddress,
      reportAddress: result.reportAddress,
    };
  } finally {
    setFaitWriting(false);
  }
}
