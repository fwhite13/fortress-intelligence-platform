# Review Brief: WI827 — Formula Intelligence

Review the following files for WI827, which adds `/formula` command with scratch-cell preview.

## Files Changed (5 total)
1. `src/taskpane/services/formulaBuilder.ts` — NEW file
2. `src/taskpane/services/suggestionParser.ts` — formula_spec parser
3. `src/taskpane/components/SlashCommandPicker.tsx` — /formula command entry
4. `src/taskpane/components/ChatPanel.tsx` — formula state, handlers, UI
5. `src/taskpane/hooks/useChat.ts` — formulaSpec on Message

## Priority Checks

### HIGH: Scratch cell always cleared (atomic pattern)

In `formulaBuilder.ts`, `previewFormula()` writes to scratch AND clears it in the SAME `Excel.run` call:

```typescript
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
    ...
}).catch((e: any) => {
    return { value: null, valueType: 'Error', isError: true, errorMessage: e?.message ?? 'Formula evaluation failed' };
});
```

Questions:
1. Is `cell.clear()` + `ctx.sync()` truly unconditional inside the `Excel.run` — will it run even if the formula evaluates to an error value (like #REF!, #VALUE!)?
2. If `Excel.run` itself throws (e.g. network failure), the `.catch()` returns an error result — but does the scratch cell remain written/dirty in this case?
3. Does `writeFormula()` ever write to scratch? Confirm it only writes to the user's target cell.
4. If `previewFormula()` was never called, is scratch guaranteed to be clean?

### HIGH: "VeryHidden" string literal

In `ensureScratchSheet()`:
```typescript
(scratch as any).visibility = "VeryHidden";
```
Should this be `Excel.SheetVisibility.veryHidden` enum instead of a string literal?

### HIGH: setFaitWriting in finally

In `writeFormula()`:
```typescript
setFaitWriting(true);
try {
    await Excel.run(async (ctx: any) => { ... });
} finally {
    setFaitWriting(false);
}
```
Verify `setFaitWriting(false)` is in `finally`, NOT inside `try`.

### MEDIUM: comments.add in try/catch

In `writeFormula()`:
```typescript
if (explanation) {
    try {
        sheet.comments.add(address, `FAIT formula: ${explanation}`);
    } catch {
        // non-fatal
    }
}
```
Verify `comments.add()` is wrapped in try/catch that swallows errors (ExcelApi 1.10 requirement).

### MEDIUM: prefixFormulaRefs for scratch, original for target

In `previewFormula()`:
```typescript
const prefixedFormula = prefixFormulaRefs(formula, activeSheet);
// ...writes prefixedFormula to scratch cell
```

In `handleFormulaWrite` in ChatPanel.tsx:
```typescript
await writeFormula(spec.formula, targetAddress, spec.explanation);
// writes spec.formula (ORIGINAL) to target cell
```

Verify: scratch gets prefixed formula, target cell gets original formula.

Also verify `prefixFormulaRefs()` regex correctly transforms bare cell refs like `A1:D5` → `SheetName!A1:D5`.

### MEDIUM: formulaSpec on ParseResult and Message

In `suggestionParser.ts`:
- `formulaSpec: FormulaSpec | null` on `ParseResult`
- `formulaSpec` initialized to `null`, destructured from single `parseSuggestions()` call

In `useChat.ts`:
- `formulaSpec?: FormulaSpec | null` on `Message`
- Propagated from `parseSuggestions()` destructuring

Verify this follows the same pattern as `reportSpec` (WI826) — not a second call to parseSuggestions.

### LOW: No new npm packages

Verify no new dependencies added to package.json.

### LOW: Exactly 5 files changed

Verify git diff shows exactly 5 files: formulaBuilder.ts (new), suggestionParser.ts, SlashCommandPicker.tsx, ChatPanel.tsx, useChat.ts.

---

Please analyze these specific checks and tell me:
1. Is the atomic clear pattern in `previewFormula()` truly safe, or can scratch be left dirty on Excel.run context-level failure?
2. Is `"VeryHidden"` string literal acceptable or a bug?
3. Is `setFaitWriting(false)` correctly in `finally`?
4. Is `comments.add()` correctly wrapped?
5. Are prefixed/original formulas correctly routed?
6. Is formulaSpec correctly typed on ParseResult and Message?
7. Any other issues found?
