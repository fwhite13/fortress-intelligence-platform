export interface PptTableSpec {
  rowCount: number;
  columnCount: number;
  headers: string[];
  values: string[][];
  headerStyle: 'darkHeader' | 'lightHeader' | 'none';
  position: { left: number; top: number; width: number; height: number } | null;
}

export interface PptChartSpec {
  type: 'bar' | 'line' | 'pie' | 'doughnut' | 'scatter';
  title: string;
  width: number;
  height: number;
  labels: string[];
  datasets: any[];
  xAxis?: { title: string };
  yAxis?: { title: string };
}

export interface PptTemplateSpec {
  templates: Array<{
    id: string;
    name: string;
    description: string;
    keepSourceFormatting: boolean;
  }>;
}

export function parseTableSpec(content: string): PptTableSpec | null {
  const match = content.match(/```ppt_table_spec\s*([\s\S]*?)```/);
  if (!match) return null;
  try {
    const parsed = JSON.parse(match[1].trim());
    if (typeof parsed.rowCount !== 'number') return null;
    if (typeof parsed.columnCount !== 'number') return null;
    if (!Array.isArray(parsed.headers)) return null;
    if (!Array.isArray(parsed.values)) return null;
    return {
      rowCount: parsed.rowCount,
      columnCount: parsed.columnCount,
      headers: parsed.headers,
      values: parsed.values,
      headerStyle: parsed.headerStyle ?? 'darkHeader',
      position: parsed.position ?? null,
    };
  } catch { return null; }
}

export function parseChartSpec(content: string): PptChartSpec | null {
  const match = content.match(/```ppt_chart_spec\s*([\s\S]*?)```/);
  if (!match) return null;
  try {
    const parsed = JSON.parse(match[1].trim());
    if (!parsed.type || !Array.isArray(parsed.labels) || !Array.isArray(parsed.datasets)) return null;
    return {
      type: parsed.type,
      title: parsed.title ?? '',
      width: parsed.width ?? 600,
      height: parsed.height ?? 400,
      labels: parsed.labels,
      datasets: parsed.datasets,
      xAxis: parsed.xAxis ?? undefined,
      yAxis: parsed.yAxis ?? undefined,
    };
  } catch { return null; }
}

export function parseTemplateSpec(content: string): PptTemplateSpec | null {
  const match = content.match(/```ppt_template_spec\s*([\s\S]*?)```/);
  if (!match) return null;
  try {
    const parsed = JSON.parse(match[1].trim());
    if (!Array.isArray(parsed.templates)) return null;
    return { templates: parsed.templates };
  } catch { return null; }
}

/** Strip ALL spec blocks (notes + table + chart + template) for chat display */
export function stripAllSpecs(content: string): string {
  return content
    .replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '')
    .replace(/```ppt_table_spec\s*[\s\S]*?```/g, '')
    .replace(/```ppt_chart_spec\s*[\s\S]*?```/g, '')
    .replace(/```ppt_template_spec\s*[\s\S]*?```/g, '')
    .trim();
}
