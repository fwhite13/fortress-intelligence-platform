# FfP Sprint 5 Spec — Multi-Slide Deck Generation + Presentation Export + `/deck` Command

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Repo:** `fip/fait-for-powerpoint/`  
**Depends on:** FfP S1–S4 fully deployed  
**PresApi baseline:** 1.8 (unchanged)

---

## Pre-Read: What S4 Gives Us

After S4: FfP can write text to individual shapes (with stable bindings), insert positioned tables, insert server-side chart images, and tag shapes for source tracking. All S1-S4 features operate on the **current slide**.

Sprint 5 is the step change: **multi-slide deck generation from a single prompt**. The user asks FAIT to generate a full section of a presentation (3–8 slides) and FfP populates each slide in sequence using the existing slide template structure. This is the feature that justifies FfP over manual copy-paste from FAIT chat.

---

## Sprint 5 Objectives

| # | Feature | Complexity |
|---|---------|------------|
| 1 | `/deck` slash command — multi-slide plan from FAIT, user reviews, FfP executes | Large |
| 2 | `deckWriter.ts` — iterate over slides, match shapes by type hint, apply content sequentially | Medium |
| 3 | Presentation scan enhancement — scan all slides for placeholder shapes (title, content, subtitle) | Small |
| 4 | `getFileAsync` export — export presentation as base64 PPTX for download or FAIT analysis | Small |

---

## Feature 1: `/deck` Command

### UX Flow

1. User types `/deck Executive summary of Q1 performance for the IC presentation`
2. FAIT reads the current slide as context (slide title, existing shapes)
3. FAIT returns a `deck_plan_spec` JSON block (analogous to `table_spec` / `ppt_notes_spec`)
4. `DeckPlanPreview.tsx` renders the plan: slide list with titles + content bullets for each slide
5. User reviews → "Generate" button → `deckWriter.ts` executes slide by slide
6. Progress shown in the step feed: "Populating slide 2 of 5…"

### `deck_plan_spec` JSON Block Format

```json
```deck_plan_spec
{
  "slideCount": 4,
  "startAfterCurrent": true,
  "slides": [
    {
      "index": 1,
      "title": "Q1 Performance Overview",
      "shapes": [
        {"hint": "title", "text": "Q1 2026 Performance Overview"},
        {"hint": "subtitle", "text": "Fortress Core Equity Fund"},
        {"hint": "content", "text": "• Net return: +2.4% vs benchmark +1.82%\n• AUM: $2.1B (up from $1.9B)\n• Sharpe ratio: 1.34"}
      ]
    },
    {
      "index": 2,
      "title": "Attribution Analysis",
      "shapes": [
        {"hint": "title", "text": "Attribution Analysis"},
        {"hint": "content", "text": "• Technology overweight: +0.8% contribution\n• Energy underweight: +0.4%\n• Currency hedge: –0.1%"}
      ]
    }
  ]
}
```
```

**`deck_plan_spec` schema:**

| Field | Type | Description |
|-------|------|-------------|
| `slideCount` | number | Total slides in the plan |
| `startAfterCurrent` | boolean | Insert new slides after the current slide (true) or at end (false) |
| `slides[].index` | number | 1-based slide order in the plan |
| `slides[].title` | string | Used to find or create the slide title shape |
| `slides[].shapes` | array | Content assignments per shape on the slide |
| `shapes[].hint` | string | `"title"` \| `"subtitle"` \| `"content"` \| `"caption"` — guides shape matching |
| `shapes[].text` | string | Text to write to the matched shape |

### Shape Matching Strategy

FfP maps `hint` values to shapes on the current slide layout by inspecting shape placeholder types:

```typescript
// From pptReader.ts — placeholder type IDs
const HINT_MAP: Record<string, number[]> = {
  title:    [1, 13],   // Title, CenteredTitle
  subtitle: [2, 12],   // Subtitle, VerticalTitle  
  content:  [7, 15],   // Body, Object
  caption:  [14, 18],  // Caption, SubTitle
};
```

For each `shapes[]` entry with a given `hint`, `deckWriter.ts` finds the first shape on the slide whose `placeholderFormat.type` matches any of the hint's type IDs. If no placeholder matches, it falls back to the shape with the largest text frame area (heuristic: biggest shape = main content area).

### Template Slide Creation

`startAfterCurrent: true` means FfP duplicates the current slide's template N times (one per `slideCount`) before writing content. This preserves the master/layout formatting.

**Slide duplication workaround** — PowerPoint JS API has no native `slide.duplicate()`. The workaround (from the architecture spec) is:

1. `getFileAsync(Office.FileType.Compressed)` — export the entire presentation as a base64 PPTX
2. Extract just the target slide's XML from the PPTX zip (using a lightweight zip library)
3. `insertSlidesFromBase64(singleSlide PPTX, { targetSlide, formatting: keepSourceFormatting })`

This is the same `insertSlidesFromBase64` call used in S3's template inject feature.

**Alternative (simpler):** Instead of duplicating the current slide, require the user to have pre-created the template slides before running `/deck`. The `/deck` command then populates existing empty slides in sequence, starting at `slideIndex + 1`.

**Decision: Use the pre-created approach for Sprint 5.** Slide duplication via `getFileAsync` round-trip is complex and slow for large presentations. Sprint 6 can add auto-duplication if users request it. For Sprint 5: if the presentation doesn't have enough blank slides after the current one, `deckWriter.ts` shows a warning: "Need N more slides after slide X — add them first."

---

## Feature 2: `deckWriter.ts`

```typescript
// src/taskpane/services/deckWriter.ts
import { DeckPlanSpec, DeckSlide } from './pptSpecParser';
import { applyTextToShape } from './pptWriter';

export interface DeckWriteProgress {
  slideIndex: number;
  totalSlides: number;
  status: 'writing' | 'done' | 'error';
  message: string;
}

export type ProgressCallback = (progress: DeckWriteProgress) => void;

/**
 * Execute a deck plan spec: write content to N slides in sequence.
 * Each slide in the plan maps to the next slide after the anchor slide.
 */
export async function executeDeckPlan(
  spec: DeckPlanSpec,
  anchorSlideIndex: number,   // 0-based index of the current slide
  onProgress: ProgressCallback
): Promise<void> {
  for (let i = 0; i < spec.slides.length; i++) {
    const slideSpec = spec.slides[i];
    const targetSlideIndex = anchorSlideIndex + i + (spec.startAfterCurrent ? 1 : 0);

    onProgress({
      slideIndex: i + 1,
      totalSlides: spec.slides.length,
      status: 'writing',
      message: `Populating slide ${i + 1} of ${spec.slides.length}: "${slideSpec.title}"`,
    });

    try {
      await writeSlide(targetSlideIndex, slideSpec);
    } catch (err: any) {
      onProgress({
        slideIndex: i + 1,
        totalSlides: spec.slides.length,
        status: 'error',
        message: `Slide ${i + 1} failed: ${err.message}`,
      });
      // Continue with remaining slides — partial output is better than none
    }
  }

  onProgress({
    slideIndex: spec.slides.length,
    totalSlides: spec.slides.length,
    status: 'done',
    message: `Deck generation complete — ${spec.slides.length} slides populated.`,
  });
}

async function writeSlide(slideIndex: number, slideSpec: DeckSlide): Promise<void> {
  await PowerPoint.run(async (ctx) => {
    const slides = ctx.presentation.slides;
    slides.load('items');
    await ctx.sync();

    if (slideIndex >= slides.items.length) {
      throw new Error(`Slide ${slideIndex + 1} doesn't exist. Add more slides to your presentation.`);
    }

    const slide = slides.items[slideIndex];
    const shapes = slide.shapes;
    shapes.load('items');
    await ctx.sync();

    for (const item of shapes.items) {
      item.load('id,name,placeholderFormat,textFrame');
    }
    await ctx.sync();

    // Match each spec shape to a presentation shape
    for (const shapeSpec of slideSpec.shapes) {
      const matched = matchShape(shapes.items, shapeSpec.hint);
      if (!matched) continue;

      matched.textFrame.textRange.text = shapeSpec.text;
      // Tag shape with source info
      matched.tags.add('FAIT_DECK', '1');
    }

    await ctx.sync();
  });
}

function matchShape(
  shapes: PowerPoint.Shape[],
  hint: string
): PowerPoint.Shape | null {
  const HINT_MAP: Record<string, number[]> = {
    title:    [1, 13],
    subtitle: [2, 12],
    content:  [7, 15],
    caption:  [14, 18],
  };
  const targetTypes = HINT_MAP[hint] ?? [];

  // Try placeholder type match first
  for (const shape of shapes) {
    const ph = shape.placeholderFormat;
    if (ph && targetTypes.includes((ph as any).type)) return shape;
  }

  // Fallback: name-based match (common PowerPoint naming conventions)
  const nameLower = hint.toLowerCase();
  for (const shape of shapes) {
    if (shape.name.toLowerCase().includes(nameLower)) return shape;
  }

  return null;
}
```

### New `pptSpecParser.ts` additions

Add parsing for `deck_plan_spec` block (same pattern as `table_spec`, `ppt_notes_spec`):

```typescript
export interface DeckSlideShape {
  hint: string;
  text: string;
}

export interface DeckSlide {
  index: number;
  title: string;
  shapes: DeckSlideShape[];
}

export interface DeckPlanSpec {
  slideCount: number;
  startAfterCurrent: boolean;
  slides: DeckSlide[];
}

export function parseDeckPlanSpec(response: string): DeckPlanSpec | null {
  const match = response.match(/```deck_plan_spec\s*([\s\S]*?)```/);
  if (!match) return null;
  try {
    return JSON.parse(match[1].trim()) as DeckPlanSpec;
  } catch { return null; }
}

export function stripDeckPlanSpec(response: string): string {
  return response.replace(/```deck_plan_spec\s*[\s\S]*?```/g, '').trim();
}
```

---

## Feature 3: Enhanced Presentation Scan

S2's `getAllSlidesContext()` capped at 20 slides, 3 shapes/slide, 150 chars/shape — optimized for token budget. Sprint 5 needs a separate **structural scan** that reads shape types and placeholder formats (not full text) to support `deckWriter.ts` shape matching.

**`pptReader.ts` — add `getSlidePlaceholderMap()`:**

```typescript
export interface SlidePlaceholderMap {
  slideIndex: number;
  slideCount: number;
  shapes: Array<{
    id: number;
    name: string;
    placeholderType: number | null;  // null = not a placeholder
    hasText: boolean;
    textPreview: string;  // first 60 chars
  }>;
}

export async function getSlidePlaceholderMap(slideIndex: number): Promise<SlidePlaceholderMap> {
  let result: SlidePlaceholderMap = { slideIndex, slideCount: 0, shapes: [] };

  await PowerPoint.run(async (ctx) => {
    const slides = ctx.presentation.slides;
    slides.load('items');
    await ctx.sync();
    result.slideCount = slides.items.length;

    if (slideIndex >= slides.items.length) return;
    const slide = slides.items[slideIndex];
    slide.shapes.load('items');
    await ctx.sync();

    for (const shape of slide.shapes.items) {
      shape.load('id,name,placeholderFormat,textFrame');
    }
    await ctx.sync();

    for (const shape of slide.shapes.items) {
      let placeholderType: number | null = null;
      let hasText = false;
      let textPreview = '';
      try {
        placeholderType = (shape.placeholderFormat as any)?.type ?? null;
        shape.textFrame.textRange.load('text');
        await ctx.sync();
        const text = shape.textFrame.textRange.text ?? '';
        hasText = text.trim().length > 0;
        textPreview = text.slice(0, 60);
      } catch { /* Shape may not have a text frame */ }

      result.shapes.push({
        id: shape.id,
        name: shape.name,
        placeholderType,
        hasText,
        textPreview,
      });
    }
  });

  return result;
}
```

This map is injected into the `/deck` prompt context so FAIT knows the exact slide structure before generating the `deck_plan_spec`.

---

## Feature 4: `getFileAsync` Export

Allow the user to download the current presentation as a `.pptx` file directly from the taskpane. This works via the Common API `getFileAsync` + a Blob download trigger.

**New component: `ExportButton.tsx`**

```tsx
// Small button in the ChatPanel header (alongside the settings icon)
const ExportButton: React.FC = () => {
  const [exporting, setExporting] = useState(false);

  const handleExport = async () => {
    setExporting(true);
    try {
      await exportPresentation();
    } catch (e: any) {
      console.error('Export failed:', e);
    } finally {
      setExporting(false);
    }
  };

  return (
    <button
      onClick={handleExport}
      disabled={exporting}
      title="Download presentation"
      style={{ /* consistent with existing header button styles */ }}>
      {exporting ? '…' : '⬇'}
    </button>
  );
};
```

**`pptExport.ts` (new service):**

```typescript
export async function exportPresentation(): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const Office = (window as any).Office;
    Office.context.document.getFileAsync(
      Office.FileType.Compressed,  // .pptx format
      { sliceSize: 65536 },         // 64KB slices
      async (result: any) => {
        if (result.status === Office.AsyncResultStatus.Failed) {
          reject(new Error(result.error.message));
          return;
        }

        const file = result.value;
        const sliceCount = file.sliceCount;
        const slices: Uint8Array[] = [];

        for (let i = 0; i < sliceCount; i++) {
          await new Promise<void>((res, rej) => {
            file.getSliceAsync(i, (sliceResult: any) => {
              if (sliceResult.status === Office.AsyncResultStatus.Failed) {
                rej(new Error(sliceResult.error.message));
              } else {
                slices.push(new Uint8Array(sliceResult.value.data));
                res();
              }
            });
          });
        }

        file.closeAsync(() => {});

        // Combine slices and trigger download
        const total = slices.reduce((acc, s) => acc + s.length, 0);
        const combined = new Uint8Array(total);
        let offset = 0;
        for (const s of slices) { combined.set(s, offset); offset += s.length; }

        const blob = new Blob([combined], {
          type: 'application/vnd.openxmlformats-officedocument.presentationml.presentation'
        });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `presentation-${new Date().toISOString().slice(0, 10)}.pptx`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        resolve();
      }
    );
  });
}
```

---

## New Component: `DeckPlanPreview.tsx`

Renders the `deck_plan_spec` plan for user review before execution. Pattern identical to `TablePreview.tsx` and `NotesPreview.tsx`.

```tsx
interface DeckPlanPreviewProps {
  spec: DeckPlanSpec;
  onAccept: () => void;
  onReject: () => void;
  isGenerating: boolean;
}

const DeckPlanPreview: React.FC<DeckPlanPreviewProps> = ({ spec, onAccept, onReject, isGenerating }) => (
  <div style={{ /* card style matching ShapePreview/TablePreview */ }}>
    <div style={{ fontWeight: 600, marginBottom: 8 }}>
      Deck plan — {spec.slideCount} slide{spec.slideCount !== 1 ? 's' : ''}
    </div>
    {spec.slides.map(slide => (
      <div key={slide.index} style={{ marginBottom: 12 }}>
        <div style={{ fontWeight: 500, fontSize: 13 }}>
          Slide {slide.index}: {slide.title}
        </div>
        {slide.shapes.map((s, i) => (
          <div key={i} style={{ fontSize: 12, color: '#8899aa', marginLeft: 12 }}>
            [{s.hint}] {s.text.slice(0, 80)}{s.text.length > 80 ? '…' : ''}
          </div>
        ))}
      </div>
    ))}
    <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
      <button onClick={onAccept} disabled={isGenerating} style={{ /* gold button */ }}>
        {isGenerating ? 'Generating…' : 'Generate Deck'}
      </button>
      <button onClick={onReject} disabled={isGenerating} style={{ /* outline button */ }}>
        Cancel
      </button>
    </div>
  </div>
);
```

---

## `ChatPanel.tsx` — `/deck` Handler

```typescript
case '/deck': {
  const slideMap = await getSlidePlaceholderMap(currentSlideIndex);
  const prompt = buildDeckPrompt(commandArgs, slideMap);
  const response = await streamFaitChat(prompt, authHeader);

  const deckPlan = parseDeckPlanSpec(response);
  if (!deckPlan) {
    appendMessage({ role: 'assistant', content: response });
    return;
  }

  const strippedResponse = stripDeckPlanSpec(response);
  if (strippedResponse) appendMessage({ role: 'assistant', content: strippedResponse });

  setPendingDeckPlan(deckPlan);
  break;
}
```

When user clicks "Generate Deck" in `DeckPlanPreview`:

```typescript
const handleDeckGenerate = async () => {
  if (!pendingDeckPlan) return;
  setIsDeckGenerating(true);
  await executeDeckPlan(pendingDeckPlan, currentSlideIndex, (progress) => {
    // Show progress in step feed
    appendMessage({ role: 'system', content: progress.message });
  });
  setPendingDeckPlan(null);
  setIsDeckGenerating(false);
};
```

---

## FAIT System Prompt Addition

Add to the FfP system prompt in `runner.ts` (or `ChatPanel.tsx` static prefix):

```
For /deck commands: return a deck_plan_spec JSON block followed by a brief explanation.
The spec must match the slide structure I report (placeholder types, shape count).
Use hint values: "title" (placeholder type 1/13), "content" (7/15), "subtitle" (2/12).
If a slide has no matching placeholder, use the shape with the largest text area.
```

---

## Files Changed Summary

### New Files

| File | Purpose |
|------|---------|
| `src/taskpane/services/deckWriter.ts` | Slide-by-slide content execution |
| `src/taskpane/services/pptExport.ts` | `getFileAsync` download helper |
| `src/taskpane/components/DeckPlanPreview.tsx` | Plan review UI |
| `src/taskpane/components/ExportButton.tsx` | Download .pptx button |

### Modified Files

| File | Change |
|------|--------|
| `src/taskpane/services/pptSpecParser.ts` | Add `parseDeckPlanSpec`, `stripDeckPlanSpec`, `DeckPlanSpec` types |
| `src/taskpane/services/pptReader.ts` | Add `getSlidePlaceholderMap()` |
| `src/taskpane/components/SlashCommandPicker.tsx` | Add `/deck` command |
| `src/taskpane/components/ChatPanel.tsx` | Handle `/deck`; `pendingDeckPlan` state; progress feed; ExportButton in header |

**Total: 4 new files + 4 modified. No manifest bump. No backend changes.**

---

## Acceptance Criteria

1. **`/deck` generates a plan:** Type `/deck Create 3 slides covering Q1 performance, attribution, and outlook`. FAIT returns a `deck_plan_spec`. `DeckPlanPreview` shows the plan with slide titles and shape assignments.

2. **Deck execution writes to slides:** Click "Generate Deck". The next 3 slides after the current one are populated with the planned content. Progress messages appear: "Populating slide 1 of 3: Q1 Performance…"

3. **Missing slides warning:** If the presentation has only 1 slide and the plan needs 3, `deckWriter.ts` shows "Slide 2 doesn't exist. Add more slides to your presentation." on the second slide — but slide 1 still gets populated.

4. **Placeholder matching:** A slide with a title placeholder (type 1) gets the `hint: "title"` content. A content placeholder (type 7) gets the `hint: "content"` content.

5. **Export:** Click the export button (⬇). A `.pptx` file downloads with today's date in the filename.

6. **`/deck` cancel:** Click "Cancel" in `DeckPlanPreview`. No slides are written. The plan disappears.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify deckWriter.ts continues with remaining slides when one slide
          fails (try/catch per slide, not a top-level try/catch around the loop).
          A single shape-matching failure should not abort the entire deck
          generation — user gets partial output, not zero output.

⚠️  HIGH: Verify pptExport.ts closes the file handle via file.closeAsync()
          even on error. Unclosed Office file handles cause memory leaks and
          can lock the presentation. Wrap in try/finally.

⚠️  MEDIUM: Verify getSlidePlaceholderMap() handles shapes with no textFrame
            (e.g. images, SmartArt) without throwing. The try/catch per shape
            must be present. Loading 'textFrame' on a non-text shape throws
            in some Office versions.

⚠️  MEDIUM: Verify DeckPlanPreview's "Generate Deck" button is disabled while
            isGenerating is true. Without this, double-clicking generates the
            deck twice — same slides get written twice.

⚠️  LOW: Verify stripDeckPlanSpec regex uses lazy *? quantifier:
         /```deck_plan_spec\s*[\s\S]*?```/g
         Without the lazy modifier, a response with two fenced blocks will
         strip everything between the first opening fence and the last
         closing fence — removing unrelated content.
```

---

_Spec by Reed Richards | FfP S5: 4 new files + 4 modified. Multi-slide deck generation via `/deck` command with plan-review UI. `getFileAsync` export. No manifest bump._
