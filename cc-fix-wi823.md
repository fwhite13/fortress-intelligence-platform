# CC Fix Brief: WI823 Cycle 2 — getDataBodyRangeOrNullObject Guard

## File to Edit
`src/taskpane/services/excelReader.ts`

## Context
In the `getSelectedRange()` function, inside the first `for` loop (lines ~57–75), there is code that reads the data body range of each table. The current code uses `getDataBodyRange()` which throws a `GeneralException` when the table has zero data rows (headers only, no data rows yet). This causes table detection to fail silently.

## Change 1: Replace getDataBodyRange() with getDataBodyRangeOrNullObject()

**Find this exact code (around line 66):**
```typescript
      const dataRange = table.getDataBodyRange();
      dataRange.load(['rowCount']);
```

**Replace with:**
```typescript
      const dataRange = table.getDataBodyRangeOrNullObject();
      dataRange.load(['isNullObject', 'rowCount']);
```

## Change 2: Add isNullObject guard before reading rowCount

After the second `await ctx.sync()`, in the post-sync loop where `dataRange.rowCount` is read, find this code:

```typescript
      baseContext.tableInfo = {
        name: table.name as string,
        columnNames,
        dataRowCount: dataRange.rowCount as number,
        boundAddress: tableRanges[i].address as string,
      };
```

Replace with:

```typescript
      const dataRowCount = dataRange.isNullObject ? 0 : (dataRange.rowCount as number);
      baseContext.tableInfo = {
        name: table.name as string,
        columnNames,
        dataRowCount,
        boundAddress: tableRanges[i].address as string,
      };
```

## Summary
- `getDataBodyRangeOrNullObject()` returns a null object proxy instead of throwing when the table has no data rows
- Loading `'isNullObject'` alongside `'rowCount'` is required so the null object check works after sync
- When `dataRange.isNullObject` is true, `rowCount` is 0 (table has headers only)
- No other files should be touched
