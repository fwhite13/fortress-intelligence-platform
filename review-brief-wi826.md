# Review Brief: WI826 — FfE S10: Multi-Sheet Report Generation

## Objective
Perform a thorough code review of WI826 changes. Commit: 5dbddd1. Build PASS — 57 modules, 0 TS errors.

## Files Changed
1. `src/taskpane/services/reportBuilder.ts` — NEW file
2. `src/taskpane/services/suggestionParser.ts` — report_spec parser + reportSpec on ParseResult
3. `src/taskpane/services/chartBuilder.ts` — optional sheetName param on insertChart()
4. `src/taskpane/components/SlashCommandPicker.tsx` — /report command added
5. `src/taskpane/components/ChatPanel.tsx` — report state, handlers, config panel, action bar
6. `src/taskpane/hooks/useChat.ts` — reportSpec on Message

## Pre-verified findings (by static analysis):

### ✅ Em dash U+2014 in sheet name
Line 46 of reportBuilder.ts: `const sheetName = \`FAIT Report — ${today}\`;`
Python char analysis confirmed: 0x2014 is present. PASS.

### ✅ chartSpec.dataRange override before insertChart()
reportBuilder.ts lines ~155-177: Creates `chartSpecForReport = { ...spec.chartSpec, type: ..., dataRange: \`A7:B${result.metricsEndRow}\`, hasHeaders: true, seriesBy: 'columns', position: {...} }` then calls `insertChart(chartSpecForReport, sheetName)`. Override is correct. PASS.

### ⚠️ setFaitWriting pattern — DUAL CALL ISSUE
`reportBuilder.ts` calls `setFaitWriting(true)` at line 48 and `setFaitWriting(false)` in its own `finally` at line 186.
`ChatPanel.tsx` handleCreateReportSheet() ALSO calls `setFaitWriting(true)` at line 767 and `setFaitWriting(false)` in its `finally` at line 796.

This means:
1. ChatPanel sets faitWriting=true (line 767)
2. createReportSheet() sets faitWriting=true AGAIN (line 48 of reportBuilder.ts) — redundant
3. createReportSheet() sets faitWriting=false in its finally — flag goes false BEFORE named range registration
4. Named range createNamedRange() executes with faitWriting=false — potential watch mode trigger
5. ChatPanel finally sets faitWriting=false again — no-op

The brief says to verify `setFaitWriting(false)` is in `finally` — it IS in both places. However, the double-wrap creates a window where Excel writes (named range registration) happen with the flag cleared.

The check "setFaitWriting(false) in finally" technically PASSES but the dual-ownership pattern is an Important bug.

### ✅ merge(false) for title and summary
reportBuilder.ts line ~76: `newSheet.getRange('A1:F1').merge(false);`
reportBuilder.ts line ~87: `newSheet.getRange('A4:F4').merge(false);`
Both use `merge(false)`. PASS.

### ✅ isNullObject read after ctx.sync()
reportBuilder.ts:
```
const existing = wb.worksheets.getItemOrNullObject(sheetName);
existing.load('isNullObject');
await ctx.sync();
if (!existing.isNullObject) {
```
Correct sync boundary. PASS.

### ✅ Double-click guard in handleCreateReportSheet()
ChatPanel.tsx line 759: `if (reportLoading || !pendingReportSpec) return;` — first statement in handler. PASS.

### ✅ chartBuilder.ts backward compatible
Line 14: `export async function insertChart(spec: ChartSpec, sheetName?: string): Promise<void>`
Has `?` on sheetName. Fallback: `ctx.workbook.worksheets.getActiveWorksheet()`. PASS.
Old S8/S9 call site at line 1902: `await insertChart(chartSpec)` — no sheetName, still works. PASS.

### ❓ stripAllSpecs() — FUNCTION DOES NOT EXIST
`stripAllSpecs()` is NOT a function in suggestionParser.ts. There is no such export.
The `report_spec` regex IS included in `parseSuggestions()` which strips it from displayText (line 111-136).
But if the review brief expected a dedicated `stripAllSpecs()` function, it's absent.
Note: The stripping IS done — within parseSuggestions() itself, each spec block is removed from displayText as it's parsed. So functionally, report_spec IS stripped. But no standalone stripAllSpecs() exists.

### ✅ parseSuggestions() called once, reportSpec destructured
useChat.ts line 109: `const { displayText, suggestions, tableData, reportSpec } = parseSuggestions(rawText);`
Single call. reportSpec is the 4th named field destructured. PASS.

### ✅ No new npm packages
`git diff HEAD~1 -- package.json` returns empty. No new dependencies. PASS.

### ✅ Only 6 specified files changed
`git diff HEAD~1 --name-only` returns exactly the 6 expected files. PASS.

## Consistency Map Check
- `ReportSpec.title`: `string` ✅ (line 14 of reportBuilder.ts)
- `ReportSpec.keyMetrics`: `KeyMetric[]` ✅ (line 15)
- `KeyMetric`: `{ label: string, value: string, note?: string }` ✅ — uses `note` not `unit` (consistent with implementation)
- `reportSpec` on `ParseResult`: `reportSpec: ReportSpec | null` ✅ (line 22 of suggestionParser.ts)
- `reportSpec` on `Message`: `reportSpec?: ReportSpec | null` ✅ (line 11 of useChat.ts)
- Sheet name: `FAIT Report — ${today}` with U+2014 ✅

## Key Issues to Assess

1. **IMPORTANT**: `setFaitWriting` double-ownership — both `reportBuilder.ts` and `ChatPanel.tsx` manage the flag independently. The flag is cleared by `createReportSheet()` BEFORE the named range registration in ChatPanel, meaning those Excel writes happen without the watch-mode guard. Should be either:
   - Remove `setFaitWriting` from `createReportSheet()` (let the caller own the flag), OR
   - Move the named range registration inside `createReportSheet()`, OR
   - Keep both but accept the small risk (named range ops are unlikely to trigger meaningful watch events)

2. **NITPICK**: `KeyMetric.value` is typed as `string` in the interface, but the consistency map says `string | number`. The parser normalizes to string with `String(m.value ?? '')`. This is fine functionally but the interface is slightly narrower than specified.

3. **NITPICK**: `createReportSheet` signature includes `sourceAddress` parameter but it's only used for tab positioning — the parameter name implies it's the data source address, which could confuse future maintainers.

## Overall Assessment
The implementation is solid. The high-priority checks mostly pass. The main concern is the double-setFaitWriting ownership which creates a brief window where named range registration runs with the watch guard cleared. This is an Important-level bug that should be fixed.

Please provide a detailed review verdict covering all the checks above, with particular attention to the setFaitWriting double-ownership issue.
