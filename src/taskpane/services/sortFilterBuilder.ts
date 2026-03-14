/* global Excel */

export interface SortSpec {
  range: string;
  fields: Array<{
    columnIndex: number; // 0-based
    ascending: boolean;
  }>;
  hasHeaders: boolean;
}

export interface FilterSpec {
  range: string;
  hasHeaders: boolean;
  criteria: Array<{
    columnIndex: number; // 0-based
    filterType: 'values' | 'top' | 'custom';
    values?: string[]; // for 'values' type
    topCount?: number; // for 'top' type
    topPercent?: boolean;
    operator1?: string; // for 'custom': "greaterThan", "lessThan", "equals", etc.
    value1?: string | number;
    operator2?: string;
    value2?: string | number;
  }>;
}

export interface SortFilterSpec {
  sort?: SortSpec;
  filter?: FilterSpec;
}

export async function applySortFilter(spec: SortFilterSpec): Promise<void> {
  await Excel.run(async (ctx: Excel.RequestContext) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();

    if (spec.sort) {
      const range = sheet.getRange(spec.sort.range);
      range.sort.apply(
        spec.sort.fields.map((f) => ({
          key: f.columnIndex,
          ascending: f.ascending,
        })),
        spec.sort.hasHeaders
      );
    }

    if (spec.filter) {
      // Apply each criterion via worksheet.autoFilter.apply(range, columnIndex, criteria)
      for (const criterion of spec.filter.criteria) {
        const filterCriteria: Excel.FilterCriteria = buildFilterCriteria(criterion);
        sheet.autoFilter.apply(spec.filter.range, criterion.columnIndex, filterCriteria);
      }
    }

    await ctx.sync();
  });
}

function buildFilterCriteria(criterion: FilterSpec['criteria'][0]): Excel.FilterCriteria {
  if (criterion.filterType === 'values' && criterion.values) {
    return {
      filterOn: Excel.FilterOn.values,
      values: criterion.values,
    };
  }

  if (criterion.filterType === 'top') {
    return {
      filterOn: criterion.topPercent
        ? Excel.FilterOn.topPercent
        : Excel.FilterOn.topItems,
      criterion1: String(criterion.topCount ?? 10),
    };
  }

  // custom
  const fc: Excel.FilterCriteria = {
    filterOn: Excel.FilterOn.custom,
  };
  if (criterion.operator1 !== undefined) {
    fc.criterion1 = String(criterion.value1 ?? '');
    // Excel custom filter criterion format: operator + value, e.g. ">50"
    fc.criterion1 = operatorPrefix(criterion.operator1) + String(criterion.value1 ?? '');
  }
  if (criterion.operator2 !== undefined && criterion.value2 !== undefined) {
    fc.criterion2 = operatorPrefix(criterion.operator2) + String(criterion.value2);
    fc.operator = Excel.FilterOperator.and;
  }
  return fc;
}

/** Map natural-language operator names to Excel filter prefix characters */
function operatorPrefix(op: string): string {
  switch (op.toLowerCase()) {
    case 'greaterthan':
    case 'gt':
      return '>';
    case 'greaterthanorequalto':
    case 'gte':
      return '>=';
    case 'lessthan':
    case 'lt':
      return '<';
    case 'lessthanorequalto':
    case 'lte':
      return '<=';
    case 'equals':
    case 'eq':
      return '=';
    case 'notequals':
    case 'neq':
      return '<>';
    default:
      return op; // pass through if already a prefix symbol
  }
}

export async function clearFilter(rangeAddress?: string): Promise<void> {
  await Excel.run(async (ctx: Excel.RequestContext) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    if (rangeAddress) {
      // Re-apply autoFilter without criteria to reset it for this range
      sheet.autoFilter.apply(rangeAddress);
    } else {
      sheet.autoFilter.clearCriteria();
    }
    await ctx.sync();
  });
}
