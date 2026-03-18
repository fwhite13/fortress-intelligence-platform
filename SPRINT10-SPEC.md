# FfE Sprint 10 Spec — Multi-Sheet Report Generation

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** Sprint 7 (Table awareness) and Sprint 8 (named ranges) recommended; Sprint 6 (write foundation) required  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What the Source Shows

### Chart API reality — no Chart.js needed

The roadmap mentioned "Chart.js → canvas → base64 → `addPicture()`" as a possible pattern. **This is the PowerPoint pattern (FfP), not the Excel pattern.** Excel's native `charts.add(type, dataRange, seriesBy)` API creates charts directly from data ranges — no canvas, no base64, no external library.

`chartBuilder.ts` already implements `insertChart()` using `sheet.charts.add()` on the **active sheet**. Sprint 10 extends this: `insertChart()` needs to target a specific sheet (the new report sheet), not always the active one. The spec adds a `sheetName?` parameter to `insertChart()`.

`chart.getImage()` is ExcelApi 1.2 — within the 1.13 baseline. Could be used for chart thumbnails in the chat thread. **Sprint 10 does NOT need this** — the chart goes in the report sheet, not the task pane.

**Answer to spec question 4: No Chart.js, no canvas, no base64 for Excel reports. Native `charts.add()` handles everything. No new npm packages needed.**

### Slash command UX — currently just pastes into input

`SlashCommandPicker.onSelect(prompt)` calls `setInputText(prompt)` in ChatPanel — it pastes the command's prompt string into the chat input. The user then manually clicks Send. **This is not ideal for `/report`** — report generation requires a multi-step confirmation flow (choose what to analyze, which chart type, confirm before sheet creation). A pure "paste into input" approach won't work.

**Decision: `/report` gets special handling in `ChatPanel`.** When the user selects `/report` from the picker, instead of pasting a prompt, it triggers a dedicated report configuration panel (same pattern as CF prompt, sort/filter prompt, watch config).

### Sheet naming — constraint: ≤31 chars, no `/ \ * ? : [ ]`

`FAIT Report — 2026-03-16` = 23 characters ✅. But Excel's sheet name max is 31 chars. The format `FAIT Report — YYYY-MM-DD` is 23 chars — fine. The `—` em dash must be a valid sheet name character (it is — only the characters listed above are prohibited).

### Existing write infrastructure

`writeRangeData()` (S6) takes a target cell and 2D data. This handles the summary table and metrics table writes. `insertChart()` in `chartBuilder.ts` handles the chart. `addNamedRange()` + `createNamedRange()` (S8) handle the optional named range registration. All building blocks exist.

---

## What Sprint 10 Delivers

1. `/report` slash command entry in `SlashCommandPicker` (new command in COMMANDS array)
2. When selected: triggers a report config panel instead of pasting into input
3. Config panel: report title (editable), chart type selector (column/line/bar), confirm/cancel
4. FAIT reads the current selection, sends it to the API for analysis, gets back a structured `report_spec` JSON block
5. ChatPanel creates the report sheet, writes sections in order, registers as a named range
6. Report sheet contains: title header, summary paragraph, key metrics table, chart
7. Sheet is tabbed gold (`#D4AF37`), positioned immediately after the source sheet
8. Source sheet is never touched

---

## Design Decisions

### Decision 1: `/report` triggers config panel, not chat input paste

When `onSelect('/report prompt...')` is called for the report command, `ChatPanel` intercepts it by checking for a special sentinel value. The cleaner approach: give the report command a `special: 'report'` marker, and `SlashCommandPicker.onSelect` passes the raw command name alongside the prompt string. But this requires changing the `onSelect` interface.

**Simpler: add a `name` field to `onSelect` callback.** Change `onSelect: (prompt: string) => void` to `onSelect: (prompt: string, name?: string) => void`. ChatPanel checks: if `name === 'report'`, open the report config panel instead of pasting into input.

### Decision 2: Two-phase report generation

**Phase 1 — Analysis:** FAIT reads the selection and calls `send(reportPrompt, context)`. The prompt asks FAIT to return a `report_spec` JSON block. FAIT's response includes:
- `summary`: 2–4 sentence prose summary of the data
- `keyMetrics`: array of `{label, value, note?}` objects
- `chartSpec`: a `ChartSpec` object (same format `chartBuilder.ts` already consumes)
- `reportTitle`: suggested title for the report

Phase 1 shows FAIT's analysis in the chat thread — user can see what FAIT found before anything touches the workbook.

**Phase 2 — Sheet creation:** A "Create Report Sheet" button appears below FAIT's response (alongside the existing "↓ Write to Sheet" button pattern). Clicking it creates the sheet and writes all sections. This is the confirmation gate — user sees the analysis, then decides whether to create the sheet.

### Decision 3: `report_spec` JSON block in `parseSuggestions`

Add a new parser in `suggestionParser.ts` for `report_spec` (same pattern as `chart_spec`, `pivot_spec`, etc.):

```json
{
  "report_spec": {
    "title": "Q1 Revenue Analysis",
    "summary": "The selected data shows Q1 revenue across 4 regions...",
    "keyMetrics": [
      { "label": "Total Revenue", "value": "$4.2M", "note": "12% above Q1 target" },
      { "label": "Top Region", "value": "North", "note": "$1.8M (43% of total)" }
    ],
    "chartSpec": {
      "type": "column",
      "title": "Q1 Revenue by Region",
      "dataRange": "A1:B5",
      "hasHeaders": true,
      "seriesBy": "columns"
    }
  }
}
```

### Decision 4: Chart positioning in the report sheet

The report sheet layout (row-by-row, starting at A1):

```
A1: [Report Title — large, bold text as a cell value]
A2: [blank]
A3: [Summary section label]
A4–A6: [Summary text — single cell, wrapped]
A7: [blank]
A8: [Key Metrics label]
A9: [Metric] [Value] [Note]     ← headers
A10–A15: [metrics rows]
A16: [blank]
A17: [Chart section label]
```

Chart positioned starting at cell D9 (to the right of the metrics table). Using `chart.setPosition("D9", "L22")` — 9 columns wide, 14 rows tall. This is the right pattern: `setPosition()` takes A1-notation strings.

**`chartSpec.dataRange` will be relative to the report sheet** — FAIT's `report_spec` includes the data range from the source sheet. The chart builder needs to work against a specific sheet name.

### Decision 5: `insertChart()` gets an optional `sheetName` parameter

Current signature:
```typescript
export async function insertChart(spec: ChartSpec): Promise<void>
```

Updated signature:
```typescript
export async function insertChart(spec: ChartSpec, sheetName?: string): Promise<void>
```

When `sheetName` is provided, `insertChart()` targets that sheet instead of the active sheet. All existing callers pass no `sheetName` — backward compatible.

### Decision 6: Chart data range in the report sheet

The report sheet has the metrics table at A9:C15 (approximately). The chart should bind to the metrics table in the report sheet — not back to the source sheet. This keeps the report self-contained.

When writing the metrics table, use a fixed address like `A9`. The chart's `dataRange` is updated by the report builder to `A9:B${9 + keyMetrics.length}` (label + value columns, skipping the note column).

### Decision 7: Named range registration

After sheet creation succeeds, register the entire report sheet's used range as a named FAIT range via S8's `createNamedRange()`. Name format: `FAIT_report_YYYYMMDD_HHMMSS`. Store in custom XML registry. This allows follow-up questions like "update the FAIT_report_20260316" to read the report content.

### Decision 8: S8 integration is optional at runtime

S8 may not be landed when S10 ships. The named range registration is wrapped in a try/catch — if `createNamedRange()` fails or isn't available, the report is still created successfully. Sprint 10 imports from S8 but gracefully degrades if the registry isn't populated.

---

## `report_spec` Prompt Engineering

The system prompt injected into FAIT for `/report` mode:

```
Please analyze the selected spreadsheet data and return a structured report_spec JSON block.
Return a JSON block with key "report_spec" containing:
- title: string — a concise report title (max 50 chars)
- summary: string — 2-4 sentences describing the data, key trends, and notable findings
- keyMetrics: array of { label: string, value: string, note?: string } — max 8 metrics
- chartSpec: object with keys: type ("column"|"bar"|"line"|"pie"), title (string), 
  dataRange (string — A1-notation range in the source sheet), hasHeaders (boolean), 
  seriesBy ("rows"|"columns")

The keyMetrics should highlight the most important numbers from the data.
The chartSpec should visualize the most relevant comparison or trend.
Return ONLY the JSON block — no prose before or after it.
```

---

## Data Model Changes

### New `ReportSpec` interface

```typescript
// reportBuilder.ts (new file)
export interface KeyMetric {
  label: string;
  value: string;
  note?: string;
}

export interface ReportSpec {
  title: string;
  summary: string;
  keyMetrics: KeyMetric[];
  chartSpec: ChartSpec;   // from chartBuilder.ts
}
```

### `ParseResult` gets `reportSpec` field (same pattern as all other specs)

```typescript
// suggestionParser.ts
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;
  reportSpec: ReportSpec | null;   // ← NEW
}
```

### New `reportSpec` state in `ChatPanel`

```typescript
// Sprint 10
const [reportSpec, setReportSpec] = useState<ReportSpec | null>(null);
const [showReportConfig, setShowReportConfig] = useState(false);
const [reportTitle, setReportTitle] = useState('');
const [reportChartType, setReportChartType] = useState<'column' | 'bar' | 'line' | 'pie'>('column');
const [reportLoading, setReportLoading] = useState(false);
const [reportError, setReportError] = useState<string | null>(null);
const [reportSuccess, setReportSuccess] = useState<string | null>(null);
const [pendingReportSpec, setPendingReportSpec] = useState<ReportSpec | null>(null);
const [sourceSheetAddress, setSourceSheetAddress] = useState<string>('');
```

---

## Parallelization Map

```
Single sequential CC session. 5 files + 1 new file. 6 total.

  Task 1: reportBuilder.ts       NEW FILE — createReportSheet(): all sheet creation logic,
                                   write summary, write metrics table, insert chart,
                                   register named range

  Task 2: suggestionParser.ts    Add ReportSpec interface + report_spec parser block
                                   (same pattern as chart_spec, pivot_spec, etc.)

  Task 3: chartBuilder.ts        Add optional sheetName param to insertChart()
                                   (backward-compatible — existing calls unaffected)

  Task 4: SlashCommandPicker.tsx Add /report entry to COMMANDS array;
                                   update onSelect callback signature to pass name

  Task 5: ChatPanel.tsx          Sprint 10 state; report config panel; handle /report
                                   command specially; "Create Report Sheet" button logic;
                                   wire reportSpec from parseSuggestions

  Task 6: useChat.ts             Propagate reportSpec from parseSuggestions result
```

---

## File-Level Spec

### Task 1 (NEW): `src/taskpane/services/reportBuilder.ts`

```typescript
/* global Excel */

import type { ChartSpec } from './chartBuilder';
import { insertChart } from './chartBuilder';
import { setFaitWriting } from './watchMode';   // Sprint 9 loop prevention

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
  reportAddress: string;   // full used range address
}

/**
 * Create a FAIT Report sheet in the active workbook.
 * Writes: title header, summary, key metrics table, chart.
 * The source sheet is never touched.
 *
 * @param spec           The report specification from FAIT
 * @param sourceAddress  Source data address (e.g. "Sheet1!A1:D10") — used as chart reference
 * @param overrideTitle  Optional user-edited title (overrides spec.title)
 * @param chartType      Optional user-selected chart type (overrides spec.chartSpec.type)
 */
export async function createReportSheet(
  spec: ReportSpec,
  sourceAddress: string,
  overrideTitle?: string,
  chartType?: 'column' | 'bar' | 'line' | 'pie'
): Promise<ReportResult> {
  const title = (overrideTitle ?? spec.title).slice(0, 45);  // enforce reasonable length
  const today = new Date().toISOString().slice(0, 10);
  const sheetName = `FAIT Report — ${today}`;                // ≤ 31 chars — 23 chars ✅

  setFaitWriting(true);
  try {
    return await Excel.run(async (ctx: any) => {
      const wb = ctx.workbook;

      // ── 1. Find and delete any existing same-day report sheet ────────────
      const existingSheet = wb.worksheets.getItemOrNullObject(sheetName);
      existingSheet.load('isNullObject');
      await ctx.sync();
      if (!existingSheet.isNullObject) {
        existingSheet.delete();
        await ctx.sync();
      }

      // ── 2. Get source sheet for positioning ──────────────────────────────
      const sourceSheetName = sourceAddress.split('!')[0] ?? '';
      const sheets = wb.worksheets;

      // Add new report sheet after the active/source sheet
      const newSheet = sheets.add(sheetName);
      newSheet.tabColor = '#D4AF37';   // FIP gold

      if (sourceSheetName) {
        // Try to position right after the source sheet
        try {
          const sourceSheet = sheets.getItemOrNullObject(sourceSheetName);
          sourceSheet.load(['isNullObject', 'position']);
          await ctx.sync();
          if (!sourceSheet.isNullObject) {
            newSheet.position = (sourceSheet.position as number) + 1;
          }
        } catch {
          // Position after active sheet — acceptable fallback
        }
      }

      await ctx.sync();

      // ── 3. Write report content ──────────────────────────────────────────
      // Row layout:
      // R1:  Title (A1, merged across A1:F1)
      // R2:  blank
      // R3:  "Summary" label (A3, section heading style)
      // R4:  Summary text (A4, wrapped)
      // R5:  blank
      // R6:  "Key Metrics" label (A6, section heading)
      // R7:  Column headers: Metric | Value | Note
      // R8…: Metric rows (up to 8)
      // Rn+2: blank
      // Rn+3: "Chart" label + chart object anchored at D7

      // ── 3a. Title row ────────────────────────────────────────────────────
      const titleCell = newSheet.getRange('A1');
      titleCell.values = [[title]];
      titleCell.format.font.size = 16;
      titleCell.format.font.bold = true;
      titleCell.format.font.color = '#D4AF37';
      titleCell.format.fill.color = '#0F1720';

      // Merge A1:F1 for the title
      newSheet.getRange('A1:F1').merge(false);

      // ── 3b. Summary section ───────────────────────────────────────────────
      const summaryLabel = newSheet.getRange('A3');
      summaryLabel.values = [['Summary']];
      summaryLabel.format.font.bold = true;
      summaryLabel.format.font.color = '#8899AA';
      summaryLabel.format.font.size = 10;

      const summaryCell = newSheet.getRange('A4');
      // Wrap long summary across A4:F4
      summaryCell.values = [[spec.summary.slice(0, 500)]];
      summaryCell.format.wrapText = true;
      newSheet.getRange('A4:F4').merge(false);
      // Set row height to accommodate wrapped text
      summaryCell.format.rowHeight = 60;

      // ── 3c. Key Metrics table ─────────────────────────────────────────────
      const metricsLabelCell = newSheet.getRange('A6');
      metricsLabelCell.values = [['Key Metrics']];
      metricsLabelCell.format.font.bold = true;
      metricsLabelCell.format.font.color = '#8899AA';
      metricsLabelCell.format.font.size = 10;

      // Headers row
      const headersRange = newSheet.getRange('A7:C7');
      headersRange.values = [['Metric', 'Value', 'Note']];
      headersRange.format.font.bold = true;
      headersRange.format.font.color = '#D4AF37';
      headersRange.format.fill.color = '#1A3A5F';

      // Metrics data rows (capped at 8)
      const metrics = spec.keyMetrics.slice(0, 8);
      const metricsStartRow = 8;  // Row 8 = index 7 (0-based)
      const metricsEndRow = metricsStartRow + metrics.length - 1;

      const metricsData: string[][] = metrics.map((m) => [
        m.label,
        m.value,
        m.note ?? '',
      ]);

      const metricsRange = newSheet.getRange(`A${metricsStartRow}:C${metricsEndRow}`);
      metricsRange.values = metricsData;

      // Zebra stripe the metrics rows
      for (let i = 0; i < metrics.length; i++) {
        const rowRange = newSheet.getRange(`A${metricsStartRow + i}:C${metricsStartRow + i}`);
        rowRange.format.fill.color = i % 2 === 0 ? '#131F2E' : '#0F1720';
      }

      // Column widths
      newSheet.getRange('A:A').format.columnWidth = 160;
      newSheet.getRange('B:B').format.columnWidth = 100;
      newSheet.getRange('C:C').format.columnWidth = 180;

      // Load the used range address for naming
      const usedRange = newSheet.getUsedRange();
      usedRange.load('address');

      await ctx.sync();

      const metricsAddress = `${sheetName}!A${metricsStartRow}:B${metricsEndRow}`;

      // ── 3d. Insert chart ──────────────────────────────────────────────────
      // Chart bound to the metrics table in the report sheet (A7:B<end>)
      // This makes the report self-contained — no reference back to source sheet
      const chartSpecForReport: ChartSpec = {
        ...spec.chartSpec,
        type: chartType ?? spec.chartSpec.type,
        dataRange: `A7:B${metricsEndRow}`,     // headers + metrics (label + value columns)
        hasHeaders: true,
        seriesBy: 'columns',
        position: undefined,   // will use setPosition below instead
      };

      // Insert chart on the report sheet using the new sheetName parameter
      await insertChart(chartSpecForReport, sheetName);

      // After insertChart, set the position to D7:L20 on the report sheet
      // setPosition is called inside a fresh Excel.run in insertChart —
      // we use spec.position passing instead: pass pixel coordinates
      // Actually insertChart already handles position — but we need it
      // relative to the report sheet. The position is set via spec.position
      // in insertChart. To position at D7:L20, we use the setPosition cell-ref API:
      // chart.setPosition("D7", "L20") — but chartBuilder.ts doesn't expose this.
      // Two options:
      //   A) Add setPosition support to ChartSpec
      //   B) Move the chart after creation using top/left/width/height
      // Decision: pass position as pixel offsets. D7 ≈ top:120, left:220, width:380, height:240
      // These are approximate — good enough for a report. User can resize if needed.

      // The chart was already inserted by insertChart(chartSpecForReport, sheetName)
      // with no position → default position at top-left. We need to set position
      // post-insertion. Get the last chart on the sheet and reposition it.
      //
      // Do this in a new Excel.run() after insertChart completes:
      await Excel.run(async (ctx2: any) => {
        const rSheet = ctx2.workbook.worksheets.getItem(sheetName);
        const charts = rSheet.charts;
        charts.load('count');
        await ctx2.sync();

        if ((charts.count as number) > 0) {
          const lastChart = charts.getItemAt((charts.count as number) - 1);
          // Position at approximately D7:L20 in the report sheet
          // D column ≈ 220px from left (3 cols × ~73px avg), row 7 ≈ 120px from top
          lastChart.top = 120;
          lastChart.left = 220;
          lastChart.width = 380;
          lastChart.height = 240;
          await ctx2.sync();
        }
      });

      return {
        sheetName,
        metricsAddress,
        reportAddress: usedRange.address as string,
      };
    });
  } finally {
    setFaitWriting(false);
  }
}
```

**Important note on `insertChart()` call:** `createReportSheet()` calls `insertChart(chartSpec, sheetName)` — this requires the updated `chartBuilder.ts` (Task 3). The chart positioning is handled via a second `Excel.run()` after `insertChart()` completes, because `insertChart()` owns its own context. This is slightly inefficient (two contexts) but keeps `chartBuilder.ts` clean and backward-compatible.

**Alternative considered:** Do everything in one `Excel.run()` — merge `createReportSheet()` and `insertChart()` into a single context. Rejected: `insertChart()` is a reusable service used by other sprints; changing its signature to accept an external context breaks the pattern. Two `Excel.run()` calls is fine performance-wise for a report generation flow.

---

### Task 2: `src/taskpane/services/suggestionParser.ts`

**Add `ReportSpec` import and `reportSpec` to `ParseResult`:**

```typescript
import type { ReportSpec } from './reportBuilder';

// Add to ParseResult:
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;
  reportSpec: ReportSpec | null;   // ← NEW
}
```

**Add `report_spec` parser block** (after `sort_filter_spec` block, before `table_data` block, same pattern as all other spec parsers):

```typescript
// ── report_spec block ─────────────────────────────────────────────────────
let reportSpec: ReportSpec | null = null;
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
```

**Initialize at top:** `let reportSpec: ReportSpec | null = null;`

**Update return statement:** `return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData, reportSpec };`

**Do NOT change** any existing parser blocks.

---

### Task 3: `src/taskpane/services/chartBuilder.ts`

Add optional `sheetName` parameter. One-line change inside `insertChart()`.

```typescript
export async function insertChart(spec: ChartSpec, sheetName?: string): Promise<void> {
  await Excel.run(async (ctx: any) => {
    // Use named sheet if provided, else active sheet
    const sheet = sheetName
      ? ctx.workbook.worksheets.getItem(sheetName)
      : ctx.workbook.worksheets.getActiveWorksheet();

    const dataRange = sheet.getRange(spec.dataRange);
    // ... rest of existing code unchanged ...
```

That's the entire change. All existing callers pass `insertChart(spec)` without `sheetName` — backward compatible.

---

### Task 4: `src/taskpane/components/SlashCommandPicker.tsx`

Two changes:

**Change 1: Add `/report` to COMMANDS array:**

```typescript
{
  name: 'report',
  description: 'Generate an analysis report sheet from selected data',
  prompt: '__REPORT_COMMAND__',   // sentinel — not pasted into input
},
```

Add it as the first command (highest priority for demo value).

**Change 2: Update `onSelect` callback to pass the command name:**

```typescript
// BEFORE
interface SlashCommandPickerProps {
  query: string;
  onSelect: (prompt: string) => void;
  onClose: () => void;
}

// AFTER
interface SlashCommandPickerProps {
  query: string;
  onSelect: (prompt: string, name?: string) => void;
  onClose: () => void;
}
```

Update the `onSelect` call inside the component:

```typescript
// BEFORE
onClick={() => onSelect(cmd.prompt)}

// AFTER
onClick={() => onSelect(cmd.prompt, cmd.name)}
```

And in the keyboard handler:
```typescript
// BEFORE
onSelect(filtered[activeIndex].prompt);

// AFTER
onSelect(filtered[activeIndex].prompt, filtered[activeIndex].name);
```

**Do NOT change** any existing command definitions (audit, clean, summarize, format).

---

### Task 5: `src/taskpane/components/ChatPanel.tsx`

Five targeted changes.

**Change 1: Add imports**

```typescript
import { createReportSheet } from '../services/reportBuilder';
import type { ReportSpec } from '../services/reportBuilder';
```

**Change 2: Add Sprint 10 state (after Sprint 9 state block)**

```typescript
// ── Sprint 10: Report generation state ───────────────────────────────────
const [showReportConfig, setShowReportConfig] = useState(false);
const [reportConfigTitle, setReportConfigTitle] = useState('');
const [reportConfigChartType, setReportConfigChartType] = useState<'column' | 'bar' | 'line' | 'pie'>('column');
const [pendingReportSpec, setPendingReportSpec] = useState<ReportSpec | null>(null);
const [reportLoading, setReportLoading] = useState(false);
const [reportError, setReportError] = useState<string | null>(null);
const [reportSuccess, setReportSuccess] = useState<string | null>(null);
const [reportSourceAddress, setReportSourceAddress] = useState('');
```

**Change 3: Update `onSelect` in `SlashCommandPicker` render to handle `/report` specially**

```typescript
// BEFORE
<SlashCommandPicker
  query={slashQuery}
  onSelect={(prompt) => {
    setInputText(prompt);
  }}
  onClose={() => setInputText('')}
/>

// AFTER
<SlashCommandPicker
  query={slashQuery}
  onSelect={(prompt, name) => {
    if (name === 'report') {
      // Special handling: open report config panel instead of pasting into input
      setInputText('');
      setShowReportConfig(true);
      setReportError(null);
      setReportSuccess(null);
    } else {
      setInputText(prompt);
    }
  }}
  onClose={() => setInputText('')}
/>
```

**Change 4: Add report handlers**

After `handleNameRangeKeyDown` (S8) / `stopWatching` (S9):

```typescript
// ── Sprint 10: Report handlers ────────────────────────────────────────────

const handleReportAnalyze = async () => {
  // Phase 1: Send to FAIT for analysis — returns report_spec JSON
  setShowReportConfig(false);
  setReportError(null);
  setReportSuccess(null);

  let context: string | undefined;
  let address = '';
  try {
    const ctx = await getSelectedRange();
    if (ctx.rows > 0 && ctx.cols > 0) {
      context = formatContext(ctx);
      address = ctx.address;
      setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
    }
  } catch {
    // Non-fatal
  }

  setReportSourceAddress(address);

  const reportPrompt = `Please analyze the selected spreadsheet data and return a structured report_spec JSON block.
Return a JSON block with key "report_spec" containing:
- title: string — a concise report title (max 50 chars)
- summary: string — 2-4 sentences describing the data, key trends, and notable findings
- keyMetrics: array of { label: string, value: string, note?: string } — max 8 metrics
- chartSpec: object with keys: type ("column"|"bar"|"line"|"pie"), title (string), dataRange (string in A1-notation for the metrics table starting at A7), hasHeaders (true), seriesBy ("columns")

The keyMetrics should highlight the most important numbers from the data.
Return ONLY the JSON block.`;

  await send(reportPrompt, context);
  // After send() completes, parseSuggestions() will have populated reportSpec
  // in the message — ChatPanel picks it up via the useEffect that watches for pendingReportSpec
};

const handleCreateReportSheet = async (spec: ReportSpec) => {
  // Phase 2: Actually create the sheet
  setReportLoading(true);
  setReportError(null);
  setReportSuccess(null);
  setPendingReportSpec(null);

  try {
    const result = await createReportSheet(
      spec,
      reportSourceAddress,
      reportConfigTitle.trim() || undefined,
      reportConfigChartType
    );

    // Sprint 8 integration: register as named range (optional, graceful degradation)
    try {
      const { createNamedRange, addNamedRange, generateFaitName, toAbsoluteReference } =
        await import('../services/excelWriter').then(() =>
          import('../services/namedRangeStorage')
        );
      // Actually — static imports are cleaner. If S8 is landed, these are already imported.
      // See note below on import strategy.
    } catch {
      // S8 not landed or failed — skip named range registration
    }

    setReportSuccess(`Report sheet created: "${result.sheetName}"`);
  } catch (e) {
    const msg = e instanceof Error ? e.message : 'Unknown error';
    setReportError(`Report creation failed: ${msg}`);
  } finally {
    setReportLoading(false);
  }
};
```

**Important note on S8 import strategy:** Use static imports at the top of `ChatPanel.tsx`. If S8 is not yet landed when S10 ships, the spec must be read as "Sprint 10 depends on Sprint 8 being landed." Both will be shipped together. The `try/catch` around the named range registration handles runtime failures (e.g., `createNamedRange()` throws `EXCEL_ERROR`) but not missing module imports.

**Revised S8 integration in `handleCreateReportSheet`:**

```typescript
// After successful createReportSheet:
try {
  const nameForReport = generateFaitName('report');
  await createNamedRange(nameForReport, result.reportAddress, 'FAIT report sheet');
  const entry: FaitNamedRange = {
    name: nameForReport,
    address: toAbsoluteReference(result.reportAddress).slice(1),
    created: new Date().toISOString(),
  };
  await addNamedRange(entry);
  setNamedRanges((prev) => [...prev, entry]);
} catch {
  // Named range registration failed — report still created successfully
}
```

This requires `generateFaitName`, `toAbsoluteReference`, `addNamedRange` imported from `namedRangeStorage.ts`, and `createNamedRange`, `FaitNamedRange` from their respective modules.

**Change 5: Add `useEffect` to capture `reportSpec` from message stream**

After FAIT's response comes in with a `report_spec` block, `parseSuggestions()` populates `message.reportSpec`. We need to detect this and offer the "Create Report Sheet" button.

Add a `useEffect` that watches `messages` for a new assistant message with `reportSpec`:

```typescript
// Sprint 10: Watch for report_spec in the latest assistant message
useEffect(() => {
  const lastMsg = messages[messages.length - 1];
  if (
    lastMsg?.role === 'assistant' &&
    !lastMsg.streaming &&
    lastMsg.reportSpec &&
    !pendingReportSpec
  ) {
    setPendingReportSpec(lastMsg.reportSpec);
    setReportConfigTitle(lastMsg.reportSpec.title);
    setReportConfigChartType(lastMsg.reportSpec.chartSpec?.type ?? 'column');
  }
}, [messages]); // eslint-disable-line react-hooks/exhaustive-deps
```

**Change 6: Add report config panel and "Create Report Sheet" button to JSX**

**Report config panel** (shown when user clicks `/report` from the slash picker, before analysis runs):

Add after the watch mode status bar and before the FORGE search bar:

```typescript
{/* ── Sprint 10: Report config panel ── */}
{showReportConfig && (
  <div
    style={{
      padding: '10px 12px',
      borderBottom: '1px solid #2e3f54',
      background: '#111d2b',
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: '8px',
    }}
  >
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      📊 Generate Report Sheet
    </div>
    <div style={{ fontSize: '11px', color: '#8899aa' }}>
      FAIT will analyze the selected range and create a report sheet with summary, 
      key metrics, and a chart.
    </div>

    {/* Chart type selector */}
    <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
      <span style={{ fontSize: '11px', color: '#8899aa', flexShrink: 0 }}>Chart:</span>
      {(['column', 'bar', 'line', 'pie'] as const).map((type) => (
        <button
          key={type}
          onClick={() => setReportConfigChartType(type)}
          style={{
            background: reportConfigChartType === type ? '#1e3a5f' : '#1a2332',
            border: `1px solid ${reportConfigChartType === type ? '#2e5080' : '#2e3f54'}`,
            borderRadius: '4px',
            color: reportConfigChartType === type ? '#d4af37' : '#556677',
            fontSize: '11px',
            padding: '3px 8px',
            cursor: 'pointer',
            textTransform: 'capitalize',
          }}
        >
          {type}
        </button>
      ))}
    </div>

    <div style={{ display: 'flex', gap: '6px' }}>
      <button
        onClick={handleReportAnalyze}
        disabled={!selectionInfo}
        style={{
          background: selectionInfo ? '#1a3020' : '#1e2d3e',
          border: `1px solid ${selectionInfo ? '#2e5040' : '#2e3f54'}`,
          borderRadius: '4px',
          color: selectionInfo ? '#6fcf97' : '#445566',
          fontSize: '11px',
          fontWeight: '600',
          padding: '5px 12px',
          cursor: selectionInfo ? 'pointer' : 'not-allowed',
        }}
      >
        {selectionInfo ? `Analyze ${selectionInfo.address}` : 'Select a range first'}
      </button>
      <button
        onClick={() => setShowReportConfig(false)}
        style={{
          background: 'none',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          color: '#556677',
          fontSize: '11px',
          padding: '5px 8px',
          cursor: 'pointer',
        }}
      >
        Cancel
      </button>
    </div>
  </div>
)}
```

**"Create Report Sheet" button** — this is a post-response action, similar to the "↓ Write to Sheet" button but shown at the bottom of the chat (not inline in the message bubble). Show it when `pendingReportSpec !== null` and `!reportLoading`:

Add after the write-table success toast and name-range prompt:

```typescript
{/* ── Sprint 10: Create Report Sheet action ── */}
{pendingReportSpec && !reportLoading && (
  <div
    style={{
      padding: '8px 10px',
      borderBottom: '1px solid #2e3f54',
      background: '#0f1720',
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: '6px',
    }}
  >
    <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
      <input
        value={reportConfigTitle}
        onChange={(e) => setReportConfigTitle(e.target.value)}
        placeholder="Report title"
        maxLength={45}
        style={{
          flex: 1,
          background: '#1a2332',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          color: '#e8edf3',
          padding: '5px 8px',
          fontSize: '12px',
          outline: 'none',
        }}
      />
      <button
        onClick={() => handleCreateReportSheet(pendingReportSpec)}
        style={{
          background: '#1a3020',
          border: '1px solid #2e5040',
          borderRadius: '4px',
          color: '#6fcf97',
          fontSize: '11px',
          fontWeight: '600',
          padding: '5px 12px',
          cursor: 'pointer',
          whiteSpace: 'nowrap',
        }}
      >
        📋 Create Report Sheet
      </button>
      <button
        onClick={() => { setPendingReportSpec(null); setReportError(null); }}
        style={{
          background: 'none',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          color: '#556677',
          fontSize: '11px',
          padding: '5px 8px',
          cursor: 'pointer',
        }}
      >
        ✕
      </button>
    </div>
    {reportError && (
      <div style={{ fontSize: '11px', color: '#e07070' }}>{reportError}</div>
    )}
  </div>
)}

{reportLoading && (
  <div style={{ padding: '6px 10px', fontSize: '11px', color: '#8899aa', borderBottom: '1px solid #2e3f54', flexShrink: 0 }}>
    Creating report sheet…
  </div>
)}

{reportSuccess && !pendingReportSpec && (
  <div
    style={{
      padding: '6px 10px',
      borderBottom: '1px solid #2e3f54',
      background: '#0f2a1a',
      color: '#6fcf97',
      fontSize: '11px',
      display: 'flex',
      justifyContent: 'space-between',
      alignItems: 'center',
      flexShrink: 0,
    }}
  >
    <span>✓ {reportSuccess}</span>
    <button
      onClick={() => setReportSuccess(null)}
      style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
    >
      ✕
    </button>
  </div>
)}
```

---

### Task 6: `src/taskpane/hooks/useChat.ts`

Propagate `reportSpec` from `parseSuggestions`.

**Update `Message` interface:**

```typescript
import type { ReportSpec } from '../services/reportBuilder';

export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
  reportSpec?: ReportSpec | null;   // ← NEW
}
```

**In `send()`, update the destructure of `parseSuggestions(rawText)`:**

```typescript
// BEFORE
const { displayText, suggestions, tableData } = parseSuggestions(rawText);

// AFTER
const { displayText, suggestions, tableData, reportSpec } = parseSuggestions(rawText);
```

**Update the finalized message object:**

```typescript
next[assistantIndex] = {
  role: 'assistant',
  content: displayText,
  streaming: false,
  tableData: tableData ?? null,
  reportSpec: reportSpec ?? null,   // ← ADD
};
```

**Do NOT change** any other logic in `useChat.ts`.

---

## Files Changed Summary

| File | Change type | Description |
|------|-------------|-------------|
| `src/taskpane/services/reportBuilder.ts` | **NEW** | `createReportSheet()`; `ReportSpec`; `KeyMetric`; sheet creation, content writes, chart insert |
| `src/taskpane/services/suggestionParser.ts` | Modify | Add `ReportSpec` import + `reportSpec` field to `ParseResult`; `report_spec` parser |
| `src/taskpane/services/chartBuilder.ts` | Modify | Add optional `sheetName` param to `insertChart()` |
| `src/taskpane/components/SlashCommandPicker.tsx` | Modify | Add `/report` entry; update `onSelect` to pass command name |
| `src/taskpane/components/ChatPanel.tsx` | Modify | Report state; config panel; `handleReportAnalyze()`; `handleCreateReportSheet()`; "Create Report Sheet" action bar |
| `src/taskpane/hooks/useChat.ts` | Modify | `reportSpec` on `Message`; propagate from `parseSuggestions` |

**1 new file + 5 modified. No new npm packages.**

---

## UX Flow — Exact Sequences

### Flow A: Full `/report` flow

```
1. User selects A1:D12 (revenue data by region)
2. User types "/" → slash picker appears → selects "/report"
3. Report config panel opens (not pasted into input):
   "📊 Generate Report Sheet"
   Chart type: [column] [bar] [line] [pie]
   [Analyze Sheet1!A1:D12]  [Cancel]
4. User selects "column" chart type → clicks "Analyze Sheet1!A1:D12"
5. Config panel closes; FAIT analysis prompt sent (with context block)
6. FAIT streams response — user sees in chat:
   "FAIT: [a report_spec JSON block, stripped from display]"
   (displayText = empty or brief intro; report_spec parsed into pendingReportSpec)
7. "Create Report Sheet" action bar appears below messages:
   [Q1 Revenue Analysis               ] [📋 Create Report Sheet] [✕]
   (title pre-filled from spec.title; user can edit)
8. User edits title to "Q1 Revenue — FAIT Analysis" → clicks "📋 Create Report Sheet"
9. createReportSheet() runs:
   - Deletes any existing "FAIT Report — 2026-03-16" sheet
   - Creates new sheet "FAIT Report — 2026-03-16", tab gold
   - Positions it after Sheet1
   - Writes title, summary, metrics table (with zebra stripes)
   - Inserts column chart bound to metrics table data
   - Registers as FAIT_report_20260316_143022 in named ranges
10. Action bar replaced by: "✓ Report sheet created: 'FAIT Report — 2026-03-16'"
11. Excel switches to the new sheet (Excel's behavior after sheet creation)
```

### Flow B: Idempotent re-run (same day)

```
1. User runs /report again on the same day
2. createReportSheet() deletes existing "FAIT Report — 2026-03-16"
   (existingSheet.isNullObject = false → existingSheet.delete())
3. Fresh report created with updated analysis
4. The old named range entry in the registry is now stale — but it still exists in
   the workbook's names (pointing to the deleted sheet). Clint should note this.
   Mitigation: the named range registration uses a new timestamp, creating a new name.
   The old FAIT_report_20260316_143022 entry becomes stale in the registry but harmless.
```

### Flow C: FAIT returns malformed `report_spec`

```
1. FAIT returns a response without a valid report_spec block
2. parseSuggestions() returns reportSpec = null
3. pendingReportSpec never gets set
4. No "Create Report Sheet" button appears
5. FAIT's text response is shown normally in the chat
6. User re-asks or proceeds manually
```

---

## Report Sheet Layout (Visual)

```
Row 1:  [FAIT Report — 2026-03-16              ] ← gold 16pt bold, merged A1:F1, dark bg
Row 2:  [blank]
Row 3:  Summary                                  ← grey label 10pt
Row 4:  [The selected data shows Q1 revenue...  ] ← wrapped text, merged A4:F4
Row 5:  [blank]
Row 6:  Key Metrics                              ← grey label 10pt
Row 7:  [Metric       | Value  | Note           ] ← gold headers, blue bg (A7:C7)
Row 8:  [Total Revenue| $4.2M  | 12% above target] ← zebra row 1
Row 9:  [Top Region   | North  | 43% of total   ] ← zebra row 2
Row 10: [YoY Growth   | +8.3%  |                ] ← zebra row 3
...
Row N:  [last metric]
               ← Chart floats over D7:L20 (approximately)
```

**Sheet tab:** gold `#D4AF37`  
**Background theme:** matches FAIT dark theme (`#0F1720`, `#131F2E`, `#1A3A5F`)  
**Font colors:** gold `#D4AF37` for titles/headers, grey `#8899AA` for labels, white `#E8EDF3` for data

---

## ExcelApi Requirement Analysis

| API | Min version | Used in Sprint 10 |
|-----|-------------|------------------|
| `worksheets.add(name)` | 1.1 | ✅ Create report sheet |
| `worksheet.tabColor` | **1.7** | ✅ Gold tab |
| `worksheet.position` | 1.1 | ✅ Position after source |
| `worksheet.delete()` | 1.1 | ✅ Idempotent re-run |
| `range.merge(across)` | 1.2 | ✅ Title + summary merge |
| `range.format.font.*` | 1.1 | ✅ Title formatting |
| `range.format.fill.color` | 1.1 | ✅ Background colors |
| `range.format.rowHeight` | 1.2 | ✅ Summary row height |
| `range.format.wrapText` | 1.1 | ✅ Summary text wrap |
| `range.format.columnWidth` | 1.2 | ✅ Column widths |
| `charts.add()` | 1.1 | ✅ Create chart |
| `chart.top/left/width/height` | 1.1 | ✅ Position chart |
| `worksheet.getItemOrNullObject()` | 1.4 | ✅ Idempotent delete check |

**All APIs ≤ ExcelApi 1.7 (tab color). Baseline is 1.13. No manifest change.**

---

## Acceptance Criteria

1. `/report` appears in the slash command picker and triggers the config panel (not input paste)
2. Config panel shows chart type selector and "Analyze [address]" button; button disabled when no selection
3. After "Analyze", FAIT sends the analysis prompt with full context; `report_spec` JSON block is returned and parsed
4. "Create Report Sheet" action bar appears with pre-filled title (editable); visible until dismissed
5. `createReportSheet()` creates a new worksheet named `FAIT Report — YYYY-MM-DD` with gold tab
6. Report sheet contains: title (merged A1:F1, gold), summary (wrapped A4:F4), key metrics table (A7:C7+), column chart (approximately D7:L20)
7. **Source sheet is untouched** — no writes, no navigation away during creation
8. Same-day re-run deletes the existing report sheet before creating a new one
9. Named range registered (if S8 is available) — `FAIT_report_YYYYMMDD_HHMMSS`
10. Success banner shows sheet name after creation; dismiss button works
11. `insertChart(spec)` (no `sheetName`) still works identically — no regression for existing sprint 4 chart flow
12. All Sprint 1–9 features unchanged

---

## Constraints for CC

- Touch only the 6 files listed (1 new, 5 modified)
- `createReportSheet()` must call `setFaitWriting(true)` before any Excel work and `setFaitWriting(false)` in a `finally` block — it modifies the workbook and must suppress watch mode triggers
- Do NOT navigate to the report sheet programmatically — Excel naturally activates the newly created sheet. Do not call `newSheet.activate()`.
- Sheet name `FAIT Report — 2026-03-16` uses an em dash (—, U+2014) — not a hyphen. This is a valid sheet name character. Confirm the character is copied correctly.
- `range.merge(false)` merges the range but does NOT merge across rows — `false` = do not merge across rows (i.e., merges all cells into one). This is the correct parameter for title and summary cells.
- The chart's `dataRange` in the report spec passed to `insertChart()` must be relative to the **report sheet** (e.g., `A7:B10`), not the source sheet. `insertChart(spec, sheetName)` calls `sheet.getRange(spec.dataRange)` where `sheet` is the report sheet. If `spec.chartSpec.dataRange` references the source sheet (e.g., `Sheet1!A1:D10`), it will target the wrong sheet. The `reportBuilder.ts` code overrides this with `dataRange: 'A7:B${metricsEndRow}'` — confirm this override is in place.
- `handleCreateReportSheet()` must NOT be called twice — the "Create Report Sheet" button sets `setPendingReportSpec(null)` first, then calls `createReportSheet()`. Confirm the button's `onClick` handler prevents double-click (e.g., checks `reportLoading`).

---

## Clint Review Priorities

```
⚠️  HIGH: Verify sheet name "FAIT Report — YYYY-MM-DD" uses em dash (U+2014), not hyphen.
          Excel sheet names cannot contain only the 8 chars: / \ * ? : [ ]
          The em dash is valid but the copy-paste from spec to code must preserve it.
          Test: confirm the sheet tab reads "FAIT Report — 2026-03-16" not "FAIT Report - 2026-03-16".

⚠️  HIGH: Confirm chart dataRange is A7:B{end} relative to the report sheet, NOT a
          reference back to the source sheet. The code in reportBuilder.ts overrides
          spec.chartSpec.dataRange with `A7:B${metricsEndRow}`. Verify this override
          fires BEFORE insertChart() is called with the chartSpecForReport object.

⚠️  HIGH: Verify setFaitWriting(true) is set and cleared in a finally block in
          createReportSheet(). The function does multiple Excel.run() calls — if the
          first succeeds but the second throws, setFaitWriting must still be cleared.
          Confirm the finally wraps all Excel.run() calls.

⚠️  MEDIUM: Verify range.merge(false) behavior. The second argument to .merge() is
            `across: boolean` — `false` means merge ALL cells into one (not across-rows).
            This is what we want for title (A1:F1) and summary (A4:F4).
            Confirm: `newSheet.getRange('A1:F1').merge(false)` produces one merged cell.

⚠️  MEDIUM: Same-day idempotency: if the user runs /report twice in one day, the first
            sheet is deleted and recreated. The named range from the first run
            (FAIT_report_20260316_143022) now points to a deleted sheet — it's stale
            in the workbook names. The second run creates a NEW name with a new timestamp.
            The registry now has two entries for the same day. Flag for Fred: consider
            whether registry cleanup on same-day re-run is needed (out of scope for S10).

⚠️  MEDIUM: Confirm the chart positioning (top:120, left:220, width:380, height:240)
            doesn't overlap the metrics table. With metrics at A7:C15 (~rows 7-15),
            the chart anchored at D9 (left:220) floats to the right of column C.
            Verify column C ends before pixel 220 (3 columns × ~73px = ~220px — tight).
            Consider bumping left to 240 for safety.

⚠️  LOW: Confirm `worksheets.getItemOrNullObject(sheetName)` is available on ExcelApi 1.4
         (it is). The isNullObject check must be AFTER ctx.sync().

⚠️  LOW: Confirm the "/report" slash command entry uses the sentinel value '__REPORT_COMMAND__'
         for the prompt field — this string must never be passed to handleSend(). The ChatPanel
         onSelect handler intercepts `name === 'report'` before any prompt is used.
```

---

## Architectural Note: Why Two `Excel.run()` Calls in `createReportSheet`

`createReportSheet()` calls `insertChart()` which owns its own `Excel.run()`. This means the report builder uses two contexts: one for sheet creation + content writing, another (inside `insertChart()`) for chart creation + a third for chart repositioning.

This is a deliberate tradeoff: keeping `insertChart()` as a self-contained reusable service with its own context is more maintainable than merging it into the report builder's context. The performance cost is negligible for a one-time report generation workflow. The alternative — inlining the chart creation into the report builder's `Excel.run()` — would duplicate chart logic and break the existing chart service abstraction.

The `setFaitWriting(true/false)` wrapping at the `createReportSheet()` level (not inside each individual `Excel.run()`) ensures the flag is set for the entire report generation sequence, covering all three contexts.

---

_Spec by Reed Richards | Sprint 10 is 1 new file + 5 edits. No new packages — Excel's native chart API handles everything. The key insight: Chart.js + canvas + base64 is the PowerPoint pattern (FfP has no chart API); Excel has had `charts.add()` since ExcelApi 1.1._
