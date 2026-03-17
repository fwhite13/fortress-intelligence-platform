# CC Brief: WI828 Cycle 2 — Three Targeted Fixes

Working directory: `/home/fredw/projects/fip/fait-for-powerpoint/`

Make exactly three targeted changes. No scope creep. Touch only these three files:
1. `manifest.local.xml`
2. `src/taskpane/services/pptReader.ts`
3. `src/taskpane/services/pptWriter.ts`

---

## Fix 1 — `manifest.local.xml`: Add `/ppt-addin/` prefix to localhost URLs

The Vite config sets `base: '/ppt-addin/'`, so all dev server URLs must include that prefix.

**In `manifest.local.xml`, find and replace these three URLs:**

Change:
```
<SourceLocation DefaultValue="https://localhost:3001/src/taskpane/index.html"/>
```
To:
```
<SourceLocation DefaultValue="https://localhost:3001/ppt-addin/src/taskpane/index.html"/>
```

Change:
```
<bt:Url id="Commands.Url" DefaultValue="https://localhost:3001/commands.html"/>
```
To:
```
<bt:Url id="Commands.Url" DefaultValue="https://localhost:3001/ppt-addin/commands.html"/>
```

Change:
```
<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3001/src/taskpane/index.html"/>
```
To:
```
<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3001/ppt-addin/src/taskpane/index.html"/>
```

---

## Fix 2 — `src/taskpane/services/pptReader.ts`: Add notes deep-path to allSlides.load()

Currently the `allSlides.load()` call loads shapes but NOT the notes property. The notes are accessed later via `slideData.notes.textFrame.textRange.text`, but that path was never declared in the load call, so it always throws a `PropertyNotLoaded` error that is silently caught, returning empty notes.

**Find this load call in `getSlideContext()`:**
```typescript
allSlides.load(['items/id', 'items/shapes/items/id',
                'items/shapes/items/name',
                'items/shapes/items/textFrame/textRange/text',
                'items/shapes/items/type']);
```

**Replace with** (add the notes deep-path as the last array element):
```typescript
allSlides.load(['items/id', 'items/shapes/items/id',
                'items/shapes/items/name',
                'items/shapes/items/textFrame/textRange/text',
                'items/shapes/items/type',
                'items/notes/textFrame/textRange/text']);
```

That's the only change needed in pptReader.ts — just add `'items/notes/textFrame/textRange/text'` to the existing array.

---

## Fix 3 — `src/taskpane/services/pptWriter.ts`: Fix dead code guard on textFrame

In `applyTextToShape()`, the shape loads `textFrame/hasText` then checks `if (!target.textFrame)`. But `target.textFrame` is always a proxy object (never null/undefined) in the Office JS API — the check is dead code and never actually guards against shapes without text.

**Find:**
```typescript
if (!target.textFrame) {
  throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
}
```

**Replace with:**
```typescript
if (!target.textFrame.hasText) {
  throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
}
```

This correctly uses the `hasText` property that was already being loaded (via `target.load('textFrame/hasText')`), making the guard functional.

---

## Summary of changes
- `manifest.local.xml`: 3 URL string replacements
- `src/taskpane/services/pptReader.ts`: 1 array element added to allSlides.load()
- `src/taskpane/services/pptWriter.ts`: 1 condition changed from `!target.textFrame` to `!target.textFrame.hasText`

Make these changes exactly as specified. Do not rename, refactor, or touch anything else.
