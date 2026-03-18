# Review Report: WI829
## Verdict: PASS
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/fait-for-powerpoint
cat review-brief-wi829.md | claude --model sonnet -p
```

**First 20 lines of output:**
```
# Hawkeye Code Review — WI829 — Cycle 1 Results

All files confirmed against source. Review findings below.

---

## Checklist

**1. `tags.add()` inside same `PowerPoint.run()` as the write — PASS**
`target.tags.add('FAIT_SOURCE', nodeId)` is called inside the same `PowerPoint.run()` callback at `pptWriter.ts:46`, inside the same `ctx` scope that performs the text write. Correctly wrapped in a non-fatal try/catch.

**2. No external `tagShape()` called from `applyTextToShape()` — PASS**
`applyTextToShape()` has zero calls to `tagShape()`. Tagging is fully inlined. Proxy object lifetime is safe.

**3. `isNotesCommand` detection — PASS**
`ChatPanel.tsx:91`: `const isNotesCommand = text.includes('ppt_notes_spec block');` Present and correctly gates `getSlideNotes()` injection at lines 93–100.

**4. `stripAllSpecs()` used for display, `stripNotesSpec()` not used — PASS**
`ChatPanel.tsx:6` imports `stripAllSpecs` (not `stripNotesSpec`). Display mapping at line 274 uses `stripAllSpecs`. `stripNotesSpec` is not imported anywhere in ChatPanel.
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| tags.add() inside applyTextToShape() PowerPoint.run() | ✅ | `pptWriter.ts:46` — `target.tags.add('FAIT_SOURCE', nodeId)` inside `PowerPoint.run()` callback, after text write, wrapped in try/catch |
| No external tagShape() called from applyTextToShape() | ✅ | Zero calls to `tagShape()` inside `applyTextToShape()`; tagging is fully inlined |
| isNotesCommand detection present | ✅ | `ChatPanel.tsx:91` — `const isNotesCommand = text.includes('ppt_notes_spec block');` gates existing-notes injection |
| stripAllSpecs() used (not stripNotesSpec) | ✅ | `ChatPanel.tsx:6` imports `stripAllSpecs`; display mapping at line 274 uses it; `stripNotesSpec` not imported in ChatPanel |
| PowerPointApi MinVersion="1.6" both manifests | ✅ | `public/manifest.xml:18` and `manifest.local.xml:18` both declare `<Set Name="PowerPointApi" MinVersion="1.6"/>` |
| MAX_SLIDES=20, MAX_SHAPES=3, SHAPE_TEXT_CAP=150 | ✅ | `pptReader.ts:141–143` — all three declared and applied: `Math.min()`, `>= MAX_SHAPES` break, slice+`…` truncation |
| getSlideNotes() loads deep path before sync | ✅ | Two-stage: (1) `slide.load('notes')` → `ctx.sync()`, (2) `notesRange.load('text')` → `ctx.sync()` — correct order |
| writeNotes() throws PptNotesError with codes | ✅ | `PptNotesError` at `pptWriter.ts:63` with union `'NO_SLIDE' \| 'NOTES_UNAVAILABLE' \| 'PPT_ERROR'`; all three thrown and foreign errors re-wrapped as PPT_ERROR |
| parseNotesSpec() regex matches ppt_notes_spec blocks | ✅ | `` /```ppt_notes_spec\s*([\s\S]*?)```/ `` — lazy capture, `\s*` tolerates newline before JSON, guarded JSON parse |
| KbResultPanel has onInsertToChat + onApplyToShape | ✅ | Both callbacks in `KbResultCardProps` and `KbResultPanelProps`; Apply button conditionally renders on `onApplyToShape && selectedShapeId` |
| searchKb() in faitApi.ts | ✅ | `export async function searchKb(...)` at `faitApi.ts:150`, typed with `KbSearchResponse`, 401 + non-ok error handling |
| FfE repo untouched | ✅ | Last commit in fait-for-excel is WI827 (0671ddc); no files from WI829 commit touch that repo |

---

## Issues Found

### Nitpick

**N1 — `stripNotesSpec` is dead code** (`pptNotesParser.ts:23–25`)
`stripNotesSpec` and `stripAllSpecs` are byte-for-byte identical functions. `stripNotesSpec` is never imported anywhere. Either remove it before the export list grows confusing, or add a meaningful behavioral difference.

**N2 — `resp.body!` non-null assertion in `sendChatStreaming`** (`faitApi.ts:87`)
`const reader = resp.body!.getReader()` — `body` can be null in Service Worker contexts or specific redirect paths. A simple `if (!resp.body) return;` guard before this line is safer than the assertion.

**N3 — `handleForgeApplyToShape` swallows all errors silently** (`ChatPanel.tsx:228–231`)
The catch block comment says "shape may have been deselected," but `applyTextToShape` can also throw `NO_TEXT_FRAME` or `PPT_ERROR`. The main chat Apply path surfaces these errors properly; FORGE Apply should too.

**N4 — Dual `useEffect` on `messages` — theoretical edge case** (`ChatPanel.tsx:110–140`)
Both Apply-to-Shape and ppt_notes_spec effects run on every `messages` update. A single response that triggers both heuristics simultaneously would render two overlapping action panels. Low probability given current slash-command prompt design, but worth noting for future expansion.

---

## Verdict

**PASS** — All 12 priority checks confirmed correct against source.

The HIGH-priority items are clean: `tags.add()` is properly inlined inside `applyTextToShape()`'s `PowerPoint.run()` context, `isNotesCommand` detection is present and correctly scoped, `stripAllSpecs()` is used (not `stripNotesSpec()`), and both manifests declare PowerPointApi 1.6. The MEDIUM and LOW items — token caps, deep-path load order, `PptNotesError` codes, regex, KbResultPanel callbacks, `searchKb()` presence — are all correct.

Four nitpicks found (dead export, one non-null assertion, one silent-fail UX gap, one dual-effect edge case). None are blocking. Recommend a fast-follow cleanup for N1–N3 in a future sprint.

**WI829 is clear to advance in the pipeline.**
