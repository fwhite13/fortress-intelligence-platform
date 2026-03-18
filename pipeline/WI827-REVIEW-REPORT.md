# Review Report: WI827
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fait-for-excel
cat review-brief-wi827.md | claude --model sonnet -p
```

**First 20 lines of output:**
```
## WI827 Review Results

### 1. Atomic Clear Pattern — PARTIALLY SAFE, one real gap

**Formula error values (e.g. #REF!, #VALUE!)** — **SAFE.** When a formula evaluates to an error,
Excel.run does NOT throw; the first ctx.sync() succeeds, the error value is read into
rawValue/valueType, then cell.clear() + second ctx.sync() run unconditionally. The isError check
is independent of whether Excel.run throws.

**Excel.run context-level failure** — **REAL GAP.** If the first ctx.sync() fails (e.g., session
drop, auth expiry, sheet race), execution never reaches cell.clear(). The .catch() on returns an
error result to the caller but does not clean up scratch. The cell stays written with the formula
until the next previewFormula() call overwrites it. Since the sheet is VeryHidden this is invisible,
and it's self-healing on next call — but it's technically a persistent dirty state.

**writeFormula() scratch safety** — CONFIRMED. It calls getActiveWorksheet() (never
SCRATCH_SHEET_NAME), writes only to the user's address. Scratch is never touched by writeFormula.

**If previewFormula() was never called** — CONFIRMED CLEAN. Scratch sheet is only created on
demand by ensureScratchSheet(), only called from previewFormula(). If preview is never called,
the sheet may not even exist.
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| Scratch cell always cleared (atomic in previewFormula OR explicit finally) | ✅ | `cell.clear(Excel.ClearApplyTo.contents)` + `ctx.sync()` runs unconditionally inside `Excel.run` — after `ctx.sync()` that loads the value, regardless of whether the value is an error. Both run in the same batch. |
| .catch() path — scratch state on Excel.run failure | ⚠️ | If the FIRST `ctx.sync()` fails (session drop, auth expiry), execution never reaches `cell.clear()`. `.catch()` returns error result but scratch stays written. Self-healing on next preview call (VeryHidden so invisible) — acceptable deviation noted but flagged. |
| writeFormula() never writes to scratch | ✅ | `writeFormula()` calls `getActiveWorksheet()` only. `SCRATCH_SHEET_NAME` not referenced. Formula `spec.formula` (original, unprefixed) passed to `writeFormula()` from ChatPanel.tsx:924. |
| "VeryHidden" string literal | ❌ | `(scratch as any).visibility = "VeryHidden"` — string literal with `as any` cast bypasses TypeScript. Should be `scratch.visibility = Excel.SheetVisibility.veryHidden`. |
| setFaitWriting(false) in finally in writeFormula | ✅ | `formulaBuilder.ts` lines 108–110: `setFaitWriting(false)` is in `finally` block, unconditionally after the `try { await Excel.run(...) }`. |
| comments.add in try/catch | ❌ | **BUG — catch is in wrong position.** `comments.add()` is an Office.js queuing call; it does NOT throw synchronously. The failure occurs at `ctx.sync()` (line 106), which is OUTSIDE the try/catch. A sync failure (ExcelApi 1.10 unavailable, duplicate comment, etc.) will propagate from `ctx.sync()` and bubble out of `Excel.run` — potentially blocking the formula write itself, since both are queued in the same sync. |
| prefixFormulaRefs for scratch, original formula to target | ✅ | `previewFormula()` calls `prefixFormulaRefs(formula, activeSheet)` and writes `prefixedFormula` to scratch. `writeFormula(spec.formula, ...)` in ChatPanel.tsx:924 writes the original `spec.formula`. Confirmed correct routing. Regex `(?<![!'A-Za-z])([A-Z]+\d+(?::[A-Z]+\d+)?)` correctly avoids double-prefixing already-qualified refs. |
| formulaSpec on ParseResult and Message | ✅ | `formulaSpec: FormulaSpec | null` on `ParseResult` (suggestionParser.ts line 23); `formulaSpec?: FormulaSpec | null` on `Message` (useChat.ts line 14). Single `parseSuggestions()` call, destructured at useChat.ts:111. Identical pattern to `reportSpec` (WI826). |
| No new npm packages | ✅ | `package.json` unchanged. No new dependencies. |
| Exactly 5 files changed | ✅ | `git diff --stat HEAD~1 HEAD`: formulaBuilder.ts (+127), suggestionParser.ts (+34/-1), SlashCommandPicker.tsx (+5), ChatPanel.tsx (+351), useChat.ts (+5/-1). Exactly 5 files, 520 additions, 2 deletions. |

---

## Issues Found

### Critical
_None_

### Important

**1. `comments.add()` try/catch in wrong position (formulaBuilder.ts lines 98–103)**

The try/catch wraps the `comments.add()` queue call but does NOT wrap `ctx.sync()`. Office.js queuing calls like `comments.add()` don't throw synchronously — errors surface at sync time. If ExcelApi 1.10 is unavailable, `ctx.sync()` throws and the exception propagates out of `Excel.run`, meaning the user's formula write may also fail (both are queued in the same sync batch).

**Fix:** Split into two sequential `ctx.sync()` calls — commit the formula first, then attempt the comment in a separate sync wrapped in try/catch:

```typescript
// In writeFormula() Excel.run callback:
cell.formulas = [[formula]];
await ctx.sync();  // formula committed unconditionally

if (explanation) {
  try {
    sheet.comments.add(address, `FAIT formula: ${explanation}`);
    await ctx.sync();
  } catch {
    // non-fatal — ExcelApi 1.10 not available or duplicate comment
  }
}
```

**2. `"VeryHidden"` string literal with `as any` cast (formulaBuilder.ts line 30)**

Using `(scratch as any).visibility = "VeryHidden"` bypasses TypeScript type safety. If Office.js normalizes this string differently (e.g., `"veryHidden"`) or the API changes, the sheet silently fails to become hidden.

**Fix:**
```typescript
scratch.visibility = Excel.SheetVisibility.veryHidden;
```
Remove the `as any` cast.

### Nitpick

**3. `.catch()` path — scratch dirty on context-level Excel.run failure**

If the first `ctx.sync()` inside `previewFormula()` fails (session drop, auth expiry), `cell.clear()` never runs and scratch remains written. The `.catch()` returns an error result to the caller but does not attempt cleanup. The scratch sheet is VeryHidden so this is invisible to the user, and it is self-healing (the next `previewFormula()` call will overwrite then clear it). Acceptable deviation from the spec's `clearScratchCell` in `finally` pattern — but noted for awareness. If we want full correctness, a separate `ensureScratchClear()` call in the `.catch()` block would address it.

---

## Verdict

**NEEDS-CHANGES** — Two Important issues must be fixed before merge.

**On the atomic-clear pattern:** Tony's inline clear within `Excel.run` is equivalent to the spec's `clearScratchCell()` in `finally` for the nominal case (formula error values, successful run). The `cell.clear()` runs unconditionally after the first `ctx.sync()` — error values don't cause `Excel.run` to throw, so the clear always executes. This is an acceptable deviation from the explicit `finally` pattern. However, there is a narrow gap on context-level failure (network drop mid-run) where scratch could remain dirty. Given VeryHidden isolation and self-healing behavior, this is a nitpick, not a blocker.

**The two blockers are:**
1. `comments.add()` catch position — must wrap `ctx.sync()`, not just the queue call. A comment-add failure at sync time can silently prevent the formula from writing, which is the primary user-facing action of this feature.
2. `"VeryHidden"` string literal — must use `Excel.SheetVisibility.veryHidden` enum. Low runtime risk today but wrong by convention and fragile.

Fix both items and re-submit for cycle 2.

---

# Cycle 2 Re-Review — WI827
## Verdict: PASS
## Review Cycle: 2 of 2
## Commit: `0671ddc` — 1 file, 4 ins/4 del

---

## CC Invocation

```bash
cd /home/fredw/projects/fait-for-excel
cat review-brief-wi827-c2.md | claude --model sonnet -p
```

---

## Targeted Checks

| # | Check | Result | Evidence |
|---|-------|--------|----------|
| 1 | `cell.formulas = [[formula]]` followed by unconditional `await ctx.sync()` BEFORE comment block | ✅ PASS | Lines 96–97: `cell.formulas = [[formula]];` then `await ctx.sync(); // commit formula first` — outside any `if`, before `if (explanation)` |
| 2 | Comment's `await ctx.sync()` is INSIDE the try/catch | ✅ PASS | Lines 100–105: `await ctx.sync(); // comment sync in its own try/catch` is line 102, inside `try { ... }` block — exception at sync time is caught |
| 3 | `scratch.visibility = Excel.SheetVisibility.veryHidden` — no `as any` | ✅ PASS | Line 30: `scratch.visibility = Excel.SheetVisibility.veryHidden;` — no cast, proper enum |
| 4 | Scope: exactly 1 file changed in commit `0671ddc` | ✅ PASS | `git show 0671ddc --stat`: `1 file changed, 4 insertions(+), 4 deletions(-)` — `src/taskpane/services/formulaBuilder.ts` only |

---

## CC Output (verbatim excerpt)

```
Check 1 — PASS
cell.formulas = [[formula]];
await ctx.sync();  // commit formula first
if (explanation) {
Line 96–99. The sync is at line 97, outside any `if`, before the `if (explanation)` block.

Check 2 — PASS
try {
  sheet.comments.add(address, `FAIT formula: ${explanation}`);
  await ctx.sync();  // comment sync in its own try/catch
} catch {
  // non-fatal — ExcelApi 1.10, may fail on duplicate or unsupported version
}
Lines 100–105. The `await ctx.sync()` is at line 102, inside the `try` block.

Check 3 — PASS
scratch.visibility = Excel.SheetVisibility.veryHidden;
Line 30. No `as any` anywhere on that line or in the surrounding block.

Check 4 — PASS
1 file changed, 4 insertions(+), 4 deletions(-)

Overall Verdict: PASS — All 4 checks pass. The fixes are correctly implemented.
```

---

## Summary

Both Cycle 1 issues are resolved:

- **Fix 1 (sync boundary):** Formula write now commits via its own `await ctx.sync()` unconditionally before the comment block. The comment's `await ctx.sync()` is inside the try/catch — a failure at sync time (ExcelApi 1.10 unavailable, duplicate comment) is caught and logged as non-fatal. Formula write is no longer at risk from comment-add failures.

- **Fix 2 (VeryHidden enum):** `(scratch as any).visibility = "VeryHidden"` replaced with `scratch.visibility = Excel.SheetVisibility.veryHidden`. Type-safe, idiomatic, no cast.

No scope creep. Exactly 1 file changed.

**Pipeline advancing to DEPLOY.**
