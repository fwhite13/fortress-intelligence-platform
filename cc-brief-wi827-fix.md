# CC Brief: WI827 Cycle 2 Fix — formulaBuilder.ts only

## Context
FAIT for Excel — Sprint 11 Formula Intelligence.
File: `src/taskpane/services/formulaBuilder.ts`
Exactly two targeted fixes. No other changes.

---

## Fix 1 — `comments.add()` sync boundary in `writeFormula()`

### Problem
The current code queues `sheet.comments.add()` inside the `Excel.run` callback, wraps it in a try/catch, but then calls a SINGLE `await ctx.sync()` AFTER the try/catch block. Office JS errors from `comments.add()` surface at `ctx.sync()` time — which is outside the catch — so a comment error can block the formula write.

### Current code (in `writeFormula()`):
```typescript
    cell.formulas = [[formula]];

    if (explanation) {
      try {
        sheet.comments.add(address, `FAIT formula: ${explanation}`);
      } catch {
        // non-fatal
      }
    }

    await ctx.sync();
```

### Fixed code:
Split into two `ctx.sync()` calls — formula first, then comment in its own try/catch:

```typescript
    cell.formulas = [[formula]];
    await ctx.sync();  // commit formula first

    if (explanation) {
      try {
        sheet.comments.add(address, `FAIT formula: ${explanation}`);
        await ctx.sync();  // comment sync in its own try/catch
      } catch {
        // non-fatal — ExcelApi 1.10, may fail on duplicate or unsupported version
      }
    }
```

The `try/finally` wrapper around `setFaitWriting(true/false)` remains unchanged. Only the interior of the `Excel.run` callback changes.

---

## Fix 2 — `VeryHidden` visibility in `ensureScratchSheet()`

### Problem
Current code uses an unnecessary `as any` cast:
```typescript
(scratch as any).visibility = "VeryHidden";
```

### Fix
Use the typed enum directly — `@types/office-js` defines `worksheet.visibility` as accepting `Excel.SheetVisibility | "Visible" | "Hidden" | "VeryHidden"`, so the enum is fully typed:
```typescript
scratch.visibility = Excel.SheetVisibility.veryHidden;
```

No `as any` needed. Remove the cast entirely.

---

## Instructions

1. Apply Fix 1 to the `writeFormula()` function in `src/taskpane/services/formulaBuilder.ts`
2. Apply Fix 2 to the `ensureScratchSheet()` function in the same file
3. No other changes. No other files. No reformatting. No refactoring.
4. These are the only two changes needed.
