# CC Brief — WI829: FfP Sprint 2
# Full Slide Scan + FORGE Search + /notes + Source Tagging
# Working directory: /home/fredw/projects/fip/fait-for-powerpoint/
# DO NOT touch /home/fredw/projects/fait-for-excel/ — that is a separate repo, hands off.

You are implementing 8 tasks for the FAIT for PowerPoint add-in (Sprint 2).
Work sequentially. All changes are surgical additions — do not break existing Sprint 1 functionality.

---

## CONTEXT: What exists now (Sprint 1 baseline)

- `src/taskpane/services/pptReader.ts` — has `getSlideContext()`, `formatSlideContext()` — DO NOT MODIFY THESE
- `src/taskpane/services/pptWriter.ts` — has `applyTextToShape(shapeId, text)`, `PptWriteError`
- `src/taskpane/services/faitApi.ts` — has `sendChat`, `sendChatStreaming`, `fetchKbList`, `fetchProjectList` — does NOT have `searchKb`
- `src/taskpane/components/ChatPanel.tsx` — has FORGE state vars (forgeQuery, forgeResults, etc.) — NO KbResultPanel rendered yet
- `src/taskpane/components/SlashCommandPicker.tsx` — has 4 commands (summarize, improve, bullets, expand)
- NO `KbResultPanel.tsx` in FfP — needs to be created
- NO `NotesPreview.tsx` in FfP — needs to be created

---

## Task 1: Manifest bumps

**File: `public/manifest.xml`**
Change `<Set Name="PowerPointApi" MinVersion="1.5"/>` to `<Set Name="PowerPointApi" MinVersion="1.6"/>`
That is the ONLY change in this file.

**File: `manifest.local.xml`**
Change `<Set Name="PowerPointApi" MinVersion="1.5"/>` to `<Set Name="PowerPointApi" MinVersion="1.6"/>`
That is the ONLY change in this file.

---

## Task 2: `src/taskpane/services/pptReader.ts` — ADD new exports

DO NOT modify `getSlideContext()` or `formatSlideContext()` — they work and are tested.

Add AFTER the existing code:

```typescript
export interface SlideSnapshot {
  slideNumber: number;   // 1-based
  title: string;
  shapes: Array<{
    name: string;
    text: string;        // truncated to 150 chars
  }>;
}

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

      if (!title && shapes.length > 0) title = shapes[0].text;

      if (shapes.length > 0) {
        snapshots.push({ slideNumber: i + 1, title, shapes });
      }
    }

    return snapshots;
  }).catch((): SlideSnapshot[] => []);
}

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

## Task 3: `src/taskpane/services/pptWriter.ts` — ADD writeNotes, tagShape; UPDATE applyTextToShape

### 3a: Update `applyTextToShape` signature to accept optional `nodeId`

The CURRENT signature is:
```typescript
export async function applyTextToShape(shapeId: string, text: string): Promise<void> {
```

Change it to:
```typescript
export async function applyTextToShape(shapeId: string, text: string, nodeId?: string): Promise<void> {
```

Inside the function, AFTER `target.textFrame.textRange.text = text; await ctx.sync();` and BEFORE the closing `});`, add:

```typescript
    // Source tagging — inside same PowerPoint.run() as the write
    if (nodeId) {
      try {
        target.tags.add('FAIT_SOURCE', nodeId);
        await ctx.sync();
      } catch {
        // Tagging failure is non-fatal — write already succeeded
      }
    }
```

### 3b: Add `PptNotesError` class and `writeNotes` function at the END of the file

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

---

## Task 4: NEW `src/taskpane/services/pptNotesParser.ts`

Create this file with the following EXACT content:

```typescript
export interface PptNotesSpec {
  speakerNotes: string;
  sources: string[];
}

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

export function stripNotesSpec(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}

export function stripAllSpecs(content: string): string {
  return content.replace(/```ppt_notes_spec\s*[\s\S]*?```/g, '').trim();
}
```

---

## Task 5: NEW `src/taskpane/components/KbResultPanel.tsx`

FfP does NOT have KbResultPanel yet. Create it. This is the FORGE results panel with Sprint 2 action buttons:

```typescript
import React, { useState } from 'react';

export interface KbResult {
  content: string;
  source: string;
  score: number;
}

interface KbResultCardProps {
  result: KbResult;
  index: number;
  onInsertToChat?: (content: string) => void;
  onApplyToShape?: (content: string, source: string) => void;
  selectedShapeId?: string | null;
}

const TRUNCATE_LEN = 200;

const KbResultCard: React.FC<KbResultCardProps> = ({
  result,
  index,
  onInsertToChat,
  onApplyToShape,
  selectedShapeId,
}) => {
  const [expanded, setExpanded] = useState(false);
  const [showMore, setShowMore] = useState(false);

  const truncated = result.content.length > TRUNCATE_LEN && !showMore;
  const displayContent = truncated
    ? result.content.slice(0, TRUNCATE_LEN) + '…'
    : result.content;

  return (
    <div
      style={{
        border: '1px solid #2e3f54',
        borderRadius: '5px',
        marginBottom: '6px',
        overflow: 'hidden',
      }}
    >
      <button
        onClick={() => setExpanded((v) => !v)}
        aria-expanded={expanded}
        style={{
          width: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '7px 10px',
          background: expanded ? '#162030' : '#0f1720',
          border: 'none',
          cursor: 'pointer',
          textAlign: 'left',
          gap: '8px',
        }}
      >
        <span
          style={{
            fontSize: '11px',
            fontWeight: '600',
            color: '#d4af37',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            flex: 1,
          }}
          title={result.source}
        >
          {index + 1}. {result.source || 'Unknown source'}
        </span>
        <span
          style={{
            fontSize: '10px',
            color: '#8899aa',
            flexShrink: 0,
            fontFamily: 'monospace',
          }}
        >
          {(result.score * 100).toFixed(0)}%
        </span>
        <span style={{ color: '#556677', fontSize: '10px', flexShrink: 0 }}>
          {expanded ? '▲' : '▼'}
        </span>
      </button>

      {expanded && (
        <div
          style={{
            padding: '8px 10px',
            background: '#111d2b',
            borderTop: '1px solid #2e3f54',
          }}
        >
          <p
            style={{
              margin: 0,
              fontSize: '12px',
              color: '#c8d8e8',
              lineHeight: 1.6,
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}
          >
            {displayContent}
          </p>
          {result.content.length > TRUNCATE_LEN && (
            <button
              onClick={(e) => {
                e.stopPropagation();
                setShowMore((v) => !v);
              }}
              style={{
                marginTop: '6px',
                background: 'none',
                border: 'none',
                color: '#d4af37',
                fontSize: '11px',
                cursor: 'pointer',
                padding: 0,
                textDecoration: 'underline',
              }}
            >
              {showMore ? 'show less' : 'show more'}
            </button>
          )}

          {/* Action buttons */}
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
        </div>
      )}
    </div>
  );
};

interface KbResultPanelProps {
  results: KbResult[];
  loading: boolean;
  onInsertToChat?: (content: string) => void;
  onApplyToShape?: (content: string, source: string) => void;
  selectedShapeId?: string | null;
}

const KbResultPanel: React.FC<KbResultPanelProps> = ({
  results,
  loading,
  onInsertToChat,
  onApplyToShape,
  selectedShapeId,
}) => {
  if (loading) {
    return (
      <div style={containerStyle}>
        <div style={headerStyle}>
          <span style={{ color: '#d4af37', fontWeight: '600', fontSize: '12px' }}>
            🔍 FORGE KB
          </span>
        </div>
        <div style={{ padding: '12px', textAlign: 'center', color: '#556677', fontSize: '12px' }}>
          Searching knowledge base…
        </div>
      </div>
    );
  }

  if (results.length === 0) {
    return (
      <div style={containerStyle}>
        <div style={headerStyle}>
          <span style={{ color: '#d4af37', fontWeight: '600', fontSize: '12px' }}>
            🔍 FORGE KB
          </span>
        </div>
        <div style={{ padding: '12px', color: '#556677', fontSize: '12px' }}>
          No results found.
        </div>
      </div>
    );
  }

  return (
    <div style={containerStyle}>
      <div style={headerStyle}>
        <span style={{ color: '#d4af37', fontWeight: '600', fontSize: '12px' }}>
          🔍 FORGE KB
        </span>
        <span style={{ color: '#556677', fontSize: '11px' }}>
          {results.length} result{results.length !== 1 ? 's' : ''}
        </span>
      </div>
      <div style={{ padding: '6px 8px' }}>
        {results.map((r, i) => (
          <KbResultCard
            key={`${r.source}-${i}`}
            result={r}
            index={i}
            onInsertToChat={onInsertToChat}
            onApplyToShape={onApplyToShape}
            selectedShapeId={selectedShapeId}
          />
        ))}
      </div>
    </div>
  );
};

const containerStyle: React.CSSProperties = {
  border: '1px solid #2e3f54',
  borderRadius: '6px',
  background: '#131e2b',
  overflow: 'hidden',
  margin: '6px 0',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '7px 10px',
  background: '#0f1720',
  borderBottom: '1px solid #2e3f54',
};

export default KbResultPanel;
```

---

## Task 5b: ADD `searchKb` to `src/taskpane/services/faitApi.ts`

FfP's faitApi.ts does NOT have `searchKb`. Add it at the END of the file:

```typescript
export interface KbSearchResponse {
  results: Array<{
    content: string;
    source: string;
    score: number;
  }>;
}

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

  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

  return resp.json();
}
```

---

## Task 6: NEW `src/taskpane/components/NotesPreview.tsx`

```typescript
import React from 'react';

interface NotesPreviewProps {
  pendingNotes: string;
  sources: string[];
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

## Task 7: `src/taskpane/components/SlashCommandPicker.tsx` — ADD /notes command

In the `COMMANDS` array, ADD this entry at the BEGINNING (before 'summarize'):

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

ONLY add this entry. Do not change anything else in the file.

---

## Task 8: `src/taskpane/components/ChatPanel.tsx` — Wire Sprint 2 features

This is the largest change. Apply these modifications surgically:

### 8a: Add new imports at the top (after the existing imports)

Add:
```typescript
import { getAllSlidesContext, getSlideNotes, formatDeckContext } from '../services/pptReader';
import { writeNotes, PptNotesError } from '../services/pptWriter';
import { parseNotesSpec, stripAllSpecs } from '../services/pptNotesParser';
import { searchKb } from '../services/faitApi';
import type { KbResult } from './KbResultPanel';
import KbResultPanel from './KbResultPanel';
import NotesPreview from './NotesPreview';
import type { PptNotesSpec } from '../services/pptNotesParser';
```

### 8b: Add new state variables

After the existing `applyError` state, add:
```typescript
  // ── Sprint 2: FORGE search ────────────────────────────────────────────────
  const [showForgeSearch, setShowForgeSearch] = useState(false);
  const [forgeQuery, setForgeQuery] = useState('');
  const [forgeLoading, setForgeLoading] = useState(false);
  const [forgeResults, setForgeResults] = useState<KbResult[] | null>(null);
  const forgeInputRef = useRef<HTMLInputElement>(null);

  // ── Sprint 2: Speaker notes ───────────────────────────────────────────────
  const [pendingNotes, setPendingNotes] = useState<PptNotesSpec | null>(null);
  const [notesLoading, setNotesLoading] = useState(false);
  const [notesError, setNotesError] = useState<string | null>(null);
```

NOTE: `forgeInputRef` requires `useRef<HTMLInputElement>` — `useRef` is already imported.

### 8c: Add effect to focus forge input when shown

After the existing useEffects, add:
```typescript
  useEffect(() => {
    if (showForgeSearch) {
      setTimeout(() => forgeInputRef.current?.focus(), 50);
    }
  }, [showForgeSearch]);
```

### 8d: Replace `handleSend` entirely

The current `handleSend` only calls `getSlideContext()`. Replace it with this version that adds deck context and notes-command detection:

```typescript
  const handleSend = async (text: string) => {
    let context: string | undefined;

    try {
      const ctx = await getSlideContext();
      if (ctx.slideNumber > 0) {
        context = formatSlideContext(ctx);
      }

      // Full deck context (Sprint 2)
      const snapshots = await getAllSlidesContext();
      if (snapshots.length > 0) {
        const deckBlock = formatDeckContext(snapshots);
        context = context ? `${context}\n\n${deckBlock}` : deckBlock;
      }

      // If /notes command: inject existing notes for rewrite context
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
    } catch {
      // Non-fatal
    }

    await send(text, context);
  };
```

### 8e: Add new useEffect for ppt_notes_spec detection

Add this useEffect AFTER the existing "Watch messages for Apply to Shape trigger" useEffect:

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

### 8f: Add FORGE handlers

Add these handlers after `handleApplyDiscard`:

```typescript
  // ── Sprint 2: FORGE handlers ──────────────────────────────────────────────
  const buildKbTypes = (): string[] => {
    const types = Object.entries(kbToggles)
      .filter(([, v]) => v)
      .map(([k]) => k);
    if (!types.includes('personal')) types.push('personal');
    return types;
  };

  const handleForgeSearch = async () => {
    if (!forgeQuery.trim()) return;
    setForgeLoading(true);
    setForgeResults(null);
    try {
      const { results } = await searchKb(
        forgeQuery.trim(),
        apiKey,
        projectId ?? undefined,
        buildKbTypes()
      );
      setForgeResults(results);
    } catch {
      setForgeResults([]);
    } finally {
      setForgeLoading(false);
    }
  };

  const handleForgeKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleForgeSearch();
    if (e.key === 'Escape') {
      setShowForgeSearch(false);
      setForgeQuery('');
      setForgeResults(null);
    }
  };

  const handleForgeInsertToChat = (content: string) => {
    setInputText((prev) =>
      prev ? `${prev}\n\nFORGE context:\n${content}` : `FORGE context:\n${content}`
    );
  };

  const handleForgeApplyToShape = async (content: string, source: string) => {
    if (!slideContext?.selectedShapeId) return;
    try {
      await applyTextToShape(slideContext.selectedShapeId, content, source);
      await refreshSlideContext();
    } catch {
      // Silent failure — shape may have been deselected
    }
  };

  // ── Sprint 2: Notes handlers ──────────────────────────────────────────────
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

### 8g: Update the render section

#### In the header buttons area, add the FORGE toggle button:
Add after the existing header buttons (🗑 and ⚙) but before the closing `</div>` of the header button group:

```typescript
          {/* FORGE search toggle */}
          <button
            onClick={() => setShowForgeSearch((v) => !v)}
            title="Search FORGE knowledge base"
            aria-label="Ask FORGE"
            style={{
              ...headerBtnStyle,
              color: showForgeSearch ? '#d4af37' : '#8899aa',
            }}
          >
            🔍
          </button>
```

#### Add the FORGE search input bar AFTER the slide context indicator div and BEFORE the error banner:

```typescript
      {/* FORGE search bar */}
      {showForgeSearch && (
        <div
          style={{
            display: 'flex',
            gap: '6px',
            padding: '6px 12px',
            borderBottom: '1px solid #2e3f54',
            background: '#0f1720',
            flexShrink: 0,
          }}
        >
          <input
            ref={forgeInputRef}
            value={forgeQuery}
            onChange={(e) => setForgeQuery(e.target.value)}
            onKeyDown={handleForgeKeyDown}
            placeholder="Search FORGE knowledge base…"
            style={{
              flex: 1,
              background: '#1a2332',
              border: '1px solid #2e3f54',
              borderRadius: '4px',
              color: '#e8edf3',
              padding: '5px 8px',
              fontSize: '12px',
              outline: 'none',
            }}
          />
          <button
            onClick={handleForgeSearch}
            disabled={forgeLoading || !forgeQuery.trim()}
            style={{
              background: '#d4af37',
              color: '#0f1720',
              border: 'none',
              borderRadius: '4px',
              padding: '5px 10px',
              fontSize: '12px',
              fontWeight: '600',
              cursor: 'pointer',
            }}
          >
            {forgeLoading ? '…' : 'Go'}
          </button>
        </div>
      )}
```

#### In the scrollable message area, add FORGE results BEFORE MessageList:

```typescript
        {/* FORGE KB results */}
        {(forgeLoading || forgeResults !== null) && (
          <div style={{ padding: '4px 8px', flexShrink: 0 }}>
            <KbResultPanel
              results={forgeResults ?? []}
              loading={forgeLoading}
              onInsertToChat={handleForgeInsertToChat}
              onApplyToShape={handleForgeApplyToShape}
              selectedShapeId={slideContext?.selectedShapeId ?? null}
            />
          </div>
        )}
```

#### Add NotesPreview and notes error AFTER the ShapePreview block (after `{applyError && ...}`) but BEFORE the input area:

```typescript
      {/* Speaker notes preview (Sprint 2) */}
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

### 8h: Strip ppt_notes_spec from displayed message content

In `MessageList.tsx` DO NOT touch it. Instead, in `ChatPanel.tsx`, the `messages` prop passed to `MessageList` should have the notes spec stripped for display purposes.

Actually — the cleanest approach: in the messages `useEffect` that watches for notes spec, store the spec but DON'T modify `messages` array. The stripping happens in the `MessageBubble` render.

INSTEAD of modifying MessageBubble, add a `displayMessages` computed value in ChatPanel and pass that to MessageList:

```typescript
  // Strip ppt_notes_spec blocks from display (raw content stays in state for history)
  const displayMessages = messages.map((msg) =>
    msg.role === 'assistant'
      ? { ...msg, content: stripAllSpecs(msg.content) }
      : msg
  );
```

Then in the JSX, change:
```typescript
        <MessageList
          messages={messages}
          loading={loading}
        />
```
to:
```typescript
        <MessageList
          messages={displayMessages}
          loading={loading}
        />
```

---

## CRITICAL CONSTRAINTS (do not violate)

1. `tags.add()` MUST be inside the same `PowerPoint.run()` as the text write — it IS in Task 3's implementation above. Do not move it out.

2. Manifest bump is exactly `"1.6"` — not `"1.60"` or `"1.5.1"`.

3. `parseNotesSpec` uses `.match(/```ppt_notes_spec\s*([\s\S]*?)```/)` — lazy match with `*?`.

4. `applyTextToShape` signature change is BACKWARD COMPATIBLE — `nodeId` is optional (`nodeId?: string`). All existing call sites pass only `(shapeId, text)` and must still work.

5. `tags.add(key, value)` NOT `tags.set()` — PowerPoint JS API uses `.add()`.

6. `getAllSlidesContext()` MUST have the `Math.min(slideItems.length, MAX_SLIDES)` cap.

7. DO NOT TOUCH `~/projects/fait-for-excel/` — it is a completely separate repo.

8. DO NOT modify `getSlideContext()` or `formatSlideContext()` in pptReader.ts.

---

## After all changes, run:
```bash
cd /home/fredw/projects/fip/fait-for-powerpoint
npm run build
```

Fix any TypeScript errors before finishing.
