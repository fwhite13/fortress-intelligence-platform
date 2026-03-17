# Review Brief: WI829 — FfP Sprint 2
# Hawkeye Code Review — Cycle 1

You are a senior TypeScript/Office.js code reviewer. Review the following files from the WI829 commit in the `fait-for-powerpoint` repo.

## Files to Review

### pptWriter.ts — applyTextToShape + tags.add + PptNotesError

```typescript
/* global PowerPoint */

declare const PowerPoint: any;

export class PptWriteError extends Error {
  constructor(
    message: string,
    public readonly code: 'SHAPE_NOT_FOUND' | 'NO_TEXT_FRAME' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptWriteError';
  }
}

export async function applyTextToShape(shapeId: string, text: string, nodeId?: string): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items/id');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptWriteError('No slide selected', 'SHAPE_NOT_FOUND');
    }

    const slide = selectedSlides.items[0];
    const shapes = slide.shapes;
    shapes.load('items/id');
    await ctx.sync();

    const target = (shapes.items as any[]).find((s: any) => s.id === shapeId);
    if (!target) {
      throw new PptWriteError(`Shape ${shapeId} not found on active slide`, 'SHAPE_NOT_FOUND');
    }

    target.load('textFrame/hasText');
    await ctx.sync();

    if (!target.textFrame.hasText) {
      throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
    }

    target.textFrame.textRange.text = text;
    await ctx.sync();

    // Source tagging — inside same PowerPoint.run() as the write
    if (nodeId) {
      try {
        target.tags.add('FAIT_SOURCE', nodeId);
        await ctx.sync();
      } catch {
        // Tagging failure is non-fatal — write already succeeded
      }
    }
  }).catch((e: any) => {
    if (e instanceof PptWriteError) throw e;
    throw new PptWriteError(
      e?.message ?? 'PowerPoint write failed',
      'PPT_ERROR'
    );
  });
}

export class PptNotesError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'NOTES_UNAVAILABLE' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptNotesError';
  }
}

export async function writeNotes(notesText: string): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptNotesError('No slide selected', 'NO_SLIDE');
    }

    const slide = selectedSlides.items[0];
    slide.load('notes');
    await ctx.sync();

    if (!slide.notes) {
      throw new PptNotesError('Notes API unavailable on this slide', 'NOTES_UNAVAILABLE');
    }

    slide.notes.textFrame.textRange.text = notesText;
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptNotesError) throw e;
    throw new PptNotesError(e?.message ?? 'Notes write failed', 'PPT_ERROR');
  });
}

export async function tagShape(
  shapeId: string,
  tagKey: string,
  tagValue: string
): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items/id');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) return;

    const slide = selectedSlides.items[0];
    const shapes = slide.shapes;
    shapes.load('items/id');
    await ctx.sync();

    const target = (shapes.items as any[]).find((s: any) => s.id === shapeId);
    if (!target) return;

    target.tags.add(tagKey, tagValue);
    await ctx.sync();
  }).catch(() => {
    // tagShape failure is always non-fatal
  });
}
```

### pptReader.ts — getAllSlidesContext + getSlideNotes

Key sections:
```typescript
const MAX_SLIDES = 20;
const MAX_SHAPES = 3;
const SHAPE_TEXT_CAP = 150;

export async function getAllSlidesContext(): Promise<SlideSnapshot[]> {
  return PowerPoint.run(async (ctx: any) => {
    const allSlides = ctx.presentation.slides;
    allSlides.load([
      'items/shapes/items/id',
      'items/shapes/items/name',
      'items/shapes/items/type',
      'items/shapes/items/textFrame/textRange/text',
    ]);
    await ctx.sync();

    const snapshots: SlideSnapshot[] = [];
    const slideItems = allSlides.items as any[];

    for (let i = 0; i < Math.min(slideItems.length, MAX_SLIDES); i++) {
      const slide = slideItems[i];
      const shapeItems = (slide.shapes?.items ?? []) as any[];

      let title = '';
      const shapes: SlideSnapshot['shapes'] = [];

      for (const shape of shapeItems) {
        const text: string = shape.textFrame?.textRange?.text ?? '';
        if (!text.trim()) continue;

        const shapeName: string = (shape.name ?? '').toLowerCase();
        if (!title && (shapeName.includes('title') || shape.type === 'title')) {
          title = text;
        }

        shapes.push({
          name: shape.name ?? '',
          text: text.length > SHAPE_TEXT_CAP ? text.slice(0, SHAPE_TEXT_CAP) + '…' : text,
        });

        if (shapes.length >= MAX_SHAPES) break;
      }
      // ...
    }
  });
}

export async function getSlideNotes(): Promise<string> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) return '';

    const slide = selectedSlides.items[0];
    slide.load('notes');
    await ctx.sync();

    if (!slide.notes) return '';

    const notesRange = slide.notes.textFrame.textRange;
    notesRange.load('text');
    await ctx.sync();

    return notesRange.text ?? '';
  }).catch((): string => '');
}
```

### pptNotesParser.ts

```typescript
export function parseNotesSpec(content: string): PptNotesSpec | null {
  const match = content.match(/```ppt_notes_spec\s*([\s\S]*?)```/);
  // ...
}

export function stripNotesSpec(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}

export function stripAllSpecs(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}
```

### ChatPanel.tsx — Key detection sections

```typescript
import { parseNotesSpec, stripAllSpecs } from '../services/pptNotesParser';
// ...

const handleSend = async (text: string) => {
  // ...
  const isNotesCommand = text.includes('ppt_notes_spec block');
  if (isNotesCommand) {
    // inject existing notes for rewrite context
  }
  // ...
};

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

// Strip ppt_notes_spec blocks from display
const displayMessages = messages.map((msg) =>
  msg.role === 'assistant'
    ? { ...msg, content: stripAllSpecs(msg.content) }
    : msg
);
```

### KbResultPanel.tsx — Key props

```typescript
interface KbResultCardProps {
  result: KbResult;
  index: number;
  onInsertToChat?: (content: string) => void;
  onApplyToShape?: (content: string, source: string) => void;
  selectedShapeId?: string | null;
}
// "Apply to Shape" button renders only when: onApplyToShape && selectedShapeId
```

### faitApi.ts — searchKb function

```typescript
export async function searchKb(
  query: string,
  apiKey: string,
  projectId?: string,
  kbTypes?: string[]
): Promise<KbSearchResponse> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
    },
    body: JSON.stringify({
      query,
      projectId: projectId ?? null,
      kbTypes: kbTypes ?? undefined,
    }),
  });
  // ...
}
```

### Manifests — both show:
`<Set Name="PowerPointApi" MinVersion="1.6"/>`

---

## Review Checklist

For each item below, answer: PASS or FAIL, with a brief explanation.

1. **tags.add() inside applyTextToShape() PowerPoint.run() block** — Is `target.tags.add('FAIT_SOURCE', nodeId)` called INSIDE the same `PowerPoint.run()` callback that writes the text? (It should be, not in a separate run or external function call.)

2. **No external tagShape() called from applyTextToShape()** — Does `applyTextToShape()` call the standalone `tagShape()` utility function? (It should NOT — proxy objects die after PowerPoint.run() completes.)

3. **isNotesCommand detection** — When a user sends the `/notes` prompt text, does `handleSend` detect it with `text.includes('ppt_notes_spec block')`? Is this detection used to inject existing notes context?

4. **stripAllSpecs() used (not stripNotesSpec())** — Does ChatPanel import and use `stripAllSpecs()` for display stripping? Is `stripNotesSpec()` NOT used for display?

5. **PowerPointApi MinVersion="1.6" both manifests** — Confirmed in both `public/manifest.xml` and `manifest.local.xml`?

6. **MAX_SLIDES=20, MAX_SHAPES=3, SHAPE_TEXT_CAP=150** — Are all three constants present AND applied in the loop?

7. **getSlideNotes() loads deep path before sync** — Is `notesRange.load('text')` called BEFORE `ctx.sync()`? Is this a 2-stage approach (load slide+notes, then load text range)?

8. **writeNotes() throws PptNotesError** — Is `PptNotesError` class defined with codes `NO_SLIDE`, `NOTES_UNAVAILABLE`, `PPT_ERROR`? Does writeNotes() use it?

9. **parseNotesSpec() regex matches ppt_notes_spec blocks** — Does the regex `/\`\`\`ppt_notes_spec\s*([\s\S]*?)\`\`\`/` correctly match backtick-fenced ppt_notes_spec blocks?

10. **KbResultPanel has onInsertToChat + onApplyToShape** — Both callbacks present? Apply button conditionally renders on `onApplyToShape && selectedShapeId`?

11. **searchKb() in faitApi.ts** — Is `searchKb()` function present and exported?

12. **FfE repo untouched** — Confirmed no modifications to fait-for-excel repo.

---

Also check for any additional issues: bugs, type safety problems, edge cases, or architectural concerns.

Provide a final verdict: PASS, NEEDS-CHANGES, or FAIL.
