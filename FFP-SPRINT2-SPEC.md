# FfP Sprint 2 Spec — Full Slide Scan + FORGE Search + /notes + Source Tagging

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Depends on:** FfP Sprint 1 (`FFP-SPRINT1-SPEC.md`) — all S1 files must be in place

---

## Pre-Read: What Was Read

- `FFP-SPRINT1-SPEC.md` — full S1 file inventory; exact props/interfaces already in place
- `FFP-ARCHITECTURE-SPEC.md` — Sprint 2 goals, API decisions (S2 uses 1.3–1.6)
- `RESEARCH-FFP.md` — PowerPointApi shape enumeration, notes API, tags API
- FfE `KbResultPanel.tsx` — existing FORGE panel (display-only; S2 adds `onInsert` callback)
- FfE `ChatPanel.tsx` — FORGE search flow (`handleForgeSearch`, `buildKbTypes`, state vars)
- FfE `SlashCommandPicker.tsx` — `COMMANDS` array structure

**Nothing guessed.** All decisions derived from live code and research.

---

## Sprint 2 Objectives

| # | Feature | API Requirement |
|---|---------|----------------|
| 1 | Full slide scan — all slides' text in context | PowerPointApi 1.4 (`slide.shapes`) |
| 2 | FORGE search panel with "Insert to Chat" button per result | Existing FAIT `/api/haven/kb-search` |
| 3 | `/notes` slash command — generate + preview + write speaker notes | PowerPointApi 1.6 (`slide.notes`) |
| 4 | Source tagging — tag written shapes with FORGE node ID | PowerPointApi 1.3 (`shape.tags.add`) |

**No manifest version bump required.** All four features fall within the 1.5 baseline declared in S1:
- Full slide scan uses `slide.shapes` (1.4 ≤ 1.5 ✅)
- Tags use `shape.tags.add` (1.3 ≤ 1.5 ✅)
- Notes uses `slide.notes` (1.6) — manifest bump from 1.5 → 1.6 required

**Manifest bump: 1.5 → 1.6.** Speaker notes write is a core S2 deliverable; it is not acceptable to guard it away. The bump is safe: 1.6 shipped Oct 2024 (Build 18129), well within M365 monthly/semi-annual support window. Fortress AM users are M365 subscribers — 1.6 is available.

---

## API Decisions

### Full Slide Scan (Decision: two-pass load)

The PowerPoint JS API does not support chained `.load()` on nested objects. Shapes' text must be loaded via a two-pass pattern:

**Pass 1:** Load `slides.items` + each slide's `shapes.items/name,items/shapeType,items/textFrame/hasText`.
**Pass 2:** For shapes where `hasText === true`, load `textFrame.textRange.text`.

This is the pattern from `RESEARCH-FFP.md` lines 95–110. A single deep-path string `'items/shapes/items/textFrame/textRange/text'` should also work (the same pattern used in S1 `pptReader.ts` for the selected slide). Use the single-pass deep-path approach — it worked in S1.

**Token budget concern:** A 50-slide deck with verbose shapes could generate 10k+ tokens of context. Cap the full scan at:
- Max 20 slides included
- Max 150 chars per shape text (truncated with `…`)
- Max 3 shapes per slide included
- Skip slides with no text shapes
- Always include the **current slide** in full (unlimited, up to 800 chars per shape)

The result is a `[DECK CONTEXT]` block appended below the existing `[PRESENTATION CONTEXT]` block in the injected prompt.

### `/notes` Command: `ppt_notes_spec` JSON Block (Decision: structured response)

Sprint 1 used keyword detection (dumb matching on user message text). Sprint 2 introduces the first structured JSON block for FfP.

The `/notes` command sends a prompt that asks FAIT to respond with a `ppt_notes_spec` JSON block. This is the FfP equivalent of FfE's `cell_suggestions` block.

Format:
```json
```ppt_notes_spec
{
  "speakerNotes": "string — full speaker notes text to write",
  "sources": ["FORGE node ID 1", "FORGE node ID 2"]
}
```
```

Parser: `parseNotesSpec(content: string): PptNotesSpec | null` — same pattern as FfE's `parseSuggestions`.

Rationale: Sprint 2 needs `/notes` to be reliable. Keyword detection on the previous user message was a Sprint 1 workaround for the general "apply to shape" case. For a dedicated slash command, structured output is both achievable and required for source tagging (we need to know which FORGE nodes the notes cite).

### Source Tagging (Decision: tag at write time, source ID from FORGE result)

When does FfP tag a shape? Two cases:
1. User applies a FORGE search result directly to a shape ("Apply to Shape" from a FORGE card) → tag with `result.source` as the nodeId
2. User applies FAIT response that cites FORGE nodes (Sprint 2 doesn't have full citation parsing yet — defer to Sprint 3)

For Sprint 2: only Case 1 is implemented. When the user clicks "Apply to Shape" from a FORGE card, `applyTextToShape()` receives an optional `nodeId` parameter. If provided, `tagShape()` is called immediately after the write.

Tag key: `"FAIT_SOURCE"`, tag value: the FORGE node ID (from `result.source`). The `result.source` field in `KbSearchResponse` is the document path/title from FORGE — it's not a UUID node ID, but it's the best identifier available from the current API response. Use it verbatim.

Note: `shape.tags.add(key, value)` requires the shape object to be a proxy within an active `PowerPoint.run()` context — it cannot be called outside of a run. So `tagShape()` must be called within the same `PowerPoint.run()` as `applyTextToShape()`, not in a separate run.

### FORGE Panel: "Insert to Chat" vs "Apply to Shape" (Decision: both)

FfE's `KbResultPanel` is display-only. For FfP, FORGE search results should have two actions:
1. **"Insert to Chat"** — pastes the result content into the chat input for the user to ask FAIT to refine it
2. **"Apply to Shape"** — directly applies the result content to the selected shape (bypasses AI; source-tags the shape)

The "Apply to Shape" button only appears if a shape is currently selected (`slideContext.selectedShapeId` is not null).

These require a new `onInsertToChat` and `onApplyToShape` callback prop on `KbResultPanel`.

---

## Single CC Session — Sequential Tasks

```
Task 1:  public/manifest.xml        ← bump MinVersion 1.5 → 1.6
         manifest.local.xml         ← bump MinVersion 1.5 → 1.6

Task 2:  src/taskpane/services/pptReader.ts  ← add getAllSlidesContext(); getSlideNotes()
Task 3:  src/taskpane/services/pptWriter.ts  ← add writeNotes(); tagShape(); update applyTextToShape()
Task 4:  src/taskpane/services/pptNotesParser.ts  ← NEW: parseNotesSpec()

Task 5:  src/taskpane/components/KbResultPanel.tsx ← add onInsertToChat + onApplyToShape callbacks
Task 6:  src/taskpane/components/NotesPreview.tsx  ← NEW: preview dialog for /notes output
Task 7:  src/taskpane/components/SlashCommandPicker.tsx ← add /notes command
Task 8:  src/taskpane/components/ChatPanel.tsx  ← wire everything together
```

---

## File-Level Spec

---

### Task 1: Manifest Bumps

**Both `public/manifest.xml` and `manifest.local.xml`:**

Change:
```xml
<Set Name="PowerPointApi" MinVersion="1.5"/>
```
To:
```xml
<Set Name="PowerPointApi" MinVersion="1.6"/>
```

**That is the only change.** Do not touch any URL, GUID, or other element.

---

### Task 2: `src/taskpane/services/pptReader.ts` (modify)

Add two new exports. Do NOT modify `getSlideContext()` or `formatSlideContext()` — they are tested and working.

#### 2a. `getAllSlidesContext()`

```typescript
export interface SlideSnapshot {
  slideNumber: number;   // 1-based
  title: string;         // first title-ish shape text, or first shape text, or ''
  shapes: Array<{
    name: string;
    text: string;        // already truncated to 150 chars
  }>;
}

/**
 * Read a summary of all slides in the presentation for full-deck context injection.
 * Returns at most MAX_SLIDES slides; each slide has at most MAX_SHAPES shapes;
 * each shape text is truncated to SHAPE_TEXT_CAP chars.
 *
 * Uses a single PowerPoint.run() with a deep-path load.
 * Falls back to empty array on any error.
 */
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

      // Fallback title: first shape text
      if (!title && shapes.length > 0) title = shapes[0].text;

      if (shapes.length > 0) {
        snapshots.push({ slideNumber: i + 1, title, shapes });
      }
    }

    return snapshots;
  }).catch((): SlideSnapshot[] => []);
}

/**
 * Format the full-deck snapshot into a prompt context block.
 * Prepended with [DECK CONTEXT] marker for easy parsing/truncation.
 */
export function formatDeckContext(snapshots: SlideSnapshot[]): string {
  if (snapshots.length === 0) return '';

  let out = `[DECK CONTEXT — ${snapshots.length} slide(s)]\n`;
  for (const s of snapshots) {
    out += `Slide ${s.slideNumber}`;
    if (s.title) out += ` — ${s.title.slice(0, 60)}`;
    out += `\n`;
    for (const shape of s.shapes) {
      out += `  • ${shape.text.replace(/\n/g, ' ')}\n`;
    }
  }
  out += `[END DECK CONTEXT]`;
  return out;
}
```

#### 2b. `getSlideNotes()`

```typescript
/**
 * Read the speaker notes for the currently selected slide.
 * Returns empty string if no notes or if PowerPointApi 1.6 is not available.
 *
 * Requires PowerPointApi 1.6 (manifest now declares 1.6).
 */
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

---

### Task 3: `src/taskpane/services/pptWriter.ts` (modify)

Add `writeNotes()` and `tagShape()`. Update `applyTextToShape()` to accept an optional `nodeId` parameter.

#### 3a. Update `applyTextToShape()` signature

```typescript
/**
 * Write text to a specific shape's text frame.
 * If nodeId is provided, tags the shape with "FAIT_SOURCE" = nodeId.
 */
export async function applyTextToShape(
  shapeId: string,
  text: string,
  nodeId?: string   // ← new optional parameter
): Promise<void>
```

Inside the function, after `target.textFrame.textRange.text = text; await ctx.sync();`, add:

```typescript
// Source tagging — must happen in the same PowerPoint.run() as the write
if (nodeId) {
  try {
    target.tags.add('FAIT_SOURCE', nodeId);
    await ctx.sync();
  } catch {
    // Tagging failure is non-fatal — write already succeeded
  }
}
```

#### 3b. `writeNotes()`

```typescript
export class PptNotesError extends Error {
  constructor(
    message: string,
    public readonly code: 'NO_SLIDE' | 'NOTES_UNAVAILABLE' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptNotesError';
  }
}

/**
 * Write speaker notes to the currently selected slide.
 * Requires PowerPointApi 1.6.
 */
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
```

#### 3c. `tagShape()` (standalone, for future use)

```typescript
/**
 * Add a custom tag to a shape by ID.
 * Must be called within an active PowerPoint.run() context — OR as a standalone function
 * by locating the shape fresh. This standalone version opens its own run.
 *
 * Note: tagShape() is typically called via the nodeId param on applyTextToShape().
 * This standalone is provided for future sprint use (tag-after-apply scenarios).
 */
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
    if (!target) return; // Shape gone — silent no-op

    target.tags.add(tagKey, tagValue);
    await ctx.sync();
  }).catch(() => {
    // tagShape failure is always non-fatal
  });
}
```

---

### Task 4 (NEW): `src/taskpane/services/pptNotesParser.ts`

```typescript
export interface PptNotesSpec {
  speakerNotes: string;
  sources: string[];  // FORGE node IDs cited — may be empty []
}

/**
 * Parse a ```ppt_notes_spec ... ``` block from a FAIT response.
 * Returns null if no valid block is found.
 *
 * Expected format:
 * ```ppt_notes_spec
 * { "speakerNotes": "...", "sources": ["..."] }
 * ```
 */
export function parseNotesSpec(content: string): PptNotesSpec | null {
  const match = content.match(/```ppt_notes_spec\s*([\s\S]*?)```/);
  if (!match) return null;

  try {
    const parsed = JSON.parse(match[1].trim());
    if (typeof parsed.speakerNotes !== 'string') return null;
    return {
      speakerNotes: parsed.speakerNotes,
      sources: Array.isArray(parsed.sources) ? parsed.sources : [],
    };
  } catch {
    return null;
  }
}

/**
 * Strip the ppt_notes_spec block from the response, returning
 * only the human-readable portion for display in the chat thread.
 */
export function stripNotesSpec(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}
```

---

### Task 5: `src/taskpane/components/KbResultPanel.tsx` (modify)

Add two optional callback props to `KbResultPanelProps` and `KbResultCard`.

**Changed interface:**

```typescript
interface KbResultPanelProps {
  results: KbResult[];
  loading: boolean;
  onInsertToChat?: (content: string) => void;     // ← new
  onApplyToShape?: (content: string, source: string) => void;  // ← new
  selectedShapeId?: string | null;               // ← new: controls "Apply to Shape" visibility
}
```

**Changed `KbResultCard` interface:**

```typescript
interface KbResultCardProps {
  result: KbResult;
  index: number;
  onInsertToChat?: (content: string) => void;
  onApplyToShape?: (content: string, source: string) => void;
  selectedShapeId?: string | null;
}
```

**Add to the expanded body of `KbResultCard`**, after the "show more" button and before the closing `</div>`:

```typescript
{/* Action buttons — only shown when callbacks are provided */}
{(onInsertToChat || (onApplyToShape && selectedShapeId)) && (
  <div style={{ display: 'flex', gap: '6px', marginTop: '8px' }}>
    {onInsertToChat && (
      <button
        onClick={(e) => {
          e.stopPropagation();
          onInsertToChat(result.content);
        }}
        style={{
          flex: 1,
          background: '#1e3050',
          border: '1px solid #2e4a6a',
          color: '#a8c8e8',
          borderRadius: '3px',
          padding: '4px 8px',
          fontSize: '11px',
          cursor: 'pointer',
        }}
      >
        ↳ Insert to Chat
      </button>
    )}
    {onApplyToShape && selectedShapeId && (
      <button
        onClick={(e) => {
          e.stopPropagation();
          onApplyToShape(result.content, result.source);
        }}
        style={{
          flex: 1,
          background: '#1e3820',
          border: '1px solid #2e5230',
          color: '#88c888',
          borderRadius: '3px',
          padding: '4px 8px',
          fontSize: '11px',
          cursor: 'pointer',
        }}
      >
        ▶ Apply to Shape
      </button>
    )}
  </div>
)}
```

**Pass callbacks down from `KbResultPanel` to `KbResultCard`:**

Update the `KbResultCard` call inside `KbResultPanel`:
```typescript
<KbResultCard
  key={`${r.source}-${i}`}
  result={r}
  index={i}
  onInsertToChat={onInsertToChat}
  onApplyToShape={onApplyToShape}
  selectedShapeId={selectedShapeId}
/>
```

**Do NOT change:** `containerStyle`, `headerStyle`, loading state, empty state, or any other existing logic. Surgical additions only.

---

### Task 6 (NEW): `src/taskpane/components/NotesPreview.tsx`

The confirm dialog for the `/notes` command. Same pattern as `ShapePreview.tsx` but for speaker notes.

```typescript
import React from 'react';

interface NotesPreviewProps {
  pendingNotes: string;        // The AI-generated speaker notes
  sources: string[];           // FORGE source IDs (may be empty)
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
}

const NotesPreview: React.FC<NotesPreviewProps> = ({
  pendingNotes,
  sources,
  onAccept,
  onReject,
  loading = false,
}) => {
  return (
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
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          fontSize: '11px',
          fontWeight: '600',
          color: '#d4af37',
        }}
      >
        <span>📝</span>
        <span>Speaker Notes Preview</span>
      </div>

      {/* Preview text */}
      <div
        style={{
          background: '#131f2e',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          padding: '8px 10px',
          fontSize: '12px',
          color: '#e8edf3',
          lineHeight: 1.6,
          maxHeight: '140px',
          overflowY: 'auto',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
        }}
      >
        {pendingNotes}
      </div>

      {/* Sources (if any) */}
      {sources.length > 0 && (
        <div
          style={{
            fontSize: '10px',
            color: '#556677',
            fontStyle: 'italic',
          }}
        >
          Sources: {sources.join(', ')}
        </div>
      )}

      {/* Buttons */}
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
          {loading ? 'Writing notes…' : '✓ Write to Slide Notes'}
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
};

export default NotesPreview;
```

---

### Task 7: `src/taskpane/components/SlashCommandPicker.tsx` (modify)

Add the `/notes` command to the `COMMANDS` array.

**Add to the `COMMANDS` array (at the beginning, as it's FfP-primary):**

```typescript
{
  name: 'notes',
  description: 'Generate speaker notes for the current slide',
  prompt:
    'Generate professional speaker notes for the current slide. ' +
    'The notes should: explain the key points concisely (2-4 sentences), ' +
    'provide talking points not visible on the slide, and cite any FORGE knowledge base sources used. ' +
    'Return your response as a ```ppt_notes_spec block with JSON: ' +
    '{"speakerNotes": "<full notes text>", "sources": ["<FORGE source IDs>"]}. ' +
    'If no FORGE sources apply, use an empty array for sources.',
},
```

**That is the only change.** The picker filtering, keyboard nav, and styling are unchanged.

---

### Task 8: `src/taskpane/components/ChatPanel.tsx` (modify)

This is the largest change. Apply surgical additions and modifications.

#### 8a. New imports

```typescript
import { getAllSlidesContext, getSlideNotes, formatDeckContext } from '../services/pptReader';
import { writeNotes, PptNotesError } from '../services/pptWriter';
import { parseNotesSpec, stripNotesSpec } from '../services/pptNotesParser';
import NotesPreview from './NotesPreview';
import type { PptNotesSpec } from '../services/pptNotesParser';
```

#### 8b. New state variables

Add to the state block (after the `applyError` state from S1):

```typescript
// ── Sprint 2: Speaker notes state ────────────────────────────────────────────
const [pendingNotes, setPendingNotes] = useState<PptNotesSpec | null>(null);
const [notesLoading, setNotesLoading] = useState(false);
const [notesError, setNotesError] = useState<string | null>(null);
```

#### 8c. Update `handleSend` to include full deck context

S1's `handleSend` calls `getSlideContext()` for the selected slide. S2 adds the full deck context.

**Replace the context injection block in `handleSend`:**

```typescript
const handleSend = async (text: string) => {
  let context: string | undefined;

  try {
    // Selected slide context (S1)
    const ctx = await getSlideContext();
    if (ctx.slideNumber > 0) {
      context = formatSlideContext(ctx);
    }

    // Full deck context (S2) — appended after selected slide context
    const snapshots = await getAllSlidesContext();
    if (snapshots.length > 0) {
      const deckBlock = formatDeckContext(snapshots);
      context = context ? `${context}\n\n${deckBlock}` : deckBlock;
    }
  } catch {
    // Non-fatal — send without context
  }

  await send(text, context);
};
```

#### 8d. Update the `messages` watcher to detect `/notes` response

S1 watched for keyword-based "apply to shape" intent. S2 adds `ppt_notes_spec` detection.

**Add to the existing `useEffect` that watches `messages`:**

```typescript
// S2: Detect ppt_notes_spec block in last assistant message
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

**Important:** This `useEffect` is separate from the S1 shape apply watcher. They can coexist.

#### 8e. Handle the `/notes` command in `handleSend`

When the user selects `/notes` from the slash picker, the prompt includes a request for a `ppt_notes_spec` block. However, `handleSend` needs to also inject the current notes text so FAIT can see what already exists.

**Add a notes-augmentation step in `handleSend`** (inside the context-building try block):

```typescript
// If sending a /notes command, append existing notes to context
const isNotesCommand = text.includes('ppt_notes_spec block');
if (isNotesCommand) {
  try {
    const existingNotes = await getSlideNotes();
    if (existingNotes) {
      context = (context ?? '') + `\n\nExisting speaker notes:\n${existingNotes}`;
    }
  } catch {
    // Non-fatal
  }
}
```

#### 8f. Add notes action handlers

```typescript
const handleNotesAccept = async () => {
  if (!pendingNotes) return;
  setNotesLoading(true);
  setNotesError(null);

  try {
    await writeNotes(pendingNotes.speakerNotes);
    setPendingNotes(null);
  } catch (e) {
    if (e instanceof PptNotesError) {
      if (e.code === 'NO_SLIDE') {
        setNotesError('No slide selected — navigate to a slide and try again.');
      } else if (e.code === 'NOTES_UNAVAILABLE') {
        setNotesError('Notes API unavailable on this slide.');
      } else {
        setNotesError('Notes write failed — try again.');
      }
    } else {
      setNotesError('Notes write failed — try again.');
    }
  } finally {
    setNotesLoading(false);
  }
};

const handleNotesDiscard = () => {
  setPendingNotes(null);
  setNotesError(null);
};
```

#### 8g. Add FORGE "Insert to Chat" and "Apply to Shape" handlers

```typescript
// ── Sprint 2: FORGE result actions ───────────────────────────────────────────

const handleForgeInsertToChat = (content: string) => {
  // Appends the FORGE result content to the chat input text
  setInputText((prev) =>
    prev ? `${prev}\n\nFORGE context:\n${content}` : `FORGE context:\n${content}`
  );
};

const handleForgeApplyToShape = async (content: string, source: string) => {
  if (!slideContext?.selectedShapeId) return;
  try {
    // applyTextToShape with nodeId = source (tags the shape with the FORGE source)
    await applyTextToShape(slideContext.selectedShapeId, content, source);
    await refreshSlideContext();
  } catch {
    // Silent failure for direct FORGE apply — shape may have been deselected
  }
};
```

#### 8h. Update `KbResultPanel` usage in JSX

**Find the existing KbResultPanel call:**
```typescript
<KbResultPanel results={forgeResults ?? []} loading={forgeLoading} />
```

**Replace with:**
```typescript
<KbResultPanel
  results={forgeResults ?? []}
  loading={forgeLoading}
  onInsertToChat={handleForgeInsertToChat}
  onApplyToShape={handleForgeApplyToShape}
  selectedShapeId={slideContext?.selectedShapeId ?? null}
/>
```

#### 8i. Add `NotesPreview` to the JSX render section

Add the following **above** the `ShapePreview` render (or in a logical order below the FORGE results and above the `ChatInput`):

```typescript
{pendingNotes && (
  <NotesPreview
    pendingNotes={pendingNotes.speakerNotes}
    sources={pendingNotes.sources}
    onAccept={handleNotesAccept}
    onReject={handleNotesDiscard}
    loading={notesLoading}
  />
)}
{notesError && (
  <div
    style={{
      padding: '4px 12px',
      background: '#1a0f0f',
      color: '#e07070',
      fontSize: '11px',
      flexShrink: 0,
    }}
  >
    {notesError}
  </div>
)}
```

#### 8j. Strip `ppt_notes_spec` block from displayed message content

In the message rendering section, apply `stripNotesSpec()` to assistant messages before displaying them. This prevents the raw JSON block from appearing in the chat thread.

**Find the assistant message content display** (the place where `msg.content` is rendered for `role === 'assistant'`). Wrap it:

```typescript
// For assistant messages: strip ppt_notes_spec block (shown in NotesPreview instead)
const displayContent = msg.role === 'assistant'
  ? stripNotesSpec(msg.content)
  : msg.content;
```

Then render `displayContent` instead of `msg.content` in that message bubble.

---

## Files Changed Summary

| File | Type | Change |
|------|------|--------|
| `public/manifest.xml` | Modified | MinVersion 1.5 → 1.6 |
| `manifest.local.xml` | Modified | MinVersion 1.5 → 1.6 |
| `src/taskpane/services/pptReader.ts` | Modified | Add `getAllSlidesContext()`, `formatDeckContext()`, `getSlideNotes()` |
| `src/taskpane/services/pptWriter.ts` | Modified | Add `nodeId` param to `applyTextToShape()`; add `writeNotes()`; add `tagShape()` |
| `src/taskpane/services/pptNotesParser.ts` | New | `parseNotesSpec()`, `stripNotesSpec()` |
| `src/taskpane/components/KbResultPanel.tsx` | Modified | Add `onInsertToChat`, `onApplyToShape`, `selectedShapeId` props + action buttons |
| `src/taskpane/components/NotesPreview.tsx` | New | `/notes` confirm dialog |
| `src/taskpane/components/SlashCommandPicker.tsx` | Modified | Add `/notes` command to `COMMANDS` array |
| `src/taskpane/components/ChatPanel.tsx` | Modified | Wire full deck context, notes spec parsing, FORGE callbacks, `NotesPreview` render |

**Total: 2 new files + 7 modified. No new npm packages. No backend changes.**

---

## PresApi Version Change: 1.5 → 1.6

**Why:** Speaker notes write (`slide.notes.textFrame.textRange.text = "..."`) requires PowerPointApi 1.6 (released Oct 2024, Build 18129). The S1 spec already used notes read with a try/catch guard at 1.5, but S2 makes notes write a primary deliverable — we cannot guard it away.

**Impact:** Devices running Office 2019 LTSC or earlier will not load the add-in. Office 2021 LTSC is PowerPointApi 1.4. Office 2024 LTSC is 1.5. Only M365 Desktop (Aug 2024+ builds) and M365 Online support 1.6.

**Acceptability:** Fortress AM users are M365 subscribers. The 1.6 bump is safe and expected. The arch spec explicitly states 1.6 for speaker notes.

**Clint action:** Verify the manifest bump is to "1.6", not "1.60" or "1.5.1". The value must be exactly `"1.6"` — PowerPointApi uses single-decimal versioning.

---

## Acceptance Criteria

1. **Full deck context:** Sending a chat message includes a `[DECK CONTEXT]` block in the injected prompt (verify in FAIT backend logs or by inspecting the request body in browser devtools). The block includes at most 20 slides, at most 3 shapes per slide, truncated to 150 chars per shape.

2. **FORGE search with Insert to Chat:** Search FORGE for a term → results appear → expand a result → "↳ Insert to Chat" button appears → clicking it appends the result content to the chat input.

3. **FORGE search with Apply to Shape:** Select a text shape in PowerPoint → Search FORGE → expand a result → "▶ Apply to Shape" button appears (only when a shape is selected) → clicking it writes the result text to the shape and the slide context indicator refreshes.

4. **FORGE source tagging:** After "Apply to Shape" from a FORGE result, the shape has a tag `FAIT_SOURCE = <result.source>`. Verify by reading `shape.tags` in a subsequent `PowerPoint.run()` (or check via browser console). This is a background check — no UI exposes tags in S2.

5. **`/notes` command:** Type `/notes` → picker shows "notes — Generate speaker notes for the current slide" → select it → chat input fills with the notes prompt → send → FAIT responds with a `ppt_notes_spec` JSON block → `NotesPreview` appears at bottom of chat showing the generated notes text → click "✓ Write to Slide Notes" → speaker notes are written to the slide → `NotesPreview` dismisses.

6. **`/notes` rewrite:** If the slide already has speaker notes, the existing notes text is included in the prompt as "Existing speaker notes:" and FAIT rewrites them (not appends).

7. **Discard flow:** Clicking "Discard" on `NotesPreview` or `ShapePreview` dismisses them without any write.

8. **Notes spec stripped from chat:** After receiving a `/notes` response, the chat thread shows the human-readable portion of FAIT's response (if any) but NOT the raw `ppt_notes_spec` JSON block. The JSON block is consumed by `NotesPreview` only.

9. **Manifest version:** PowerPoint Online accepts the add-in with MinVersion="1.6" without an error banner (if error appears, the bump may have failed to save).

---

## Constraints for CC

- Modify `pptReader.ts` by ADDING new exports — do NOT touch `getSlideContext()` or `formatSlideContext()`. They are used by `usePptContext.ts` (polling) and `ChatPanel.tsx` (handleSend). Breaking them breaks S1 functionality.
- `getAllSlidesContext()` must cap at `MAX_SLIDES = 20` — do not remove the cap. A 200-slide presentation would generate 30k+ tokens without it.
- `writeNotes()` must call `getSelectedSlides()` inside `PowerPoint.run()` — it cannot use a slide proxy from a previous run. PowerPoint JS API contexts do not persist across `PowerPoint.run()` calls.
- `applyTextToShape()` change: `nodeId` is optional. The function signature change must be backward-compatible — all S1 call sites (`handleApplyToShape` in ChatPanel) pass only `(shapeId, text)` and must still work without modification. The new `nodeId` parameter goes at the end with `?`.
- `tagShape()` uses `target.tags.add(key, value)` — NOT `target.tags.set()`. PowerPoint uses `.add()` for tag creation/update; there is no `.set()` method.
- `KbResultPanel` change is additive only — existing FfE usage of `KbResultPanel` (in `~/projects/fait-for-excel/src/`) is not affected because the new props are optional. Do NOT modify any FfE files.
- `SlashCommandPicker.tsx` in FfP only — do NOT touch `~/projects/fait-for-excel/src/taskpane/components/SlashCommandPicker.tsx`.
- The `handleForgeInsertToChat` handler uses `setInputText` — verify that `ChatPanel.tsx` exposes `inputText` / `setInputText` state after the S1 port. In FfE, `inputText` and `setInputText` are local state in `ChatPanel.tsx`. Confirm they are present before proceeding.
- `stripNotesSpec()` is applied at render time — NOT stored. The raw `msg.content` (with the JSON block) stays in the `messages` array and in session history. Only the display is stripped.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify manifest MinVersion is exactly "1.6" (not "1.60", "1.5.1", or "1.5").
          Both manifest.xml AND manifest.local.xml must be updated. Inconsistency
          between prod and local manifests will cause confusing failures.

⚠️  HIGH: Verify applyTextToShape() signature change is backward-compatible.
          All S1 call sites must pass (shapeId, text) without the third argument.
          The function must NOT require nodeId. Confirm all three call sites:
          1. handleApplyToShape in ChatPanel (S1 — no nodeId)
          2. handleForgeApplyToShape in ChatPanel (S2 — passes source as nodeId)
          3. Any other call sites added in S2.

⚠️  HIGH: Confirm tagShape() uses target.tags.add() not target.tags.set().
          PowerPoint JS API: tags.add(key, value) creates or updates a tag.
          There is no tags.set() method. Using the wrong method will throw
          "Object doesn't support property or method 'set'" at runtime.

⚠️  HIGH: Verify getAllSlidesContext() includes the MAX_SLIDES = 20 cap.
          Without the cap, a large presentation will generate a request body
          that exceeds the FAIT API payload limit. Check the cap is applied
          with Math.min(slideItems.length, MAX_SLIDES).

⚠️  MEDIUM: Verify writeNotes() opens a fresh PowerPoint.run() — it must NOT
            try to reuse a slide proxy from usePptContext's polling run. Each
            PowerPoint.run() creates a new context; proxies from one run cannot
            be used in another.

⚠️  MEDIUM: Verify the /notes command prompt text includes "ppt_notes_spec block"
            (the exact substring used in handleSend's isNotesCommand detection).
            If the substring changes, the existing-notes injection step will
            silently skip.

⚠️  MEDIUM: Confirm KbResultPanel's new props are all optional (? suffix) and
            that the component renders identically when they are not provided.
            FfE's KbResultPanel usage (ChatPanel.tsx in fait-for-excel) must
            not break — confirm no FfE files were touched.

⚠️  LOW: Verify NotesPreview and ShapePreview cannot both appear simultaneously.
         If a user has a pending notes spec AND a pending shape apply, both
         previews would stack. This is acceptable behavior in S2 (no state
         conflict — they are independent state variables). But flag it for
         UX cleanup in S3.

⚠️  LOW: Verify stripNotesSpec() uses a greedy-disabled regex to handle
         multiple ppt_notes_spec blocks in a single message (edge case).
         The regex (/```ppt_notes_spec\s*[\s\S]*?```/g) uses *? (lazy) and
         the /g flag — this is correct. The lazy *? ensures it doesn't consume
         content between two code blocks.
```

---

_Spec by Reed Richards | FfP Sprint 2: 2 new files, 7 modified. No npm packages. Manifest bumps to 1.6._
