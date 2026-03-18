# CC Brief: WI826 — FfE S10: Multi-Sheet Report Generation

## Overview
Implement the `/report` slash command and report sheet generation for FAIT for Excel.
Two-phase flow: FAIT analyzes workbook data → returns `report_spec` JSON → user clicks
"Create Report Sheet" → formatted Excel sheet is created.

**1 new file + 5 modified. No new npm packages.**

Working directory: `/home/fredw/projects/fait-for-excel`

---

## Files to touch

1. `src/taskpane/services/reportBuilder.ts` — **NEW FILE** (create from scratch)
2. `src/taskpane/services/suggestionParser.ts` — add `report_spec` parser
3. `src/taskpane/services/chartBuilder.ts` — add optional `sheetName` param
4. `src/taskpane/components/SlashCommandPicker.tsx` — add `/report` entry
5. `src/taskpane/components/ChatPanel.tsx` — report state, config panel, handlers, action bar
6. `src/taskpane/hooks/useChat.ts` — `reportSpec` on `Message`

---

## Task 1: CREATE `src/taskpane/services/reportBuilder.ts`

Create this file with EXACTLY this content:

```typescript
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
```

---

## Task 2: MODIFY `src/taskpane/services/suggestionParser.ts`

### 2a. Add import at top (after existing imports)

Add this import after the `import type { SortFilterSpec }` line:

```typescript
import type { ReportSpec } from './reportBuilder';
```

### 2b. Add `reportSpec` field to `ParseResult` interface

Find the `ParseResult` interface and add `reportSpec` field:

```typescript
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;
  reportSpec: ReportSpec | null;   // ← NEW Sprint 10
}
```

### 2c. Add `reportSpec` variable initialization

In `parseSuggestions()`, after `let tableData: ParsedTable | null = null;`, add:

```typescript
  let reportSpec: ReportSpec | null = null;
```

### 2d. Add `report_spec` parser block

Insert this block AFTER the sort_filter_spec block and BEFORE the table_data block:

```typescript
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
```

### 2e. Update return statement

Change the return from:
```typescript
  return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData };
```
to:
```typescript
  return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData, reportSpec };
```

---

## Task 3: MODIFY `src/taskpane/services/chartBuilder.ts`

Update `insertChart` signature and sheet selection. Change:

```typescript
export async function insertChart(spec: ChartSpec): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
```

To:

```typescript
export async function insertChart(spec: ChartSpec, sheetName?: string): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const sheet = sheetName
      ? ctx.workbook.worksheets.getItem(sheetName)
      : ctx.workbook.worksheets.getActiveWorksheet();
```

All other code in `chartBuilder.ts` is UNCHANGED.

---

## Task 4: MODIFY `src/taskpane/components/SlashCommandPicker.tsx`

### 4a. Add `/report` as FIRST entry in COMMANDS array

Insert this object as the FIRST element of the `COMMANDS` array (before `audit`):

```typescript
  {
    name: 'report',
    description: 'Generate an analysis report sheet from selected data',
    prompt: '__REPORT_COMMAND__',
  },
```

### 4b. Update `SlashCommandPickerProps` interface

Change:
```typescript
interface SlashCommandPickerProps {
  query: string;
  onSelect: (prompt: string) => void;
  onClose: () => void;
}
```

To:
```typescript
interface SlashCommandPickerProps {
  query: string;
  onSelect: (prompt: string, name?: string) => void;
  onClose: () => void;
}
```

### 4c. Update the onClick handler in the filtered.map

Change:
```typescript
          onClick={() => onSelect(cmd.prompt)}
```

To:
```typescript
          onClick={() => onSelect(cmd.prompt, cmd.name)}
```

### 4d. Update the keyboard Enter handler

Change:
```typescript
        onSelect(filtered[activeIndex].prompt);
```

To:
```typescript
        onSelect(filtered[activeIndex].prompt, filtered[activeIndex].name);
```

---

## Task 5: MODIFY `src/taskpane/components/ChatPanel.tsx`

### 5a. Add imports after existing imports

After the line `import type { ParsedTable } from '../services/suggestionParser';`, add:

```typescript
import { createReportSheet } from '../services/reportBuilder';
import type { ReportSpec } from '../services/reportBuilder';
```

### 5b. Add Sprint 10 state

After the Sprint 9 watch mode state block (after the `const debounceTimerRef` line), add:

```typescript
  // ── Sprint 10: Report generation state ────────────────────────────────────
  const [showReportConfig, setShowReportConfig] = useState(false);
  const [reportConfigTitle, setReportConfigTitle] = useState('');
  const [reportConfigChartType, setReportConfigChartType] = useState<'column' | 'bar' | 'line' | 'pie'>('column');
  const [pendingReportSpec, setPendingReportSpec] = useState<ReportSpec | null>(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [reportError, setReportError] = useState<string | null>(null);
  const [reportSuccess, setReportSuccess] = useState<string | null>(null);
  const [reportSourceAddress, setReportSourceAddress] = useState('');
```

### 5c. Add Sprint 10 useEffect to capture reportSpec from messages

After the Sprint 9 cleanup `useEffect` (the one with `debounceTimerRef`), add:

```typescript
  // ── Sprint 10: Capture reportSpec from latest assistant message ───────────
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

### 5d. Add Sprint 10 handlers

After `handleNameRangeKeyDown` and before the Sprint 9 `handleWatchToggle`, add:

```typescript
  // ── Sprint 10: Report handlers ─────────────────────────────────────────────

  const handleReportAnalyze = async () => {
    setShowReportConfig(false);
    setReportError(null);
    setReportSuccess(null);
    setPendingReportSpec(null);

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
- chartSpec: object with keys: type ("column"|"bar"|"line"|"pie"), title (string), dataRange (string — use "A7:B14" as a placeholder since the actual range will be computed), hasHeaders (true), seriesBy ("columns")

The keyMetrics should highlight the most important numbers from the data.
Return ONLY the JSON block, no prose before or after it.`;

    await send(reportPrompt, context);
  };

  const handleCreateReportSheet = async () => {
    if (reportLoading || !pendingReportSpec) return;

    const spec = pendingReportSpec;
    setReportLoading(true);
    setReportError(null);
    setReportSuccess(null);
    setPendingReportSpec(null);

    setFaitWriting(true);
    try {
      const result = await createReportSheet(
        spec,
        reportSourceAddress,
        reportConfigTitle.trim() || undefined,
        reportConfigChartType
      );

      // Sprint 8 integration: register as named range (graceful degradation)
      try {
        const nameForReport = generateFaitName('report');
        await createNamedRange(nameForReport, result.reportAddress, 'FAIT report sheet');
        const entry: FaitNamedRange = {
          name: nameForReport,
          address: toAbsoluteReference(result.reportAddress),
          created: new Date().toISOString(),
        };
        await addNamedRange(entry);
        setNamedRanges((prev) => [...prev, entry]);
      } catch {
        // S8 named range registration failed — report still created successfully
      }

      setReportSuccess(`Report sheet created: "${result.sheetName}"`);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      setReportError(`Report creation failed: ${msg}`);
    } finally {
      setFaitWriting(false);
      setReportLoading(false);
    }
  };
```

### 5e. Update the SlashCommandPicker onSelect handler in JSX

Find this block in the JSX:
```typescript
          <SlashCommandPicker
            query={slashQuery}
            onSelect={(prompt) => {
              setInputText(prompt);
            }}
            onClose={() => setInputText('')}
          />
```

Replace with:
```typescript
          <SlashCommandPicker
            query={slashQuery}
            onSelect={(prompt, name) => {
              if (name === 'report') {
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

### 5f. Add Report Config Panel to JSX

Insert the report config panel AFTER the watch mode active status bar block (after the closing `}` of `{watchModeOn && (...)}`), and BEFORE the `{/* CF inline prompt input */}` section:

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
            FAIT will analyze the selected range and create a formatted report sheet.
          </div>
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
              onClick={() => void handleReportAnalyze()}
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

### 5g. Add Create Report Sheet action bar to JSX

Insert these blocks AFTER the `{/* ── Sprint 8: Name range prompt ── */}` section (after its closing `}`), and BEFORE the `{/* FORGE search bar */}` section:

```typescript
      {/* ── Sprint 10: Create Report Sheet action bar ── */}
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
              onClick={() => void handleCreateReportSheet()}
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
        <div
          style={{
            padding: '6px 10px',
            fontSize: '11px',
            color: '#8899aa',
            borderBottom: '1px solid #2e3f54',
            flexShrink: 0,
          }}
        >
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

## Task 6: MODIFY `src/taskpane/hooks/useChat.ts`

### 6a. Add ReportSpec import

After `import { parseSuggestions, type ParsedTable } from '../services/suggestionParser';`, add:

```typescript
import type { ReportSpec } from '../services/reportBuilder';
```

### 6b. Add `reportSpec` to `Message` interface

Change:
```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
}
```

To:
```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
  reportSpec?: ReportSpec | null;   // Sprint 10
}
```

### 6c. Update parseSuggestions destructure in `send()`

Change:
```typescript
      const { displayText, suggestions, tableData } = parseSuggestions(rawText);
```

To:
```typescript
      const { displayText, suggestions, tableData, reportSpec } = parseSuggestions(rawText);
```

### 6d. Update the finalized assistant message

Change:
```typescript
        next[assistantIndex] = {
          role: 'assistant',
          content: displayText,
          streaming: false,
          tableData: tableData ?? null,
        };
```

To:
```typescript
        next[assistantIndex] = {
          role: 'assistant',
          content: displayText,
          streaming: false,
          tableData: tableData ?? null,
          reportSpec: reportSpec ?? null,
        };
```

---

## Critical Checks After Implementation

1. Verify the em dash `—` (U+2014) is in the sheet name: `FAIT Report — ${today}`
   - Run: `grep "FAIT Report" src/taskpane/services/reportBuilder.ts`
   - Should show `—` not `-`

2. Verify `chartSpecForReport.dataRange` is overridden BEFORE `insertChart()` call
   - The dataRange must be `A7:B${result.metricsEndRow}`, NOT any original value from spec

3. Verify `setFaitWriting(true)` wraps `createReportSheet` body with `finally { setFaitWriting(false) }`

4. Verify `range.merge(false)` — NOT `merge(true)` — for A1:F1 and A4:F4

5. Verify `existing.load('isNullObject')` then `await ctx.sync()` then `if (!existing.isNullObject)`

6. Verify double-click guard: `if (reportLoading || !pendingReportSpec) return;` at top of `handleCreateReportSheet`

7. Run `npm run build` — must compile with zero errors
