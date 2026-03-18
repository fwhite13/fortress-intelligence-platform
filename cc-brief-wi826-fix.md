# CC Brief: WI826 Cycle 2 Fix — Remove setFaitWriting from reportBuilder.ts

## Task
Make exactly 3 surgical removals from `src/taskpane/services/reportBuilder.ts`. No other changes.

## File to Edit
`src/taskpane/services/reportBuilder.ts`

## Change 1: Remove the import
Remove this line entirely:
```
import { setFaitWriting } from './watchMode';
```

## Change 2: Remove the setFaitWriting(true) call
Remove this line (it appears just before `try {` inside `createReportSheet()`):
```
  setFaitWriting(true);
```

## Change 3: Remove the finally block
Remove the entire `finally` block at the end of `createReportSheet()`:
```
  } finally {
    setFaitWriting(false);
  }
```
This means the `return { ... }` inside the `try` block becomes the last statement before the closing `}` of the function.

## What to Preserve
- All `Excel.run()` calls remain unchanged
- The `try { ... }` block structure stays but the `try` keyword and its braces can be removed now that there is no `finally` — OR keep `try` without `finally`, either is fine as long as it compiles
- Actually: remove `try {` and the matching `}` too since without `finally` the try block is unnecessary. The code inside the try block (from `const result = await Excel.run(...)` through `return { ... }`) should remain at function body level.

## Clarification on Change 3
After removing `setFaitWriting(true)`, the `try { ... } finally { setFaitWriting(false); }` wrapper becomes a bare `try { ... }` with no catch/finally. Remove the `try {` line and the corresponding closing `}` before `finally`, and remove the `} finally { setFaitWriting(false); }` block entirely. The code that was inside `try` stays in place as direct function body statements.

## Do NOT
- Change any Excel.run() logic
- Change any imports other than the setFaitWriting one
- Change ChatPanel.tsx or any other file
- Add any new code
- Reformat or reorganize anything else

## Expected Result
`grep "setFaitWriting\|watchMode" src/taskpane/services/reportBuilder.ts` returns EMPTY.
