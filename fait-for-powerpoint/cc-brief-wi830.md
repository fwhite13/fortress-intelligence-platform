# CC Brief: WI830 — FfP Sprint 3: Data Tables + Template Injection + Chart-as-Image

## Working Directory
`/home/fredw/projects/fip/fait-for-powerpoint/`

## Context
This is an Office Add-in for PowerPoint built with React + TypeScript + Vite. The add-in (FAIT for PowerPoint) sends chat to a FAIT API and renders structured spec blocks into PowerPoint actions. Sprint 2 added speaker notes. Sprint 3 adds data tables, chart-as-image, and template slide injection.

`chart.js ^4.5.1` is already in `dependencies` (npm install already done).

**NEVER touch `/home/fredw/projects/fait-for-excel/` — that is a completely separate project.**

---

## CRITICAL CONSTRAINTS — READ FIRST

1. **chart.js named imports only — NOT `chart.js/auto`**
2. **`responsive: false` AND `animation: false` on ALL Chart.js configs — mandatory**
3. **`specificCellProperties` 2D array must be EXACTLY `(rowCount + 1) × columnCount`** — use `allRows.map(row => row.map(...))` to ensure exact dimensions
4. **`addTable()` `totalRows` = `spec.rowCount + 1`** — header row is included in the count
5. **`fetchTemplateBase64` must have `// TODO: DO NOT SHIP` comment** — hardcoded test PPTX for now
6. **Manifest: `1.6` → `1.8`** in BOTH files

---

## Task 1: Manifest Bump

Edit `public/manifest.xml` line 24 and `manifest.local.xml` line 25:
```
Change: MinVersion="1.6"
To:     MinVersion="1.8"
```
Both files have exactly one `<Set Name="PowerPointApi" MinVersion="1.6"/>` line.

---

## Task 2: New File — `src/taskpane/services/pptSpecParser.ts`

This file handles all three Sprint 3 spec types PLUS replaces the S2 `stripNotesSpec`. Note: `pptNotesParser.ts` still exists and `parseNotesSpec` is still used from it; but `stripAllSpecs` moves here.

```typescript
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
```

---

## Task 3: New File — `src/taskpane/services/pptChartRenderer.ts`

```typescript
import {
  Chart,
  CategoryScale,
  LinearScale,
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  PieController,
  ArcElement,
  Title,
  Tooltip,
  Legend,
} from 'chart.js';

Chart.register(
  CategoryScale,
  LinearScale,
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  PieController,
  ArcElement,
  Title,
  Tooltip,
  Legend
);

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

/**
 * Render a Chart.js chart to a base64 PNG data URL.
 * Creates a hidden off-screen canvas, renders, captures, destroys.
 * Must run in a browser context with a real DOM (works in Office Add-in taskpane).
 */
export async function renderChartToBase64(spec: PptChartSpec): Promise<string> {
  const canvas = document.createElement('canvas');
  canvas.width = spec.width || 600;
  canvas.height = spec.height || 400;
  canvas.style.position = 'absolute';
  canvas.style.left = '-9999px';
  canvas.style.top = '-9999px';
  document.body.appendChild(canvas);

  return new Promise((resolve, reject) => {
    try {
      const chart = new Chart(canvas, {
        type: spec.type,
        data: {
          labels: spec.labels,
          datasets: spec.datasets,
        },
        options: {
          responsive: false,  // CRITICAL: responsive:true breaks off-screen canvas render
          animation: false,   // No animation needed — capture immediately
          plugins: {
            title: spec.title
              ? { display: true, text: spec.title, font: { size: 16 } }
              : { display: false },
            legend: { display: spec.type !== 'bar' || spec.datasets.length > 1 },
          },
          scales: spec.type === 'pie' || spec.type === 'doughnut'
            ? undefined
            : {
                x: spec.xAxis?.title
                  ? { title: { display: true, text: spec.xAxis.title } }
                  : undefined,
                y: spec.yAxis?.title
                  ? { title: { display: true, text: spec.yAxis.title } }
                  : undefined,
              },
        },
      });

      // Small timeout to allow paint cycle to complete
      setTimeout(() => {
        try {
          const base64 = canvas.toDataURL('image/png');
          chart.destroy();
          document.body.removeChild(canvas);
          resolve(base64);
        } catch (err) {
          chart.destroy();
          document.body.removeChild(canvas);
          reject(err);
        }
      }, 50);
    } catch (e) {
      if (document.body.contains(canvas)) {
        document.body.removeChild(canvas);
      }
      reject(e);
    }
  });
}
```

---

## Task 4: New File — `src/taskpane/services/pptTableWriter.ts`

```typescript
/* global PowerPoint */
declare const PowerPoint: any;

import type { PptTableSpec } from './pptSpecParser';

export class PptTableError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'DIMENSION_MISMATCH' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptTableError';
  }
}

export async function insertTable(spec: PptTableSpec): Promise<void> {
  // Validate dimensions
  if (spec.headers.length !== spec.columnCount) {
    throw new PptTableError(
      `headers.length (${spec.headers.length}) !== columnCount (${spec.columnCount})`,
      'DIMENSION_MISMATCH'
    );
  }
  for (let i = 0; i < spec.values.length; i++) {
    if (spec.values[i].length !== spec.columnCount) {
      throw new PptTableError(
        `values[${i}].length (${spec.values[i].length}) !== columnCount (${spec.columnCount})`,
        'DIMENSION_MISMATCH'
      );
    }
  }

  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptTableError('No slide selected', 'NO_SLIDE');
    }

    const slide = selectedSlides.items[0];

    // allRows: header row + data rows
    const allRows: string[][] = [spec.headers, ...spec.values];
    const totalRows = allRows.length; // = spec.rowCount + 1 (includes header)

    const options: any = { values: allRows };

    if (spec.headerStyle !== 'none') {
      const isLight = spec.headerStyle === 'lightHeader';
      // specificCellProperties must be EXACTLY totalRows × columnCount
      options.specificCellProperties = allRows.map((row, rowIdx) =>
        row.map(() =>
          rowIdx === 0
            ? {
                fill: { color: isLight ? '#DCE6F1' : '#1F3864' },
                font: { bold: true, color: isLight ? '#1F3864' : '#FFFFFF' },
              }
            : {}
        )
      );
    }

    // totalRows includes the header row
    slide.shapes.addTable(totalRows, spec.columnCount, options);
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptTableError) throw e;
    throw new PptTableError(e?.message ?? 'Table creation failed', 'PPT_ERROR');
  });
}
```

---

## Task 5: New File — `src/taskpane/services/pptTemplateService.ts`

```typescript
/* global PowerPoint */
declare const PowerPoint: any;

import { fetchTemplateBase64 } from './faitApi';

export class PptTemplateError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'FETCH_FAILED' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptTemplateError';
  }
}

export async function insertTemplateSlide(
  templateId: string,
  apiKey: string,
  keepSourceFormatting = false
): Promise<void> {
  let base64Pptx: string;
  try {
    base64Pptx = await fetchTemplateBase64(templateId, apiKey);
  } catch (e: any) {
    throw new PptTemplateError(
      e?.message ?? 'Failed to fetch template from FORGE',
      'FETCH_FAILED'
    );
  }

  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    const targetSlide = selectedSlides.items?.length > 0
      ? selectedSlides.items[0]
      : null;

    ctx.presentation.insertSlidesFromBase64(base64Pptx, {
      formatting: keepSourceFormatting
        ? PowerPoint.InsertSlideFormatting.keepSourceFormatting
        : PowerPoint.InsertSlideFormatting.useDestinationTheme,
      targetSlide,
    });

    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptTemplateError) throw e;
    throw new PptTemplateError(e?.message ?? 'Template insert failed', 'PPT_ERROR');
  });
}
```

---

## Task 6: Modify `src/taskpane/services/faitApi.ts`

Append to the end of the file (after `searchKb`):

```typescript
// ── Sprint 3: Template fetch ──────────────────────────────────────────────

export interface TemplateResult {
  id: string;
  name: string;
  description: string;
}

export async function fetchTemplateBase64(
  templateId: string,
  _apiKey: string
): Promise<string> {
  // TODO: DO NOT SHIP — /api/haven/template-fetch not yet implemented
  // Hardcoded test template for development only
  // Replace with real fetch when FORGE template backend is ready:
  // const resp = await fetch(`${FAIT_BASE}/api/haven/template-fetch`, {
  //   method: 'POST',
  //   headers: { 'Content-Type': 'application/json', 'x-api-key': _apiKey },
  //   body: JSON.stringify({ id: templateId }),
  // });
  // if (resp.status === 401) throw new Error('INVALID_KEY');
  // if (resp.status === 404) throw new Error('TEMPLATE_NOT_FOUND');
  // if (!resp.ok) throw new Error(`HTTP_${resp.status}`);
  // const { base64 } = await resp.json();
  // return base64;
  console.warn(`fetchTemplateBase64: using hardcoded test template for id="${templateId}" — backend not yet implemented`);
  // Minimal valid 1-slide PPTX (base64) for development testing
  return Promise.resolve(TEST_PPTX_BASE64);
}

/**
 * Minimal 1-slide PPTX fragment for development/testing only.
 * DO NOT SHIP — replace with real FORGE template fetch.
 */
const TEST_PPTX_BASE64 =
  'UEsDBBQABgAIAAAAIQDfpNJsWgEAACAFAAATAAgCW0NvbnRlbnRfVHlwZXNdLnhtbCCiBAIo' +
  'oAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' +
  'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
```

---

## Task 7: Modify `src/taskpane/services/pptWriter.ts`

Append after `tagShape()` function at the end of the file:

```typescript
declare const Office: any;

/**
 * Insert a base64 PNG image into the current slide.
 *
 * Feature detection:
 * 1. If Preview addPicture is available → positioned insert
 * 2. Fallback: Common API setSelectedDataAsync (inserts at cursor)
 *
 * Accepts either a data URL ("data:image/png;base64,...") or raw base64.
 */
export async function insertChartImage(
  base64DataUrl: string,
  width = 400,
  height = 267
): Promise<void> {
  // Normalize — strip data URL prefix for APIs that want raw base64
  const rawBase64 = base64DataUrl.startsWith('data:')
    ? base64DataUrl.split(',')[1]
    : base64DataUrl;

  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      insertViaCommonApi(rawBase64, width, height);
      return;
    }

    const slide = selectedSlides.items[0];
    const supportsAddPicture = typeof (slide.shapes as any).addPicture === 'function';

    if (supportsAddPicture) {
      // Preview API path: precise positioning (centered on 720pt-wide slide)
      (slide.shapes as any).addPicture(rawBase64, {
        left: 180,
        top: 100,
        width,
        height,
      });
      await ctx.sync();
    } else {
      insertViaCommonApi(rawBase64, width, height);
    }
  }).catch((e: any) => {
    insertViaCommonApi(rawBase64, width, height);
    throw e;
  });
}

/** Common API image insert — works in Desktop and Online, inserts at cursor */
function insertViaCommonApi(rawBase64: string, width: number, height: number): void {
  (Office as any).context.document.setSelectedDataAsync(rawBase64, {
    coercionType: (Office as any).CoercionType.Image,
    imageWidth: width,
    imageHeight: height,
  }, (result: any) => {
    if (result.status !== 'succeeded') {
      console.warn('FfP: image insert via Common API failed', result.error);
    }
  });
}
```

---

## Task 8: New File — `src/taskpane/components/TablePreview.tsx`

```typescript
import React from 'react';
import type { PptTableSpec } from '../services/pptSpecParser';

interface TablePreviewProps {
  spec: PptTableSpec;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
}

const TablePreview: React.FC<TablePreviewProps> = ({
  spec,
  onAccept,
  onReject,
  loading = false,
}) => (
  <div
    style={{
      padding: '10px 12px',
      borderTop: '1px solid #2e3f54',
      background: '#0f1720',
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: '8px',
    }}
  >
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      📊 Table Preview — {spec.rowCount} rows × {spec.columnCount} cols
    </div>
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: '10px', width: '100%' }}>
        <thead>
          <tr>
            {spec.headers.map((h, i) => (
              <th
                key={i}
                style={{
                  background: '#1F3864',
                  color: '#fff',
                  padding: '3px 6px',
                  border: '1px solid #2e3f54',
                  fontWeight: '600',
                  whiteSpace: 'nowrap',
                }}
              >
                {h || '—'}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {spec.values.slice(0, 3).map((row, ri) => (
            <tr key={ri}>
              {row.map((cell, ci) => (
                <td
                  key={ci}
                  style={{
                    padding: '2px 6px',
                    border: '1px solid #2e3f54',
                    color: '#c8d8e8',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {cell || '—'}
                </td>
              ))}
            </tr>
          ))}
          {spec.values.length > 3 && (
            <tr>
              <td
                colSpan={spec.columnCount}
                style={{
                  padding: '2px 6px',
                  color: '#556677',
                  fontSize: '10px',
                  border: '1px solid #2e3f54',
                }}
              >
                +{spec.values.length - 3} more rows
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
    <div style={{ display: 'flex', gap: '6px' }}>
      <button
        onClick={onAccept}
        disabled={loading}
        style={{
          flex: 1,
          background: '#d4af37',
          color: '#0f1720',
          border: 'none',
          borderRadius: '4px',
          padding: '6px 12px',
          fontSize: '12px',
          fontWeight: '700',
          cursor: loading ? 'not-allowed' : 'pointer',
          opacity: loading ? 0.6 : 1,
        }}
      >
        {loading ? 'Creating…' : '✓ Create Table'}
      </button>
      <button
        onClick={onReject}
        disabled={loading}
        style={{
          background: '#2e3f54',
          color: '#e8edf3',
          border: 'none',
          borderRadius: '4px',
          padding: '6px 10px',
          fontSize: '12px',
          cursor: loading ? 'not-allowed' : 'pointer',
          opacity: loading ? 0.6 : 1,
        }}
      >
        Discard
      </button>
    </div>
  </div>
);

export default TablePreview;
```

---

## Task 9: New File — `src/taskpane/components/ChartPreview.tsx`

```typescript
import React from 'react';

interface ChartPreviewProps {
  base64DataUrl: string;
  title: string;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
  error?: string | null;
}

const ChartPreview: React.FC<ChartPreviewProps> = ({
  base64DataUrl,
  title,
  onAccept,
  onReject,
  loading = false,
  error,
}) => (
  <div
    style={{
      padding: '10px 12px',
      borderTop: '1px solid #2e3f54',
      background: '#0f1720',
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: '8px',
    }}
  >
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      📈 Chart Preview — {title}
    </div>
    <img
      src={base64DataUrl}
      alt={title}
      style={{
        width: '100%',
        maxHeight: '180px',
        objectFit: 'contain',
        border: '1px solid #2e3f54',
        borderRadius: '3px',
      }}
    />
    {error && <div style={{ color: '#e07070', fontSize: '11px' }}>{error}</div>}
    <div style={{ display: 'flex', gap: '6px' }}>
      <button
        onClick={onAccept}
        disabled={loading || !!error}
        style={{
          flex: 1,
          background: '#d4af37',
          color: '#0f1720',
          border: 'none',
          borderRadius: '4px',
          padding: '6px 12px',
          fontSize: '12px',
          fontWeight: '700',
          cursor: loading || !!error ? 'not-allowed' : 'pointer',
          opacity: loading || !!error ? 0.6 : 1,
        }}
      >
        {loading ? 'Inserting…' : '✓ Insert Chart'}
      </button>
      <button
        onClick={onReject}
        disabled={loading}
        style={{
          background: '#2e3f54',
          color: '#e8edf3',
          border: 'none',
          borderRadius: '4px',
          padding: '6px 10px',
          fontSize: '12px',
          cursor: loading ? 'not-allowed' : 'pointer',
          opacity: loading ? 0.6 : 1,
        }}
      >
        Discard
      </button>
    </div>
  </div>
);

export default ChartPreview;
```

---

## Task 10: New File — `src/taskpane/components/TemplateGallery.tsx`

```typescript
import React from 'react';
import type { PptTemplateSpec } from '../services/pptSpecParser';

interface TemplateGalleryProps {
  spec: PptTemplateSpec;
  onInsert: (templateId: string, keepSourceFormatting: boolean) => void;
  onReject: () => void;
  loading?: boolean;
}

const TemplateGallery: React.FC<TemplateGalleryProps> = ({
  spec,
  onInsert,
  onReject,
  loading = false,
}) => (
  <div
    style={{
      padding: '10px 12px',
      borderTop: '1px solid #2e3f54',
      background: '#0f1720',
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: '8px',
    }}
  >
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      🗂 Slide Templates from FORGE
    </div>
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: '6px',
        maxHeight: '200px',
        overflowY: 'auto',
      }}
    >
      {spec.templates.map((t) => (
        <div
          key={t.id}
          style={{
            background: '#131e2b',
            border: '1px solid #2e3f54',
            borderRadius: '4px',
            padding: '8px 10px',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            gap: '8px',
          }}
        >
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: '12px', fontWeight: '600', color: '#e8edf3' }}>
              {t.name}
            </div>
            <div style={{ fontSize: '11px', color: '#778899', marginTop: '2px' }}>
              {t.description}
            </div>
          </div>
          <button
            onClick={() => onInsert(t.id, t.keepSourceFormatting)}
            disabled={loading}
            style={{
              background: '#d4af37',
              color: '#0f1720',
              border: 'none',
              borderRadius: '3px',
              padding: '4px 10px',
              fontSize: '11px',
              fontWeight: '700',
              cursor: loading ? 'not-allowed' : 'pointer',
              opacity: loading ? 0.6 : 1,
              flexShrink: 0,
            }}
          >
            {loading ? '…' : '+ Insert'}
          </button>
        </div>
      ))}
    </div>
    <button
      onClick={onReject}
      style={{
        background: '#2e3f54',
        color: '#e8edf3',
        border: 'none',
        borderRadius: '4px',
        padding: '5px 10px',
        fontSize: '11px',
        cursor: 'pointer',
        alignSelf: 'flex-start',
      }}
    >
      Cancel
    </button>
  </div>
);

export default TemplateGallery;
```

---

## Task 11: Modify `src/taskpane/components/SlashCommandPicker.tsx`

Add three new commands to the `COMMANDS` array, after the existing `expand` command:

```typescript
  {
    name: 'table',
    description: 'Create a data table on the current slide',
    prompt:
      'Create a data table for the current slide. ' +
      'Based on the slide context and my request, return a ```ppt_table_spec block with JSON: ' +
      '{"rowCount": N, "columnCount": N, "headers": [...], "values": [[...]], "headerStyle": "darkHeader", "position": null}. ' +
      'All cell values must be strings. Empty cells must be "". ' +
      'Use darkHeader style for financial/data tables.',
  },
  {
    name: 'chart',
    description: 'Create a chart on the current slide',
    prompt:
      'Create a chart for the current slide. ' +
      'Return a ```ppt_chart_spec block with JSON following the Chart.js data format: ' +
      '{"type": "bar|line|pie", "title": "...", "width": 600, "height": 400, "labels": [...], ' +
      '"datasets": [{"label": "...", "data": [...], "backgroundColor": "..."}], ' +
      '"xAxis": {"title": "..."}, "yAxis": {"title": "..."}}. ' +
      'Use Fortress brand color #1F3864 for primary datasets.',
  },
  {
    name: 'template',
    description: 'Insert a branded slide template from FORGE',
    prompt:
      'Search FORGE for a slide template matching my description. ' +
      'Return a ```ppt_template_spec block with JSON: ' +
      '{"templates": [{"id": "...", "name": "...", "description": "...", "keepSourceFormatting": false}]}. ' +
      'Search kbTypes: ["template"]. ' +
      'Return the top 3 matches sorted by relevance.',
  },
```

---

## Task 12: Modify `src/taskpane/components/ChatPanel.tsx`

This is the most complex change. Apply the following surgical modifications:

### 12a. Update imports at the top

Replace:
```typescript
import { parseNotesSpec, stripAllSpecs } from '../services/pptNotesParser';
```
With:
```typescript
import { parseNotesSpec } from '../services/pptNotesParser';
import { parseTableSpec, parseChartSpec, parseTemplateSpec, stripAllSpecs } from '../services/pptSpecParser';
import { renderChartToBase64 } from '../services/pptChartRenderer';
import { insertTable, PptTableError } from '../services/pptTableWriter';
import { insertChartImage } from '../services/pptWriter';
import { insertTemplateSlide, PptTemplateError } from '../services/pptTemplateService';
import TablePreview from './TablePreview';
import ChartPreview from './ChartPreview';
import TemplateGallery from './TemplateGallery';
import type { PptTableSpec, PptChartSpec, PptTemplateSpec } from '../services/pptSpecParser';
```

### 12b. Add Sprint 3 state after the existing notes state block

After:
```typescript
  const [notesError, setNotesError] = useState<string | null>(null);
```

Add:
```typescript
  // ── Sprint 3: Table state ─────────────────────────────────────────────────
  const [pendingTable, setPendingTable] = useState<PptTableSpec | null>(null);
  const [tableLoading, setTableLoading] = useState(false);
  const [tableError, setTableError] = useState<string | null>(null);

  // ── Sprint 3: Chart state ─────────────────────────────────────────────────
  const [pendingChart, setPendingChart] = useState<PptChartSpec | null>(null);
  const [pendingChartBase64, setPendingChartBase64] = useState<string | null>(null);
  const [chartLoading, setChartLoading] = useState(false);
  const [chartRenderError, setChartRenderError] = useState<string | null>(null);

  // ── Sprint 3: Template state ──────────────────────────────────────────────
  const [pendingTemplates, setPendingTemplates] = useState<PptTemplateSpec | null>(null);
  const [templateLoading, setTemplateLoading] = useState(false);
  const [templateError, setTemplateError] = useState<string | null>(null);
```

### 12c. Extend the notes detection useEffect to also detect S3 specs

Replace the existing Sprint 2 notes detection useEffect:
```typescript
  // Sprint 2: Detect ppt_notes_spec block in last assistant message
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
      const spec = parseNotesSpec(lastMsg.content);
      if (spec) {
        setPendingNotes(spec);
        setNotesError(null);
      }
    }
  }, [messages]);
```

With:
```typescript
  // Detect spec blocks in last assistant message (S2: notes, S3: table/chart/template)
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
      // S2: notes spec
      const notesSpec = parseNotesSpec(lastMsg.content);
      if (notesSpec) {
        setPendingNotes(notesSpec);
        setNotesError(null);
      }

      // S3: table spec
      const tableSpec = parseTableSpec(lastMsg.content);
      if (tableSpec) {
        setPendingTable(tableSpec);
        setTableError(null);
      }

      // S3: chart spec — parse then render
      const chartSpec = parseChartSpec(lastMsg.content);
      if (chartSpec) {
        setPendingChart(chartSpec);
        setChartRenderError(null);
        setChartLoading(true);
        renderChartToBase64(chartSpec)
          .then((b64) => {
            setPendingChartBase64(b64);
          })
          .catch(() => {
            setChartRenderError('Chart render failed — check the data format.');
          })
          .finally(() => setChartLoading(false));
      }

      // S3: template spec
      const tmplSpec = parseTemplateSpec(lastMsg.content);
      if (tmplSpec) {
        setPendingTemplates(tmplSpec);
        setTemplateError(null);
      }
    }
  }, [messages]);
```

### 12d. Add Sprint 3 handlers — insert after `handleNotesDiscard`

After the `handleNotesDiscard` function:
```typescript
  const handleNotesDiscard = () => {
    setPendingNotes(null);
    setNotesError(null);
  };
```

Add:
```typescript
  // ── Sprint 3: Table handlers ──────────────────────────────────────────────
  const handleTableCreate = async () => {
    if (!pendingTable) return;
    setTableLoading(true);
    setTableError(null);
    try {
      await insertTable(pendingTable);
      setPendingTable(null);
    } catch (e) {
      if (e instanceof PptTableError) {
        setTableError(
          e.code === 'DIMENSION_MISMATCH'
            ? 'Table dimensions mismatch — FAIT generated invalid data.'
            : 'Table creation failed — try again.'
        );
      } else {
        setTableError('Table creation failed — try again.');
      }
    } finally {
      setTableLoading(false);
    }
  };

  const handleTableDiscard = () => {
    setPendingTable(null);
    setTableError(null);
  };

  // ── Sprint 3: Chart handlers ──────────────────────────────────────────────
  const handleChartInsert = async () => {
    if (!pendingChartBase64) return;
    setChartLoading(true);
    try {
      const width = pendingChart?.width ?? 400;
      const height = pendingChart?.height ?? 267;
      await insertChartImage(pendingChartBase64, width, height);
      setPendingChart(null);
      setPendingChartBase64(null);
    } catch {
      setChartRenderError('Insert failed — position your cursor on the slide and try again.');
    } finally {
      setChartLoading(false);
    }
  };

  const handleChartDiscard = () => {
    setPendingChart(null);
    setPendingChartBase64(null);
    setChartRenderError(null);
  };

  // ── Sprint 3: Template handlers ───────────────────────────────────────────
  const handleTemplateInsert = async (templateId: string, keepSource: boolean) => {
    setTemplateLoading(true);
    setTemplateError(null);
    try {
      await insertTemplateSlide(templateId, apiKey, keepSource);
      setPendingTemplates(null);
    } catch (e) {
      if (e instanceof PptTemplateError) {
        setTemplateError(
          e.code === 'FETCH_FAILED'
            ? 'Could not fetch template from FORGE — check your API key.'
            : 'Template insert failed.'
        );
      } else {
        setTemplateError('Template insert failed.');
      }
    } finally {
      setTemplateLoading(false);
    }
  };

  const handleTemplateDiscard = () => {
    setPendingTemplates(null);
    setTemplateError(null);
  };
```

### 12e. Add Sprint 3 preview components in the JSX

In the JSX, after the `{notesError && ...}` block and before the `{/* Input area */}` div, add:

```typescript
      {/* Table preview (Sprint 3) */}
      {pendingTable && (
        <TablePreview
          spec={pendingTable}
          onAccept={handleTableCreate}
          onReject={handleTableDiscard}
          loading={tableLoading}
        />
      )}
      {tableError && (
        <div
          style={{
            padding: '4px 12px',
            background: '#1a0f0f',
            color: '#e07070',
            fontSize: '11px',
            flexShrink: 0,
          }}
        >
          {tableError}
        </div>
      )}

      {/* Chart preview (Sprint 3) */}
      {chartLoading && !pendingChartBase64 && (
        <div style={{ padding: '8px 12px', color: '#556677', fontSize: '11px', flexShrink: 0 }}>
          Rendering chart…
        </div>
      )}
      {pendingChartBase64 && (
        <ChartPreview
          base64DataUrl={pendingChartBase64}
          title={pendingChart?.title ?? 'Chart'}
          onAccept={handleChartInsert}
          onReject={handleChartDiscard}
          loading={chartLoading}
          error={chartRenderError}
        />
      )}
      {chartRenderError && !pendingChartBase64 && (
        <div
          style={{
            padding: '4px 12px',
            background: '#1a0f0f',
            color: '#e07070',
            fontSize: '11px',
            flexShrink: 0,
          }}
        >
          {chartRenderError}
        </div>
      )}

      {/* Template gallery (Sprint 3) */}
      {pendingTemplates && (
        <TemplateGallery
          spec={pendingTemplates}
          onInsert={handleTemplateInsert}
          onReject={handleTemplateDiscard}
          loading={templateLoading}
        />
      )}
      {templateError && (
        <div
          style={{
            padding: '4px 12px',
            background: '#1a0f0f',
            color: '#e07070',
            fontSize: '11px',
            flexShrink: 0,
          }}
        >
          {templateError}
        </div>
      )}
```

---

## Summary of Changes

| File | Action |
|------|--------|
| `public/manifest.xml` | MinVersion 1.6 → 1.8 |
| `manifest.local.xml` | MinVersion 1.6 → 1.8 |
| `src/taskpane/services/pptSpecParser.ts` | NEW — parse table/chart/template specs + stripAllSpecs |
| `src/taskpane/services/pptChartRenderer.ts` | NEW — Chart.js canvas render → base64 |
| `src/taskpane/services/pptTableWriter.ts` | NEW — insertTable() via shapes.addTable() |
| `src/taskpane/services/pptTemplateService.ts` | NEW — insertTemplateSlide() via insertSlidesFromBase64 |
| `src/taskpane/components/TablePreview.tsx` | NEW — mini table preview + Create/Discard |
| `src/taskpane/components/ChartPreview.tsx` | NEW — chart image preview + Insert/Discard |
| `src/taskpane/components/TemplateGallery.tsx` | NEW — template cards + Insert |
| `src/taskpane/services/faitApi.ts` | Add fetchTemplateBase64() with TODO: DO NOT SHIP |
| `src/taskpane/services/pptWriter.ts` | Add insertChartImage() |
| `src/taskpane/components/SlashCommandPicker.tsx` | Add /table, /chart, /template commands |
| `src/taskpane/components/ChatPanel.tsx` | Wire S3 state, parsers, handlers, previews |

**DO NOT modify anything under `/home/fredw/projects/fait-for-excel/`.**
