/* global Excel */

export type CfRuleType =
  | { kind: 'colorScale'; min: string; mid?: string; max: string }  // CSS colors
  | { kind: 'dataBar'; color: string }
  | { kind: 'iconSet'; style: 'ThreeArrows' | 'ThreeTrafficLights1' | 'FourRating' | 'FiveRating' }
  | { kind: 'topN'; rank: number; percent: boolean; format: CellFormatSpec }
  | { kind: 'formula'; formula: string; format: CellFormatSpec }
  | { kind: 'cellValue'; operator: string; value1: string | number; value2?: string | number; format: CellFormatSpec };

export interface CellFormatSpec {
  backgroundColor?: string;
  fontColor?: string;
  bold?: boolean;
}

export interface CfSpec {
  range: string;
  rule: CfRuleType;
  stopIfTrue?: boolean;
}

export async function applyConditionalFormat(spec: CfSpec): Promise<void> {
  await Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const range = sheet.getRange(spec.range);

    const rule = spec.rule;

    switch (rule.kind) {
      case 'colorScale': {
        const cf = range.conditionalFormats.add('ColorScale');
        cf.colorScale.criteria = {
          minimum: { type: 'LowestValue', color: rule.min },
          ...(rule.mid ? { midpoint: { type: 'Percentile', percentile: 50, color: rule.mid } } : {}),
          maximum: { type: 'HighestValue', color: rule.max },
        };
        break;
      }
      case 'dataBar': {
        const cf = range.conditionalFormats.add('DataBar');
        cf.dataBar.barFillType = 'Gradient';
        cf.dataBar.positiveFormat.fillColor = rule.color;
        break;
      }
      case 'iconSet': {
        const cf = range.conditionalFormats.add('IconSet');
        cf.iconSet.style = rule.style;
        break;
      }
      case 'topN': {
        const cf = range.conditionalFormats.add('TopBottom');
        cf.topBottom.rule = {
          rank: rule.rank,
          percent: rule.percent,
          type: 'TopItems',
        };
        applyFormatSpec(cf.topBottom.format, rule.format);
        break;
      }
      case 'formula': {
        const cf = range.conditionalFormats.add('Custom');
        cf.custom.rule.formula = rule.formula;
        applyFormatSpec(cf.custom.format, rule.format);
        break;
      }
      case 'cellValue': {
        const cf = range.conditionalFormats.add('CellValue');
        cf.cellValue.rule = {
          operator: rule.operator,
          formula1: String(rule.value1),
          ...(rule.value2 !== undefined ? { formula2: String(rule.value2) } : {}),
        };
        applyFormatSpec(cf.cellValue.format, rule.format);
        break;
      }
    }

    await ctx.sync();
  });
}

function applyFormatSpec(format: any, spec: CellFormatSpec): void {
  if (spec.backgroundColor) format.fill.color = spec.backgroundColor;
  if (spec.fontColor)        format.font.color = spec.fontColor;
  if (spec.bold !== undefined) format.font.bold = spec.bold;
}
