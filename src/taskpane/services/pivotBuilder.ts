/* global Excel */

export interface PivotSpec {
  name: string;
  sourceRange: string;   // flat data range, e.g. "A1:E50"
  targetCell: string;    // top-left cell for pivot output, e.g. "G1"
  rows: string[];        // field names for row grouping
  columns: string[];     // field names for column grouping (can be empty)
  values: Array<{ field: string; aggregation: 'sum' | 'count' | 'average' | 'max' | 'min' }>;
  filters?: string[];    // field names for filter area
}

export async function insertPivotTable(spec: PivotSpec): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const sourceRange = sheet.getRange(spec.sourceRange);
    const targetCell  = sheet.getRange(spec.targetCell);

    // Create pivot table
    const pivot = sheet.pivotTables.add(spec.name, sourceRange, targetCell);
    await ctx.sync();

    // Add row hierarchy fields
    for (const fieldName of spec.rows) {
      const field = pivot.rowHierarchies.add(pivot.fields.getItem(fieldName));
      void field; // suppress unused warning
    }

    // Add column hierarchy fields
    for (const fieldName of spec.columns) {
      pivot.columnHierarchies.add(pivot.fields.getItem(fieldName));
    }

    // Add filter fields
    for (const fieldName of (spec.filters ?? [])) {
      pivot.filterHierarchies.add(pivot.fields.getItem(fieldName));
    }

    // Add value fields
    const aggMap: Record<string, string> = {
      sum:     'Sum',
      count:   'Count',
      average: 'Average',
      max:     'Max',
      min:     'Min',
    };
    for (const v of spec.values) {
      const dataHierarchy = pivot.dataHierarchies.add(pivot.fields.getItem(v.field));
      dataHierarchy.summarizeBy = aggMap[v.aggregation] ?? 'Sum';
    }

    await ctx.sync();
  });
}
