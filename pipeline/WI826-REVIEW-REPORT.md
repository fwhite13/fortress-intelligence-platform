# Review Report: WI826
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fait-for-excel
cat review-brief-wi826.md | claude --model sonnet -p
```

**First 20 lines of output:**
```
## WI826 Code Review Verdict

---

### CRITICAL: `setFaitWriting` Double-Ownership Bug — **MUST FIX**

**Confirmed.** The execution sequence is:

```
ChatPanel line 767:   setFaitWriting(true)
  → createReportSheet() line 48: setFaitWriting(true)   // redundant, no harm yet
  → Excel.run() writes title, metrics, chart...
  → createReportSheet() finally line 186: setFaitWriting(false)  // ← FLAG CLEARED HERE
← control returns to ChatPanel line 776
ChatPanel lines 777-789: createNamedRange() + addNamedRange()  // ← watch guard is GONE
ChatPanel finally line 796: setFaitWriting(false)  // ← no-op
```

The named range registration happens entirely outside the watch guard. Since `watchMode.ts` is a
module-level singleton (not per-call), any `onChanged` events fired during `createNamedRange()` will
see `isFaitWriting() === false` and could trigger a spurious FAIT re-query.
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| Em dash U+2014 in sheet name | ✅ | `python3` char analysis: `0x2014` confirmed at `reportBuilder.ts:46` — `const sheetName = \`FAIT Report — ${today}\`` |
| chartSpec.dataRange override before insertChart() | ✅ | `reportBuilder.ts:163-177`: spread creates copy, `dataRange: \`A7:B${result.metricsEndRow}\`` set before `insertChart(chartSpecForReport, sheetName)` call |
| setFaitWriting(false) in finally | ✅ | Both `reportBuilder.ts:185-187` and `ChatPanel.tsx:795-797` have `finally` blocks — but see Issues Found |
| merge(false) for title and summary | ✅ | `reportBuilder.ts` line ~90: `newSheet.getRange('A1:F1').merge(false)` and line ~103: `newSheet.getRange('A4:F4').merge(false)` |
| isNullObject read after ctx.sync() | ✅ | `reportBuilder.ts:53-58`: `existing.load('isNullObject')` → `await ctx.sync()` → `if (!existing.isNullObject)` — correct sync boundary |
| Double-click guard in handleCreateReportSheet | ✅ | `ChatPanel.tsx:759`: `if (reportLoading \|\| !pendingReportSpec) return;` — first statement in handler |
| chartBuilder.ts backward compatible (sheetName optional) | ✅ | `chartBuilder.ts:14`: `insertChart(spec: ChartSpec, sheetName?: string)` — optional param with `getActiveWorksheet()` fallback; S8/S9 call at line 1902 `await insertChart(chartSpec)` unchanged and functional |
| stripAllSpecs() covers report_spec | ✅ | No standalone `stripAllSpecs()` exists — stripping is done inline within `parseSuggestions()` at line 134 (`displayText = displayText.replace(reportSpecMatch[0], '')`), consistent with all other spec parsers. Functionally correct. |
| parseSuggestions() called once, reportSpec destructured | ✅ | `useChat.ts:109`: `const { displayText, suggestions, tableData, reportSpec } = parseSuggestions(rawText);` — single call, `reportSpec` is the 4th named field |
| No new npm packages | ✅ | `git diff HEAD~1 -- package.json` returns empty — no new dependencies |
| Only 6 specified files changed | ✅ | `git diff HEAD~1 --name-only` returns exactly the 6 expected files |

---

## Issues Found

### IMPORTANT — `setFaitWriting` Double-Ownership (Watch Mode Guard Broken)

**Files:** `reportBuilder.ts` (lines 5, 48, 185–187) + `ChatPanel.tsx` (lines 767, 795–797)

`createReportSheet()` manages `setFaitWriting` internally:
- Sets `true` at line 48
- Sets `false` in its own `finally` at line 186

`ChatPanel.tsx::handleCreateReportSheet()` also manages the flag:
- Sets `true` at line 767 (before calling `createReportSheet()`)
- Sets `false` in its own `finally` at line 796

**The problem:** When `createReportSheet()` completes, it sets `faitWriting=false` before returning to ChatPanel. The named range registration code (`createNamedRange()` + `addNamedRange()`) at lines 777–789 then executes with `_isFaitWriting === false`. Any `onChanged` events fired by those Excel writes will NOT be suppressed by the watch mode guard — potentially triggering a spurious FAIT re-query in watch mode.

**Fix:** Remove `setFaitWriting` from `reportBuilder.ts`. It is a library function and should not own shared write-guard state. ChatPanel already has the correct try/finally frame that covers the full operation.

Lines to remove from `reportBuilder.ts`:
- Line 5: `import { setFaitWriting } from './watchMode';`
- Line 48: `setFaitWriting(true);`
- Lines 185–187: `} finally { setFaitWriting(false); }`

### NITPICK — `KeyMetric.value` typed as `string` (consistency map says `string | number`)

**File:** `reportBuilder.ts:9`

Interface declares `value: string`. The consistency map specifies `string | number`. The parser normalizes to string via `String(m.value ?? '')` so the interface is internally consistent with actual usage. Non-blocking — document the coercion intent with a JSDoc comment if this interface is ever public-facing.

### NITPICK — `sourceAddress` parameter name could mislead

**File:** `reportBuilder.ts:38`

`createReportSheet(spec, sourceAddress, ...)` — the parameter is only used to derive `sourceSheetName` for tab positioning (lines 63–77). It is never used as the chart's data source address. A future maintainer might expect it to influence the chart `dataRange`. Consider renaming to `sourceSheetRef` or adding a clarifying JSDoc. Non-blocking.

---

## Verdict

**NEEDS-CHANGES.** One required fix before merge.

The implementation is architecturally sound: all Excel API patterns are correct (null object guard, sync boundaries, merge API, backward compat), types are consistent across all 6 files, the em dash is correct, the chart dataRange override is properly scoped, and the new file count is exact.

The single blocker is the `setFaitWriting` double-ownership: `reportBuilder.ts` resets the watch-mode guard before ChatPanel's named range registration runs, leaving those Excel writes unguarded in watch mode. Fix is 3 line deletions from `reportBuilder.ts`. Send back to Tony for the targeted fix — no scope creep, no other changes needed.

---

## Cycle 2 (c1093f8)

### Fix Verification
| Check | Result | Evidence |
|-------|--------|----------|
| setFaitWriting/watchMode absent from reportBuilder.ts | ✅ | `grep` returns empty; file only imports from `chartBuilder`. No reference to `setFaitWriting`, `isFaitWriting`, or `watchMode` anywhere. |
| ChatPanel still owns setFaitWriting guard with finally | ✅ | Lines 767–796: `setFaitWriting(true)` before `createReportSheet()`, `setFaitWriting(false)` in `finally` block. Guard intact and correctly scoped. |
| No scope creep — logic unchanged | ✅ | `createReportSheet()` body identical to Sprint 10 implementation. Only the 3 removals (import + true call + try/finally wrapper) — all write logic, chart insertion, and return values intact. |
| Em dash U+2014 still in sheet name | ✅ | Line 45: `` `FAIT Report — ${today}` `` — explicit comment on line 43 flags character; confirmed U+2014 (—). |
| chartSpec.dataRange override still in place | ✅ | Lines 157–164: `dataRange: \`A7:B${result.metricsEndRow}\`` overrides `spec.chartSpec.dataRange` with report-sheet-relative range before `insertChart()`. |
| merge(false) on title and summary | ✅ | Line 87: `getRange('A1:F1').merge(false)` (title); Line 100: `getRange('A4:F4').merge(false)` (summary). Both present. |

### Claude Code Invocation
```
cat review-brief-wi826-c2.md | claude --model sonnet -p
```
CC confirmed all 6 checks pass. Fix is clean and surgical — exactly 3 removals, no scope creep.

### Cycle 2 Verdict: PASS
