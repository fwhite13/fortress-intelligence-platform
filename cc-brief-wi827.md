# CC Brief: WI827 — FfE S11: Formula Intelligence

You are implementing Sprint 11 of FAIT for Excel. The spec is in `SPRINT11-SPEC.md` (already read and fully understood). This brief contains everything you need.

## Working Directory
`/home/fredw/projects/fait-for-excel/`

## What You're Building
The `/formula` slash command triggers FAIT to suggest an Excel formula. A preview is written to a hidden scratch cell (`__FAIT_SCRATCH__!A1`). On accept, the formula is written to the active cell and the scratch cell is cleared.

**1 new file + 4 modified. No new packages.**

---

## CRITICAL RULES (read first)

1. **`clearScratchCell` / scratch cell clearing in `finally` — NOT `try`**
   After writing to and reading from the scratch cell, the clear MUST be in a `try/finally` so it runs even if the formula evaluates to an error value. In `previewFormula()`, after `cell.load()` + `await ctx.sync()`, call `cell.clear(Excel.ClearApplyTo.contents)` + `await ctx.sync()` in a way that it ALWAYS runs. The current spec shows it inside the same `Excel.run` callback after reading — make sure the clear is guaranteed (either use try/finally inside the run callback, or put it after ctx.sync unconditionally).

2. **Scratch sheet name: `__FAIT_SCRATCH__`** — exact string, double underscores each side, all caps.

3. **Scratch sheet visibility: use string `"VeryHidden"` NOT enum**
   ```typescript
   (sheet as any).visibility = "VeryHidden";
   ```
   Do NOT use `Excel.SheetVisibility.veryHidden` — enum may not resolve with `any` types.

4. **`formulaBuilder.ts` owns ALL scratch sheet logic** — `ChatPanel.tsx` only calls `previewFormula()`, `writeFormula()`, `formatPreviewValue()`. No `Excel.run()` in ChatPanel for formula preview.

5. **`worksheet.comments.add()` wrapped in try/catch** — non-fatal if it fails.

6. **`formulaSpec` on `ParseResult` same pattern as `reportSpec`** (existing Sprint 10 field).

7. **`setFaitWriting` wraps `writeFormula()` in `formulaBuilder.ts`** — already shown in spec. The `finally` in ChatPanel's `handleFormulaWrite` must clear `setFaitWriting(false)` but `writeFormula()` already handles that internally.

8. **The PREFIXED formula is used for scratch-cell preview; the ORIGINAL formula is used for writing to the target cell.** These MUST be different paths — do not mix them up.

---

## File 1 (NEW): `src/taskpane/services/formulaBuilder.ts`

Create this file with the following exports:

```typescript
/* global Excel */

import { setFaitWriting } from './watchMode';

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

async function ensureScratchSheet(): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const existing = ctx.workbook.worksheets.getItemOrNullObject(SCRATCH_SHEET_NAME);
    existing.load('isNullObject');
    await ctx.sync();

    if (existing.isNullObject) {
      const scratch = ctx.workbook.worksheets.add(SCRATCH_SHEET_NAME);
      (scratch as any).visibility = "VeryHidden";
      await ctx.sync();
    }
  });
}

export function prefixFormulaRefs(formula: string, sheetName: string): string {
  const escapedSheet = sheetName.includes(' ') ? `'${sheetName}'` : sheetName;
  return formula.replace(
    /(?<![!'A-Za-z])([A-Z]+\d+(?::[A-Z]+\d+)?)/g,
    (match) => `${escapedSheet}!${match}`
  );
}

export async function previewFormula(
  formula: string,
  activeSheet: string
): Promise<FormulaPreviewResult> {
  await ensureScratchSheet();

  const prefixedFormula = prefixFormulaRefs(formula, activeSheet);

  return Excel.run(async (ctx: any) => {
    const scratch = ctx.workbook.worksheets.getItem(SCRATCH_SHEET_NAME);
    const cell = scratch.getRange('A1');

    cell.formulas = [[prefixedFormula]];
    cell.load(['values', 'valueTypes']);
    await ctx.sync();

    const rawValue = (cell.values as any[][])[0][0];
    const valueType = (cell.valueTypes as string[][])[0][0];

    // CRITICAL: clear ALWAYS runs — even if the value is an error
    cell.clear(Excel.ClearApplyTo.contents);
    await ctx.sync();

    const isError = valueType === 'Error' || (typeof rawValue === 'string' && rawValue.startsWith('#'));

    return {
      value: isError ? null : rawValue,
      valueType,
      isError,
      errorMessage: isError ? String(rawValue) : undefined,
    };
  }).catch((e: any) => {
    return {
      value: null,
      valueType: 'Error',
      isError: true,
      errorMessage: e?.message ?? 'Formula evaluation failed',
    };
  });
}

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
          // non-fatal
        }
      }

      await ctx.sync();
    });
  } finally {
    setFaitWriting(false);
  }
}

export function formatPreviewValue(result: FormulaPreviewResult): string {
  if (result.isError) {
    return `→ ${result.errorMessage ?? '#ERROR'}`;
  }
  if (result.value === null || result.value === undefined) {
    return '→ (empty)';
  }
  if (typeof result.value === 'number') {
    if (Math.abs(result.value) >= 1000) {
      return `→ ${result.value.toLocaleString('en-US', { maximumSignificantDigits: 6 })}`;
    }
    return `→ ${result.value}`;
  }
  return `→ ${String(result.value)}`;
}
```

---

## File 2 (MODIFY): `src/taskpane/services/suggestionParser.ts`

### Current state of ParseResult interface:
```typescript
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;
  reportSpec: ReportSpec | null;   // Sprint 10
}
```

### Changes needed:

**1. Add import at top (after existing imports):**
```typescript
import type { FormulaSpec } from './formulaBuilder';
```

**2. Add `formulaSpec` to ParseResult interface:**
```typescript
  reportSpec: ReportSpec | null;
  formulaSpec: FormulaSpec | null;   // Sprint 11
```

**3. Add `let formulaSpec: FormulaSpec | null = null;` in the function body (after `let reportSpec` declaration).**

**4. Add the formula_spec parser block after the `report_spec` block (before the `table_data` block):**

```typescript
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
```

**5. Update return statement** — add `formulaSpec` to the returned object:
```typescript
  return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData, reportSpec, formulaSpec };
```

---

## File 3 (MODIFY): `src/taskpane/components/SlashCommandPicker.tsx`

Add `/formula` as the SECOND command in the COMMANDS array (after `/report`, before `/audit`):

```typescript
  {
    name: 'formula',
    description: 'Generate a formula from a plain-English description',
    prompt: '__FORMULA_COMMAND__',
  },
```

The sentinel `'__FORMULA_COMMAND__'` is NEVER pasted into input — ChatPanel handles `name === 'formula'` specially.

Do NOT change any other commands.

---

## File 4 (MODIFY): `src/taskpane/components/ChatPanel.tsx`

### Current imports area — add:
```typescript
import { previewFormula, writeFormula, formatPreviewValue } from '../services/formulaBuilder';
import type { FormulaSpec, FormulaPreviewResult } from '../services/formulaBuilder';
```

### State — add Sprint 11 state block after Sprint 10 state (around line 150):
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

### Update onSelect handler (currently around line 1853-1863):

Current code:
```typescript
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
```

New code:
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

### Add formula handlers — add after `handleCreateReportSheet` function (Sprint 10):

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
\`\`\`json
{
  "formula_spec": {
    "formula": "=THE_FORMULA_HERE",
    "explanation": "Plain-English explanation of what this formula does",
    "functionNames": ["LIST", "OF", "FUNCTIONS", "USED"],
    "targetCell": "__SELECTED__",
    "previewable": true
  }
}
\`\`\`

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
  if (!spec) return;
  if (!selectionInfo?.address) {
    setFormulaError('Select a target cell first.');
    return;
  }

  setFormulaWriteLoading(true);
  setFormulaError(null);

  const targetAddress = selectionInfo.address.split(':')[0];

  try {
    await writeFormula(spec.formula, targetAddress, spec.explanation);
    setFormulaSuccess(`Formula written to ${targetAddress}`);
    setPendingFormulaSpec(null);
    setFormulaPreview(null);
  } catch (e) {
    setFormulaError(e instanceof Error ? e.message : 'Failed to write formula');
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
  if (e.key === 'Enter') void handleFormulaGenerate();
  if (e.key === 'Escape') setShowFormulaConfig(false);
};
```

### Add useEffect to capture formulaSpec — add after Sprint 10 useEffect that captures reportSpec:

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
    if (lastMsg.formulaSpec.previewable) {
      void handleFormulaPreview(lastMsg.formulaSpec);
    }
  }
}, [messages]); // eslint-disable-line react-hooks/exhaustive-deps
```

### JSX — Formula config panel — add immediately after the Sprint 10 report config panel (`{showReportConfig && (...)}` block):

```tsx
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
        onClick={() => void handleFormulaGenerate()}
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

### JSX — Formula action bar — add after the Sprint 10 report success toast (after the `{reportSuccess && !pendingReportSpec && (...)}` block):

```tsx
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

    <div style={{ fontSize: '11px', color: '#8899aa' }}>
      {pendingFormulaSpec.explanation}
    </div>

    {pendingFormulaSpec.functionNames.length > 0 && (
      <div style={{ fontSize: '10px', color: '#556677' }}>
        Uses: {pendingFormulaSpec.functionNames.join(', ')}
      </div>
    )}

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

    <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
      <span style={{ fontSize: '11px', color: '#556677', flexShrink: 0 }}>
        Write to: {selectionInfo?.address?.split(':')[0] ?? '(select a cell)'}
      </span>
      <div style={{ flex: 1 }} />
      <button
        onClick={() => void handleFormulaWrite(pendingFormulaSpec)}
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

## File 5 (MODIFY): `src/taskpane/hooks/useChat.ts`

### Add import:
```typescript
import type { FormulaSpec } from '../services/formulaBuilder';
```

### Update Message interface — add `formulaSpec` after `reportSpec`:
```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
  reportSpec?: ReportSpec | null;
  formulaSpec?: FormulaSpec | null;   // Sprint 11
}
```

### In the `send()` function — update the `parseSuggestions` destructure and message assignment:

Current:
```typescript
const { displayText, suggestions, tableData, reportSpec } = parseSuggestions(rawText);

next[assistantIndex] = {
  role: 'assistant',
  content: displayText,
  streaming: false,
  tableData: tableData ?? null,
  reportSpec: reportSpec ?? null,
};
```

New:
```typescript
const { displayText, suggestions, tableData, reportSpec, formulaSpec } = parseSuggestions(rawText);

next[assistantIndex] = {
  role: 'assistant',
  content: displayText,
  streaming: false,
  tableData: tableData ?? null,
  reportSpec: reportSpec ?? null,
  formulaSpec: formulaSpec ?? null,   // Sprint 11
};
```

---

## Summary

- Touch ONLY these 5 files. No new packages.
- The `__FAIT_SCRATCH__` scratch cell clear is non-negotiable — it MUST run even on error.
- Use `"VeryHidden"` string literal, not enum.
- PREFIXED formula for preview, ORIGINAL formula for write.
- `worksheet.comments.add()` in try/catch (non-fatal).
- `formulaSpec` field on ParseResult and Message follows exact same optional-null pattern as `reportSpec`.
