import type { CellSuggestion } from '../components/WriteSuggestionsDialog';
import type { ChartSpec } from './chartBuilder';
import type { PivotSpec } from './pivotBuilder';
import type { CfSpec } from './cfBuilder';
import type { SortFilterSpec } from './sortFilterBuilder';
import type { ReportSpec } from './reportBuilder';
import type { FormulaSpec } from './formulaBuilder';

export interface ParsedTable {
  headers: string[];
  rows: (string | number | boolean | null)[][];
}

export interface ParseResult {
  displayText: string;   // response with JSON blocks stripped
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;
  reportSpec: ReportSpec | null;   // ← NEW Sprint 10
  formulaSpec: FormulaSpec | null;   // Sprint 11
}

export function parseSuggestions(rawText: string): ParseResult {
  let displayText = rawText;
  let suggestions: CellSuggestion[] | null = null;
  let chartSpec: ChartSpec | null = null;
  let pivotSpec: PivotSpec | null = null;
  let cfSpec: CfSpec | null = null;
  let sortFilterSpec: SortFilterSpec | null = null;
  let tableData: ParsedTable | null = null;
  let reportSpec: ReportSpec | null = null;
  let formulaSpec: FormulaSpec | null = null;

  // ── suggestions block ─────────────────────────────────────────────────────
  const suggestionsRegex = /```json\s*(\{[\s\S]*?"suggestions"[\s\S]*?\})\s*```/;
  const suggestionsMatch = displayText.match(suggestionsRegex);
  if (suggestionsMatch) {
    try {
      const parsed = JSON.parse(suggestionsMatch[1]);
      const arr: CellSuggestion[] = parsed.suggestions;
      if (Array.isArray(arr) && arr.length > 0) {
        suggestions = arr;
        displayText = displayText.replace(suggestionsMatch[0], '');
      }
    } catch {
      // Bad JSON — leave displayText unchanged
    }
  }

  // ── chart_spec block ──────────────────────────────────────────────────────
  const chartRegex = /```json\s*(\{[\s\S]*?"chart_spec"[\s\S]*?\})\s*```/;
  const chartMatch = displayText.match(chartRegex);
  if (chartMatch) {
    try {
      const parsed = JSON.parse(chartMatch[1]);
      if (parsed.chart_spec) {
        chartSpec = parsed.chart_spec as ChartSpec;
        displayText = displayText.replace(chartMatch[0], '');
      }
    } catch {
      // Bad JSON — ignore
    }
  }

  // ── pivot_spec block ──────────────────────────────────────────────────────
  const pivotRegex = /```json\s*(\{[\s\S]*?"pivot_spec"[\s\S]*?\})\s*```/;
  const pivotMatch = displayText.match(pivotRegex);
  if (pivotMatch) {
    try {
      const parsed = JSON.parse(pivotMatch[1]);
      if (parsed.pivot_spec) {
        pivotSpec = parsed.pivot_spec as PivotSpec;
        displayText = displayText.replace(pivotMatch[0], '');
      }
    } catch {
      // Bad JSON — ignore
    }
  }

  // ── cf_spec block ─────────────────────────────────────────────────────────
  const cfRegex = /```json\s*(\{[\s\S]*?"cf_spec"[\s\S]*?\})\s*```/;
  const cfMatch = displayText.match(cfRegex);
  if (cfMatch) {
    try {
      const parsed = JSON.parse(cfMatch[1]);
      if (parsed.cf_spec) {
        cfSpec = parsed.cf_spec as CfSpec;
        displayText = displayText.replace(cfMatch[0], '');
      }
    } catch {
      // Bad JSON — ignore
    }
  }

  // ── sort_filter_spec block ────────────────────────────────────────────────
  const sortFilterRegex = /```json\s*(\{[\s\S]*?"sort_filter_spec"[\s\S]*?\})\s*```/;
  const sortFilterMatch = displayText.match(sortFilterRegex);
  if (sortFilterMatch) {
    try {
      const parsed = JSON.parse(sortFilterMatch[1]);
      if (parsed.sort_filter_spec) {
        sortFilterSpec = parsed.sort_filter_spec as SortFilterSpec;
        displayText = displayText.replace(sortFilterMatch[0], '');
      }
    } catch {
      // Bad JSON — ignore
    }
  }

  // ── report_spec block ─────────────────────────────────────────────────────
  const reportSpecRegex = /```json\s*(\{[\s\S]*?"report_spec"[\s\S]*?\})\s*```/;
  const reportSpecMatch = displayText.match(reportSpecRegex);
  if (reportSpecMatch) {
    try {
      const parsed = JSON.parse(reportSpecMatch[1]);
      const rs = parsed.report_spec;
      if (
        rs &&
        typeof rs.title === 'string' &&
        typeof rs.summary === 'string' &&
        Array.isArray(rs.keyMetrics) &&
        rs.chartSpec
      ) {
        reportSpec = {
          title: rs.title as string,
          summary: rs.summary as string,
          keyMetrics: (rs.keyMetrics as any[]).map((m) => ({
            label: String(m.label ?? ''),
            value: String(m.value ?? ''),
            note: m.note ? String(m.note) : undefined,
          })),
          chartSpec: rs.chartSpec as ChartSpec,
        };
        displayText = displayText.replace(reportSpecMatch[0], '');
      }
    } catch {
      // Bad JSON — leave displayText unchanged
    }
  }

  // ── formula_spec block ────────────────────────────────────────────────────
  const formulaSpecRegex = /```json\s*(\{[\s\S]*?"formula_spec"[\s\S]*?\})\s*```/;
  const formulaSpecMatch = displayText.match(formulaSpecRegex);
  if (formulaSpecMatch) {
    try {
      const parsed = JSON.parse(formulaSpecMatch[1]);
      const fs = parsed.formula_spec;
      if (
        fs &&
        typeof fs.formula === 'string' &&
        fs.formula.startsWith('=') &&
        typeof fs.explanation === 'string'
      ) {
        formulaSpec = {
          formula: fs.formula as string,
          explanation: fs.explanation as string,
          functionNames: Array.isArray(fs.functionNames)
            ? (fs.functionNames as string[])
            : [],
          targetCell: typeof fs.targetCell === 'string' ? fs.targetCell : '__SELECTED__',
          previewable: fs.previewable !== false,
        };
        displayText = displayText.replace(formulaSpecMatch[0], '');
      }
    } catch {
      // Bad JSON — leave displayText unchanged
    }
  }

  // ── table_data block ──────────────────────────────────────────────────────
  const tableDataRegex = /```json\s*(\{[\s\S]*?"table_data"[\s\S]*?\})\s*```/;
  const tableDataMatch = displayText.match(tableDataRegex);
  if (tableDataMatch && !tableData) {
    try {
      const parsed = JSON.parse(tableDataMatch[1]);
      const td = parsed.table_data;
      if (
        td &&
        Array.isArray(td.headers) &&
        td.headers.length > 0 &&
        Array.isArray(td.rows) &&
        td.rows.length > 0
      ) {
        tableData = {
          headers: td.headers as string[],
          rows: td.rows as (string | number | boolean | null)[][],
        };
        displayText = displayText.replace(tableDataMatch[0], '');
      }
    } catch {
      // Bad JSON — leave displayText unchanged
    }
  }

  // ── markdown table detection ──────────────────────────────────────────────
  // Only run if no table_data JSON block was found
  if (!tableData) {
    const mdTableRegex = /(\|.+\|\s*\n\|[-| :]+\|\s*\n(?:\|.+\|\s*\n?)+)/g;
    const mdTableMatch = mdTableRegex.exec(displayText);
    if (mdTableMatch) {
      try {
        const lines = mdTableMatch[1]
          .trim()
          .split('\n')
          .map((l) => l.trim());

        // lines[0] = header, lines[1] = separator, lines[2..] = data rows
        if (lines.length >= 3) {
          const parseRow = (line: string): string[] =>
            line
              .replace(/^\|/, '')
              .replace(/\|$/, '')
              .split('|')
              .map((c) => c.trim());

          const headers = parseRow(lines[0]);
          // Skip separator (lines[1])
          const rows = lines.slice(2).map((line) => {
            return parseRow(line).map((cell) => {
              // Coerce numeric strings to numbers
              const n = Number(cell.replace(/,/g, ''));
              return cell !== '' && !isNaN(n) && isFinite(n) ? n : cell;
            });
          }) as (string | number | boolean | null)[][];

          if (headers.length > 0 && rows.length > 0) {
            tableData = { headers, rows };
            // Leave the markdown table in displayText — MessageBubble renders it as HTML
          }
        }
      } catch {
        // Malformed table — ignore
      }
    }
  }

  // Clean up excess blank lines left behind by stripped blocks
  displayText = displayText.replace(/\n{3,}/g, '\n\n').trim();

  return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData, reportSpec, formulaSpec };
}
