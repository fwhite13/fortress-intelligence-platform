# FfP Sprint 3 Spec — Data Tables + Template Slide Injection + Chart-as-Image

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Depends on:** FfP S1 (`FFP-SPRINT1-SPEC.md`) + S2 (`FFP-SPRINT2-SPEC.md`)

---

## Pre-Read: What Was Read

- `FFP-SPRINT1-SPEC.md` + `FFP-SPRINT2-SPEC.md` — S1+S2 baseline files and interfaces
- `FFP-ARCHITECTURE-SPEC.md` — Sprint 3 goals, Gap 1 (addPicture), Gap 2 (no chart API), template-first philosophy
- `RESEARCH-FFP.md` — PowerPointApi 1.8 tables, `insertSlidesFromBase64` (1.2), image gaps, Common API fallback
- `learn.microsoft.com/office/dev/add-ins/powerpoint/work-with-tables` — exact tables API: `shapes.addTable(rows, cols, options)`, `TableAddOptions.values`, `TableAddOptions.uniformCellProperties`, `specificCellProperties`

**Nothing guessed.** All decisions derived from live code and documentation.

---

## Sprint 3 Objectives

| # | Feature | API | Notes |
|---|---------|-----|-------|
| 1 | Data table creation from FAIT response | PowerPointApi 1.8 `shapes.addTable()` | Manifest bumps 1.6 → 1.8 |
| 2 | Template slide injection from FORGE | PowerPointApi 1.2 `insertSlidesFromBase64` | No version bump needed (1.2 ≤ 1.8) |
| 3 | Chart-as-image (client-side canvas) | Common API `setSelectedDataAsync` (stable) + Preview `addPicture` (feature-detected) | No manifest bump |
| 4 | `/table` slash command | — | Triggers structured `ppt_table_spec` response |
| 5 | `/chart` slash command | — | Triggers structured `ppt_chart_spec` response |
| 6 | `/template` slash command | — | Triggers FORGE template search + inject |

---

## PresApi Version: 1.6 → 1.8

**Why:** `shapes.addTable()` requires PowerPointApi **1.8** (released Apr 2025, Build 18730).

**Impact:** Devices requiring 1.6 will continue to work because 1.6 ⊂ 1.8. The bump from 1.6 (S2) to 1.8 (S3) does NOT exclude Office 2024 LTSC (which is 1.5) or Office 2021 LTSC (1.4) — those were already excluded at 1.6. The only group affected by the 1.6 → 1.8 bump are M365 subscribers on significantly lagged builds (pre-Aug 2025). Fortress AM users are M365 subscribers on current channels — 1.8 is safe.

**Clint check:** Manifest changes from `MinVersion="1.6"` to `MinVersion="1.8"`. Confirm both `manifest.xml` and `manifest.local.xml` are updated.

**Feature detection note:** Tables (1.8) and chart image insert (Common API — no requirement set) have different availability footprints. The table feature is properly gated by the 1.8 manifest minimum. Chart-as-image uses the Common API which works from day one. Template injection uses `insertSlidesFromBase64` (1.2) — also within 1.8 baseline.

---

## Feature 1: Data Tables

### When does FAIT use this?

The user asks for tabular data:
- "Create a portfolio attribution table for Q1"
- "Build a 3-column comparison table: fund, benchmark, alpha"
- "Make a table of our top 10 holdings"

FfP sends the `/table` prompt → FAIT returns a `ppt_table_spec` JSON block → FfP shows `TablePreview` component → user confirms → `shapes.addTable()` is called.

### UX Flow

```
User: /table → selects "table" command → chat input fills with table prompt
User: types specifics, sends ("attribution table, 5 rows, 3 cols: Fund Name, Alpha, Sharpe")
FAIT: responds with ppt_table_spec block + optional preamble text
FfP: parses ppt_table_spec → shows TablePreview (header + row count + "Create Table" button)
User: clicks Create Table
FfP: calls shapes.addTable() on the active slide → table appears
```

No shape needs to be selected first — `addTable()` creates a new shape. The table is placed at a default position by PowerPoint; Sprint 4 can add position controls.

### `ppt_table_spec` JSON Block Format

```json
```ppt_table_spec
{
  "rowCount": 5,
  "columnCount": 3,
  "headers": ["Fund Name", "Alpha (%)", "Sharpe Ratio"],
  "values": [
    ["Fortress Core Equity", "2.4", "1.82"],
    ["Fortress Fixed Income", "0.8", "2.10"],
    ["Fortress Alternatives", "3.1", "1.45"],
    ["Fortress Multi-Asset", "1.9", "1.67"],
    ["Fortress Absolute Return", "4.2", "2.03"]
  ],
  "headerStyle": "darkHeader",
  "position": null
}
```
```

Fields:
- `rowCount` + `columnCount` — must match `values.length` and `values[0].length`. Does NOT include the header row.
- `headers` — array of strings, length must equal `columnCount`. Empty strings `""` for blank headers.
- `values` — 2D array of strings. All cells must be strings (numbers stringified). Missing/undefined cells must be `""` not null/undefined.
- `headerStyle` — one of `"darkHeader"` (dark navy fill, white bold text), `"lightHeader"` (light blue fill, dark text), or `"none"` (no special header formatting). Defaults to `"darkHeader"`.
- `position` — null (use PowerPoint default) or `{ left: number, top: number, width: number, height: number }` in points. Sprint 3 ignores position — always null.

### `addTable()` API (PowerPointApi 1.8)

```typescript
const shape = slide.shapes.addTable(rowCount + 1, columnCount, options);
```

Note: `rowCount + 1` because the API's row count includes the header row. The `options.values` array must be a flat 2D array — headers go in `options.values[0]`, data rows go in `options.values[1..n]`.

Full options object:

```typescript
const tableValues: string[][] = [
  spec.headers,           // row 0 = header row
  ...spec.values,         // rows 1..n = data rows
];

const options: any = {
  values: tableValues,
};

if (spec.headerStyle !== 'none') {
  const isLight = spec.headerStyle === 'lightHeader';
  options.specificCellProperties = tableValues.map((row, rowIdx) =>
    row.map(() =>
      rowIdx === 0
        ? {
            fill: { color: isLight ? '#DCE6F1' : '#1F3864' },
            font: {
              bold: true,
              color: isLight ? '#1F3864' : '#FFFFFF',
            },
          }
        : {}
    )
  );
}
```

**Critical `specificCellProperties` constraint:** The `specificCellProperties` 2D array must be the **exact same dimensions** as the table. If `rowCount + 1 = 6` and `columnCount = 3`, the array must be `6 × 3`. Missing rows or columns will throw. Populate with empty objects `{}` for cells with no special formatting.

---

## Feature 2: Template Slide Injection

### What is a FORGE template?

A FORGE template is a FORGE KB node of type `"template"` whose content is a base64-encoded PPTX fragment. The PPTX fragment is a minimal single-slide (or multi-slide) file with pre-built layout, brand colors, placeholders, and optionally animations.

**Sprint 3 dependency:** FORGE must support the `"template"` node type. This is a **backend ask** — the FAIT FORGE API must be able to return template nodes from `/api/haven/kb-search` when `kbTypes` includes `"template"`. Details TBD with the FAIT/FORGE backend team. Sprint 3 ships the FfP client side; FORGE template support can trail by a sprint.

**Fallback for testing:** A hardcoded test template PPTX (1 slide, minimal layout) can be used during development. Tony should create a `testTemplates.ts` with a single hardcoded base64 fragment for dev/testing. This file must not ship to production.

### UX Flow

```
User: /template → selects "template" command
Chat input fills with: "Search FORGE for a slide template. Describe what you need:"
User: types "Q1 performance summary slide" and sends
FAIT: searches FORGE with kbTypes: ["template"] → returns template nodes
FfP: shows TemplateGallery component — cards with template name + description
User: clicks "Insert" on a template card
FfP: decodes base64 PPTX → calls insertSlidesFromBase64() → new slide appears after current slide
FfP: shows success banner "Template slide inserted"
```

### `insertSlidesFromBase64` API (PowerPointApi 1.2)

```typescript
await PowerPoint.run(async (ctx: any) => {
  // Insert after the currently selected slide
  const selectedSlides = ctx.presentation.getSelectedSlides();
  selectedSlides.load('items');
  await ctx.sync();

  const targetSlide = selectedSlides.items.length > 0
    ? selectedSlides.items[0]
    : null;

  ctx.presentation.insertSlidesFromBase64(base64Pptx, {
    formatting: PowerPoint.InsertSlideFormatting.useDestinationTheme,
    targetSlide: targetSlide,   // if null, inserts at end
  });

  await ctx.sync();
});
```

**`InsertSlideFormatting.useDestinationTheme`** — applies the current presentation's theme to the imported slide. This is the correct choice for branded templates: the FORGE template has the right layout and structure; the current presentation provides the theme colors.

**`InsertSlideFormatting.keepSourceFormatting`** — keeps the template's own theme/colors. Use this if the template is meant to look exactly as-is (e.g., a cover slide with custom brand colors that shouldn't be overridden).

Sprint 3 uses `useDestinationTheme` by default. The `ppt_template_spec` JSON block (below) includes a `keepSourceFormatting` boolean field to let FAIT recommend which to use.

### `ppt_template_spec` JSON Block Format

FAIT does not generate template specs from scratch. The `/template` slash command instructs FAIT to search FORGE for templates matching the user's description and return a list. The JSON block is generated by FAIT based on FORGE search results:

```json
```ppt_template_spec
{
  "templates": [
    {
      "id": "forge-node-id-1",
      "name": "Q1 Performance Summary",
      "description": "Single slide: title, 3-metric summary boxes, footer",
      "base64": "<base64 PPTX fragment>",
      "keepSourceFormatting": false
    }
  ]
}
```
```

**Size concern:** A single-slide PPTX fragment base64-encoded is typically 30–150KB. Including it in the FAIT response may hit response payload limits. Sprint 3 should test with a real FORGE template fetch. If payload is too large, the alternative is for FAIT to return only the FORGE node ID and for FfP to fetch the base64 separately via a new `/api/haven/template-fetch?id=<nodeId>` endpoint.

**Sprint 3 safe fallback:** FAIT returns `id` + `name` + `description` only (no base64 in chat response). FfP fetches base64 via a separate `fetchTemplate(nodeId, apiKey)` call in `faitApi.ts`. This is the recommended architecture — keeps the chat response clean and avoids 150KB blobs in SSE streams.

### Recommended Architecture: Separate Template Fetch

```typescript
// In faitApi.ts — new S3 function
export interface TemplateResult {
  id: string;
  name: string;
  description: string;
}

export async function fetchTemplateBase64(
  templateId: string,
  apiKey: string
): Promise<string> {  // returns base64 PPTX fragment
  const resp = await fetch(`${FAIT_BASE}/api/haven/template-fetch`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
    },
    body: JSON.stringify({ id: templateId }),
  });
  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (resp.status === 404) throw new Error('TEMPLATE_NOT_FOUND');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);
  const { base64 } = await resp.json();
  return base64;
}
```

**Backend dependency:** `/api/haven/template-fetch` must be added to the FAIT backend. This is a Sprint 3 backend ask alongside the FORGE template node type. Until the endpoint exists, Tony uses hardcoded test templates.

---

## Feature 3: Chart-as-Image (Client-Side Canvas)

### Architecture Decision: Client-Side (Sprint 3) vs Server-Side (Sprint 4+)

The arch spec documents two approaches:

**Option A — Client-side Chart.js (Sprint 3):**
- FfP renders a Chart.js chart on a hidden `<canvas>` element in the taskpane DOM
- `canvas.toDataURL('image/png')` → base64
- Insert via Common API (`setSelectedDataAsync`) or Preview `addPicture`
- Pro: No new backend endpoint; pure client
- Con: Chart.js adds ~250KB to bundle; rendering quality is screen-pixel-density; no position control (Common API inserts at cursor)

**Option B — Server-side chart image (Sprint 4+):**
- FAIT backend returns a `chartImage` base64 PNG directly in the response
- Pro: Clean bundle, server controls quality/size, can position precisely
- Con: Requires FAIT backend chart rendering service (not built yet)

**Decision for Sprint 3: Option A (Chart.js).** Option B is better long-term but requires backend work. Sprint 3 ships value now with the client-side approach. Sprint 4 can replace the Chart.js render path with the server-side image when the backend is ready — the FfP side is the same `insertImageFromBase64()` call either way.

### `npm install chart.js`

**Chart.js** is the only new npm package in Sprint 3. It is a devDependency (tree-shaken in production build). Add to `package.json`:

```json
"chart.js": "^4.4.0"
```

Import style (tree-shaken):
```typescript
import { Chart, CategoryScale, LinearScale, BarController, BarElement, LineController, LineElement, PointElement, PieController, ArcElement, Title, Tooltip, Legend } from 'chart.js';

Chart.register(CategoryScale, LinearScale, BarController, BarElement, LineController, LineElement, PointElement, PieController, ArcElement, Title, Tooltip, Legend);
```

Only register what's used. Don't `import 'chart.js/auto'` — that pulls in all chart types (~500KB uncompressed).

### UX Flow

```
User: /chart → selects "chart" command
Chat input fills with chart prompt
User: describes chart ("bar chart of Q1 returns by fund, 5 funds")
FAIT: responds with ppt_chart_spec JSON block
FfP: parses spec → renders hidden canvas → shows ChartPreview component (React img tag from base64)
User: clicks "Insert Chart" (or positions cursor on slide first)
FfP: inserts via setSelectedDataAsync or addPicture (feature detected)
```

### `ppt_chart_spec` JSON Block Format

```json
```ppt_chart_spec
{
  "type": "bar",
  "title": "Q1 2026 Fund Returns",
  "width": 600,
  "height": 400,
  "labels": ["Core Equity", "Fixed Income", "Alternatives", "Multi-Asset", "Abs Return"],
  "datasets": [
    {
      "label": "Return (%)",
      "data": [2.4, 0.8, 3.1, 1.9, 4.2],
      "backgroundColor": "#1F3864",
      "borderColor": "#1F3864"
    }
  ],
  "xAxis": { "title": "Fund" },
  "yAxis": { "title": "Return (%)" }
}
```
```

Fields:
- `type` — `"bar"` | `"line"` | `"pie"` | `"doughnut"` | `"scatter"` (S3 supports bar, line, pie only)
- `width` / `height` — canvas dimensions in pixels; defaults 600×400
- `labels` — category axis labels
- `datasets` — Chart.js dataset array (full Chart.js `ChartDataset` schema)
- `xAxis`, `yAxis` — optional axis title labels

### `pptChartRenderer.ts` — Renders Chart.js to Base64

This module creates a hidden `<canvas>` element in the document body, renders a Chart.js chart, calls `toDataURL`, then removes the canvas.

```typescript
import {
  Chart, CategoryScale, LinearScale,
  BarController, BarElement,
  LineController, LineElement, PointElement,
  PieController, ArcElement,
  Title, Tooltip, Legend,
} from 'chart.js';

Chart.register(
  CategoryScale, LinearScale,
  BarController, BarElement,
  LineController, LineElement, PointElement,
  PieController, ArcElement,
  Title, Tooltip, Legend
);

export interface PptChartSpec {
  type: 'bar' | 'line' | 'pie' | 'doughnut' | 'scatter';
  title: string;
  width: number;
  height: number;
  labels: string[];
  datasets: any[];   // Chart.js ChartDataset[]
  xAxis?: { title: string };
  yAxis?: { title: string };
}

/**
 * Render a Chart.js chart to a base64 PNG data URL.
 * Creates a hidden canvas, renders, captures, destroys.
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
          animation: false,   // No animation needed — we capture immediately
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

      // Chart.js renders synchronously when animation: false and responsive: false
      // Small timeout to allow paint cycle to complete
      setTimeout(() => {
        const base64 = canvas.toDataURL('image/png');
        chart.destroy();
        document.body.removeChild(canvas);
        resolve(base64);
      }, 50);
    } catch (e) {
      document.body.removeChild(canvas);
      reject(e);
    }
  });
}
```

**Critical flags:**
- `responsive: false` — REQUIRED. Chart.js's resize observer fires on off-screen elements inconsistently; disabling it makes rendering deterministic.
- `animation: false` — REQUIRED. Without this, `toDataURL()` may capture an intermediate animation frame.

### `pptWriter.ts` Addition: `insertChartImage()`

```typescript
/**
 * Insert a base64 PNG into the current slide.
 * 
 * Uses feature detection:
 * 1. If Preview addPicture is available → use it (positioned insert)
 * 2. Fallback: Common API setSelectedDataAsync (inserts at cursor)
 *
 * The base64 string must include the data: prefix:
 *   "data:image/png;base64,iVBORw0KGgo..."
 * OR the raw base64 without prefix (function handles both).
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
      // No slide selected — fallback to Common API insert at cursor
      insertViaCommonApi(rawBase64, width, height);
      return;
    }

    const slide = selectedSlides.items[0];

    // Feature detection: addPicture is Preview-only as of early 2026
    const supportsAddPicture = typeof (slide.shapes as any).addPicture === 'function';

    if (supportsAddPicture) {
      // Preview path: precise positioning (center of a 10×7.5 inch slide in points)
      (slide.shapes as any).addPicture(rawBase64, {
        left: 180,   // approx centered on 720pt-wide slide
        top: 100,
        width,
        height,
      });
      await ctx.sync();
    } else {
      // Fallback: Common API — inserts at cursor position
      insertViaCommonApi(rawBase64, width, height);
    }
  }).catch((e: any) => {
    // If PowerPoint.run fails entirely, try Common API fallback
    insertViaCommonApi(rawBase64, width, height);
    throw e;  // Re-throw for caller to surface error
  });
}

/** Common API image insert — works in both Desktop and Online, inserts at cursor */
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

**Declare `Office` at the top of `pptWriter.ts`:**
```typescript
declare const Office: any;
declare const PowerPoint: any;
```

---

## Feature 4–6: Slash Commands

### Task: `SlashCommandPicker.tsx` — Add `/table`, `/chart`, `/template`

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

## New Files + Modified Files

### New Files

**`src/taskpane/services/pptChartRenderer.ts`** — Chart.js canvas render → base64 (full code in Feature 3 above)

**`src/taskpane/services/pptSpecParser.ts`** — Parse all three spec types (`ppt_table_spec`, `ppt_chart_spec`, `ppt_template_spec`). Consolidates parsers.

```typescript
export interface PptTableSpec { /* as defined in Feature 1 */ }
export interface PptChartSpec { /* as defined in Feature 3 */ }
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

export function stripAllSpecs(content: string): string {
  return content
    .replace(/```ppt_table_spec\s*[\s\S]*?```/g, '')
    .replace(/```ppt_chart_spec\s*[\s\S]*?```/g, '')
    .replace(/```ppt_template_spec\s*[\s\S]*?```/g, '')
    .trim();
}
```

**`src/taskpane/components/TablePreview.tsx`** — Confirm dialog for table creation

```typescript
import React from 'react';
import type { PptTableSpec } from '../services/pptSpecParser';

interface TablePreviewProps {
  spec: PptTableSpec;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
}

const TablePreview: React.FC<TablePreviewProps> = ({ spec, onAccept, onReject, loading = false }) => (
  <div style={{ padding: '10px 12px', borderTop: '1px solid #2e3f54', background: '#0f1720', flexShrink: 0, display: 'flex', flexDirection: 'column', gap: '8px' }}>
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      📊 Table Preview — {spec.rowCount} rows × {spec.columnCount} cols
    </div>
    {/* Mini table preview */}
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: '10px', width: '100%' }}>
        <thead>
          <tr>
            {spec.headers.map((h, i) => (
              <th key={i} style={{ background: '#1F3864', color: '#fff', padding: '3px 6px', border: '1px solid #2e3f54', fontWeight: '600', whiteSpace: 'nowrap' }}>
                {h || '—'}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {spec.values.slice(0, 3).map((row, ri) => (
            <tr key={ri}>
              {row.map((cell, ci) => (
                <td key={ci} style={{ padding: '2px 6px', border: '1px solid #2e3f54', color: '#c8d8e8', whiteSpace: 'nowrap' }}>
                  {cell || '—'}
                </td>
              ))}
            </tr>
          ))}
          {spec.values.length > 3 && (
            <tr>
              <td colSpan={spec.columnCount} style={{ padding: '2px 6px', color: '#556677', fontSize: '10px', border: '1px solid #2e3f54' }}>
                +{spec.values.length - 3} more rows
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
    <div style={{ display: 'flex', gap: '6px' }}>
      <button onClick={onAccept} disabled={loading}
        style={{ flex: 1, background: '#d4af37', color: '#0f1720', border: 'none', borderRadius: '4px', padding: '6px 12px', fontSize: '12px', fontWeight: '700', cursor: loading ? 'not-allowed' : 'pointer', opacity: loading ? 0.6 : 1 }}>
        {loading ? 'Creating…' : '✓ Create Table'}
      </button>
      <button onClick={onReject} disabled={loading}
        style={{ background: '#2e3f54', color: '#e8edf3', border: 'none', borderRadius: '4px', padding: '6px 10px', fontSize: '12px', cursor: loading ? 'not-allowed' : 'pointer', opacity: loading ? 0.6 : 1 }}>
        Discard
      </button>
    </div>
  </div>
);
export default TablePreview;
```

**`src/taskpane/components/ChartPreview.tsx`** — Confirm dialog for chart insert

```typescript
import React from 'react';

interface ChartPreviewProps {
  base64DataUrl: string;     // 'data:image/png;base64,...'
  title: string;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
  error?: string | null;
}

const ChartPreview: React.FC<ChartPreviewProps> = ({ base64DataUrl, title, onAccept, onReject, loading = false, error }) => (
  <div style={{ padding: '10px 12px', borderTop: '1px solid #2e3f54', background: '#0f1720', flexShrink: 0, display: 'flex', flexDirection: 'column', gap: '8px' }}>
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>📈 Chart Preview — {title}</div>
    <img src={base64DataUrl} alt={title}
      style={{ width: '100%', maxHeight: '180px', objectFit: 'contain', border: '1px solid #2e3f54', borderRadius: '3px' }} />
    {error && <div style={{ color: '#e07070', fontSize: '11px' }}>{error}</div>}
    <div style={{ display: 'flex', gap: '6px' }}>
      <button onClick={onAccept} disabled={loading || !!error}
        style={{ flex: 1, background: '#d4af37', color: '#0f1720', border: 'none', borderRadius: '4px', padding: '6px 12px', fontSize: '12px', fontWeight: '700', cursor: (loading || !!error) ? 'not-allowed' : 'pointer', opacity: (loading || !!error) ? 0.6 : 1 }}>
        {loading ? 'Inserting…' : '✓ Insert Chart'}
      </button>
      <button onClick={onReject} disabled={loading}
        style={{ background: '#2e3f54', color: '#e8edf3', border: 'none', borderRadius: '4px', padding: '6px 10px', fontSize: '12px', cursor: loading ? 'not-allowed' : 'pointer', opacity: loading ? 0.6 : 1 }}>
        Discard
      </button>
    </div>
  </div>
);
export default ChartPreview;
```

**`src/taskpane/components/TemplateGallery.tsx`** — Template selection panel

```typescript
import React from 'react';
import type { PptTemplateSpec } from '../services/pptSpecParser';

interface TemplateGalleryProps {
  spec: PptTemplateSpec;
  onInsert: (templateId: string, keepSourceFormatting: boolean) => void;
  onReject: () => void;
  loading?: boolean;
}

const TemplateGallery: React.FC<TemplateGalleryProps> = ({ spec, onInsert, onReject, loading = false }) => (
  <div style={{ padding: '10px 12px', borderTop: '1px solid #2e3f54', background: '#0f1720', flexShrink: 0, display: 'flex', flexDirection: 'column', gap: '8px' }}>
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>🗂 Slide Templates from FORGE</div>
    <div style={{ display: 'flex', flexDirection: 'column', gap: '6px', maxHeight: '200px', overflowY: 'auto' }}>
      {spec.templates.map((t) => (
        <div key={t.id} style={{ background: '#131e2b', border: '1px solid #2e3f54', borderRadius: '4px', padding: '8px 10px', display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '8px' }}>
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: '12px', fontWeight: '600', color: '#e8edf3' }}>{t.name}</div>
            <div style={{ fontSize: '11px', color: '#778899', marginTop: '2px' }}>{t.description}</div>
          </div>
          <button
            onClick={() => onInsert(t.id, t.keepSourceFormatting)}
            disabled={loading}
            style={{ background: '#d4af37', color: '#0f1720', border: 'none', borderRadius: '3px', padding: '4px 10px', fontSize: '11px', fontWeight: '700', cursor: loading ? 'not-allowed' : 'pointer', opacity: loading ? 0.6 : 1, flexShrink: 0 }}>
            {loading ? '…' : '+ Insert'}
          </button>
        </div>
      ))}
    </div>
    <button onClick={onReject}
      style={{ background: '#2e3f54', color: '#e8edf3', border: 'none', borderRadius: '4px', padding: '5px 10px', fontSize: '11px', cursor: 'pointer', alignSelf: 'flex-start' }}>
      Cancel
    </button>
  </div>
);
export default TemplateGallery;
```

**`src/taskpane/services/pptTableWriter.ts`** — Calls `shapes.addTable()`

```typescript
/* global PowerPoint */
declare const PowerPoint: any;
import type { PptTableSpec } from './pptSpecParser';

export class PptTableError extends Error {
  constructor(message: string, public readonly code: 'NO_SLIDE' | 'DIMENSION_MISMATCH' | 'PPT_ERROR') {
    super(message);
    this.name = 'PptTableError';
  }
}

export async function insertTable(spec: PptTableSpec): Promise<void> {
  // Validation: headers.length must equal columnCount
  if (spec.headers.length !== spec.columnCount) {
    throw new PptTableError(
      `headers.length (${spec.headers.length}) !== columnCount (${spec.columnCount})`,
      'DIMENSION_MISMATCH'
    );
  }
  // Validation: each row must have columnCount values
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

    // Build full values array: headers row + data rows
    const allRows: string[][] = [spec.headers, ...spec.values];
    const totalRows = allRows.length;
    const options: any = { values: allRows };

    // Build specificCellProperties for header row
    if (spec.headerStyle !== 'none') {
      const isLight = spec.headerStyle === 'lightHeader';
      // MUST be exact same dimensions as the table
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

    // addTable(rows, cols, options) — rows includes header row
    slide.shapes.addTable(totalRows, spec.columnCount, options);
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptTableError) throw e;
    throw new PptTableError(e?.message ?? 'Table creation failed', 'PPT_ERROR');
  });
}
```

**`src/taskpane/services/pptTemplateService.ts`** — `insertSlidesFromBase64` wrapper

```typescript
/* global PowerPoint */
declare const PowerPoint: any;
import { fetchTemplateBase64 } from './faitApi';

export class PptTemplateError extends Error {
  constructor(message: string, public readonly code: 'NO_SLIDE' | 'FETCH_FAILED' | 'PPT_ERROR') {
    super(message);
    this.name = 'PptTemplateError';
  }
}

export async function insertTemplateSlide(
  templateId: string,
  apiKey: string,
  keepSourceFormatting = false
): Promise<void> {
  // Fetch base64 PPTX fragment from FORGE via FAIT API
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
      targetSlide,  // null = insert at end
    });

    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptTemplateError) throw e;
    throw new PptTemplateError(e?.message ?? 'Template insert failed', 'PPT_ERROR');
  });
}
```

---

### Modified Files

**`public/manifest.xml`** + **`manifest.local.xml`**: `MinVersion="1.6"` → `MinVersion="1.8"`

**`src/taskpane/services/faitApi.ts`**: Add `fetchTemplateBase64()` function (see Feature 2 above)

**`src/taskpane/components/SlashCommandPicker.tsx`**: Add `/table`, `/chart`, `/template` commands

**`src/taskpane/components/ChatPanel.tsx`**: Wire up table/chart/template spec detection, state, handlers, and preview components

#### ChatPanel.tsx changes:

**New imports:**
```typescript
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

**New state:**
```typescript
// Sprint 3: Table state
const [pendingTable, setPendingTable] = useState<PptTableSpec | null>(null);
const [tableLoading, setTableLoading] = useState(false);
const [tableError, setTableError] = useState<string | null>(null);

// Sprint 3: Chart state
const [pendingChart, setPendingChart] = useState<PptChartSpec | null>(null);
const [pendingChartBase64, setPendingChartBase64] = useState<string | null>(null);
const [chartLoading, setChartLoading] = useState(false);
const [chartRenderError, setChartRenderError] = useState<string | null>(null);

// Sprint 3: Template state
const [pendingTemplates, setPendingTemplates] = useState<PptTemplateSpec | null>(null);
const [templateLoading, setTemplateLoading] = useState(false);
const [templateError, setTemplateError] = useState<string | null>(null);
```

**Updated `messages` watcher** (extend the existing S2 `useEffect`):
```typescript
useEffect(() => {
  const lastMsg = messages[messages.length - 1];
  if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
    // S2: notes spec
    const notes = parseNotesSpec(lastMsg.content);
    if (notes) { setPendingNotes(notes); setNotesError(null); }

    // S3: table spec
    const table = parseTableSpec(lastMsg.content);
    if (table) { setPendingTable(table); setTableError(null); }

    // S3: chart spec — parse then render
    const chart = parseChartSpec(lastMsg.content);
    if (chart) {
      setPendingChart(chart);
      setChartRenderError(null);
      setChartLoading(true);
      renderChartToBase64(chart)
        .then((b64) => { setPendingChartBase64(b64); })
        .catch(() => { setChartRenderError('Chart render failed — check the data format.'); })
        .finally(() => setChartLoading(false));
    }

    // S3: template spec
    const tmpl = parseTemplateSpec(lastMsg.content);
    if (tmpl) { setPendingTemplates(tmpl); setTemplateError(null); }
  }
}, [messages]);
```

**Handler: `handleTableCreate`:**
```typescript
const handleTableCreate = async () => {
  if (!pendingTable) return;
  setTableLoading(true);
  setTableError(null);
  try {
    await insertTable(pendingTable);
    setPendingTable(null);
  } catch (e) {
    if (e instanceof PptTableError) {
      setTableError(e.code === 'DIMENSION_MISMATCH' ? 'Table dimensions mismatch — FAIT generated invalid data.' : 'Table creation failed — try again.');
    } else {
      setTableError('Table creation failed — try again.');
    }
  } finally {
    setTableLoading(false);
  }
};
```

**Handler: `handleChartInsert`:**
```typescript
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
```

**Handler: `handleTemplateInsert`:**
```typescript
const handleTemplateInsert = async (templateId: string, keepSource: boolean) => {
  setTemplateLoading(true);
  setTemplateError(null);
  try {
    await insertTemplateSlide(templateId, apiKey, keepSource);
    setPendingTemplates(null);
  } catch (e) {
    if (e instanceof PptTemplateError) {
      setTemplateError(e.code === 'FETCH_FAILED' ? 'Could not fetch template from FORGE — check your API key.' : 'Template insert failed.');
    } else {
      setTemplateError('Template insert failed.');
    }
  } finally {
    setTemplateLoading(false);
  }
};
```

**Updated message display:**
Replace `stripNotesSpec(msg.content)` (S2) with `stripAllSpecs(msg.content)` (S3 — strips all three spec types):
```typescript
const displayContent = msg.role === 'assistant'
  ? stripAllSpecs(msg.content)   // S3: replaces stripNotesSpec
  : msg.content;
```

**JSX additions** (in logical order below FORGE results and above ChatInput):
```typescript
{pendingTable && (
  <TablePreview spec={pendingTable} onAccept={handleTableCreate} onReject={() => { setPendingTable(null); setTableError(null); }} loading={tableLoading} />
)}
{tableError && <div style={errorBarStyle}>{tableError}</div>}

{(pendingChart || chartLoading || chartRenderError) && pendingChartBase64 && (
  <ChartPreview base64DataUrl={pendingChartBase64} title={pendingChart?.title ?? 'Chart'} onAccept={handleChartInsert} onReject={() => { setPendingChart(null); setPendingChartBase64(null); }} loading={chartLoading} error={chartRenderError} />
)}
{chartLoading && !pendingChartBase64 && (
  <div style={{ padding: '8px 12px', color: '#556677', fontSize: '11px' }}>Rendering chart…</div>
)}

{pendingTemplates && (
  <TemplateGallery spec={pendingTemplates} onInsert={handleTemplateInsert} onReject={() => { setPendingTemplates(null); setTemplateError(null); }} loading={templateLoading} />
)}
{templateError && <div style={errorBarStyle}>{templateError}</div>}
```

Where `errorBarStyle`:
```typescript
const errorBarStyle: React.CSSProperties = {
  padding: '4px 12px', background: '#1a0f0f', color: '#e07070', fontSize: '11px', flexShrink: 0,
};
```

---

## Files Changed Summary

| File | Type | Change |
|------|------|--------|
| `public/manifest.xml` | Modified | MinVersion 1.6 → 1.8 |
| `manifest.local.xml` | Modified | MinVersion 1.6 → 1.8 |
| `src/taskpane/services/pptSpecParser.ts` | **New** | `parseTableSpec`, `parseChartSpec`, `parseTemplateSpec`, `stripAllSpecs` |
| `src/taskpane/services/pptChartRenderer.ts` | **New** | Chart.js canvas render → base64 |
| `src/taskpane/services/pptTableWriter.ts` | **New** | `insertTable()` using `shapes.addTable()` |
| `src/taskpane/services/pptTemplateService.ts` | **New** | `insertTemplateSlide()` using `insertSlidesFromBase64` |
| `src/taskpane/components/TablePreview.tsx` | **New** | Mini table preview + Create/Discard |
| `src/taskpane/components/ChartPreview.tsx` | **New** | Chart image preview + Insert/Discard |
| `src/taskpane/components/TemplateGallery.tsx` | **New** | Template cards + Insert |
| `src/taskpane/services/faitApi.ts` | Modified | Add `fetchTemplateBase64()` |
| `src/taskpane/components/SlashCommandPicker.tsx` | Modified | Add `/table`, `/chart`, `/template` |
| `src/taskpane/components/ChatPanel.tsx` | Modified | Wire S3 state, parsers, handlers, previews |
| `package.json` | Modified | Add `"chart.js": "^4.4.0"` |

**Total: 7 new files + 6 modified. One new npm package (chart.js).**

---

## Acceptance Criteria

1. **Tables:** `/table` command → user describes table → FAIT responds with `ppt_table_spec` → `TablePreview` renders mini HTML table showing headers + first 3 rows → user clicks "✓ Create Table" → a PowerPoint table appears on the current slide with the correct data and dark navy header row.

2. **Table validation:** If FAIT returns a `ppt_table_spec` with mismatched dimensions (e.g., `columnCount: 3` but `headers.length: 4`), the error `"Table dimensions mismatch"` appears in the error bar. No PowerPoint API call is made.

3. **Charts — render:** `/chart` command → user describes chart → FAIT responds with `ppt_chart_spec` → "Rendering chart…" loading state appears briefly → `ChartPreview` appears showing the rendered chart as an image → chart matches the data in the spec.

4. **Charts — insert (Common API path):** Clicking "✓ Insert Chart" inserts the chart image into the current slide. On builds where `addPicture` is not available, the image inserts at the cursor position.

5. **Charts — insert (Preview addPicture path):** On M365 Insider builds with Preview APIs enabled, the chart is inserted at a centered position on the slide (left: 180pt, top: 100pt).

6. **Template injection (with FORGE backend):** `/template` → user describes slide type → FAIT returns `ppt_template_spec` → `TemplateGallery` shows 1–3 template cards with name + description → user clicks "+ Insert" on a card → `fetchTemplateBase64` is called → `insertSlidesFromBase64` runs → a new slide appears after the current slide.

7. **Template injection (dev/test):** With hardcoded test template PPTX, the insert works end-to-end even before the FORGE template backend is ready.

8. **Spec stripping:** After any response containing `ppt_table_spec`, `ppt_chart_spec`, or `ppt_template_spec`, the chat thread shows the human-readable portion only — the raw JSON block is not visible.

9. **Manifest version:** PowerPoint Online accepts the add-in with `MinVersion="1.8"` without error. The `/table` command is functional (confirms 1.8 APIs are available).

---

## Constraints for CC

- `shapes.addTable(totalRows, spec.columnCount, options)` — `totalRows` must be `spec.rowCount + 1` (includes the header row). Never pass `spec.rowCount` directly.
- `specificCellProperties` must be a 2D array of **exactly** `totalRows × columnCount`. If dimensions don't match, PowerPoint will throw. Use `.map()` to ensure the array exactly mirrors `allRows` structure.
- `insertSlidesFromBase64` uses `InsertSlideFormatting.useDestinationTheme` by default — not `keepSourceFormatting`. The `keepSourceFormatting` value comes from the template spec field, not hardcoded.
- Chart.js: import named exports only, not `chart.js/auto`. Register only the controllers/elements used: `BarController`, `LineController`, `PieController` + their required elements. Omitting `CategoryScale` and `LinearScale` causes a silent render failure on bar/line charts.
- `renderChartToBase64`: `responsive: false` and `animation: false` are both required. Do not remove these options.
- `pptSpecParser.ts` replaces S2's `pptNotesParser.ts` for spec stripping — `ChatPanel.tsx` must import `stripAllSpecs` from `pptSpecParser.ts`, not `stripNotesSpec` from `pptNotesParser.ts`. The `pptNotesParser.ts` file still exists (the `parseNotesSpec` function is still used); only the `stripNotesSpec` function is retired.
- `fetchTemplateBase64` in `faitApi.ts` will return a `404` until the FORGE backend supports the template node type. Tony must handle this with the test template fallback — hardcode a minimal base64 PPTX in `testTemplates.ts` for development. Flag this clearly in a `TODO` comment in `pptTemplateService.ts`.
- Do NOT modify any files in `~/projects/fait-for-excel/` — all changes are in `~/projects/fip/fait-for-powerpoint/`.
- `chart.js` goes in `dependencies`, not `devDependencies` — it's a runtime dependency for client-side rendering.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify manifest MinVersion is exactly "1.8" in BOTH manifest.xml AND
          manifest.local.xml. Also confirm the PowerPointApi requirement set
          tag syntax is correct: <Set Name="PowerPointApi" MinVersion="1.8"/>
          (not ExcelApi, not PowerPoint.Api, not 1.80).

⚠️  HIGH: Verify shapes.addTable() receives totalRows = spec.rowCount + 1
          (header row included in total). If spec.rowCount = 5, addTable must
          receive 6, not 5. Passing 5 will create a table with 4 data rows,
          silently dropping the last row of data.

⚠️  HIGH: Verify specificCellProperties dimensions match table dimensions.
          specificCellProperties.length must equal totalRows (= spec.rowCount + 1).
          specificCellProperties[i].length must equal spec.columnCount for every i.
          PowerPoint throws "InvalidArgument" if dimensions mismatch — confirm
          the .map() builds exact dimensions, not spec.values.map() (which
          would be 1 row short, missing the header row).

⚠️  HIGH: Verify Chart.js imports include CategoryScale and LinearScale.
          These are required for bar and line charts. Omitting them produces
          a console error "Category scale not registered" and a blank canvas.
          Check the Chart.register() call includes all 8 items listed in the spec.

⚠️  MEDIUM: Verify renderChartToBase64 canvas has responsive: false and
            animation: false. If responsive: true is present, Chart.js will
            try to resize the canvas and may capture a 0x0 image.

⚠️  MEDIUM: Verify insertSlidesFromBase64 uses keepSourceFormatting vs
            useDestinationTheme based on template.keepSourceFormatting field,
            not hardcoded. The three test template cards should each have
            keepSourceFormatting: false in the test data.

⚠️  MEDIUM: Confirm stripAllSpecs() is used in ChatPanel message display,
            replacing the S2 stripNotesSpec() call. The ppt_notes_spec block
            must also be stripped by stripAllSpecs (it handles all four spec
            types). Verify by checking that the regex in pptSpecParser.ts
            includes the ppt_notes_spec pattern alongside table/chart/template.

⚠️  LOW: Verify chart.js is in dependencies (not devDependencies). Vite
         tree-shakes it correctly in production builds, but npm install on
         the Docker build stage requires it in dependencies.

⚠️  LOW: Flag the testTemplates.ts file with a TODO: DO NOT SHIP comment.
         Tony must add a guard (e.g., NODE_ENV check or an explicit import
         guard) so it can't accidentally be used in production builds.
```

---

_Spec by Reed Richards | FfP Sprint 3: 7 new files, 6 modified, 1 new package (chart.js). Manifest bumps to 1.8._

---

## Appendix: What S3 Doesn't Include

The following items from the arch spec are explicitly deferred:

**Server-side chart rendering (Sprint 4+):** FAIT backend returns `chartImage` base64 PNG. Cleaner than Chart.js client-side. Requires backend work not yet planned. When available, `pptChartRenderer.ts` is retired and `insertChartImage()` in `pptWriter.ts` is called with the server-generated base64 directly.

**Positioned table insert:** Sprint 3 uses PowerPoint's default table position. `spec.position` is parsed but ignored. Sprint 4 can use `TableAddOptions` position fields if the API supports them (check 1.8 docs — not confirmed in research).

**`addPicture()` promotion:** If `addPicture` is promoted to a numbered requirement set (1.11, expected mid-2026) before S3 ships, update the `insertChartImage` function to call it directly instead of using feature detection. The conditional logic stays but the stable path becomes the primary path.
