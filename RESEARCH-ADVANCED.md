# RESEARCH-ADVANCED.md
## Excel JavaScript API — Advanced Feature Research
### FAIT for Excel · Sprint 3+ Roadmap

**Author:** Bruce Banner (researcher subagent)  
**Date:** 2026-03-16  
**Baseline:** ExcelApi 1.13 (current FAIT requirement)  
**Reference:** [RESEARCH.md](./RESEARCH.md) (prior report — core architecture, auth, performance patterns)

---

## Table of Contents

1. [Charts](#1-charts)
2. [Pivot Tables](#2-pivot-tables)
3. [Named Ranges & Table Objects](#3-named-ranges--table-objects)
4. [Formulas & workbook.functions](#4-formulas--workbookfunctions)
5. [Conditional Formatting](#5-conditional-formatting)
6. [Workbook Events](#6-workbook-events)
7. [Sheets (Worksheets)](#7-sheets-worksheets)
8. [Comments & Notes](#8-comments--notes)
9. [API Requirement Sets Summary](#9-api-requirement-sets-summary)
10. [Known Limitations vs VBA/COM](#10-known-limitations-vs-vbacom)

---

## 1. Charts

### Key API Objects & Methods

| Object / Method | Description |
|---|---|
| `worksheet.charts` | `ChartCollection` — all charts on a sheet |
| `charts.add(type, dataRange, seriesBy)` | Create a chart |
| `charts.getItem(name)` / `getItemAt(index)` | Get existing chart |
| `chart.delete()` | Remove chart |
| `chart.series.add(name)` | Add a new data series |
| `series.setValues(range)` / `setXAxisValues(range)` | Bind series to range |
| `chart.title.text` | Set chart title |
| `chart.axes.valueAxis` / `.categoryAxis` | Axis configuration |
| `chart.legend` | Legend position and style |
| `chart.dataLabels` | Show/style data labels |
| `chart.getImage()` | Export chart as Base64 JPEG |
| `series.trendlines.add(type)` | Add trendline |
| `chart.getDataTableOrNullObject()` | Access/add chart data table |

### Chart Types Available (`Excel.ChartType`)
All major chart types are supported including:
`line`, `lineStacked`, `lineStacked100`, `lineMarkers`,
`bar`, `barStacked`, `columnClustered`, `columnStacked`,
`pie`, `doughnut`, `scatter`, `area`, `areaStacked`,
`radar`, `bubble`, `stockOHLC`, `stockHLC`, `waterfall`,
`histogram`, `pareto`, `boxWhisker`, `sunburst`, `treemap`,
`funnel`, `regionMap` (Power Map, Desktop only)

### Minimal Working Example

```typescript
// Create a chart bound to data range, then read it back
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getItem("Data");
  const dataRange = sheet.getRange("A1:B10");

  // Create chart
  const chart = sheet.charts.add(
    Excel.ChartType.columnClustered,
    dataRange,
    Excel.ChartSeriesBy.columns
  );
  chart.title.text = "Monthly Revenue";
  chart.setPosition("D1", "L15");      // position by cell addresses
  chart.name = "RevenueChart";
  await context.sync();

  // Read chart back
  const existing = sheet.charts.getItem("RevenueChart");
  existing.load(["title", "chartType"]);
  await context.sync();
  console.log(existing.title.text, existing.chartType);

  // Update data series dynamically
  existing.series.getItemAt(0).setValues(sheet.getRange("B2:B12"));
  await context.sync();

  // Delete
  // existing.delete(); await context.sync();
});
```

### Export Chart as Image

```typescript
const chart = context.workbook.worksheets.getItem("Sheet1").charts.getItem("Chart1");
const image = chart.getImage(600, 400, Excel.ImageFittingMode.fit);
await context.sync();
const base64 = image.value; // embed in <img src="data:image/jpeg;base64,...">
```

### Gotchas & Limitations

- **Chart types with Maps** (`regionMap`) require Desktop — not available in Excel Online.
- `chart.setPosition()` takes A1-notation strings or Range objects; passing pixel coords is NOT supported.
- You can bind a chart series to a range, but the binding is by value at the time of `setValues`; there is no live "live link" that auto-tracks. The chart does re-read the range data when the workbook recalculates normally.
- `chart.getImage()` returns JPEG only (not PNG). Image fidelity may differ between Online and Desktop.
- **Chart events** (`onActivated`, `onDeactivated`, `onAdded`, `onDeleted`) are available from ExcelApi 1.8.
- Cannot change chart type after creation via JS API directly — delete and recreate if type change is needed (the `chartType` property is read-only on an existing `Chart` object).

### Minimum Requirement Set
- Basic charts (create, format, read): **ExcelApi 1.1**
- Series manipulation, trendlines: **ExcelApi 1.7**
- Chart events, PivotCharts: **ExcelApi 1.8**
- `getDataTableOrNullObject`, data table formatting: **ExcelApi 1.14**
- `getImage()`: **ExcelApi 1.2**

---

## 2. Pivot Tables

### Key API Objects & Methods

| Object / Method | Description |
|---|---|
| `worksheet.pivotTables.add(name, source, dest)` | Create PivotTable |
| `workbook.pivotTables.getItem(name)` | Access by name |
| `pivotTable.rowHierarchies.add(hierarchy)` | Add row field |
| `pivotTable.columnHierarchies.add(hierarchy)` | Add column field |
| `pivotTable.dataHierarchies.add(hierarchy)` | Add value field |
| `pivotTable.filterHierarchies.add(hierarchy)` | Add filter field |
| `pivotTable.hierarchies.getItem(name)` | Get a specific hierarchy |
| `pivotTable.refresh()` | Refresh data |
| `pivotTable.layout.getDataBodyRange()` | Get the values range |
| `pivotTable.layout.layoutType` | compact/outline/tabular |
| `pivotTable.layout.showColumnGrandTotals` | Toggle grand totals |
| `dataHierarchy.summarizeBy` | SUM, COUNT, AVERAGE, MAX, MIN, etc. |
| `pivotField.applyFilter(filter)` | Apply PivotFilter |
| `pivotField.clearAllFilters()` | Clear filters |

### Object Model Hierarchy
```
PivotTable
  ├── hierarchies: PivotHierarchyCollection  (all available fields)
  ├── rowHierarchies: RowColumnPivotHierarchyCollection
  ├── columnHierarchies: RowColumnPivotHierarchyCollection
  ├── dataHierarchies: DataPivotHierarchyCollection
  ├── filterHierarchies: FilterPivotHierarchyCollection
  └── layout: PivotLayout
       ├── getDataBodyRange()
       ├── getColumnLabelRange()
       └── getRowLabelRange()
```

### Minimal Working Example

```typescript
await Excel.run(async (context) => {
  // Create PT from data on "DataSheet", place on "PivotSheet"
  const dataRange = context.workbook.worksheets.getItem("DataSheet").getRange("A1:E21");
  const pivotSheet = context.workbook.worksheets.getItem("PivotSheet");

  pivotSheet.pivotTables.add("SalesPivot", dataRange, pivotSheet.getRange("A2"));
  await context.sync();

  const pt = pivotSheet.pivotTables.getItem("SalesPivot");

  // Add fields
  pt.rowHierarchies.add(pt.hierarchies.getItem("Category"));
  pt.rowHierarchies.add(pt.hierarchies.getItem("Region"));
  pt.columnHierarchies.add(pt.hierarchies.getItem("Year"));
  pt.dataHierarchies.add(pt.hierarchies.getItem("Revenue"));
  await context.sync();

  // Change aggregation
  const dataHierarchy = pt.dataHierarchies.getItemAt(0);
  dataHierarchy.load("summarizeBy");
  await context.sync();
  dataHierarchy.summarizeBy = Excel.AggregationFunction.sum;

  // Refresh
  pt.refresh();
  await context.sync();

  // Read values from PT output range
  const bodyRange = pt.layout.getDataBodyRange();
  bodyRange.load("values");
  await context.sync();
  console.log(bodyRange.values);
});
```

### Filtering PivotFields (ExcelApi 1.12+)

```typescript
// Value filter: show only rows where sum > 500
const pivotField = pt.dataHierarchies.getItemAt(0).field;
pivotField.applyFilter({
  valueFilter: {
    condition: Excel.ValueFilterCondition.greaterThan,
    comparator: { value: 500 },
    value: "Revenue",
  }
});
await context.sync();
```

### Gotchas & Limitations

- **OLAP PivotTables are not supported** by the JS API at all — you can read them but cannot programmatically create or fully configure them. Power Pivot data models are similarly off-limits.
- **PivotTable creation is from a flat range or Table only** — not from an external data connection.
- Each `PivotHierarchy` can only exist in one bucket (row, column, data, or filter) at a time. Adding it elsewhere automatically removes it from its current bucket.
- `pivotTable.refresh()` triggers data refresh — but if the source data is a static range (not an external connection), "refresh" just recalculates. For external data connections (e.g., Power Query), refresh is possible programmatically in some configurations.
- `summarizeBy` options: `sum`, `count`, `average`, `max`, `min`, `product`, `countNumbers`, `standardDeviation`, `standardDeviationP`, `variance`, `varianceP`, `unknown`.
- No API for **Calculated Fields** or **Calculated Items** — these must be created via UI.
- Slicers can be created from code (ExcelApi 1.10) and connected to PivotTables.
- **Excel Online vs Desktop:** Generally parity for PT creation and hierarchy management. OLAP and Power Pivot remain Desktop-only.

### Minimum Requirement Set
- PivotTable creation & hierarchy manipulation: **ExcelApi 1.8**
- PivotFilters (value/date/label/manual): **ExcelApi 1.12**
- Slicers: **ExcelApi 1.10**

---

## 3. Named Ranges & Table Objects

### Named Ranges — `workbook.names` / `worksheet.names`

| Object / Method | Description |
|---|---|
| `workbook.names` | `NamedItemCollection` — workbook-scoped names |
| `worksheet.names` | `NamedItemCollection` — sheet-scoped names |
| `names.add(name, reference, comment)` | Create named range |
| `names.getItem(name)` | Get by name |
| `names.getItemOrNullObject(name)` | Safe get |
| `namedItem.getRange()` | Get the Range object for a named range |
| `namedItem.formula` | Get/set formula (e.g. `"=Sheet1!$A$1:$B$10"`) |
| `namedItem.delete()` | Delete the name |
| `namedItem.scope` | `"Workbook"` or `"Worksheet"` |
| `namedItem.type` | `"Range"`, `"String"`, `"Integer"`, etc. |
| `namedItem.visible` | Hide name from Name Manager UI |

#### Named Ranges Example

```typescript
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getItem("Data");

  // Create workbook-scoped named range
  context.workbook.names.add("TotalRevenue", sheet.getRange("C2:C100"));

  // Create sheet-scoped named range
  sheet.names.add("LocalHeader", sheet.getRange("A1:E1"));
  await context.sync();

  // Read it back
  const namedRange = context.workbook.names.getItem("TotalRevenue");
  const range = namedRange.getRange();
  range.load("address");
  await context.sync();
  console.log(range.address); // "Data!C2:C100"

  // Update the formula (move the range)
  namedRange.formula = "=Data!$C$2:$C$200";
  await context.sync();

  // List all names
  const allNames = context.workbook.names.load("items");
  await context.sync();
  allNames.items.forEach(n => console.log(n.name, n.type, n.scope));
});
```

### Table Objects — `worksheet.tables`

Tables (`ListObject` in COM/VBA, `Table` in JS API) are first-class structured objects.

| Object / Method | Description |
|---|---|
| `worksheet.tables.add(address, hasHeaders)` | Create table from range |
| `worksheet.tables.getItem(name)` | Get by name |
| `table.name` | Get/set table name |
| `table.getRange()` | Full table range including headers |
| `table.getDataBodyRange()` | Data rows only (no header/total) |
| `table.getHeaderRowRange()` | Header row |
| `table.getTotalRowRange()` | Total row (if enabled) |
| `table.columns` | `TableColumnCollection` |
| `table.rows` | `TableRowCollection` |
| `table.rows.add(index, values)` | Add row(s) |
| `table.rows.deleteRowsAt(index, count)` | Delete rows |
| `table.columns.add(index, values, name)` | Add column |
| `table.showTotals` | Show/hide totals row |
| `table.sort.apply(fields)` | Sort the table |
| `table.autoFilter` | Access/apply autofilter on table |
| `table.convertToRange()` | Convert table back to plain range |
| `table.delete()` | Delete the table (and its data) |
| `table.onChanged` | Event: data changed |
| `table.onSelectionChanged` | Event: selection changed |

#### Structured References
Structured references (`Table1[Column1]`) can be used in formula strings. You cannot use them as a direct API argument — always pass a `Range` object or A1 address instead.

#### Tables Example

```typescript
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getItem("Data");

  // Create table
  const table = sheet.tables.add("A1:D10", true); // true = has headers
  table.name = "SalesTable";
  await context.sync();

  // Add a column
  table.columns.add(null, [["Total"], [100], [200], [300]], "Calculated");

  // Add rows
  table.rows.add(null, [["Alice", "East", 50, 150], ["Bob", "West", 75, 225]]);
  await context.sync();

  // Read data body
  const dataRange = table.getDataBodyRange();
  dataRange.load("values");
  await context.sync();
  console.log(dataRange.values);

  // Sort by column index 2 descending
  table.sort.apply([{ key: 2, ascending: false }]);
  await context.sync();
});
```

### Gotchas & Limitations

- Table names must be unique per workbook (Excel enforces this at the application level).
- `table.convertToRange()` destroys the Table object — subsequent `getItem()` calls will throw.
- Structured references in formula strings work fine in ExcelApi (set `range.formulas`), but the JS API itself doesn't expose a structured-reference resolver.
- Named items at worksheet scope shadow workbook scope names of the same name.
- `names.add()` with a formula reference must use an `=` prefix: `"=Sheet1!$A$1:$B$5"`.
- `namedItem.formula` getter returns the formula including the `=`. Setter expects no `=` prefix in some versions — test carefully and use the full `"=..."` form consistently.

### Minimum Requirement Set
- `NamedItemCollection.add`: **ExcelApi 1.4**
- `worksheet.names`: **ExcelApi 1.4**
- `Table` full CRUD: **ExcelApi 1.1**
- Table sort: **ExcelApi 1.2**
- `table.convertToRange()`: **ExcelApi 1.2**
- Table events (`onChanged`, `onSelectionChanged`): **ExcelApi 1.7**

---

## 4. Formulas & `workbook.functions`

### Formula Read/Write

| Operation | API | Notes |
|---|---|---|
| Set formula | `range.formulas = [["=SUM(A1:A10)"]]` | Always en-US function names |
| Set localized formula | `range.formulasLocal = [["=SUMME(A1:A10)"]]` | User's locale |
| Set R1C1 formula | `range.formulasR1C1 = [["=R[-1]C+R[-2]C"]]` | R1C1 notation |
| Read formula | `range.load("formulas")` | Returns formula string or value if no formula |
| Read computed value | `range.load("values")` | Always the computed/displayed result |
| Read value with type | `range.load("valueTypes")` | `"Boolean"`, `"Double"`, `"Error"`, `"Empty"`, `"String"` |
| Read raw number | `range.load("numberFormat")` | Format string (not the value itself) |

### Formulas vs Values

```typescript
await Excel.run(async (context) => {
  const range = context.workbook.worksheets.getActiveWorksheet().getRange("A1:A5");
  range.load(["formulas", "values", "valueTypes"]);
  await context.sync();

  // formulas[i][0] — the formula text (e.g. "=SUM(B1:B10)") or the raw value if no formula
  // values[i][0]  — the computed result (always a number, string, or bool)
  range.formulas.forEach((row, i) => {
    console.log(`A${i+1}: formula=${row[0]}, value=${range.values[i][0]}, type=${range.valueTypes[i][0]}`);
  });
});
```

> **Key distinction:** `formulas` returns the formula text if a formula exists, or the raw cell value otherwise. `values` always returns the computed/displayed value.

### Dynamic Arrays (ExcelApi 1.12+)
Cells with spill formulas expose:
- `range.hasSpill` — true if the cell has a spill formula
- `range.getSpillingToRange()` — the full spill output range
- `range.getSpillParent()` — the anchor cell of a spill range

### `workbook.functions` — Evaluate Without Writing to Cells

`workbook.functions` exposes Excel's built-in worksheet functions as callable JS methods. Results are returned as `FunctionResult<T>` objects — **no cells are written**.

**Critical:** Only Excel's ~300+ built-in worksheet functions are available. Custom functions (UDFs) and arbitrary formula strings are NOT evaluatable this way.

```typescript
await Excel.run(async (context) => {
  const fns = context.workbook.functions;

  // Simple: SUM of a range
  const range = context.workbook.worksheets.getItem("Sheet1").getRange("A1:A10");
  const sumResult = fns.sum(range);
  sumResult.load("value");

  // VLOOKUP
  const lookupRange = context.workbook.worksheets.getItem("Sheet1").getRange("A1:D50");
  const vlookupResult = fns.vlookup("ProductA", lookupRange, 2, false);
  vlookupResult.load("value");

  // Chained/nested: SUM of two VLOOKUPs (no intermediate sync needed)
  const sumOfLookups = fns.sum(
    fns.vlookup("ProductA", lookupRange, 3, false),
    fns.vlookup("ProductB", lookupRange, 3, false)
  );
  sumOfLookups.load("value");

  await context.sync();
  console.log(sumResult.value, vlookupResult.value, sumOfLookups.value);
});
```

### Important `workbook.functions` Limitation

> ⚠️ **You cannot evaluate an arbitrary formula string** (e.g., `"=SUM(A1)+IF(B1>0, C1, D1)"`). The `functions` object only exposes pre-typed methods for known worksheet functions. For arbitrary formula evaluation without writing to a cell, there is **no official API** as of 2026. The only workaround is to write to a scratch cell in a hidden sheet, sync, read back, then clear it.

### Minimum Requirement Set
- `range.formulas` read/write: **ExcelApi 1.1**
- `workbook.functions`: **ExcelApi 1.2**
- `range.formulasR1C1`: **ExcelApi 1.1**
- Dynamic array / spill properties: **ExcelApi 1.12**
- `range.valuesAsJson` (rich value types): **ExcelApi 1.16**

---

## 5. Conditional Formatting

### Key API Objects

| Object / Method | Description |
|---|---|
| `range.conditionalFormats` | `ConditionalFormatCollection` |
| `conditionalFormats.add(type)` | Add a CF rule |
| `conditionalFormats.getItem(id)` | Get rule by ID |
| `conditionalFormats.clearAll()` | Remove all rules from range |
| `conditionalFormat.priority` | Rule priority (lower = higher priority) |
| `conditionalFormat.stopIfTrue` | Stop evaluating further rules if this fires |

### Format Types (`Excel.ConditionalFormatType`)

| Type | Description |
|---|---|
| `cellValue` | Value-based (>, <, between, etc.) |
| `colorScale` | 2- or 3-color gradient |
| `dataBar` | In-cell bar chart |
| `iconSet` | Traffic lights, arrows, stars, etc. |
| `preset` | Built-in presets (AboveAverage, TopTen, Unique, etc.) |
| `textComparison` | Text contains/starts with/ends with |
| `topBottom` | Top/bottom N values or % |
| `custom` | Formula-based (arbitrary formula) |
| `presetCriteria` | Specific presets (BelowAverage, Duplicate, etc.) |

### Minimal Working Examples

```typescript
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getItem("Data");
  const range = sheet.getRange("B2:B100");

  // 1. Cell value rule: red font for negatives
  const negRule = range.conditionalFormats.add(Excel.ConditionalFormatType.cellValue);
  negRule.cellValue.format.font.color = "red";
  negRule.cellValue.format.font.bold = true;
  negRule.cellValue.rule = { formula1: "=0", operator: "LessThan" };

  // 2. Color scale: blue (min) → yellow (mid) → red (max)
  const scaleRule = range.conditionalFormats.add(Excel.ConditionalFormatType.colorScale);
  scaleRule.colorScale.criteria = {
    minimum: { type: Excel.ConditionalFormatColorCriterionType.lowestValue, formula: null, color: "#0070C0" },
    midpoint: { type: Excel.ConditionalFormatColorCriterionType.percent, formula: "50", color: "#FFFF00" },
    maximum: { type: Excel.ConditionalFormatColorCriterionType.highestValue, formula: null, color: "#FF0000" }
  };

  // 3. Data bar
  const barRule = range.conditionalFormats.add(Excel.ConditionalFormatType.dataBar);
  barRule.dataBar.barDirection = Excel.ConditionalDataBarDirection.leftToRight;
  barRule.dataBar.positiveFormat.fillColor = "#638EC6";
  barRule.dataBar.negativeFormat.fillColor = "#FF0000";

  // 4. Icon set (traffic lights)
  const iconRule = range.conditionalFormats.add(Excel.ConditionalFormatType.iconSet);
  iconRule.iconSet.style = Excel.IconSet.threeTrafficLights1;

  // 5. Custom formula-based (highlight if > cell to the left)
  const customRule = range.conditionalFormats.add(Excel.ConditionalFormatType.custom);
  customRule.custom.rule.formula = "=B2>A2";
  customRule.custom.format.fill.color = "#E2EFDA";

  await context.sync();
});
```

### Reading Existing Rules

```typescript
await Excel.run(async (context) => {
  const range = context.workbook.worksheets.getItem("Data").getRange("B2:B100");
  const formats = range.conditionalFormats;
  formats.load("items");
  await context.sync();

  formats.items.forEach((cf) => {
    cf.load(["type", "priority", "stopIfTrue"]);
  });
  await context.sync();

  formats.items.forEach((cf) => {
    console.log(`Type: ${cf.type}, Priority: ${cf.priority}`);
  });
});
```

### Gotchas & Limitations

- `clearAll()` removes rules from the **intersection** of the specified range and existing rules — not exactly the same semantics as "clear all from this exact range". Rules applied to larger ranges survive but their extent is trimmed.
- `priority` works inversely to what you might expect: priority `1` is evaluated **first** (highest priority).
- The `custom` format type with `formulaR1C1` is useful for relative-reference rules (like "highlight if this cell > cell to its left").
- There is no API to **get the actual fill/font of a cell after CF is applied** — you can only read the CF rule definitions, not the visual result.
- **Excel Online vs Desktop:** Full parity for all CF types listed above. The `presetCriteria` type (unique values, duplicates, blanks) is fully supported.

### Minimum Requirement Set
- All conditional formatting types: **ExcelApi 1.6**

---

## 6. Workbook Events

### Full Events Table

| Event | Object(s) | ExcelApi |
|---|---|---|
| `onActivated` | Worksheet, WorksheetCollection, Chart, ChartCollection, Shape, Workbook | 1.7 |
| `onDeactivated` | Worksheet, WorksheetCollection, Chart, ChartCollection, Shape | 1.7 |
| `onChanged` | Worksheet, WorksheetCollection, Table, TableCollection, CommentCollection | 1.7 |
| `onSelectionChanged` | Worksheet, WorksheetCollection, Table, Workbook, Binding | 1.7 |
| `onCalculated` | Worksheet, WorksheetCollection | 1.8 |
| `onAdded` | WorksheetCollection, ChartCollection, TableCollection, CommentCollection | 1.7 |
| `onDeleted` | WorksheetCollection, ChartCollection, TableCollection, CommentCollection | 1.7 |
| `onFormatChanged` | Worksheet, WorksheetCollection | 1.9 |
| `onFormulaChanged` | Worksheet, WorksheetCollection | 1.13 |
| `onRowHiddenChanged` | Worksheet, WorksheetCollection | 1.11 |
| `onColumnSorted` | Worksheet, WorksheetCollection | 1.10 |
| `onRowSorted` | Worksheet, WorksheetCollection | 1.10 |
| `onSingleClicked` | Worksheet, WorksheetCollection | 1.10 |
| `onVisibilityChanged` | Worksheet, WorksheetCollection | 1.11 |
| `onProtectionChanged` | Worksheet, WorksheetCollection | 1.14 |
| `onNameChanged` | Worksheet, WorksheetCollection | 1.17 |
| `onMoved` | WorksheetCollection | ExcelApiOnline / 1.17 |
| `onAutoSaveSettingChanged` | Workbook | 1.9 |
| `onSettingsChanged` | SettingCollection | 1.4 |
| `onDataChanged` | Binding | 1.1 |

### Key `onChanged` Event Args

```typescript
worksheet.onChanged.add(async (eventArgs: Excel.WorksheetChangedEventArgs) => {
  await Excel.run(async (context) => {
    console.log({
      address: eventArgs.address,         // e.g. "A1:B3"
      changeType: eventArgs.changeType,   // "RangeEdited", "RowInserted", "RowDeleted", "ColumnInserted", "ColumnDeleted", "CellInserted", "CellDeleted"
      source: eventArgs.source,           // "Local" or "Remote"
      worksheetId: eventArgs.worksheetId,
      triggerSource: eventArgs.triggerSource, // "ThisLocalAddin" or "OtherLocalAddin" etc. (ExcelApi 1.14)
    });

    // Optionally read what changed:
    const changed = context.workbook.worksheets
      .getItem(eventArgs.worksheetId)
      .getRange(eventArgs.address);
    changed.load("values");
    await context.sync();
    console.log("New values:", changed.values);
  });
});
await context.sync();
```

### `onFormulaChanged` (ExcelApi 1.13 — already in FAIT baseline!)

```typescript
worksheet.onFormulaChanged.add(async (event: Excel.WorksheetFormulaChangedEventArgs) => {
  await Excel.run(async (context) => {
    event.formulaDetails.forEach(detail => {
      console.log(`Cell: ${detail.cellAddress}`);
      console.log(`Old formula: ${detail.previousFormula}`);
    });
  });
});
```

### Disable Events for Batch Operations

```typescript
await Excel.run(async (context) => {
  context.runtime.enableEvents = false;  // suppress event firing
  // ... batch writes ...
  context.runtime.enableEvents = true;
  await context.sync();
});
```

### Gotchas & Limitations

- **Event handlers do NOT persist** across task pane reload/close. Re-register on init.
- `onChanged` `source` is `"Local"` for user edits, `"Remote"` for changes by other users in co-authoring (Excel Online), and `"ThisLocalAddin"` for changes made by your own add-in code. Use this to avoid infinite loops.
- **Excel Online coalesces rapid events** — if cells change rapidly (e.g., pasting a large range), you may get one event with a combined address range rather than individual cell events.
- **`onCalculated` fires after Excel finishes computing ALL formulas** — useful for reading updated formula results. In Excel Online this may be slightly delayed vs Desktop due to async nature.
- `context.runtime.enableEvents = false` is a critical pattern for bulk writes — without it, every cell write triggers an event, which can cause recursive loops and severe perf degradation.
- Event handlers that do heavy async work should avoid blocking — best practice is to queue work or debounce.
- **`onSelectionChanged` fires very frequently** (on every arrow key press, mouse click, etc.) — debounce aggressively if you do any work in this handler.

### Excel Online vs Desktop Parity
- All events from 1.7 onward available in Excel Online.
- `onMoved` (worksheet moved) is in `ExcelApiOnline` first, then promoted.
- Events from remote users (`source: "Remote"`) are **only available in Excel Online** (co-authoring) — Desktop single-user mode always shows `"Local"`.

### Minimum Requirement Set
- Core events (`onChanged`, `onSelectionChanged`, `onActivated`, etc.): **ExcelApi 1.7**
- `onCalculated`: **ExcelApi 1.8**
- `onFormulaChanged`: **ExcelApi 1.13** ✅ (already in FAIT baseline)
- `triggerSource` on event args: **ExcelApi 1.14**
- `onNameChanged`: **ExcelApi 1.17**

---

## 7. Sheets (Worksheets)

### Key API Objects & Methods

| Object / Method | Description |
|---|---|
| `workbook.worksheets` | `WorksheetCollection` |
| `worksheets.add(name)` | Add a new sheet |
| `worksheets.getItem(name)` | Get by name |
| `worksheets.getItemOrNullObject(name)` | Safe get |
| `worksheets.getActiveWorksheet()` | Active sheet |
| `worksheets.getFirst()` / `getLast()` | First/last sheet |
| `worksheet.delete()` | Delete a sheet |
| `worksheet.name` | Get/set sheet name |
| `worksheet.position` | Get/set position (0-indexed) |
| `worksheet.visibility` | `"Visible"`, `"Hidden"`, `"VeryHidden"` |
| `worksheet.copy(positionType, relativeTo)` | Copy sheet |
| `worksheet.activate()` | Make sheet active |
| `worksheet.getUsedRange()` | Used range |
| `worksheet.protection.protect(options)` | Protect sheet |
| `worksheet.protection.unprotect(password)` | Unprotect |
| `worksheet.showGridlines` | Toggle gridlines |
| `worksheet.tabColor` | Set tab color |

### Minimal Working Example

```typescript
await Excel.run(async (context) => {
  const sheets = context.workbook.worksheets;

  // Add new sheet
  const newSheet = sheets.add("Analysis");
  newSheet.tabColor = "#0070C0"; // blue tab
  await context.sync();

  // Rename existing
  const dataSheet = sheets.getItem("Sheet1");
  dataSheet.name = "RawData";
  await context.sync();

  // Reorder: move "Analysis" to position 0 (first)
  newSheet.position = 0;
  await context.sync();

  // Copy a sheet — place it after "RawData"
  const copyRef = sheets.getItem("Template");
  const copied = copyRef.copy(
    Excel.WorksheetPositionType.after,
    sheets.getItem("RawData")
  );
  await context.sync();
  copied.name = "Report_" + new Date().toISOString().slice(0, 10);
  await context.sync();

  // Hide a sheet
  sheets.getItem("Scratch").visibility = Excel.SheetVisibility.hidden;

  // Very hide (not visible in Excel's UI sheet tab bar — requires API to unhide)
  sheets.getItem("Config").visibility = Excel.SheetVisibility.veryHidden;

  await context.sync();

  // Delete
  // sheets.getItem("OldSheet").delete(); await context.sync();
});
```

### List All Sheets with Properties

```typescript
await Excel.run(async (context) => {
  const sheets = context.workbook.worksheets;
  sheets.load("items/name,items/position,items/visibility,items/tabColor");
  await context.sync();

  sheets.items.forEach(ws => {
    console.log(`${ws.position}: ${ws.name} [${ws.visibility}] tab:${ws.tabColor}`);
  });
});
```

### Worksheet Protection

```typescript
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getItem("Report");

  sheet.protection.protect({
    allowFormatCells: true,
    allowFormatColumns: false,
    allowFormatRows: false,
    allowInsertRows: false,
    allowDeleteRows: false,
    allowSort: true,
    allowAutoFilter: true,
    allowPivotTables: false,
    selectionMode: Excel.ProtectionSelectionMode.normal // or "none" or "unlocked"
  }); // optionally pass a password string as second arg

  await context.sync();
});
```

### Gotchas & Limitations

- `worksheet.delete()` throws if only one sheet remains in the workbook — guard with a count check.
- Sheet `name` must be unique, ≤31 characters, and cannot contain: `/ \ * ? : [ ]`.
- `worksheet.copy()` is available in ExcelApi **1.7**; the `positionType` and `relativeTo` parameters are required.
- `veryHidden` sheets cannot be unhidden by the user through the UI — only via API. This is a useful technique for config/scratch sheets.
- **Tab color**: pass HTML hex color (`"#FF0000"`) or `""` to clear. Available since ExcelApi 1.7.
- `worksheets.getFirst(visibleOnly: boolean)` and `getLast(visibleOnly: boolean)` — the `visibleOnly` parameter is ExcelApi 1.5.
- **Excel Online:** No UI-level "very hidden" toggle (VeryHidden is maintained from Desktop files), but API can set/clear it in both environments.

### Minimum Requirement Set
- Add/delete/rename/position/visibility: **ExcelApi 1.1**
- `tabColor`, `copy()`: **ExcelApi 1.7**
- `showGridlines`, `showHeadings`: **ExcelApi 1.8**
- `onNameChanged` event: **ExcelApi 1.17**
- `protection` allow-edit ranges: **ExcelApiOnline / ExcelApi 1.16**

---

## 8. Comments & Notes

### Comments vs Notes — Important Distinction

Excel has **two distinct annotation types** since Excel 365:

| | Comments (`workbook.comments`) | Notes (`range.note`) |
|---|---|---|
| UI name | "Comment" (with speech bubble) | "Note" (with red triangle) |
| Threading | ✅ Full thread with replies | ❌ Single author, no replies |
| Author | Attributed to current user | Plain text |
| Co-authoring | Visible to all; resolves | Visible to all |
| API since | ExcelApi 1.10 | ExcelApi 1.12 |
| @mentions | ✅ Yes | ❌ No |

### Comments API

```typescript
await Excel.run(async (context) => {
  // Add a comment thread to a cell
  const comments = context.workbook.comments;
  comments.add("Sheet1!A1", "TODO: Verify this total.");
  await context.sync();

  // Add a comment with @mention
  const mention = { email: "jane@contoso.com", id: 0, name: "Jane Smith" };
  comments.add("Sheet1!B2", {
    mentions: [mention],
    richContent: `<at id="0">${mention.name}</at> please review this.`
  }, Excel.ContentType.mention);
  await context.sync();

  // Read comment at a cell
  const comment = comments.getItemByCell("Sheet1!A1");
  comment.load(["content", "authorName", "authorEmail", "creationDate", "resolved"]);
  await context.sync();
  console.log(comment.authorName, comment.content, comment.creationDate);

  // Add reply to comment thread
  comment.replies.add("Verified — looks correct.");
  await context.sync();

  // Resolve a thread
  comment.resolved = true;
  await context.sync();

  // Delete comment (deletes entire thread + all replies)
  // comment.delete(); await context.sync();

  // Delete a single reply
  const reply = comment.replies.getItemAt(0);
  reply.delete();
  await context.sync();
});
```

### Notes API (ExcelApi 1.12+)

```typescript
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getItem("Sheet1");

  // Add note
  sheet.notes.add("A1", "This value is sourced from Q3 2025 report.");
  await context.sync();

  // Read note
  const note = sheet.notes.getItem("A1");
  note.load(["note", "author"]);
  await context.sync();
  console.log(note.note, note.author);

  // Edit note
  note.note = "Updated: sourced from Q4 2025 report.";
  await context.sync();

  // Delete note
  note.delete();
  await context.sync();
});
```

### Comment Events

```typescript
// Listen for comment additions at workbook level
const commentEventHandler = context.workbook.comments.onAdded.add(async (event) => {
  console.log("Comment added:", event.commentDetails[0].commentId);
});

// Also available: onChanged, onDeleted
await context.sync();
```

### Gotchas & Limitations

- `comments.add()` takes a **single cell** address only — multi-cell ranges throw `InvalidArgument`.
- Comments attributed to the add-in are attributed to the **current signed-in user** — you cannot spoof authorship.
- The `@mention` format is strict: `<at id="{index}">{Full Name}</at>` — shortened names not yet supported.
- Reading `comment.resolved` is available read/write; toggling to `true` collapses the thread in the UI.
- **Excel Online:** Full comment API parity including @mentions. Mentions trigger email notifications.
- **Notes (`worksheet.notes`):** Available since ExcelApi 1.12, fully supported in both Online and Desktop.
- Notes are entirely separate from comment threads — you cannot mix them at the same cell, but a cell can have both a note AND a comment thread.

### Minimum Requirement Set
- Comments (threads, replies, resolve, @mentions): **ExcelApi 1.10**
- Comment events (`onAdded`, `onChanged`, `onDeleted`): **ExcelApi 1.10** (on `CommentCollection`)
- Notes (`worksheet.notes`): **ExcelApi 1.12**

---

## 9. API Requirement Sets Summary

Current FAIT baseline: **ExcelApi 1.13**

| Feature Area | Minimum ExcelApi | Notes |
|---|---|---|
| Charts (basic create/format/read) | 1.1 | Basic charts |
| Chart image export | 1.2 | `chart.getImage()` |
| Chart events (onActivated etc.) | 1.8 | + ChartCollection events |
| Chart data table | 1.14 | `getDataTableOrNullObject()` |
| PivotTables (create/configure) | **1.8** | Core PT creation |
| PivotTable filters (`applyFilter`) | **1.12** | PivotFilters API |
| Slicers | 1.10 | Slicer + PivotTable link |
| Named ranges (`names.add`) | 1.4 | Workbook & worksheet scope |
| Tables (full CRUD) | 1.1 | ListObject equivalent |
| Table events | 1.7 | `onChanged`, `onSelectionChanged` |
| `workbook.functions` | 1.2 | Evaluate built-in functions |
| Dynamic array / spill | **1.12** | `hasSpill`, `getSpillingToRange()` |
| `valuesAsJson` (rich types) | 1.16 | Beyond baseline |
| Conditional formatting | **1.6** | All types |
| Events (onChanged, onSelectionChanged) | 1.7 | Core events |
| `onCalculated` | 1.8 | Post-calculation hook |
| `onFormulaChanged` | 1.13 | ✅ In FAIT baseline |
| `onFormatChanged` | 1.9 | |
| `onRowSorted`, `onColumnSorted` | 1.10 | |
| `onSingleClicked` | 1.10 | |
| `triggerSource` on event args | 1.14 | Beyond baseline |
| `onNameChanged` | 1.17 | Beyond baseline |
| Sheet add/delete/rename/move | 1.1 | |
| Sheet `copy()`, `tabColor` | 1.7 | |
| Sheet `showGridlines` | 1.8 | |
| Sheet protection (allow-edit ranges) | 1.16 | Beyond baseline |
| Comments (threads + replies) | 1.10 | |
| Comment events | 1.10 | |
| Notes (`worksheet.notes`) | **1.12** | |
| `context.runtime.enableEvents` | 1.9 | Batch write optimization |

### Requirement Set Availability Map

| ExcelApi | Min M365 Windows | Min Mac | Online |
|---|---|---|---|
| 1.13 | v2102 (Build 13801) | 16.50 | ✅ |
| 1.14 | v2108 (Build 14326) | 16.52 | ✅ |
| 1.15 | v2202 (Build 14931) | 16.58 | ✅ |
| 1.16 | v2208 (Build 15601) | 16.64 | ✅ |
| 1.17 | v2302 (Build 16130) | 16.70 | ✅ |
| 1.18 | v2501 (Build 18429) | 16.93 | ✅ |
| 1.19 | v2504 (Build 18730) | 16.96 | ✅ |
| 1.20 | v2509 (Build 19201) | 16.100 | ✅ |

> **Note:** ExcelApi 1.17 is the highest version available in Office 2024 LTSC. Versions 1.18+ require Microsoft 365 subscription. If FAIT targets Office 2024 perpetual, cap at 1.17.

### FAIT Upgrade Recommendation

**Upgrade from 1.13 → 1.14** immediately (free wins):
- `triggerSource` on event args (prevents add-in self-loops)
- Chart data table API
- Sheet protection enhancements

**Upgrade to 1.16** for Sprint 4+:
- `valuesAsJson` for rich cell types
- Allow-edit range protection API

**Stay at 1.13 if LTSC is a target** — all features 1.1–1.13 covered.

---

## 10. Known Limitations vs VBA/COM

This section documents what is **not possible** from a JS API task pane that VBA/COM can do.

### 🚫 Hard Limits (No API exists — unlikely to ever land)

| Limitation | Details |
|---|---|
| **Run VBA macros** | Zero interop between JS add-ins and VBA. No `Application.Run()` equivalent. Workaround: write cell value → VBA event triggers (but this is fragile). |
| **Trigger Application-level events** | No access to `Application.WorkbookBeforeSave`, `Application.WorkbookOpen`, etc. from JS. |
| **File system access** | No `FileSystemObject`, no file open/save dialogs from the JS API itself. Use `OfficeRuntime.displayWebDialog()` or the Office Dialog API for file interactions. |
| **Spawn processes / shell** | Completely sandboxed browser runtime. No `Shell()`, no COM automation of other apps. |
| **Access other Office apps** | No automation of Word, Outlook, PowerPoint from Excel JS API. |
| **Read clipboard programmatically** | No `Application.ClipboardFormats` or programmatic clipboard read. |
| **Access printer settings / print** | No `Range.PrintPreview()`, no printer dialog, no print from JS API. |
| **Custom ribbon tabs from task pane code** | Ribbon XML is declared at manifest time, not runtime. No dynamic add/remove of ribbon controls at runtime (beyond enable/disable via `Office.ribbon.requestUpdate`). |
| **OLAP / Power Pivot** | No create/modify OLAP PivotTables or data models. |
| **Calculated Fields/Items in PivotTables** | No API — must use UI. |
| **User-defined functions called by formulas in workbook.functions** | Only built-in worksheet functions callable; no UDF evaluation. |
| **Arbitrary formula string evaluation** | `workbook.functions` only exposes named methods for built-in functions; no `Evaluate("=...")`. |
| **Excel data connections (Power Query, ODBC, etc.)** | No API to create or fully manage data connections. Can `refresh()` existing connections in some cases. |
| **Flash Fill** | No API equivalent. |
| **Named styles beyond built-ins** | Limited style creation; not full parity with VBA `Styles.Add`. |

### ⚠️ Partial / Limited

| Limitation | Details |
|---|---|
| **Worksheet protection with password** | You can protect/unprotect with password from JS API (ExcelApi 1.2+), but password management is limited. |
| **Shape/Drawing manipulation** | Basic shape add/move/delete exists (ExcelApi 1.9), but no full drawing canvas parity with VBA MSO shapes. |
| **Data validation** | Full data validation API exists (ExcelApi 1.8), including list, number, date, custom. |
| **Cell formatting breadth** | Most formatting is covered, but some advanced number formats or legacy features may not be settable. |
| **Performance at scale** | Bulk operations on 100K+ cells are possible but much slower than VBA's direct COM calls. Use `values` batch read, avoid cell-by-cell loops. Use `context.runtime.enableEvents = false` for bulk writes. |
| **Real-time streaming data** | No push from server directly into Excel without an add-in round-trip. Custom Functions support `CustomFunctions.StreamingInvocationContext` for streaming UDFs. |
| **Workbook-level events before save/close** | `Workbook.onBeforeSave` is available via **ExcelApiOnline** (Online only) and ExcelApi 1.13+. Desktop parity is newer. |

### 🟡 Excel Online-Specific Gaps vs Desktop

| Feature | Status |
|---|---|
| `regionMap` chart type (Power Map) | Desktop only |
| VBA macro triggering | N/A (no VBA in Online) |
| COM Add-in interaction | N/A (COM not available in Online) |
| Print dialog | No JS API equivalent in either Online or Desktop |
| External data connections (create) | Limited in Online |
| OLAP PivotTables | Not supported in either via JS API |
| File format save-as (`.xls`, `.csv`, etc.) | No JS API; use server-side conversion |

---

## Quick Reference: FAIT Sprint 3+ Candidates

Based on this research, here are the highest-value features to build on top of ExcelApi 1.13:

| Feature | Effort | API Set | Value for FAIT |
|---|---|---|---|
| **Formula validation & evaluation** via `workbook.functions` | Low | 1.2 ✅ | High — validate form data against Excel functions without writing cells |
| **onChanged reactive updates** + `triggerSource` guard | Low | 1.7 ✅ / 1.14 | High — react to user edits in the spreadsheet |
| **Named range create/manage** | Low | 1.4 ✅ | Medium — register FAIT output ranges for stable addressing |
| **Table CRUD** (detect, read, manipulate Tables) | Medium | 1.1 ✅ | High — most structured data in enterprise Excel is in Tables |
| **Conditional formatting** (apply FAIT-defined rules) | Medium | 1.6 ✅ | High — visual feedback layer on top of validated data |
| **Chart generation** (auto-chart from FAIT data) | Medium | 1.8 ✅ | Medium-High — reporting use case |
| **Comments** (annotate cells from FAIT) | Low | 1.10 ✅ | Medium — audit trail / annotation feature |
| **Sheet management** (create Report sheets) | Low | 1.7 ✅ | Medium — multi-sheet output |
| **PivotTable creation** | High | 1.8 ✅ | Medium — power user feature |

---

## References

- [Excel JS API: Charts](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-charts)
- [Excel JS API: PivotTables](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-pivottables)
- [Excel JS API: Tables](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-tables)
- [Excel JS API: Events](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-events)
- [Excel JS API: Conditional Formatting](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-conditional-formatting)
- [Excel JS API: Comments](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-comments)
- [Excel JS API: Worksheet Functions](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-worksheet-functions)
- [Excel JS API: Worksheets](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-worksheets)
- [Excel.NamedItem class](https://learn.microsoft.com/en-us/javascript/api/excel/excel.nameditem?view=excel-js-preview)
- [ExcelApi Requirement Sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/excel/excel-api-requirement-sets)
- [ExcelApiOnline Requirement Set](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/excel/excel-api-online-requirement-set)
