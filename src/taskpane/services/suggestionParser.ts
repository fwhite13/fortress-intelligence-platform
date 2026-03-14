import type { CellSuggestion } from '../components/WriteSuggestionsDialog';
import type { ChartSpec } from './chartBuilder';
import type { PivotSpec } from './pivotBuilder';
import type { CfSpec } from './cfBuilder';

export interface ParseResult {
  displayText: string;   // response with JSON blocks stripped
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
}

export function parseSuggestions(rawText: string): ParseResult {
  let displayText = rawText;
  let suggestions: CellSuggestion[] | null = null;
  let chartSpec: ChartSpec | null = null;
  let pivotSpec: PivotSpec | null = null;
  let cfSpec: CfSpec | null = null;

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

  // Clean up excess blank lines left behind by stripped blocks
  displayText = displayText.replace(/\n{3,}/g, '\n\n').trim();

  return { displayText, suggestions, chartSpec, pivotSpec, cfSpec };
}
