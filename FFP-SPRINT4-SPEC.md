# FfP Sprint 4 Spec — Shape Bindings + Positioned Table + Server-Side Chart Image

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Repo:** `fip/fait-for-powerpoint/`  
**Depends on:** FfP S1–S3 fully deployed  
**PresApi baseline:** 1.8 (unchanged from S3)

---

## Pre-Read: What Was Confirmed

- S3 shipped: `pptTableWriter.ts`, `pptChartRenderer.ts` (Chart.js client-side), `pptTemplateService.ts`, `pptNotesParser.ts`, `NotesPreview.tsx`, `TablePreview.tsx`, `ChartPreview.tsx`, `TemplateGallery.tsx`
- Slash commands available: `/notes`, `/summarize`, `/improve`, `/bullets`, `/expand`, `/table`, `/chart`, `/template`
- S3 deferred explicitly: (1) table position control, (2) server-side chart image, (3) Bindings API for stable shape re-addressing
- `pptWriter.ts` `applyTextToShape()` — writes to shape by ID, but the ID can change after slide copy or presentation re-open (PowerPoint re-assigns IDs). Bindings API (PresApi 1.8) solves this.
- `insertChartImage()` in `pptWriter.ts` uses `setSelectedDataAsync` (Common API) — inserts at cursor, not at coordinates

---

## Sprint 4 Objectives

| # | Feature | PresApi | Complexity |
|---|---------|---------|------------|
| 1 | Shape Bindings — stable shape addressing across sessions | 1.8 | Medium |
| 2 | Positioned table insert — `left`, `top`, `width`, `height` | 1.8 | Small |
| 3 | Server-side chart image — FAIT backend returns base64 PNG | N/A (backend) | Medium |
| 4 | `/rewrite` slash command — FAIT rewrites selected shape text with KB grounding | — | Small |

---

## Feature 1: Shape Bindings

### Problem

`applyTextToShape(shapeId, text)` uses `slide.shapes.getById(shapeId)`. PowerPoint assigns shape IDs dynamically. After:
- Duplicating a slide
- Copy/paste from another presentation
- Closing and reopening the presentation

…the shape IDs assigned by PowerPoint may change. The binding the user thought they had to a specific shape is broken.

PowerPointApi 1.8 adds `Bindings` — a stable, persistent named identifier that survives session restarts and slide operations.

### What Bindings Provide

```typescript
// Create a binding: maps a persistent name to the currently selected shape
await PowerPoint.run(async (ctx) => {
  const binding = ctx.presentation.bindings.add(
    PowerPoint.BindingType.shape,  // Only type available in 1.8
    'FAIT_output_slide2_titleBox'  // User-defined ID — must be unique in the presentation
  );
  await ctx.sync();
  // binding.id is the stable reference we store
});

// Write to a shape via binding (later, even after reopen):
await PowerPoint.run(async (ctx) => {
  const binding = ctx.presentation.bindings.getItem('FAIT_output_slide2_titleBox');
  binding.load('id');
  await ctx.sync();
  // binding.shape gives us the shape regardless of whether its numeric ID changed
  const shape = binding.shape;
  shape.textFrame.textRange.text = 'New content';
  await ctx.sync();
});
```

### New Service: `pptBindings.ts`

```typescript
// src/taskpane/services/pptBindings.ts

const BINDING_PREFIX = 'FAIT_';

/**
 * Register a binding for the currently selected shape.
 * Returns the binding ID (same as bindingName).
 * If a binding with this name already exists, overwrites it.
 */
export async function bindSelectedShape(bindingName: string): Promise<string> {
  const fullName = `${BINDING_PREFIX}${bindingName}`;
  await PowerPoint.run(async (ctx) => {
    // Remove existing binding with same name if it exists
    try {
      const existing = ctx.presentation.bindings.getItemOrNullObject(fullName);
      await ctx.sync();
      if (!existing.isNullObject) existing.delete();
      await ctx.sync();
    } catch { /* Ignore if not found */ }

    ctx.presentation.bindings.add(PowerPoint.BindingType.shape, fullName);
    await ctx.sync();
  });
  return fullName;
}

/**
 * Write text to a shape via its binding name.
 * Falls back to getById if binding not found (backward compat with S1-S3 shapes).
 */
export async function writeToBinding(
  bindingName: string,
  text: string,
  fallbackShapeId?: string
): Promise<void> {
  const fullName = bindingName.startsWith(BINDING_PREFIX)
    ? bindingName
    : `${BINDING_PREFIX}${bindingName}`;

  await PowerPoint.run(async (ctx) => {
    // Try binding first
    const binding = ctx.presentation.bindings.getItemOrNullObject(fullName);
    await ctx.sync();

    if (!binding.isNullObject) {
      const shape = binding.shape;
      shape.textFrame.textRange.text = text;
      await ctx.sync();
      return;
    }

    // Fallback: address by shape ID (S1-S3 behavior)
    if (fallbackShapeId) {
      const slide = ctx.presentation.slides.getItemAt(0);
      slide.load('shapes');
      await ctx.sync();
      const shape = slide.shapes.getById(parseInt(fallbackShapeId, 10));
      shape.textFrame.textRange.text = text;
      await ctx.sync();
    }
  });
}

/**
 * List all FAIT bindings in the presentation.
 * Used for the /rewrite command to show bindable shapes.
 */
export async function listFaitBindings(): Promise<Array<{ id: string; name: string }>> {
  const results: Array<{ id: string; name: string }> = [];
  await PowerPoint.run(async (ctx) => {
    const bindings = ctx.presentation.bindings;
    bindings.load('items');
    await ctx.sync();
    for (const b of bindings.items) {
      b.load('id');
    }
    await ctx.sync();
    for (const b of bindings.items) {
      if (b.id.startsWith(BINDING_PREFIX)) {
        results.push({ id: b.id, name: b.id.slice(BINDING_PREFIX.length) });
      }
    }
  });
  return results;
}
```

### Integration: Tag Shape on Write

When `applyTextToShape()` writes to a shape, also register a binding if no binding exists yet. This migrates all S1-S3 output shapes to Bindings transparently.

**Update `pptWriter.ts` `applyTextToShape()`:**

```typescript
// After the shape write, register a binding using the nodeId as the name:
if (nodeId) {
  try {
    // Only bind if we can determine the current selection
    // Use the nodeId as the binding name — stable across FfP sessions
    await PowerPoint.run(async (ctx) => {
      const slide = ctx.presentation.getSelectedSlides().getItemAt(0);
      const shape = slide.shapes.getById(parseInt(shapeId, 10));
      shape.load('id');
      await ctx.sync();
      // Register binding: FAIT_<nodeId>
      const bindName = `FAIT_${nodeId}`;
      try {
        ctx.presentation.bindings.add(PowerPoint.BindingType.shape, bindName);
        await ctx.sync();
      } catch { /* Binding may already exist — ignore */ }
    });
  } catch { /* Non-fatal — bindings are an enhancement */ }
}
```

---

## Feature 2: Positioned Table Insert

S3's `pptTableWriter.ts` accepted `spec.position` but ignored it (PowerPoint used default placement). PowerPointApi 1.8 `TableAddOptions` accepts `left`, `top`, `width` in points.

**Update `pptTableWriter.ts` `insertTable()`:**

```typescript
interface TableSpec {
  // Existing fields unchanged
  headers: string[];
  rows: string[][];
  // S4 additions — optional, all in points (1 inch = 72 points)
  position?: {
    left?: number;    // Distance from left edge of slide
    top?: number;     // Distance from top edge of slide
    width?: number;   // Total table width
  };
}

// In insertTable():
const options: PowerPoint.TableAddOptions = {
  values: [spec.headers, ...spec.rows],
  rowCount: spec.rows.length + 1,  // Including header row
  columnCount: spec.headers.length,
  // S4: apply position if provided
  ...(spec.position?.left  !== undefined && { left:  spec.position.left  }),
  ...(spec.position?.top   !== undefined && { top:   spec.position.top   }),
  ...(spec.position?.width !== undefined && { width: spec.position.width }),
  specificCellProperties: buildCellProperties(spec.headers.length, spec.rows.length + 1),
};
```

**Update the `table_spec` JSON block format** parsed by `pptSpecParser.ts` to include optional `position`:

```
"position": {"left": 50, "top": 180, "width": 620}
```

All position values default to PowerPoint's auto-placement if absent — backward compatible with S3.

**Update `pptSpecParser.ts` `parseTableSpec()`:**

```typescript
interface TableSpec {
  // existing fields...
  position?: { left?: number; top?: number; width?: number };
}

// In the parse function, add:
position: raw.position as TableSpec['position'] ?? undefined,
```

**Slide dimensions reference (for FAIT system prompt):**
- Standard widescreen (16:9): 960 × 540 points
- Standard (4:3): 720 × 540 points
- Typical safe content area: left 50, top 100, width 860

Add this to the FAIT system prompt in `runner.ts`:
```
Table position reference (points, widescreen 16:9 slide):
- Full-width: {"left": 50, "top": 180, "width": 860}
- Right half: {"left": 500, "top": 150, "width": 410}
- Below title: {"left": 50, "top": 200, "width": 860}
```

---

## Feature 3: Server-Side Chart Image

### Problem with S3 Chart.js Approach

S3 uses Chart.js in a hidden `<canvas>` element. Issues:
- Chart.js canvas rendering is async and size-constrained by the DOM
- Output quality is screen-resolution (72 DPI) — looks pixelated in presentations
- Font rendering differs from FAIT's backend (different font stack)

### Server-Side Alternative

FAIT backend renders the chart using a lightweight SSR library (or a server-side canvas) and returns a base64 PNG. FfP receives it and inserts directly. Higher quality, consistent rendering.

**Backend change (FAIT `HavenChatController.cs` or a new `ChartController.cs`):**

```csharp
// POST /api/excel/chart-image
// Accepts a chart spec JSON; returns base64 PNG
[HttpPost("chart-image")]
[Authorize(Policy = "ExcelAddinAccess")]
public async Task<IActionResult> RenderChartImage([FromBody] ChartImageRequest request)
{
    // Use SkiaSharp (already available via SkiaSharp.NativeAssets.Linux) to render
    // a simple bar/line/pie chart from the spec. Returns 400x300 PNG by default.
    var png = await _chartRenderer.RenderAsync(request);
    return Ok(new { imageBase64 = Convert.ToBase64String(png) });
}

public record ChartImageRequest(
    string Type,              // "bar" | "line" | "pie"
    string? Title,
    int Width,
    int Height,
    string[] Labels,
    ChartDataset[] Datasets
);
public record ChartDataset(string Label, double[] Data, string? BackgroundColor);
```

**SkiaSharp approach:** FAIT's `FortressAI.Web.csproj` already uses SkiaSharp for PDF processing. Add `ChartRendererService.cs` using `SKCanvas` + `SKBitmap`. A 600×400 chart renders in ~5ms server-side.

**FfP client change (`pptWriter.ts` `insertChartImage()`):**

Add a server-side path before the Chart.js fallback:

```typescript
export async function insertChartImage(spec: ChartSpec): Promise<void> {
  let base64: string;

  // Try server-side rendering first (better quality)
  try {
    const resp = await fetch(`${FAIT_BASE}/api/excel/chart-image`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...await getAuthHeader() },
      body: JSON.stringify(spec),
    });
    if (resp.ok) {
      const { imageBase64 } = await resp.json();
      base64 = imageBase64;
    } else {
      throw new Error('Server chart failed');
    }
  } catch {
    // Fallback: client-side Chart.js (S3 behavior — retained for offline/error resilience)
    base64 = await renderChartClientSide(spec);
  }

  await insertImageBase64(base64, spec.width ?? 600, spec.height ?? 400);
}
```

**New file: `src/taskpane/services/chartImageService.ts`** — extracts the server-side fetch; `pptChartRenderer.ts` becomes the client-side fallback only.

---

## Feature 4: `/rewrite` Slash Command

### What It Does

`/rewrite` rewrites the text of the currently selected shape using FORGE KB grounding. The user selects a shape, types `/rewrite [optional instruction]`, and FAIT produces a revised version with KB context injected. The new text appears in `ShapePreview.tsx` for Accept/Reject before applying.

This is the first command that is **purely about refining existing content** (not inserting new content). It's the highest-value day-2+ feature — analysts can select their draft bullet points and ask FAIT to refine them against the KB.

### Implementation

**New slash command entry in `SlashCommandPicker.tsx`:**
```typescript
{
  label: '/rewrite',
  description: 'Rewrite selected shape text with KB-grounded improvements',
  template: '/rewrite [optional instruction, e.g. "make more concise" or "add data points"]',
}
```

**`pptReader.ts` — `getSelectedShapeText()`** (new helper):

```typescript
export async function getSelectedShapeText(): Promise<{ shapeId: string; text: string } | null> {
  let result: { shapeId: string; text: string } | null = null;
  await PowerPoint.run(async (ctx) => {
    const selection = ctx.presentation.getSelectedShapes();
    selection.load('items');
    await ctx.sync();
    if (selection.items.length === 0) return;
    const shape = selection.items[0];
    shape.load('id,textFrame');
    await ctx.sync();
    shape.textFrame.textRange.load('text');
    await ctx.sync();
    result = {
      shapeId: String(shape.id),
      text: shape.textFrame.textRange.text ?? '',
    };
  });
  return result;
}
```

**`ChatPanel.tsx` `handleSlashCommand()` — add `/rewrite` case:**

```typescript
case '/rewrite': {
  const selected = await getSelectedShapeText();
  if (!selected || !selected.text.trim()) {
    appendError('Select a text shape first, then use /rewrite.');
    return;
  }
  const instruction = commandArgs.trim() || 'Improve clarity and impact';
  const prompt = `Rewrite the following slide text with these instructions: ${instruction}\n\nOriginal text:\n${selected.text}\n\n` +
    `Use FORGE KB context to ground the rewrite. Return ONLY the rewritten text — no preamble, no explanation.`;
  
  const response = await streamFaitChat(prompt, authHeader);
  // Show in ShapePreview for Accept/Reject
  setPendingShape({ shapeId: selected.shapeId, text: response });
  break;
}
```

`ShapePreview.tsx` is already built (S1) — no changes needed. Accept writes via `applyTextToShape()`.

---

## Files Changed Summary

### New Files

| File | Purpose |
|------|---------|
| `src/taskpane/services/pptBindings.ts` | Binding registration, write-to-binding, list bindings |
| `src/taskpane/services/chartImageService.ts` | Server-side chart image fetch (replaces direct Chart.js) |

### Modified Files (FfP Taskpane)

| File | Change |
|------|--------|
| `src/taskpane/services/pptWriter.ts` | `applyTextToShape()` registers binding after write; `insertChartImage()` tries server path first |
| `src/taskpane/services/pptTableWriter.ts` | Add `position` support to `insertTable()` via `TableAddOptions` |
| `src/taskpane/services/pptSpecParser.ts` | Parse `position` field from `table_spec` block |
| `src/taskpane/services/pptReader.ts` | Add `getSelectedShapeText()` |
| `src/taskpane/components/SlashCommandPicker.tsx` | Add `/rewrite` command |
| `src/taskpane/components/ChatPanel.tsx` | Handle `/rewrite` case |

### Modified Files (FAIT Backend)

| File | Change |
|------|--------|
| `Controllers/ExcelAddinController.cs` (or new `ChartController.cs`) | Add `POST /api/excel/chart-image` |
| `Services/ChartRendererService.cs` | **New** — SkiaSharp chart rendering |
| `FortressAI.Web.csproj` | Add `SkiaSharp` if not already present (check existing deps first) |

**Total: 2 new files (FfP) + 6 modified (FfP) + 1–2 new (FAIT backend). No manifest version bump (stays at 1.8).**

---

## Acceptance Criteria

1. **Binding persistence:** Apply text to a shape via `/improve`. Close and reopen the presentation. `/rewrite` can still target the same shape via binding (not by numeric ID).

2. **Positioned table:** Send a `table_spec` with `"position": {"left": 50, "top": 200, "width": 860}`. Table appears at that position, not at PowerPoint's default position.

3. **Server-side chart:** Send a `/chart` command. The chart image is rendered server-side (verify via Network tab — `POST /api/excel/chart-image` appears). Image quality is noticeably sharper than the S3 Chart.js render (compare at 200% zoom).

4. **Chart fallback:** Take the FAIT API offline (block the request in DevTools). `/chart` still produces a chart using the Chart.js fallback. No error shown.

5. **`/rewrite`:** Select a text shape, type `/rewrite make more concise`. `ShapePreview` shows the rewritten text. Accept applies it. Reject restores the original.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify pptBindings.ts uses getItemOrNullObject before attempting
          to delete an existing binding. Calling delete() on a non-existent
          binding throws, which will surface as an unhandled promise rejection
          in the binding registration path.

⚠️  HIGH: Verify applyTextToShape() binding registration is wrapped in
          try/catch and is non-fatal. The primary write must succeed even if
          the binding registration fails. The binding is an enhancement —
          a failure must not block the user's shape write.

⚠️  MEDIUM: Verify pptTableWriter.ts TableAddOptions position spread:
            ...(spec.position?.left !== undefined && { left: spec.position.left })
            The spread of a boolean false is a no-op — this pattern is correct
            but subtle. Confirm that undefined and 0 are treated correctly
            (0 is a valid left position — it should be included, not excluded).
            Use `spec.position?.left !== undefined` not `spec.position?.left`.

⚠️  MEDIUM: Verify server-side chart POST includes the auth header.
            chartImageService.ts calls getAuthHeader() — confirm it's awaited
            and the result is spread into the fetch headers. A missing auth
            header returns 401 and triggers the Chart.js fallback silently,
            which would hide the issue.

⚠️  LOW: Verify /rewrite strips FAIT's response before displaying in
         ShapePreview — no preamble, no markdown fences. The system prompt
         instructs FAIT to return "ONLY the rewritten text." Add a
         .trim() + stripMarkdownFences() pass on the response before
         setting pendingShape.text.
```

---

_Spec by Reed Richards | FfP S4: 2 new files + 8 modified (FfP + FAIT). Bindings for stable shape addressing; positioned tables; server-side chart PNG via SkiaSharp; `/rewrite` command. No manifest bump._
