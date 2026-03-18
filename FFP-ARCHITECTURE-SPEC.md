# FfP (FAIT for PowerPoint) — Architecture Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Input:** Bruce's `RESEARCH-FFP.md` + FfE codebase lessons  
**Status:** Initial design — planning and scoping doc  
**Audience:** Fred (product decisions), Tony (implementation orientation)

---

## 1. Product Overview

### What FfP Is

FAIT for PowerPoint (FfP) is a PowerPoint Office Add-in taskpane that lets users generate, populate, and refine slide content using FAIT's FORGE knowledge base and AI capabilities. It runs inside PowerPoint (Online and Desktop) as a permanent sidebar, not a separate web app.

The core workflow: user selects a slide or shape → asks FfP to populate it → FfP queries FORGE for relevant knowledge → generates slide-ready content → user reviews and applies.

FfP is **not a full deck generator from a blank prompt.** It is a **contextual content assistant** that operates on existing structure. The user controls the template, the layout, and the flow; FfP populates the substance with grounded, sovereign content.

### What Makes FfP Different

Every other AI presentation tool does one of two things:
- **Generic prompting** — "write me 10 slides about supply chain" — fast, cheap, factually unreliable, cloud-dependent
- **Design automation** — auto-layout and visual polish with no domain knowledge

FfP's differentiated position is the intersection of three capabilities no competitor provides together:

| Capability | Copilot | Plus AI | ChatGPT for PPT | Beautiful.ai | Gamma | **FfP** |
|------------|---------|---------|-----------------|--------------|-------|---------|
| Native Office Add-in | ✅ | ✅ | ✅ | Partial | ❌ | **✅** |
| Enterprise KB (FORGE) | ❌ | ❌ | ❌ | ❌ | ❌ | **✅** |
| Data sovereignty | ❌ | ❌ | ❌ | ❌ | ❌ | **✅** |
| Works in regulated industries | ❌ | ❌ | ❌ | ❌ | ❌ | **✅** |

**The whitespace FfP owns:** grounded generation from private knowledge, inside PowerPoint, without leaving the corporate security boundary.

### Who It's For

**Primary:** Analysts and portfolio managers at Fortress Asset Management who produce recurring data decks, client briefings, and investment committee presentations. These are people who spend 2–4 hours updating a 40-slide deck by hand every week.

**Secondary:** Anyone at Fortress who needs to produce accurate, on-brand slides that cite firm-approved content rather than hallucinated AI output.

### The Core Value Proposition

"Tell FfP what you want this slide to say. It will find the right content in your firm's knowledge base and write it — accurately, in your brand, without leaving PowerPoint."

---

## 2. FIP Suite Coherence

### FfP Is Part of FIP

FfP is the third FIP application, sitting alongside FAIT (web), FIRM (web), FORMS (web), and FfE (Excel add-in). From the user's perspective, FfP is "FAIT in PowerPoint" — the same assistant, the same knowledge, the same credential, in a new surface.

### What FfP Shares with FfE

FfE already solved the hard problems for Office Add-in integration. FfP inherits all of it:

| Concern | FfE Solution | FfP Inherits |
|---------|-------------|--------------|
| Auth | App key via `OfficeRuntime.storage` | ✅ Same pattern, same key format |
| Backend | FAIT API (`https://fait.dev.fortressam.ai`) | ✅ Same API, same auth header |
| Build | Vite + HTML entry points + `vite-plugin-mkcert` | ✅ Same config, day 1 |
| Dev manifest | `manifest.local.xml` with localhost URLs | ✅ Same pattern |
| Settings persistence | `OfficeRuntime.storage` with `localStorage` shim for dev | ✅ Same pattern |
| Deployment | Served from FAIT `wwwroot/ppt-addin/` | ✅ Mirrors FfE's `excel-addin/` |
| Design tokens | FIP color palette, Inter font | ✅ Same CSS variables |
| No `@microsoft/office-js` npm | CDN-only | ✅ Same discipline |

### What FfP Does NOT Share

FfP uses the **PowerPoint JS API**, not the Excel JS API. There is no code sharing between `excelReader.ts` / `excelWriter.ts` and FfP's shape/slide services — these are completely different APIs.

FfP does not use MudBlazor, FipNavBar, or any .NET components. It is a React/TypeScript taskpane — same stack as FfE.

`FipShared` (the .NET RCL) is irrelevant to FfP. The "shared" between FfE and FfP is pattern and convention, not code.

### FipNavBar in an Add-in Context

FipNavBar doesn't render inside a taskpane. The taskpane is a narrow sidebar (320px wide typically). FfP should use a minimal in-pane header consistent with FfE's approach: FAIT shield logo, app name ("FAIT for PowerPoint"), and a settings gear icon. Apply FIP design tokens (gold `#d4af37`, navy `#0f1923`, Inter font) for brand coherence.

### User Experience: Having Both Installed

When a user has both FfE and FfP installed:
- They see "FAIT for Excel" in the Excel taskpane ribbon
- They see "FAIT for PowerPoint" in the PowerPoint taskpane ribbon
- Both use the same API key stored in `OfficeRuntime.storage` per-app — **no cross-app storage sharing** (OfficeRuntime.storage is scoped to the add-in + user + document)
- They authenticate once per add-in (API key entry)
- The FAIT backend is the same — same KBs, same conversation history (if session IDs are consistent)
- They feel like the same product in different surfaces

**Decision: FfP should use the same API key convention as FfE** (`faitApiKey` storage key). The user enters their API key once in each app, not once globally. This is a minor friction point, but simplicity beats complexity at this stage.

---

## 3. Lessons Learned from FfE

These are the specific mistakes made in FfE that FfP must avoid from day 1, in priority order:

### L1 — Start with the correct Vite config (avoid the blank screen crisis)

FfE shipped with `input: 'src/taskpane/index.tsx'` and `format: 'iife'`, producing a JS bundle with no HTML wrapper. The manifest's `<SourceLocation>` pointed to a path that returned nothing. The add-in showed a blank screen in Excel Online and took all night to debug.

**FfP rule:** The first commit of `vite.config.ts` uses HTML entry points:
```typescript
input: {
  taskpane: 'src/taskpane/index.html',
  commands: 'public/commands.html'
}
```
No IIFE. No `inlineDynamicImports`. No `format: 'iife'`. The manifest `<SourceLocation>` points to `/ppt-addin/taskpane/index.html`.

### L2 — Write `manifest.local.xml` before any other file

FfE's first manifest pointed at prod URLs. Developers had to hand-edit `manifest.xml` to test locally, creating risk of accidentally committing localhost URLs to the prod manifest.

**FfP rule:** Create `manifest.local.xml` (pointing to `https://localhost:3001/`) before writing a single component. `manifest.xml` (the prod copy) is committed once with final prod URLs and never edited during development.

### L3 — Never put `@microsoft/office-js` in package.json

FfE shipped with `"@microsoft/office-js": "^1.1.110"` in dependencies. This is an officially unsupported npm package — a stale mirror. Office.js must be loaded via CDN `<script>` tag in `index.html`.

**FfP rule:** `@microsoft/office-js` never appears in `package.json`. `@types/office-js` in devDependencies is fine (TypeScript types only, not a runtime package).

### L4 — Reed specs before Tony writes

FfE was built without architecture review. The initial implementation required a refactor sprint (WI#813) to fix the build foundation before Sprint 2 features could land. This cost a full sprint of capacity.

**FfP rule:** No code before this spec is reviewed by Fred and understood by Tony. Sprint 1 begins with a foundation build spec (equivalent to WI#813) that Tony implements in a single CC session.

### L5 — `OfficeRuntime.storage` needs a `localStorage` shim for dev

FfE's `settings.ts` used `window.OfficeRuntime?.storage ?? OfficeRuntime.storage` — the second fallback threw a `ReferenceError` when run in a plain browser context (dev mode without Office loaded). The fix is a `localStorage`-backed shim.

**FfP rule:** `settings.ts` is copied from FfE with the working shim pattern. No new `OfficeRuntime` access without testing the non-Office browser path.

### L6 — Use `public/` for static assets, `src/` for component code

FfE's file structure mixed static HTML (commands.html) with compiled component source. The `public/` directory should contain: `commands.html`, `manifest.xml`, icons. The `src/` directory contains React/TS source only.

### L7 — Port 3001, not 3000, to avoid collision with FfE dev server

FfE uses port 3000. Running both simultaneously (plausible for a developer working on both) requires FfP to use a different port.

**FfP rule:** `vite.config.ts` dev server port is `3001`.

---

## 4. API Baseline Decision

### Recommendation: Target PowerPointApi 1.5 as minimum, 1.8 as full feature target

**Why 1.5 as baseline:**
- Adds `getSelectedShapes()`, `getSelectedSlides()`, `getSelectedTextRange()` (Req Set 1.5)
- These selection APIs are the foundation of FfP's contextual generation model — without them, FfP has no way to know what the user is working on
- 1.5 is available on Office 2021 LTSC, Office 2024 LTSC, M365 Web, and M365 Desktop
- Catches effectively 100% of the target audience (Fortress AM users are on M365)

**Why not lower than 1.5:**
- 1.4 has shape/text manipulation but no selection awareness — FfP would be blind to context
- 1.3 and below have minimal shape access — not viable for the product we're building

**Why target 1.8 for full features:**
- 1.8 adds Tables (create/read/write), Bindings (bind shapes by ID for stable addressing), `BorderProperties`, `FillProperties`, `FontProperties`
- Tables in PowerPoint are a primary use case (data slides with data tables)
- Bindings enable FfP to track which shapes it has written to, enabling "update my last output" flows
- 1.8 is available on M365 Web and M365 Desktop (Build 18730, Aug 2025) — fully in range for Fortress AM users

**1.9/1.10 features** (advanced table cell formatting, alt-text, bullet format styles) are nice-to-haves that can be used via feature detection without raising the manifest minimum.

**Preview APIs** (image insert, selection change events) are used behind a feature detection flag. Not required for MVP.

### What the Baseline Unlocks vs. Excludes

| Capability | 1.5 baseline | 1.8 target |
|------------|--------------|------------|
| Add/delete slides | ✅ | ✅ |
| Read/write shape text | ✅ | ✅ |
| Selection-aware context | ✅ | ✅ |
| Custom tags on shapes | ✅ (1.3) | ✅ |
| Speaker notes read/write | ✅ (1.6) | ✅ |
| Custom document properties | ✅ (1.7) | ✅ |
| Insert slides from base64 PPTX | ✅ (1.2) | ✅ |
| Tables (create/read/write) | ❌ | ✅ |
| Bindings (stable shape addressing) | ❌ | ✅ |
| Image insert (base64, positioned) | ❌ stable, Preview only | Preview |
| Events (slide selection change) | ❌ stable, Preview only | Preview |
| Chart creation | ❌ | ❌ (API gap, workaround required) |
| Animations | ❌ | ❌ (permanent API gap) |
| Slide reorder by index | ❌ | ❌ (no stable API, workaround) |

**The permanent gaps that define FfP's architecture:**
1. No chart creation API → chart-as-image workaround (Canvas → base64 → addPicture)
2. No stable image insert → Common API workaround for MVP; Preview API when available
3. No events → polling or user-triggered actions (this is fine — matches the deliberate user action model)
4. No animations → out of scope entirely; template-first approach preserves existing animations

---

## 5. MVP Scope

### What the MVP Delivers

A user sitting in PowerPoint with an existing branded deck can:

1. **Select a text shape or slide** → ask FfP "write content for this slide about [topic]" → FfP queries FORGE → generates text → user sees a preview → user applies it to the shape
2. **Ask for speaker notes** → FfP generates speaker notes for the selected slide, citing FORGE sources
3. **Search FORGE KB** from the taskpane → find approved content → paste/apply to a selected shape
4. **Ask FfP a question** about the presentation's content ("what's on slide 5?") — basic contextual Q&A

That's it. Everything else is Sprint 2+.

### What's Explicitly Out of MVP Scope

| Feature | Why Deferred |
|---------|-------------|
| Full deck generation from blank | High complexity, low differentiation vs. Copilot; not FfP's lane |
| Chart generation | API gap (no chart creation); workaround is complex for MVP |
| Image insertion | Preview API — not stable; adds friction for a first ship |
| Template-based slide injection | Requires FORGE integration work (template storage in FORGE) — Sprint 2 |
| Table generation | 1.8 feature — available but not MVP priority |
| Slide reorder | No stable API; workaround is user-friction; deferred |
| Multi-slide context ("what's on slide 5?") | Sprint 2 — requires slide enumeration service |
| Export/download | Browser security limitation in taskpane context |
| Brand compliance checking | Sprint 3+ |

### MVP API Operations (all within 1.5 baseline)

**Read operations (every user action starts here):**
- `presentation.getSelectedSlides()` — which slide(s) is the user on
- `slide.shapes` — enumerate shapes on selected slide
- `shape.textFrame.textRange.text` — read existing text content
- `slide.notes.textFrame.textRange.text` — read existing speaker notes
- `shape.tags` — read FAIT metadata tags if present

**Write operations (after user confirms):**
- `shape.textFrame.textRange.text = "..."` — replace shape text with AI-generated content
- `slide.notes.textFrame.textRange.text = "..."` — write speaker notes
- `shape.tags.add("FAIT_SOURCE", nodeId)` — tag shape with FORGE source reference

**FAIT API operations:**
- Same `/api/chat` endpoint as FfE with appropriate system prompt for PPT context
- Same API key auth header pattern
- Same model selection (haiku/sonnet)

---

## 6. Sprint Sequence

### Sprint 1 — Foundation + Core Chat (Small-Medium)

**Goal:** Add-in loads correctly, authenticates, and can write AI-generated text to a selected shape. This is the "it works" sprint.

**Delivers:**
- FfP loads in PowerPoint Online without blank screen (correct Vite config from day 1)
- Settings panel: API key entry, model selection, stored in `OfficeRuntime.storage`
- Chat panel: user can type a message and get a FAIT response
- "Apply to shape" action: selected response can be applied to the currently selected PPT shape
- Minimal slide context injection: current slide number + existing text of selected shape injected into prompt
- Speaker notes read included in context

**APIs used:** `getSelectedSlides()` (1.5), `getSelectedShapes()` (1.5), `shape.textFrame.textRange.text` (1.4), `slide.notes` (1.6)

**Effort:** Small-Medium (benefit of starting from FfE's working architecture)

**CC task structure:**
```
Foundation session (single CC run, sequential):
  1. Repo scaffold: package.json, vite.config.ts, tsconfig.json
  2. index.html (taskpane) + commands.html (public/)
  3. manifest.xml (prod) + manifest.local.xml (local dev)
  4. App.tsx + settings.ts (OfficeRuntime.storage + shim)
  5. pptReader.ts: getSelectionContext() → slide + shapes + notes
  6. pptWriter.ts: applyTextToShape(shapeId, text)
  7. ChatPanel.tsx: chat UI (port from FfE, minimal changes)
  8. ShapePreview.tsx: shows pending text before applying, Accept/Reject buttons
```

**Acceptance criteria:**
- Add-in loads in PowerPoint Online (no blank screen)
- Can enter API key in settings and persist it across reloads
- Can chat with FAIT and get a response (slide context injected)
- Can click "Apply to Shape" and have the text written to the selected shape
- Apply does not fire unless user clicks Accept in the preview dialog

---

### Sprint 2 — Slide Context Awareness + FORGE Search (Medium)

**Goal:** FfP understands the full slide and queries FORGE directly.

**Delivers:**
- **Full slide scan:** enumerate all shapes on the current slide, extract text, include in context
- **FORGE search panel:** user can search FORGE KB from the taskpane, see relevant nodes, and paste content into a selected shape without going through chat
- **Speaker notes generation:** `/notes` slash command generates or rewrites speaker notes for the current slide
- **Source tagging:** when FfP writes content derived from a FORGE node, it tags the shape with `shape.tags.add("FAIT_SOURCE", nodeId)` — enables traceability
- **Custom document property:** write `FAIT_Last_Updated` timestamp to `presentation.customProperties` on first write

**APIs used:** `slide.shapes` enumerate (1.4), `slide.notes` (1.6), `presentation.customProperties` (1.7), `shape.tags` (1.3)

**Effort:** Medium

**Dependencies:** Sprint 1 must be stable and deployed

---

### Sprint 3 — Data Tables + Template Slide Injection (Medium-Large)

**Goal:** FfP can insert structured data as a table, and can inject new slides from pre-built FORGE templates.

**Delivers:**
- **Table generation:** FAIT generates tabular data (e.g., portfolio attribution table) → FfP creates a PowerPoint table on the current slide (requires 1.8 target bump)
- **Template slide injection:** FORGE stores a library of approved slide templates as base64 PPTX fragments. User asks FfP to "add a title slide" or "insert a KPI summary slide" → FfP retrieves the template fragment from FORGE, calls `insertSlidesFromBase64()`, inserts the slide, then populates it with FORGE-grounded content
- **Chart-as-image workaround:** FfP generates a chart using Chart.js in a hidden canvas element → `canvas.toDataURL('image/png')` → base64 → `setSelectedDataAsync(base64, {coercionType: Office.CoercionType.Image})` → inserted at cursor. Not pixel-perfect positioning, but functional for MVP charts.

**APIs used:** PowerPointApi 1.8 tables, `insertSlidesFromBase64` (1.2), Common API `setSelectedDataAsync` for images

**Effort:** Medium-Large

**Dependencies:** Sprint 2 (FORGE search, source tagging). FORGE must support a "template library" node type — this is a backend ask.

---

## 7. Known API Gaps and Workarounds

### Gap 1 — No `addPicture()` in stable API (Preview only)

**Gap:** Inserting an image at a specific position (`left`, `top`, `width`, `height`) requires `ShapeCollection.addPicture(base64, options)` which is Preview-only as of early 2026.

**Workaround for MVP (Sprint 1 and 2):** Skip image insertion entirely. FfP MVP is text-only. Images are Sprint 3+.

**Workaround for Sprint 3:** Use `Office.context.document.setSelectedDataAsync(base64, { coercionType: Office.CoercionType.Image, imageLeft: X, imageTop: Y, imageWidth: W, imageHeight: H })`. This works on both Desktop and Online. Limitation: inserts at the current cursor position, not a specified coordinate. For chart images, this means the user needs to position their cursor before triggering the insert — acceptable UX tradeoff.

**Future:** `addPicture()` is expected to be promoted to a numbered requirement set (likely 1.11) by mid-2026. When it stabilizes, FfP can add a `requiresPreview` flag and use it on supported builds.

**Detection pattern:**
```typescript
const supportsAddPicture = typeof (slide.shapes as any).addPicture === 'function';
if (supportsAddPicture) {
  // Preview path: precise positioning
  (slide.shapes as any).addPicture(base64, { left, top, width, height });
} else {
  // Fallback: insert at cursor via Common API
  Office.context.document.setSelectedDataAsync(base64, {
    coercionType: Office.CoercionType.Image,
    imageWidth: width, imageHeight: height
  }, () => {});
}
```

---

### Gap 2 — No chart creation API

**Gap:** PowerPoint JS API has no `charts.add()` equivalent. Excel's chart API does not exist in the PPT API. Charts in PPT presentations can be detected by shape type (`PowerPoint.ShapeType.chart`) but not created or modified.

**Workaround for MVP:** No charts in MVP. Text and tables only.

**Workaround for Sprint 3 (chart-as-image):**
```
User asks for a chart →
  FfP: generate chart data (JSON) via FAIT API →
  FfP: render Chart.js chart in an off-screen canvas element in the taskpane DOM →
  FfP: canvas.toDataURL('image/png') → base64 string →
  FfP: insert as image via Common API (Gap 1 workaround) or Preview addPicture
```

This gives a static snapshot — not live-linked to data. For a "here is a chart of Q3 performance" use case, this is entirely acceptable. The chart lives as an image on the slide; updating it requires generating a new one.

**Long-term alternative (Sprint 4+):** FAIT backend generates a chart image (matplotlib, Recharts SSR, or similar) and returns a base64 PNG directly. Offloads all chart rendering to the server, eliminates client-side canvas complexity. This is cleaner and produces higher-quality output.

---

### Gap 3 — No events in stable API

**Gap:** `onSlideSelectionChanged` is Preview-only. There are no stable events for slide navigation, shape selection, or text changes.

**Design response:** FfP is designed around deliberate user actions, not reactive automation. This is the right model for a presentation context anyway — presentations are not live data dashboards. The user clicks a shape, then interacts with FfP.

**Polling approach (optional):** A 2-second `setInterval` calling `getSelectedSlides()` provides "soft reactivity" — the slide context indicator updates automatically as the user navigates slides. This is FfE's polling model for range selection. Use the same pattern.

**Preview detection:**
```typescript
if ((context.presentation as any).onSlideSelectionChanged) {
  // Register event handler on supported builds
  (context.presentation as any).onSlideSelectionChanged.add(handler);
  await context.sync();
} else {
  // Fall back to polling
  setInterval(refreshSelection, 2000);
}
```

---

### Gap 4 — No slide reorder API

**Gap:** There is no `slide.moveTo(index)` method. The only workaround is `insertSlidesFromBase64` from a temporary presentation.

**Response:** Slide reordering is out of scope for all three initial sprints. FfP does not offer a "reorder slides" feature. If it becomes needed, the implementation is:
1. Export presentation to base64 (via `getFileAsync`)
2. Reconstruct desired order
3. Re-import via `insertSlidesFromBase64`
This is complex and destructive. Defer indefinitely.

---

### Gap 5 — Applying a layout to an existing slide

**Gap:** There is no API to change the layout of an existing slide. Layout is set at creation time (`slides.add({ layoutId: "..." })`). To change it, the slide must be deleted and re-created.

**Response:** FfP's template injection model (Sprint 3) uses `insertSlidesFromBase64` to add pre-built slides rather than creating new slides and applying layouts. This sidesteps the gap entirely.

---

## 8. Architecture

### Repo Location

```
~/projects/fait-for-powerpoint/
```

Independent repo from FfE. Not a monorepo with FfE — different manifest GUIDs, different deployment paths, different PowerPoint API surface.

### Directory Structure

```
fait-for-powerpoint/
├── src/
│   └── taskpane/
│       ├── index.html          ← HTML entry point (Sprint 1: commit this first)
│       ├── index.tsx           ← React root, Office.onReady() wrapper
│       ├── App.tsx             ← Root component (settings gate + main layout)
│       ├── components/
│       │   ├── ChatPanel.tsx   ← Port from FfE; PPT-specific context formatting
│       │   ├── ChatInput.tsx   ← Port from FfE; no selection toggle (whole slide is context)
│       │   ├── ShapePreview.tsx    ← Pending text before applying (Accept/Reject)
│       │   ├── SlideContext.tsx    ← Shows current slide # and shape count
│       │   └── SettingsPanel.tsx  ← API key, model, FORGE KB toggles
│       ├── hooks/
│       │   ├── usePptContext.ts   ← Polls getSelectedSlides/Shapes every 2s
│       │   └── useChat.ts         ← Port from FfE with minimal changes
│       └── services/
│           ├── pptReader.ts       ← getSelectionContext(), getAllShapeText()
│           ├── pptWriter.ts       ← applyTextToShape(), writeNotes(), tagShape()
│           ├── settings.ts        ← Port from FfE with working OfficeRuntime shim
│           ├── faitApi.ts         ← Port from FfE — identical (same backend)
│           └── pptContextFormatter.ts  ← Format slide + shapes into prompt context
├── public/
│   ├── commands.html          ← Required for manifest; empty function commands
│   ├── manifest.xml           ← Prod manifest (committed once, never edited)
│   └── assets/
│       ├── icon-16.png
│       ├── icon-32.png
│       └── icon-80.png
├── manifest.local.xml         ← Dev manifest (localhost:3001 URLs)
├── package.json
├── vite.config.ts
├── tsconfig.json
└── .gitignore
```

### Vite Config (Day 1 — Do Not Deviate)

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [react(), mkcert()],
  server: {
    port: 3001,  // Not 3000 — avoid collision with FfE dev server
    https: true,
    host: '127.0.0.1',
  },
  build: {
    outDir: 'dist',
    target: 'es2018',
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html',  // HTML entry point — NOT index.tsx
        commands: 'public/commands.html',
      },
      output: {
        // No format: 'iife' — let Rollup handle ES modules normally
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },
  base: '/ppt-addin/',  // Must match deployment path and manifest URLs
});
```

### Manifest Structure

```xml
<!-- manifest.xml — abbreviated key sections -->
<Id>b2c3d4e5-f6a7-8901-bcde-f12345678901</Id>  <!-- New GUID — not FfE's GUID -->
<Version>1.0.0.0</Version>
<DisplayName DefaultValue="FAIT for PowerPoint" />

<!-- API requirement: 1.5 minimum -->
<Requirements>
  <Sets DefaultMinVersion="1.5">
    <Set Name="PowerPointApi" MinVersion="1.5" />
  </Sets>
</Requirements>

<!-- Host: PowerPoint only — not Excel, not Word -->
<Hosts>
  <Host Name="Presentation" />
</Hosts>

<!-- TaskPane source location -->
<DefaultSettings>
  <SourceLocation DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/taskpane/index.html" />
</DefaultSettings>
```

### Deployment Path in FAIT wwwroot

```
fait/src/FortressAI.Web/wwwroot/
├── excel-addin/          ← FfE static files (existing)
│   ├── taskpane/
│   │   └── index.html
│   └── assets/
└── ppt-addin/            ← FfP static files (new)
    ├── taskpane/
    │   └── index.html
    └── assets/
```

**Build pipeline:** FfP's `dist/` is copied to `fait/src/FortressAI.Web/wwwroot/ppt-addin/` as part of the FAIT Docker build. Same pattern as FfE (currently at `fait/src/FortressAI.Web/wwwroot/excel-addin/`).

**FAIT Dockerfile addition:**
```dockerfile
# Copy FfP static assets (same pattern as FfE)
COPY --from=ffp-build /app/fait-for-powerpoint/dist ./wwwroot/ppt-addin
```

This requires `fait-for-powerpoint/` to be within the Docker build context, which means either:
- Adding it to the FAIT repo subdirectory (simplest — mirrors how `fait-for-excel/` might move into the fip repo)
- Expanding the buildspec to include a separate build step that copies FfP dist into the FAIT build context before Docker build

**Decision for Fred:** Does `fait-for-powerpoint/` live inside the `fip/` monorepo (alongside `fip/fait/`) or as a standalone repo? 

**Recommendation:** Inside `fip/` at `fip/fait-for-powerpoint/`. Simplifies Docker build context (already expanded for FfE lessons). Avoids a third repo to manage. The FfE lessons on buildspec context expansion are already documented in WI813.

### FAIT Backend Changes Required

**Sprint 1:** None. FfP uses the same `/api/chat` endpoint as FfE. The system prompt passed from FfP's task pane tells FAIT it's operating in a PowerPoint context.

**Sprint 2:** None for core features. FORGE search uses the existing FORGE search endpoint.

**Sprint 3 (template injection):** FORGE needs to support a "template" node type that stores a base64 PPTX fragment. This is a FAIT/FORGE backend addition — not a FAIT web server change, but a FORGE KB data structure change. Details TBD by Sprint 3 planning.

**Future (chart-as-image server-side):** FAIT API returns a `chartImage` base64 field in certain response types. This is cleaner than client-side Chart.js rendering. Not required for MVP or Sprint 2.

---

## Appendix A: FfP vs FfE Differences — Quick Reference

| Concern | FfE (Excel) | FfP (PowerPoint) |
|---------|------------|-----------------|
| API namespace | `Excel.run()` | `PowerPoint.run()` |
| Context unit | Selected range (cells) | Selected slide + shapes |
| Write target | Range values/formulas | Shape textFrame.textRange.text |
| Selection polling | `getSelectedRange()` | `getSelectedSlides()` + `getSelectedShapes()` |
| Events | `worksheet.onChanged` (stable) | `onSlideSelectionChanged` (Preview only) |
| Write confirmation | `WriteSuggestionsDialog` (cell-by-cell) | `ShapePreview` (shape-by-shape) |
| Dev port | 3000 | 3001 |
| Build base | `/excel-addin/` | `/ppt-addin/` |
| API req set | ExcelApi 1.13 | PowerPointApi 1.5 (MVP) / 1.8 (target) |
| Charts | Full create/modify API | No chart API; image workaround |
| Tables | Already in ExcelApi 1.1 | Tables added in 1.8 |
| Images | No insert needed | `addPicture` Preview; Common API fallback |

---

## Appendix B: What "Template-First" Means in Practice

FfP's architectural philosophy for complex elements (charts, images, complex layouts) is **template-first**: the hard parts are baked into a PPTX template in FORGE, and FfP's job is to populate the content.

**Example:** Monthly Portfolio Update deck.
1. FORGE stores `template_portfolio_update.pptx` — a 10-slide base with correct layout, logo placements, chart placeholders, brand colors, and existing animations.
2. User opens the template in PowerPoint.
3. FfP loads. User selects the "Executive Summary" text box.
4. User asks: "Summarize our Q1 performance for the executive summary."
5. FfP queries FORGE for Q1 performance data, generates a 150-word summary.
6. FfP shows preview. User clicks Apply.
7. The shape is updated. Layout, brand, animations unchanged.

This is the model FfP optimizes for. Not "generate a deck from nothing" — but "make my existing template say the right thing."

---

_Spec by Reed Richards | FfP is FfE's sibling — same foundation, PowerPoint surface._
