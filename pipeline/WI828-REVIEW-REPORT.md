# Review Report: WI828
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/fait-for-powerpoint
cat review-brief-wi828.md | claude --model sonnet -p
```

**First 20 lines of output:**
```
## Code Review: WI828 — FfP Sprint 1 Foundation

**Reviewer: Hawkeye (Clint Barton)**
**Verdict: NEEDS-CHANGES**

---

## Pre-checked Items — All Pass

| Check | Status |
|---|---|
| Manifest `Host Name="Presentation"` (both) | ✓ |
| Manifest `xsi:type="Presentation"` (both) | ✓ |
| `PowerPointApi` MinVersion 1.5 (both) | ✓ |
| GUID `b2c3d4e5` (both) | ✓ |
| `PowerPoint.run()` in reader/writer | ✓ |
| `declare const PowerPoint: any` in reader/writer | ✓ |
| No `Excel.run` | ✓ |
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| Host Name="Presentation" — all 3 places, both manifests | ✅ | manifest.xml L18 + L28; manifest.local.xml L18 + L28; both have top-level `<Host Name="Presentation"/>`, VersionOverrides `<Host xsi:type="Presentation">` |
| PowerPointApi MinVersion="1.5" | ✅ | manifest.xml L21; manifest.local.xml L21: `<Set Name="PowerPointApi" MinVersion="1.5"/>` |
| PowerPoint.run() only (no Excel.run) in pptReader + pptWriter | ✅ | pptReader.ts L24: `PowerPoint.run(async (ctx: any) =>`; pptWriter.ts L16: same. `grep Excel.run` returns no matches. |
| declare const PowerPoint: any in both files | ✅ | pptReader.ts L3: `declare const PowerPoint: any;`; pptWriter.ts L3: same |
| @microsoft/office-js absent from package.json | ✅ | package.json dependencies: `react`, `react-dom` only. devDependencies: only `@types/office-js` (types only). No `@microsoft/office-js`. |
| tags.add() N/A or in same PowerPoint.run() | ✅ | No `tags.add()` calls anywhere in pptWriter.ts. Text written via `target.textFrame.textRange.text = text` (L42) |
| base: '/ppt-addin/' in vite.config.ts | ✅ | vite.config.ts last line: `base: '/ppt-addin/',` |
| Port 3001 | ✅ | vite.config.ts: `server: { port: 3001, ... }` |
| GUID b2c3d4e5 | ✅ | Both manifests L7: `<Id>b2c3d4e5-f6a7-8901-bcde-f12345678902</Id>` |
| getSlideContext() reads title + body + notes | ⚠️ | Title ✅ (shape name includes 'title' OR shape.type === 'title', fallback to first shape); Body ✅ (all shapes with hasText); Notes ❌ — `allSlides.load()` does NOT include `items/notes/textFrame/textRange/text`, so notes proxy throws PropertyNotLoaded; silently swallowed by catch → always empty string |
| applyTextToShape() shape lookup via PowerPoint API | ✅ | pptWriter.ts: iterates `slide.shapes.items`, finds by `s.id === shapeId`, writes via `target.textFrame.textRange.text = text` |
| useChat.ts Message has no FfE-specific fields | ✅ | Message interface: `{ role, content, streaming? }` — no `tableData`, `reportSpec`, or `formulaSpec` |
| FfE repo untouched | ✅ | `git diff HEAD~1 HEAD -- fait-for-excel/` in fip parent repo returns empty |

---

## Issues Found

### Critical

**1. `manifest.local.xml` — localhost URLs missing `/ppt-addin/` base path**

**File:** `manifest.local.xml` lines 29, 80, 81

With Vite 8 (`"vite": "^8.0.0"` in package.json), `base: '/ppt-addin/'` applies to **both** the dev server and production build. All pages are served under this prefix in dev mode.

Current (broken):
```xml
<SourceLocation DefaultValue="https://localhost:3001/src/taskpane/index.html"/>
<bt:Url id="Commands.Url" DefaultValue="https://localhost:3001/commands.html"/>
<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3001/src/taskpane/index.html"/>
```

Required:
```xml
<SourceLocation DefaultValue="https://localhost:3001/ppt-addin/src/taskpane/index.html"/>
<bt:Url id="Commands.Url" DefaultValue="https://localhost:3001/ppt-addin/commands.html"/>
<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3001/ppt-addin/src/taskpane/index.html"/>
```

Loading this manifest in PowerPoint Desktop will produce a blank/broken taskpane. The add-in will not start in local dev.

---

### Important

**2. `pptReader.ts` — Notes path not loaded; speaker notes always empty**

**File:** `pptReader.ts` lines 38–41

The `allSlides.load()` call does not include the notes property path:

```typescript
// CURRENT — notes path missing
allSlides.load(['items/id', 'items/shapes/items/id',
                'items/shapes/items/name',
                'items/shapes/items/textFrame/textRange/text',
                'items/shapes/items/type']);
```

At runtime, accessing `slideData.notes.textFrame.textRange.text` on an unloaded proxy throws `PropertyNotLoaded`. The `try/catch` at lines 96–101 silently swallows it. Speaker notes will **always return empty string** on every version of the API — this is not a version compatibility issue, it's a missing load path.

**Fix:** Add `'items/notes/textFrame/textRange/text'` to the load array.

---

**3. `pptWriter.ts` — `textFrame` guard is dead code; `hasText` never checked**

**File:** `pptWriter.ts` lines 35–42

```typescript
target.load('textFrame/hasText');
await ctx.sync();

if (!target.textFrame) {   // ← always false: Office.js proxy is never null
  throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
}

target.textFrame.textRange.text = text;  // will throw on sync for shapes without text
```

In Office.js, `shape.textFrame` returns a proxy object unconditionally — it is a non-nullable property. The guard `!target.textFrame` is always `false`. The `NO_TEXT_FRAME` code path is unreachable dead code. For shapes that genuinely lack text content (images, connectors), the write on L42 will throw during `ctx.sync()` and be rethrown as `PPT_ERROR` — wrong error code/message.

**Fix:**
```typescript
if (!target.textFrame.hasText) {
  throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
}
```

---

### Nitpick

**4. `useChat.ts` — Fallback `sendChat` call missing abort signal**

**File:** `useChat.ts` line 90

When `sendChatStreaming` aborts (including via the 30s timeout), the fallback `sendChat()` call does not pass the `AbortController` signal. If the server hangs after a stream timeout, the fallback waits indefinitely with `loading = true`. The 30s timeout provides false assurance. Low severity for Sprint 1, track for next sprint.

---

## Required Changes Before Merge

| # | File | Severity | Fix |
|---|-------|----------|-----|
| 1 | `manifest.local.xml` L29, 80, 81 | **Critical** | Add `/ppt-addin/` prefix to all 3 localhost URLs |
| 2 | `pptReader.ts` L38–41 | **Important** | Add `'items/notes/textFrame/textRange/text'` to `allSlides.load()` |
| 3 | `pptWriter.ts` L38 | **Important** | Change dead guard to `if (!target.textFrame.hasText)` |
| 4 | `useChat.ts` L90 | Nitpick | Pass abort signal to fallback `sendChat` |

---

## Verdict

**NEEDS-CHANGES.** The PowerPoint isolation is clean — all manifest Host names, API namespaces, and declarations are correct. No FfE bleed. However, 3 bugs require fixes before merge:

1. **Critical:** `manifest.local.xml` missing `/ppt-addin/` base in localhost URLs — add-in won't load in local dev at all.
2. **Important:** Notes never loaded in `pptReader.ts` — speaker notes context will always be empty string.
3. **Important:** Dead code guard in `pptWriter.ts` — `NO_TEXT_FRAME` error is unreachable; fix to use `hasText`.

Fix those three, and this is a clean Sprint 1 scaffold. Architecture is sound.

---

---

# Review Report: WI828 — Cycle 2
## Verdict: PASS
## Review Cycle: 2 of 2
## Commit Reviewed: `240c3b3`

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/fait-for-powerpoint
cat review-brief-wi828-c2.md | claude --model sonnet -p
```

---

## Cycle 2 Checks — Targeted Re-Review

| # | Check | File | Status | Evidence |
|---|-------|------|--------|----------|
| 1 | All 3 localhost URLs have `/ppt-addin/` prefix | `manifest.local.xml` | ✅ PASS | L29 SourceLocation, L80 Commands.Url, L81 Taskpane.Url — all include `/ppt-addin/` |
| 2 | `'items/notes/textFrame/textRange/text'` in `allSlides.load()` BEFORE `ctx.sync()` | `pptReader.ts` | ✅ PASS | Load array L38–42 includes notes path at L42; `ctx.sync()` at L43 — load precedes sync |
| 3 | Guard uses `!target.textFrame.hasText` (not `!target.textFrame`) | `pptWriter.ts` | ✅ PASS | L38: `if (!target.textFrame.hasText)` confirmed |
| 4 | No scope creep — exactly 3 source files changed | commit `240c3b3` | ✅ PASS | 3 source/config files changed (`manifest.local.xml`, `pptReader.ts`, `pptWriter.ts`) + 2 doc files added (`cc-brief-wi828-fix.md`, `review-brief-wi828.md`) — docs are non-code artifacts, not scope creep |

---

## Detail: Check 1 — manifest.local.xml

```xml
<!-- L29 -->
<SourceLocation DefaultValue="https://localhost:3001/ppt-addin/src/taskpane/index.html"/>
<!-- L80 -->
<bt:Url id="Commands.Url" DefaultValue="https://localhost:3001/ppt-addin/commands.html"/>
<!-- L81 -->
<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3001/ppt-addin/src/taskpane/index.html"/>
```
All 3 URLs correctly prefixed. **PASS.**

---

## Detail: Check 2 — pptReader.ts

```typescript
// Lines 38–43
allSlides.load(['items/id', 'items/shapes/items/id',
                'items/shapes/items/name',
                'items/shapes/items/textFrame/textRange/text',
                'items/shapes/items/type',
                'items/notes/textFrame/textRange/text']);  // ← added, L42
await ctx.sync();  // L43 — load precedes sync ✓
```
Notes path present and sequenced correctly. **PASS.**

---

## Detail: Check 3 — pptWriter.ts

```typescript
// Line 38
if (!target.textFrame.hasText) {  // ← was !target.textFrame (dead code)
  throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
}
```
Guard correctly checks `.hasText` property. `NO_TEXT_FRAME` error path is now reachable. **PASS.**

---

## Detail: Check 4 — Scope Creep

Commit `240c3b3` stats: 5 files, 227 insertions, 5 deletions.
- `manifest.local.xml` — target fix ✓
- `src/taskpane/services/pptReader.ts` — target fix ✓
- `src/taskpane/services/pptWriter.ts` — target fix ✓
- `cc-brief-wi828-fix.md` — Claude Code brief (doc artifact, not source) ✓
- `review-brief-wi828.md` — Review brief from Cycle 1 (doc artifact, not source) ✓

No unexpected source changes. **PASS.**

---

## Nitpick from Cycle 1

**Item 4** (`useChat.ts` fallback abort signal) — carried forward. Not required for Cycle 2 fixes. Track for next sprint.

---

## Final Verdict: **REVIEW PASS**

All 3 Cycle 1 issues are correctly and precisely resolved. No regressions. No scope creep. Architecture remains sound.

WI828 — FfP Sprint 1 foundation is **cleared for deploy.**
