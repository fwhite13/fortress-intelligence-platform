# FfE Sprint 11 Spec — Formula Intelligence

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** Sprint 7 (Table awareness for context quality) — recommended. No hard dependency.  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What the Source Shows

### Formula handling already exists — partially

`CellSuggestion` interface in `WriteSuggestionsDialog.tsx` already has a `formula: string | null` field. `applySuggestions()` in `excelWriter.ts` already writes formulas: `range.formulas = [[s.formula]]` when `s.formula` is set. So the **write path for formulas is already built**.

What doesn't exist:
- No `formula_spec` JSON block in `suggestionParser.ts`
- No formula preview mechanism (no scratch-cell pattern, no `workbook.functions` calls)
- No `/formula` slash command
- No dedicated formula UX — the existing flow pastes a formula via the `suggestions` JSON block with no preview step

### `workbook.functions` reality — typed methods, not string evaluation

**The critical API limitation:** `workbook.functions` exposes ~300 Excel built-in functions as typed JS methods: `fns.sum(range)`, `fns.vlookup(lookup_value, range, col, exact)`, etc. Each function is a typed call with Excel Range or literal arguments.

**You cannot pass an arbitrary formula string.** There is no `fns.evaluate("=SUM(A1)+IF(B1>0,C1,D1)")`. This is a hard Excel JS API limitation as of 2026.

**Consequence:** Using `workbook.functions` for preview means FAIT must decompose the generated formula into individual `fns.*` method calls — only feasible for simple single-function formulas like `=SUM(A1:A10)` or `=VLOOKUP(...)`. For compound formulas (`=IF(AVERAGE(B2:B10)>1000, "High", "Low")`), `workbook.functions` cannot evaluate the whole expression.

**The general solution: scratch-cell pattern.** Write the formula to a hidden scratch cell, `sync()`, read the computed value, then clear the cell. This evaluates ANY valid Excel formula string without permanently modifying the workbook.

**Decision: Use the scratch-cell pattern for formula preview.** It evaluates arbitrary formulas reliably, works for all formula types FAIT might generate, and is completely transparent to the user. `workbook.functions` is kept as an optional optimization path only for the simplest single-function cases — but the scratch-cell pattern is the spec's primary approach.

### Scratch-cell pattern mechanics

```typescript
await Excel.run(async (ctx) => {
  // Create (or reuse) a VeryHidden scratch sheet
  let scratchSheet = ctx.workbook.worksheets.getItemOrNullObject('__FAIT_SCRATCH__');
  scratchSheet.load('isNullObject');
  await ctx.sync();

  if (scratchSheet.isNullObject) {
    scratchSheet = ctx.workbook.worksheets.add('__FAIT_SCRATCH__');
    scratchSheet.visibility = Excel.SheetVisibility.veryHidden;
    await ctx.sync();
  }

  // Write formula to scratch cell A1
  const scratchCell = scratchSheet.getRange('A1');
  scratchCell.formulas = [[formula]];
  scratchCell.load(['values', 'valueTypes']);
  await ctx.sync();

  const result = scratchCell.values[0][0];
  const valueType = scratchCell.valueTypes[0][0];

  // Clear the scratch cell
  scratchCell.clear(Excel.ClearApplyTo.contents);
  await ctx.sync();

  return { result, valueType };
});
```

`VeryHidden` sheets cannot be shown by the user via Excel's UI — only via API. The scratch sheet is effectively invisible and persistent across sessions. Creating it once and reusing it avoids the add/delete overhead.

### Existing formula write via `suggestions` block

The existing path: FAIT returns a `suggestions` JSON block with `formula: "=SUM(A1:A10)"`. `WriteSuggestionsDialog` shows it, user clicks Accept, `applySuggestions()` writes it. This works but has no preview.

Sprint 11 adds a dedicated `formula_spec` flow with a preview step. The `suggestions` path continues working unchanged.

---

## What Sprint 11 Delivers

1. `/formula` slash command — user describes what formula they want ("sum revenue for North region where Q > 0")
2. FAIT generates the formula + explanation, returns a `formula_spec` JSON block
3. **Formula preview:** FAIT evaluates the formula against the current selection using the scratch-cell pattern — shows the computed result before writing
4. User sees: the formula, its explanation, the previewed value ("→ $42,150"), then confirms
5. Write: formula is written to the user's selected cell via `range.formulas = [[formula]]`
6. Error handling: `#VALUE!`, `#REF!`, `#DIV/0!`, etc. are caught and shown as preview errors

---

## Design Decisions

### Decision 1: `formula_spec` vs reusing `suggestions`

The `suggestions` block already supports formulas (`formula: string | null`). Why add `formula_spec`?

Two reasons:
1. **Preview step.** A `formula_spec` triggers the preview flow. A `suggestions` block goes straight to `WriteSuggestionsDialog` — no preview, no scratch-cell evaluation.
2. **Target cell.** `formula_spec` uses a special `"__SELECTED__"` target address that resolves to whatever cell the user has selected — not a hardcoded address. The user selects the output cell, then invokes `/formula`. The `suggestions` block uses hardcoded addresses.

If FAIT returns a `suggestions` block with a formula, it still uses the existing flow. `formula_spec` is an additive path.

### Decision 2: Scratch sheet lifecycle — create once, keep VeryHidden

The scratch sheet `__FAIT_SCRATCH__` is created on first use and set `VeryHidden`. It persists in the workbook. On subsequent sessions, `getItemOrNullObject()` finds it and reuses it. The user never sees it.

**One concern:** What if the user sends the workbook to a colleague? The scratch sheet travels with the file. It's VeryHidden so the colleague won't see it in the tab bar. If FAIT is not installed for the colleague, it's harmless data overhead. Acceptable tradeoff — the sheet holds nothing after each preview (contents are cleared).

**Alternative rejected:** Create/delete the scratch sheet on every preview. This is slower (two extra `ctx.sync()` calls per preview) and risks leaving a half-created sheet if the session dies mid-preview.

### Decision 3: `/formula` invokes config panel, not input paste

Same pattern as `/report` (Sprint 10). Selecting `/formula` opens a config panel with a text input: "Describe the formula you want." User types the description, clicks "Generate Formula" — this sends the prompt to FAIT. The formula_spec response triggers the preview + write flow.

### Decision 4: Preview target cell

The formula is previewed using the **current selection's top-left cell** as the reference context. When FAIT generates `=VLOOKUP(A5, B2:D20, 3, FALSE)`, the preview evaluates it with A5 and B2:D20 resolved relative to the active sheet. The scratch cell just holds `=VLOOKUP(A5, Sheet1!B2:D20, 3, FALSE)` — it reads the same workbook data the real cell would.

**Important: the formula is NOT written to the scratch sheet with a reference adjustment.** The formula is written verbatim to A1 of the scratch sheet. Cross-sheet references (`Sheet1!A1`) work fine. Single-sheet references (`A1`) in the scratch sheet will resolve to the scratch sheet's own A1 — which is empty. This means simple range-relative formulas like `=SUM(A1:A10)` will evaluate to 0 in the scratch sheet, not the intended range.

**Solution: prefix bare cell addresses with the source sheet name.** If the formula contains references like `A1:A10` (no sheet prefix), rewrite them to `Sheet1!A1:Sheet1!A10` where `Sheet1` is the active worksheet name before writing to the scratch cell.

**Simpler alternative: write formula directly to the target cell if the user confirms.** Skip the scratch sheet for formulas that don't contain cross-references. But this removes the "preview before write" guarantee.

**Decision: for preview purposes, use the scratch cell with a best-effort address prefixing for the active sheet. Add a disclaimer in the preview UI: "Preview is approximate — actual result may differ for relative references." The write still uses the formula verbatim in the target cell (where relative references work correctly).**

This is honest about the limitation without abandoning the feature.

### Decision 5: Formula write target — use `applySingleSuggestion()`

Rather than a new write function, reuse `applySingleSuggestion()` with a `CellSuggestion` where:
- `address`: the user's selected cell address (from `selectionInfo`)
- `formula`: the formula string from `formula_spec`
- `value`: null
- `explanation`: the formula explanation from FAIT

This reuses all existing error handling in `WriteSuggestionsDialog` / `applySuggestions()`. No new write path needed.

But — the existing `WriteSuggestionsDialog` is designed for multi-cell suggestions. Sprint 11 shows the formula inline in the chat (preview bar), not in a dialog. So the write is a direct `applySingleSuggestion()` call from `ChatPanel`, not via `WriteSuggestionsDialog`.

### Decision 6: `workbook.functions` — deferred to future enhancement

`workbook.functions` is useful only for single-function calls with explicit arguments. For FAIT's use case (arbitrary generated formulas), the scratch-cell pattern is universally applicable. Leave `workbook.functions` as a future optimization path — document it in the spec but don't implement it in Sprint 11. The roadmap description mentioned it, but the scratch-cell approach is strictly more capable.

---

## Data Model

### `formula_spec` JSON block shape

```json
{
  "formula_spec": {
    "formula": "=SUMIF(B2:B20, \"North\", C2:C20)",
    "explanation": "Sums the Revenue column (C) for all rows where the Region column (B) equals 'North'",
    "functionNames": ["SUMIF"],
    "targetCell": "__SELECTED__",
    "previewable": true
  }
}
```

Fields:
- `formula`: string — the complete Excel formula, starts with `=`
- `explanation`: string — plain-English explanation of what the formula does
- `functionNames`: string[] — list of Excel functions used (for display: "Uses: SUMIF")
- `targetCell`: string — `"__SELECTED__"` means write to the user's current selection; otherwise an A1 address
- `previewable`: boolean — FAIT sets this false for formulas it knows can't be previewed (e.g., volatile functions like `=NOW()`, `=RAND()`, formulas that depend on other-workbook references)

### `FormulaSpec` TypeScript interface

```typescript
// formulaBuilder.ts (new file)
export interface FormulaSpec {
  formula: string;
  explanation: string;
  functionNames: string[];
  targetCell: string;
  previewable: boolean;
}

export interface FormulaPreviewResult {
  value: string | number | boolean | null;
  valueType: string;   // Excel.RangeValueType: "Boolean" | "Double" | "Error" | "Empty" | "String"
  isError: boolean;
  errorMessage?: string;
}
```

### `ParseResult` addition

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
  reportSpec: ReportSpec | null;
  formulaSpec: FormulaSpec | null;   // ← NEW
}
```

### `Message` addition

```typescript
// useChat.ts
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
  reportSpec?: ReportSpec | null;
  formulaSpec?: FormulaSpec | null;   // ← NEW
}
```

### New `ChatPanel` state

```typescript
// Sprint 11
const [showFormulaConfig, setShowFormulaConfig] = useState(false);
const [formulaDescription, setFormulaDescription] = useState('');
const [pendingFormulaSpec, setPendingFormulaSpec] = useState<FormulaSpec | null>(null);
const [formulaPreview, setFormulaPreview] = useState<FormulaPreviewResult | null>(null);
const [formulaPreviewLoading, setFormulaPreviewLoading] = useState(false);
const [formulaWriteLoading, setFormulaWriteLoading] = useState(false);
const [formulaError, setFormulaError] = useState<string | null>(null);
const [formulaSuccess, setFormulaSuccess] = useState<string | null>(null);
```

---

## Parallelization Map

```
Single sequential CC session. 4 files + 1 new file. 5 total.

  Task 1: formulaBuilder.ts      NEW FILE — FormulaSpec; FormulaPreviewResult;
                                   previewFormula() (scratch-cell pattern);
                                   writeFormula(); ensureScratchSheet()

  Task 2: suggestionParser.ts    Add FormulaSpec import + formulaSpec to ParseResult;
                                   formula_spec parser block

  Task 3: SlashCommandPicker.tsx Add /formula entry to COMMANDS; pass name in onSelect
                                   (onSelect signature already updated in Sprint 10 for /report)

  Task 4: ChatPanel.tsx          Sprint 11 state; handle /formula command specially
                                   (same pattern as /report); formula config panel;
                                   handleFormulaGenerate(); useEffect to capture formulaSpec;
                                   formula preview bar with confirm/cancel

  Task 5: useChat.ts             Add formulaSpec to Message; propagate from parseSuggestions
```

---

## File-Level Spec

### Task 1 (NEW): `src/taskpane/services/formulaBuilder.ts`

```typescript
/* global Excel */

import { setFaitWriting } from './watchMode';   // Sprint 9 loop prevention

export interface FormulaSpec {
  formula: string;
  explanation: string;
  functionNames: string[];
  targetCell: string;   // "__SELECTED__" | A1-notation address
  previewable: boolean;
}

export interface FormulaPreviewResult {
  value: string | number | boolean | null;
  valueType: string;
  isError: boolean;
  errorMessage?: string;
}

const SCRATCH_SHEET_NAME = '__FAIT_SCRATCH__';

/**
 * Ensure the hidden scratch sheet exists. Creates it VeryHidden on first call.
 * Returns silently if already present.
 */
async function ensureScratchSheet(): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const existing = ctx.workbook.worksheets.getItemOrNullObject(SCRATCH_SHEET_NAME);
    existing.load('isNullObject');
    await ctx.sync();

    if (existing.isNullObject) {
      const scratch = ctx.workbook.worksheets.add(SCRATCH_SHEET_NAME);
      scratch.visibility = Excel.SheetVisibility.veryHidden;
      await ctx.sync();
    }
  });
}

/**
 * Prefix bare cell/range references in a formula with the given sheet name.
 * "=SUM(A1:A10)" on activeSheet "Sheet1" → "=SUM(Sheet1!A1:Sheet1!A10)"
 *
 * Only prefixes references that don't already have a sheet name (no "!" present).
 * Conservative regex: only matches standard A1:Z99999 references.
 *
 * This is a best-effort rewrite for preview purposes only.
 * The formula written to the target cell uses the ORIGINAL formula (not prefixed).
 */
export function prefixFormulaRefs(formula: string, sheetName: string): string {
  // Escape the sheet name for use in Excel references (wrap in '' if contains spaces)
  const escapedSheet = sheetName.includes(' ') ? `'${sheetName}'` : sheetName;

  // Match A1-style references not already preceded by '!'
  // Negative lookbehind: not preceded by '!' or "'" (already has a sheet prefix)
  // Pattern: letter(s) + digit(s), not preceded by ! or '
  return formula.replace(
    /(?<![!'A-Za-z])([A-Z]+\d+(?::[A-Z]+\d+)?)/g,
    (match) => `${escapedSheet}!${match}`
  );
}

/**
 * Preview a formula using the scratch-cell pattern.
 * Writes the formula to the hidden scratch sheet A1, reads the computed value, clears.
 *
 * @param formula        The formula string (must start with =)
 * @param activeSheet    Name of the active worksheet (for reference prefixing)
 */
export async function previewFormula(
  formula: string,
  activeSheet: string
): Promise<FormulaPreviewResult> {
  await ensureScratchSheet();

  // Prefix bare cell references with the active sheet name so they resolve correctly
  const prefixedFormula = prefixFormulaRefs(formula, activeSheet);

  return Excel.run(async (ctx: any) => {
    const scratch = ctx.workbook.worksheets.getItem(SCRATCH_SHEET_NAME);
    const cell = scratch.getRange('A1');

    cell.formulas = [[prefixedFormula]];
    cell.load(['values', 'valueTypes']);
    await ctx.sync();

    const rawValue = (cell.values as any[][])[0][0];
    const valueType = (cell.valueTypes as string[][])[0][0];

    // Clear the scratch cell before returning
    cell.clear(Excel.ClearApplyTo.contents);
    await ctx.sync();

    // Detect Excel error values (#VALUE!, #REF!, #DIV/0!, etc.)
    const isError = valueType === 'Error' || (typeof rawValue === 'string' && rawValue.startsWith('#'));

    return {
      value: isError ? null : rawValue,
      valueType,
      isError,
      errorMessage: isError ? String(rawValue) : undefined,
    };
  }).catch((e: any) => {
    // Formula syntax error or Excel.run failure
    return {
      value: null,
      valueType: 'Error',
      isError: true,
      errorMessage: e?.message ?? 'Formula evaluation failed',
    };
  });
}

/**
 * Write a formula to the user's selected cell (or a specified address).
 * Uses setFaitWriting to prevent watch mode triggering.
 *
 * @param formula      The formula string (must start with =)
 * @param address      A1-notation target address (e.g. "Sheet1!B5" or "B5")
 * @param explanation  Optional explanation to attach as a cell comment
 */
export async function writeFormula(
  formula: string,
  address: string,
  explanation?: string
): Promise<void> {
  setFaitWriting(true);
  try {
    await Excel.run(async (ctx: any) => {
      const sheet = ctx.workbook.worksheets.getActiveWorksheet();
      const cell = sheet.getRange(address);

      cell.formulas = [[formula]];

      if (explanation) {
        try {
          sheet.comments.add(address, `FAIT formula: ${explanation}`);
        } catch {
          // Comment add may fail (e.g., cell already has a comment) — non-fatal
        }
      }

      await ctx.sync();
    });
  } finally {
    setFaitWriting(false);
  }
}

/**
 * Format a preview result for display in the UI.
 * Returns a string like "→ $42,150" or "→ #VALUE! (division by zero)"
 */
export function formatPreviewValue(result: FormulaPreviewResult): string {
  if (result.isError) {
    return `→ ${result.errorMessage ?? '#ERROR'}`;
  }
  if (result.value === null || result.value === undefined) {
    return '→ (empty)';
  }
  if (typeof result.value === 'number') {
    // Format large numbers with commas; show up to 6 significant digits
    if (Math.abs(result.value) >= 1000) {
      return `→ ${result.value.toLocaleString('en-US', { maximumSignificantDigits: 6 })}`;
    }
    return `→ ${result.value}`;
  }
  return `→ ${String(result.value)}`;
}
```

---

### Task 2: `src/taskpane/services/suggestionParser.ts`

**Add `FormulaSpec` import and `formulaSpec` to `ParseResult`:**

```typescript
import type { FormulaSpec } from './formulaBuilder';

// Add to ParseResult:
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;
  reportSpec: ReportSpec | null;
  formulaSpec: FormulaSpec | null;   // ← NEW
}
```

**Add `formula_spec` parser block** (after `report_spec` block, before `table_data` block):

```typescript
// ── formula_spec block ────────────────────────────────────────────────────
let formulaSpec: FormulaSpec | null = null;
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
        previewable: fs.previewable !== false,   // default true unless explicitly false
      };
      displayText = displayText.replace(formulaSpecMatch[0], '');
    }
  } catch {
    // Bad JSON — leave displayText unchanged
  }
}
```

**Initialize at top:** `let formulaSpec: FormulaSpec | null = null;`

**Update return statement:** add `formulaSpec` to the returned object.

**Do NOT change** any existing parser blocks.

---

### Task 3: `src/taskpane/components/SlashCommandPicker.tsx`

Add `/formula` to COMMANDS. The `onSelect` signature was already updated for `/report` (Sprint 10) to include `name?: string`.

```typescript
// Add as the second command (after /report, before /audit):
{
  name: 'formula',
  description: 'Generate a formula from a plain-English description',
  prompt: '__FORMULA_COMMAND__',   // sentinel — not pasted into input
},
```

The sentinel value `'__FORMULA_COMMAND__'` is never pasted into the chat input. `ChatPanel.onSelect` checks `name === 'formula'` and opens the config panel.

**Do NOT change** any existing commands.

---

### Task 4: `src/taskpane/components/ChatPanel.tsx`

Five targeted changes.

**Change 1: Add imports**

```typescript
import { previewFormula, writeFormula, formatPreviewValue } from '../services/formulaBuilder';
import type { FormulaSpec, FormulaPreviewResult } from '../services/formulaBuilder';
```

**Change 2: Add Sprint 11 state (after Sprint 10 state block)**

```typescript
// ── Sprint 11: Formula intelligence state ──────────────────────────────────
const [showFormulaConfig, setShowFormulaConfig] = useState(false);
const [formulaDescription, setFormulaDescription] = useState('');
const [pendingFormulaSpec, setPendingFormulaSpec] = useState<FormulaSpec | null>(null);
const [formulaPreview, setFormulaPreview] = useState<FormulaPreviewResult | null>(null);
const [formulaPreviewLoading, setFormulaPreviewLoading] = useState(false);
const [formulaWriteLoading, setFormulaWriteLoading] = useState(false);
const [formulaError, setFormulaError] = useState<string | null>(null);
const [formulaSuccess, setFormulaSuccess] = useState<string | null>(null);
const formulaInputRef = useRef<HTMLInputElement>(null);
```

**Change 3: Update `onSelect` handler for `/formula`**

In the `SlashCommandPicker` render (same block updated for `/report` in Sprint 10):

```typescript
onSelect={(prompt, name) => {
  if (name === 'report') {
    setInputText('');
    setShowReportConfig(true);
    setReportError(null);
    setReportSuccess(null);
  } else if (name === 'formula') {
    setInputText('');
    setShowFormulaConfig(true);
    setFormulaError(null);
    setFormulaSuccess(null);
    setFormulaDescription('');
    setPendingFormulaSpec(null);
    setFormulaPreview(null);
    setTimeout(() => formulaInputRef.current?.focus(), 50);
  } else {
    setInputText(prompt);
  }
}}
```

**Change 4: Add formula handlers**

After `handleCreateReportSheet` (Sprint 10):

```typescript
// ── Sprint 11: Formula Intelligence handlers ──────────────────────────────

const handleFormulaGenerate = async () => {
  if (!formulaDescription.trim()) return;
  setShowFormulaConfig(false);
  setFormulaError(null);
  setFormulaSuccess(null);
  setPendingFormulaSpec(null);
  setFormulaPreview(null);

  let context: string | undefined;
  try {
    const ctx = await getSelectedRange();
    if (ctx.rows > 0 && ctx.cols > 0) {
      context = formatContext(ctx);
      setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
    }
  } catch {
    // Non-fatal
  }

  const formulaPrompt = `The user wants a formula for: "${formulaDescription.trim()}"

Please generate an Excel formula and return it as a formula_spec JSON block:
{
  "formula_spec": {
    "formula": "=THE_FORMULA_HERE",
    "explanation": "Plain-English explanation of what this formula does",
    "functionNames": ["LIST", "OF", "FUNCTIONS", "USED"],
    "targetCell": "__SELECTED__",
    "previewable": true
  }
}

Rules:
- formula must start with = and use en-US Excel function names
- If the formula uses volatile functions (NOW, TODAY, RAND, RANDBETWEEN, OFFSET, INDIRECT, INFO, CELL), set previewable: false
- If you cannot generate a valid formula, return a prose explanation instead of the JSON block
Return ONLY the JSON block.`;

  await send(formulaPrompt, context);
};

const handleFormulaPreview = async (spec: FormulaSpec) => {
  if (!spec.previewable) {
    setFormulaPreview({
      value: null,
      valueType: 'String',
      isError: false,
      errorMessage: 'Preview unavailable for this formula type',
    });
    return;
  }

  setFormulaPreviewLoading(true);

  try {
    // Get active sheet name for reference prefixing
    const activeSheetName = await Excel.run(async (ctx: any) => {
      const sheet = ctx.workbook.worksheets.getActiveWorksheet();
      sheet.load('name');
      await ctx.sync();
      return sheet.name as string;
    });

    const result = await previewFormula(spec.formula, activeSheetName);
    setFormulaPreview(result);
  } catch (e) {
    setFormulaPreview({
      value: null,
      valueType: 'Error',
      isError: true,
      errorMessage: e instanceof Error ? e.message : 'Preview failed',
    });
  } finally {
    setFormulaPreviewLoading(false);
  }
};

const handleFormulaWrite = async (spec: FormulaSpec) => {
  if (!selectionInfo?.address) {
    setFormulaError('Select a target cell first.');
    return;
  }

  setFormulaWriteLoading(true);
  setFormulaError(null);

  // Target: use selectionInfo top-left cell (not the full range)
  const targetAddress = selectionInfo.address.split(':')[0];

  try {
    await writeFormula(spec.formula, targetAddress, spec.explanation);
    setFormulaSuccess(`Formula written to ${targetAddress}`);
    setPendingFormulaSpec(null);
    setFormulaPreview(null);
  } catch (e) {
    setFormulaError(
      e instanceof Error ? e.message : 'Failed to write formula'
    );
  } finally {
    setFormulaWriteLoading(false);
  }
};

const handleFormulaDismiss = () => {
  setPendingFormulaSpec(null);
  setFormulaPreview(null);
  setFormulaError(null);
};

const handleFormulaInputKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
  if (e.key === 'Enter') handleFormulaGenerate();
  if (e.key === 'Escape') setShowFormulaConfig(false);
};
```

**Change 5: Add `useEffect` to capture `formulaSpec` from messages**

After the Sprint 10 `useEffect` that captures `reportSpec`:

```typescript
// Sprint 11: Watch for formula_spec in the latest assistant message
useEffect(() => {
  const lastMsg = messages[messages.length - 1];
  if (
    lastMsg?.role === 'assistant' &&
    !lastMsg.streaming &&
    lastMsg.formulaSpec &&
    !pendingFormulaSpec
  ) {
    setPendingFormulaSpec(lastMsg.formulaSpec);
    setFormulaPreview(null);
    // Trigger preview automatically (non-volatile formulas)
    if (lastMsg.formulaSpec.previewable) {
      handleFormulaPreview(lastMsg.formulaSpec);
    }
  }
}, [messages]); // eslint-disable-line react-hooks/exhaustive-deps
```

**Change 6: Add formula config panel and formula action bar to JSX**

**Formula config panel** — add after the report config panel:

```typescript
{/* ── Sprint 11: Formula config panel ── */}
{showFormulaConfig && (
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
      ƒx Formula Generator
    </div>
    <input
      ref={formulaInputRef}
      value={formulaDescription}
      onChange={(e) => setFormulaDescription(e.target.value)}
      onKeyDown={handleFormulaInputKeyDown}
      placeholder="e.g. sum revenue where region is North and quarter > 0"
      style={{
        background: '#1a2332',
        border: '1px solid #2e3f54',
        borderRadius: '4px',
        color: '#e8edf3',
        padding: '6px 8px',
        fontSize: '12px',
        outline: 'none',
        width: '100%',
        boxSizing: 'border-box',
      }}
    />
    <div style={{ display: 'flex', gap: '6px' }}>
      <button
        onClick={handleFormulaGenerate}
        disabled={!formulaDescription.trim()}
        style={{
          background: formulaDescription.trim() ? '#1a3020' : '#1e2d3e',
          border: `1px solid ${formulaDescription.trim() ? '#2e5040' : '#2e3f54'}`,
          borderRadius: '4px',
          color: formulaDescription.trim() ? '#6fcf97' : '#445566',
          fontSize: '11px',
          fontWeight: '600',
          padding: '5px 12px',
          cursor: formulaDescription.trim() ? 'pointer' : 'not-allowed',
        }}
      >
        Generate Formula
      </button>
      <button
        onClick={() => setShowFormulaConfig(false)}
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

**Formula action bar** — add after the report success toast:

```typescript
{/* ── Sprint 11: Formula preview + write action bar ── */}
{pendingFormulaSpec && (
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
    {/* Formula display */}
    <div
      style={{
        fontFamily: 'monospace',
        fontSize: '12px',
        color: '#d4af37',
        background: '#131f2e',
        padding: '5px 8px',
        borderRadius: '4px',
        border: '1px solid #2e3f54',
        wordBreak: 'break-all',
      }}
    >
      {pendingFormulaSpec.formula}
    </div>

    {/* Explanation */}
    <div style={{ fontSize: '11px', color: '#8899aa' }}>
      {pendingFormulaSpec.explanation}
    </div>

    {/* Function names badge */}
    {pendingFormulaSpec.functionNames.length > 0 && (
      <div style={{ fontSize: '10px', color: '#556677' }}>
        Uses: {pendingFormulaSpec.functionNames.join(', ')}
      </div>
    )}

    {/* Preview result */}
    <div style={{ fontSize: '11px', display: 'flex', alignItems: 'center', gap: '6px' }}>
      {formulaPreviewLoading ? (
        <span style={{ color: '#556677' }}>Computing preview…</span>
      ) : formulaPreview ? (
        <span
          style={{
            color: formulaPreview.isError ? '#e07070' : '#6fcf97',
            fontFamily: 'monospace',
            fontWeight: '600',
          }}
        >
          {formatPreviewValue(formulaPreview)}
          {formulaPreview.isError && (
            <span style={{ color: '#556677', fontFamily: 'sans-serif', fontWeight: 'normal' }}>
              {' '}(preview error — formula may still be valid)
            </span>
          )}
        </span>
      ) : !pendingFormulaSpec.previewable ? (
        <span style={{ color: '#556677' }}>Preview unavailable (volatile function)</span>
      ) : null}
    </div>

    {/* Target cell + action buttons */}
    <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
      <span style={{ fontSize: '11px', color: '#556677', flexShrink: 0 }}>
        Write to: {selectionInfo?.address?.split(':')[0] ?? '(select a cell)'}
      </span>
      <div style={{ flex: 1 }} />
      <button
        onClick={() => handleFormulaWrite(pendingFormulaSpec)}
        disabled={formulaWriteLoading || !selectionInfo}
        style={{
          background: selectionInfo ? '#d4af37' : '#2e3f54',
          color: selectionInfo ? '#0f1720' : '#445566',
          border: 'none',
          borderRadius: '4px',
          padding: '5px 12px',
          fontSize: '11px',
          fontWeight: '600',
          cursor: selectionInfo ? 'pointer' : 'not-allowed',
        }}
      >
        {formulaWriteLoading ? '…' : 'Write Formula'}
      </button>
      <button
        onClick={handleFormulaDismiss}
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

    {formulaError && (
      <div style={{ fontSize: '11px', color: '#e07070' }}>{formulaError}</div>
    )}
  </div>
)}

{formulaSuccess && !pendingFormulaSpec && (
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
    <span>✓ {formulaSuccess}</span>
    <button
      onClick={() => setFormulaSuccess(null)}
      style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
    >
      ✕
    </button>
  </div>
)}
```

---

### Task 5: `src/taskpane/hooks/useChat.ts`

**Update `Message` interface:**

```typescript
import type { FormulaSpec } from '../services/formulaBuilder';

export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
  reportSpec?: ReportSpec | null;
  formulaSpec?: FormulaSpec | null;   // ← NEW
}
```

**In `send()`, destructure `formulaSpec` from `parseSuggestions`:**

```typescript
const { displayText, suggestions, tableData, reportSpec, formulaSpec } = parseSuggestions(rawText);

next[assistantIndex] = {
  role: 'assistant',
  content: displayText,
  streaming: false,
  tableData: tableData ?? null,
  reportSpec: reportSpec ?? null,
  formulaSpec: formulaSpec ?? null,   // ← ADD
};
```

---

## Files Changed Summary

| File | Change type | Description |
|------|-------------|-------------|
| `src/taskpane/services/formulaBuilder.ts` | **NEW** | `FormulaSpec`, `FormulaPreviewResult`, `previewFormula()`, `writeFormula()`, `ensureScratchSheet()`, `prefixFormulaRefs()`, `formatPreviewValue()` |
| `src/taskpane/services/suggestionParser.ts` | Modify | Add `formulaSpec` to `ParseResult`; `formula_spec` parser block |
| `src/taskpane/components/SlashCommandPicker.tsx` | Modify | Add `/formula` entry; sentinel prompt value |
| `src/taskpane/components/ChatPanel.tsx` | Modify | Sprint 11 state; `/formula` config panel; formula preview bar; handlers |
| `src/taskpane/hooks/useChat.ts` | Modify | `formulaSpec` on `Message`; propagate from `parseSuggestions` |

**1 new file + 4 modified. No new npm packages.**

---

## UX Flow — Exact Sequences

### Flow A: Full `/formula` flow

```
1. User selects B2:B10 (revenue column for North region data)
2. User types "/" → picker shows → selects "/formula"
3. Formula config panel opens:
   "ƒx Formula Generator"
   [sum revenue where region is North and quarter > 0          ]
   [Generate Formula]  [Cancel]
4. User types description → presses Enter
5. Config panel closes; formula generation prompt sent to FAIT (with context)
6. FAIT streams response — formula_spec JSON parsed, stripped from displayText
   displayText may be empty or contain a brief intro
7. Formula action bar appears:
   =SUMIFS(B2:B10, A2:A10, "North", C2:C10, ">"&0)
   "Sums Revenue column for rows where Region is North and Quarter is > 0"
   Uses: SUMIFS
   Computing preview…
   → 42,150                              ← preview result appears
   Write to: Sheet1!B2  [Write Formula] [✕]
8. User sees "42,150" previewed → clicks "Write Formula"
9. writeFormula("=SUMIFS(B2:B10, A2:A10, "North", C2:C10, ">"&0)", "Sheet1!B2")
10. Success: "✓ Formula written to Sheet1!B2"
```

### Flow B: Formula with preview error (wrong range reference)

```
1. FAIT generates: =VLOOKUP(Z999, A1:B100, 2, FALSE)
2. previewFormula() writes to scratch cell → Excel returns #N/A
3. Preview shows: "→ #N/A (preview error — formula may still be valid)"
   (The preview error is shown in red but the formula is still writeable)
4. User can still click "Write Formula" — the formula is written verbatim
5. The #N/A may be correct for the user's actual data (Z999 may exist)
   The preview was computed against the current selection state
```

### Flow C: Volatile formula — no preview

```
1. User asks for "today's date as a formula"
2. FAIT returns: { formula: "=TODAY()", previewable: false, ... }
3. Formula action bar shows:
   =TODAY()
   "Returns today's date"
   Uses: TODAY
   Preview unavailable (volatile function)
   Write to: Sheet1!A1  [Write Formula] [✕]
4. No preview attempted — user writes directly
```

### Flow D: FAIT can't generate a formula

```
1. User asks for "predict next quarter revenue using machine learning"
2. FAIT returns prose text (no formula_spec block)
3. parseSuggestions() returns formulaSpec = null
4. No formula action bar appears
5. FAIT's text response explains why it can't generate the formula
```

---

## Error Handling Matrix

| Scenario | Behavior |
|----------|----------|
| Excel formula syntax error (e.g. `=SUM(` missing close) | `previewFormula()` returns `isError: true`; bar shows "→ #ERROR" in red |
| `#VALUE!` in preview | Shown as "→ #VALUE!" with "(preview error)" note; write still enabled |
| `#DIV/0!` in preview | Same as above |
| `#N/A` in preview (VLOOKUP not found) | Same — preview shows the error, write still enabled |
| FAIT returns no formula_spec | No action bar; prose response shown normally |
| `writeFormula()` fails (Excel.run error) | Red error below the action bar; formula not written |
| `ensureScratchSheet()` fails | `previewFormula()` catches; returns `isError: true` with the error message |
| VeryHidden scratch sheet deleted by user | `getItemOrNullObject()` finds it null → recreated. Next preview works. |
| No cell selected when "Write Formula" clicked | Button shows "(select a cell)" disabled state; `handleFormulaWrite` early-returns with error |

---

## `workbook.functions` — Why Not Used (and When It Would Be)

`workbook.functions` has a genuine use case: **single-function evaluation with explicit typed arguments**. For example:

```typescript
const result = fns.sum(range);          // evaluates SUM(A1:A10) properly
const result = fns.vlookup("ProductA", lookupRange, 2, false);  // evaluates VLOOKUP
```

This would give a clean, no-scratch-sheet preview for simple formulas. The reason Sprint 11 does NOT use it:

1. FAIT generates arbitrary formulas — `=SUMIFS(...)`, `=INDEX(MATCH(...))`, `=IF(ISNUMBER(...))`. These are compound expressions that `workbook.functions` cannot evaluate as a whole.
2. Decomposing `=IF(AVERAGE(B2:B10)>1000, "High", "Low")` into `.if(.gt(.average(range), 1000), "High", "Low")` requires a typed tree interpretation of the formula — that's a full formula parser, out of scope.
3. The scratch-cell pattern handles 100% of formula cases with zero decomposition logic.

**Future enhancement path (not in Sprint 11):** For formulas FAIT knows are single-function (e.g., `functionNames.length === 1`), try the `workbook.functions` path first for a cleaner preview (no scratch cell pollution). Fall back to scratch-cell if the function signature doesn't match. Not worth the complexity for an initial implementation.

---

## ExcelApi Requirement Analysis

| API | Min version | Used in Sprint 11 |
|-----|-------------|------------------|
| `range.formulas` read/write | 1.1 | ✅ Write formula to target cell + scratch cell |
| `range.values` read | 1.1 | ✅ Read preview result |
| `range.valueTypes` read | 1.1 | ✅ Detect error types |
| `range.clear(applyTo)` | 1.1 | ✅ Clear scratch cell after preview |
| `worksheet.visibility = veryHidden` | 1.1 | ✅ Hide scratch sheet |
| `worksheets.add(name)` | 1.1 | ✅ Create scratch sheet |
| `worksheets.getItemOrNullObject(name)` | 1.4 | ✅ Check if scratch sheet exists |
| `worksheet.comments.add()` | 1.10 | ✅ Attach explanation as comment (non-fatal if missing) |
| `workbook.functions` | 1.2 | ✅ Available but not used in this sprint |

**All APIs ≤ ExcelApi 1.10. Baseline is 1.13. No manifest change.**

---

## Acceptance Criteria

1. `/formula` appears in the slash command picker and triggers the config panel (not input paste)
2. Config panel has a free-form text input and "Generate Formula" button
3. Formula generation sends a structured prompt to FAIT with current selection context
4. `formula_spec` JSON block is parsed; formula action bar appears with formula + explanation
5. **Preview fires automatically** after `formula_spec` is received (non-volatile formulas)
6. Preview result shown: green for computed value, red for error values (`#VALUE!`, `#N/A`, etc.)
7. Preview errors show "(preview error — formula may still be valid)" — write still enabled
8. Volatile formulas (`previewable: false`) show "Preview unavailable" — write still enabled
9. "Write Formula" writes to the top-left cell of the current selection via `range.formulas = [[formula]]`
10. Success banner shown after write; action bar dismissed
11. Scratch sheet `__FAIT_SCRATCH__` exists in the workbook as VeryHidden after first `/formula` use
12. `worksheet.comments.add()` failure is non-fatal (caught, logged, ignored)
13. All Sprint 1–10 features unchanged

---

## Constraints for CC

- Touch only the 5 files listed (1 new, 4 modified)
- `prefixFormulaRefs()` is best-effort — the regex `(?<![!'A-Za-z])([A-Z]+\d+(?::[A-Z]+\d+)?)` handles standard A1 and A1:B10 references; it does NOT handle named ranges, R1C1 notation, or array constants. This is acceptable — the spec says preview is approximate. Do NOT attempt a full formula parser.
- The scratch cell write uses the PREFIXED formula (for preview accuracy); the target cell write uses the ORIGINAL formula (so relative references work correctly). Confirm both paths use the correct formula string.
- `cell.clear(Excel.ClearApplyTo.contents)` must be called after reading the preview value — even if the value is an error. Do not leave the scratch cell populated. Use a `try/finally` or ensure the clear always runs.
- `handleFormulaPreview()` is called from a `useEffect` — it must be wrapped in `handleFormulaPreview(spec)` outside of the effect dependency array (use a ref or `useCallback` to avoid stale closure). Alternatively, call it inside the `useEffect` directly with the spec as a parameter.
- The `/formula` sentinel value `'__FORMULA_COMMAND__'` must never be passed to `handleSend()`. The `onSelect` guard `name === 'formula'` must be checked before any fallback to `setInputText(prompt)`.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify the scratch cell is ALWAYS cleared — even if the formula evaluates to an error.
          If the clear() is inside a try block (not finally), a JavaScript error between
          the formulas write and the clear() would leave the scratch cell populated.
          The clear() must be in a finally block or run unconditionally after ctx.sync().

⚠️  HIGH: Confirm the PREFIXED formula is used for scratch-cell preview and the ORIGINAL
          formula is used for the target-cell write. If prefixedFormula is accidentally
          used for the write, the formula in the target cell will have absolute sheet refs
          (`Sheet1!A1`) that don't work correctly when the formula is copied to other cells.

⚠️  HIGH: Verify `pendingFormulaSpec` is null-checked before calling handleFormulaWrite().
          The "Write Formula" button onClick calls `handleFormulaWrite(pendingFormulaSpec)`.
          If `pendingFormulaSpec` is somehow null at that moment (e.g. rapid double-click),
          the function would receive null. Add a guard: `if (!spec) return;`.

⚠️  MEDIUM: Confirm `Excel.SheetVisibility.veryHidden` is the correct enum value.
            In the Excel JS API, the visibility values are:
            `Excel.SheetVisibility.visible`, `.hidden`, `.veryHidden`
            Note camelCase `.veryHidden` — not `VeryHidden` or `"veryHidden"` (string).
            The enum path is: `(Excel as any).SheetVisibility?.veryHidden ?? "VeryHidden"`.
            Since we're using `any` types throughout, pass the string `"VeryHidden"` to be safe.

⚠️  MEDIUM: Confirm the `useEffect` that auto-triggers preview doesn't cause infinite loops.
            The effect depends on `[messages]` and calls `handleFormulaPreview()` which
            calls `setFormulaPreview()`. Verify `setFormulaPreview()` does not update
            `messages` (it doesn't — it's a separate state slice). No loop risk.
            But: confirm `!pendingFormulaSpec` guard prevents re-triggering if the effect
            runs again after `setPendingFormulaSpec()` is called.

⚠️  LOW: The `prefixFormulaRefs` regex uses a negative lookbehind `(?<![!'A-Za-z])`.
         Confirm this is supported in the target JS environment (modern browsers support it).
         If there's a concern about older Office versions' webviews, simplify the regex
         to a positive match and post-filter out already-prefixed references.

⚠️  LOW: `worksheet.comments.add()` requires ExcelApi 1.10. Since baseline is 1.13,
         this is fine. But it's wrapped in a try/catch because adding a comment to a cell
         that already has a comment throws. Confirm the catch is present in writeFormula().
```

---

_Spec by Reed Richards | Sprint 11 is 1 new file + 4 edits. No new packages. The key insight: `workbook.functions` evaluates typed method calls, not formula strings — the scratch-cell pattern is the universal solution for arbitrary formula preview. FfE sprint specs complete (S6–S11)._
