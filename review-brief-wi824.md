# Review Brief: WI824 — FfE S8: Named Range Registration

You are reviewing the implementation of Sprint 8 for the FAIT for Excel add-in.
This sprint adds named range registration: after writing to a cell range, FAIT offers to
name it; names persist in custom XML; they can be resolved by name in future prompts;
SettingsPanel gets a management section.

Working directory: /home/fredw/projects/fait-for-excel/
Commit: ed195f7

## Files to Review

1. src/taskpane/services/namedRangeStorage.ts  (NEW)
2. src/taskpane/services/excelWriter.ts        (MODIFIED — sprint 8 functions at bottom)
3. src/taskpane/components/ChatPanel.tsx       (MODIFIED)
4. src/taskpane/components/SettingsPanel.tsx   (MODIFIED)
5. src/taskpane/services/contextFormatter.ts  (MODIFIED)

## Priority Checks — READ and verify EACH one

### CHECK 1 (HIGH): names.add() address format in excelWriter.ts
Read excelWriter.ts around createNamedRange(). Verify:
- `absAddr` is built with regex `([A-Z]+)(\d+)` → `$$$1$$$2`
- `formula = \`=${absAddr}\`` — the = prefix is present
- Mental test: address "Sheet1!A1:D11" → absAddr "Sheet1!$A$1:$D$11" → formula "=Sheet1!$A$1:$D$11"
Exact code to find: `const absAddr = address.replace(/([A-Z]+)(\d+)/g, '$$$1$$$2');`
Then: `const formula = \`=${absAddr}\`;`

### CHECK 2 (HIGH): toAbsoluteReference() regex in namedRangeStorage.ts
Read toAbsoluteReference(). The regex MUST be /([A-Z]+)(\d+)/g — note the + after [A-Z].
Single [A-Z] (no +) would break multi-letter columns: AA10 → $A10 (wrong).
Exact line to find: `return cellPart.replace(/([A-Z]+)(\d+)/g, '$$$1$$$2');`

### CHECK 3 (HIGH): Duplicate check before names.add() in excelWriter.ts
Read createNamedRange(). Verify this EXACT ordering:
1. `const existing = ctx.workbook.names.getItemOrNullObject(name);`
2. `existing.load('isNullObject');`
3. `await ctx.sync();`  ← MUST come BEFORE reading isNullObject
4. `if (!existing.isNullObject) { throw ... }`  ← read AFTER sync
5. THEN: `ctx.workbook.names.add(name, formula);`

### CHECK 4 (HIGH): Name prompt only fires in cell-address branch of ChatPanel.tsx
Read handleWriteTableConfirm(). There are two branches:
- `if (isTableTarget)` → calls writeToTable() → must NOT call handleNameRangeRequest()
- `else` → calls writeRangeData() → MUST call handleNameRangeRequest(result.address)
Verify handleNameRangeRequest is ONLY called in the else branch.

### CHECK 5 (HIGH): Reference resolution uses namedItem.getRange() in ChatPanel.tsx
Read handleSend(). For FAIT_ reference resolution, verify the call chain:
1. `ctx.workbook.names.getItemOrNullObject(entry.name)`  ← workbook.names, NOT worksheet
2. `.load('isNullObject')`
3. `await ctx.sync()`
4. `if (namedItem.isNullObject) throw ...`
5. `const range = namedItem.getRange()`  ← NOT worksheet.getRange(address)
6. `range.load([...])`
7. `await ctx.sync()`

### CHECK 6 (MEDIUM): syncRegistry() empty-list guard in namedRangeStorage.ts
Read syncRegistry(). Verify the FIRST statement is:
`if (liveNames.length === 0) return;`
This MUST be the first check — syncing with empty list would wipe the registry.

### CHECK 7 (MEDIUM): renameWorkbookNamedRange() loads value before delete in excelWriter.ts
Read renameWorkbookNamedRange(). Verify:
1. `item.load(['isNullObject', 'value'])` — both loaded together
2. `await ctx.sync()` — value read HERE
3. `const formula = item.value` — captured BEFORE delete
4. `item.delete()` — THEN delete
5. `ctx.workbook.names.add(newName, formula)` — re-add with captured formula
Must NOT call `.rename()` or set `.name =` (Excel API doesn't support those).

### CHECK 8 (LOW): generateFaitName() uniqueness in namedRangeStorage.ts
Read generateFaitName(). It uses timestamp (date+time) in the name.
Collision risk: if called twice in the same second. Does it have any extra uniqueness mechanism?
Note: since name includes seconds, same-second collision is a low risk in practice.

### CHECK 9 (LOW): contextFormatter.ts namedRangeName param
Read formatContext(). Verify:
- Signature: `formatContext(ctx: SpreadsheetContext, namedRangeName?: string): string`
- When namedRangeName provided: emits `Named range: ${namedRangeName}\n` line
- When NOT provided: output identical to pre-WI824 (guard is `if (namedRangeName)`)

### CHECK 10: Consistency Map
Verify these cross-file consistency items:
- NamedRangeError codes in excelWriter.ts: 'DUPLICATE_NAME' | 'INVALID_NAME' | 'EXCEL_ERROR'
- createNamedRange return type: void (throws on error)
- Name prompt trigger: only after writeRangeData() success in cell-address else branch
- namedRangeName type in contextFormatter.ts: string | undefined (optional param)
- FaitNamedRange shape in namedRangeStorage.ts: { name: string, address: string, created: string }

### CHECK 11: toA1Address() regex in namedRangeStorage.ts (NITPICK)
Read toA1Address(). It strips $ signs. Check if the regex handles multi-letter columns:
`return absAddress.replace(/\$([A-Z])/g, '$1').replace(/\$(\d)/g, '$1');`
For "Sheet1!$AA$10:$BC$20":
- First replace: \$([A-Z]) → removes $ before single letter only
  - "$A" → "A" ✓ but "$AA" → "A" + remaining "A" → "AA" ✓ (captures one letter at a time, which is correct since $ appears before each letter only once in $AA)
  Wait: "$AA" — the regex \$([A-Z]) matches "$A" → "A", leaving "A" → "AA" total. Correct.
  For "$BC": matches "$B" → "B", leaving "C" → "BC". Correct.
- Second replace: \$(\d) → removes $ before single digit
  - "$1" → "1" ✓ but "$10" → "1" + "0" → "10" ✓ (same logic)
Actually verify: does \$(\d) correctly strip "$10"? It matches "$1" leaving "0" → "10". Yes, correct.
Report your conclusion on whether this regex is safe for multi-letter columns.

### CHECK 12: No new npm packages
Check that git diff HEAD~1 HEAD -- package.json shows no new dependencies.

### CHECK 13: Only 5 specified files changed
Check git diff --name-only HEAD~1 HEAD. Should show exactly:
- src/taskpane/services/namedRangeStorage.ts
- src/taskpane/services/excelWriter.ts
- src/taskpane/components/ChatPanel.tsx
- src/taskpane/components/SettingsPanel.tsx
- src/taskpane/services/contextFormatter.ts
(cc-brief-wi824.md is also in the diff — this is the CC brief file, acceptable)

## Output Format

Provide a structured review with:
1. Result for each CHECK (PASS/FAIL/WARN + exact evidence from the code)
2. Overall verdict: PASS / NEEDS-CHANGES / FAIL
3. Issues list: Critical / Important / Nitpick
4. For any FAIL check: quote the exact problematic line and state the exact fix needed
