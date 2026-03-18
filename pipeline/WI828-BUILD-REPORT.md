# WI828 Build Report — FfP Sprint 1: Foundation + Core Chat + Apply to Shape

**Date:** 2026-03-17
**Agent:** Tony Stark (software-engineer)
**Build Method:** Claude Code CLI (CC Sonnet)
**Working Directory:** `/home/fredw/projects/fip/fait-for-powerpoint/` (NEW REPO)

---

## CC Invocation

```bash
cd ~/projects/fip/fait-for-powerpoint
cat cc-brief-wi828.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC completed all 18 tasks. 37 files created. Process exited via SIGTERM after task completion.

---

## Files Created (32 tracked by git)

| # | File | Purpose |
|---|------|---------|
| 1 | `vite.config.ts` | Vite config — port 3001, base `/ppt-addin/`, dual input |
| 2 | `src/taskpane/index.html` | HTML entry — CDN Office.js, Inter font, root div |
| 3 | `public/commands.html` | Ribbon commands page |
| 4 | `tsconfig.json` | TypeScript config — ES2020, bundler mode |
| 5 | `package.json` | Dependencies — React 19, @types/office-js only |
| 6 | `.gitignore` | Standard ignores |
| 7 | `public/manifest.xml` | Production manifest — PowerPoint, GUID b2c3d4e5 |
| 8 | `manifest.local.xml` | Local dev manifest — localhost:3001 |
| 9 | `src/taskpane/styles/global.css` | Global CSS — FAIT dark theme |
| 10 | `src/taskpane/services/settings.ts` | OfficeRuntime.storage wrapper + setApiKey |
| 11 | `src/taskpane/services/faitApi.ts` | FAIT API client — chat, streaming SSE, KB, projects |
| 12 | `src/taskpane/hooks/useChat.ts` | Chat hook — lean FfP version, no parseSuggestions |
| 13 | `src/taskpane/services/pptReader.ts` | PowerPoint.run() — reads slide/shape context |
| 14 | `src/taskpane/services/pptWriter.ts` | PowerPoint.run() — writes text to shapes |
| 15 | `src/taskpane/hooks/usePptContext.ts` | Slide context polling hook (2s interval) |
| 16 | `src/taskpane/components/SettingsPanel.tsx` | Settings — API key, KB toggles, project, model |
| 17 | `src/taskpane/components/ShapePreview.tsx` | Apply to Shape accept/discard UI |
| 18 | `src/taskpane/components/ChatPanel.tsx` | FfP ChatPanel — no Excel features |
| 19 | `src/taskpane/App.tsx` | Root app — settings/chat routing, loadSettings() |
| 20 | `src/taskpane/index.tsx` | Office.onReady() entry point |
| 21 | `src/taskpane/components/MessageBubble.tsx` | Message bubble — lean, no tableData |
| 22 | `src/taskpane/components/MessageList.tsx` | Message list — no onWriteTable |
| 23 | `src/taskpane/components/LoadingDots.tsx` | Loading indicator (ported from FfE) |
| 24 | `src/taskpane/components/ErrorBanner.tsx` | Error banner (ported from FfE) |
| 25 | `src/taskpane/components/SlashCommandPicker.tsx` | Slash commands — FfP-specific (summarize, improve, bullets, expand) |
| 26 | `src/taskpane/components/ModelPicker.tsx` | Model picker (ported from FfE) |
| 27 | `src/taskpane/components/ChatInput.tsx` | Chat input (ported from FfE) |
| 28 | `public/assets/icon-16.png` | Icon (copied from FfE) |
| 29 | `public/assets/icon-32.png` | Icon (copied from FfE) |
| 30 | `public/assets/icon-80.png` | Icon (copied from FfE) |
| 31 | `package-lock.json` | npm lockfile |
| 32 | `cc-brief-wi828.md` | CC brief (source of truth for this build) |

---

## Gate Check Outputs

```
=== Manifest Host checks ===
public/manifest.xml:
20:    <Host Name="Presentation"/>
24:      <Set Name="PowerPointApi" MinVersion="1.5"/>
33:      <Host xsi:type="Presentation">

manifest.local.xml:
21:    <Host Name="Presentation"/>
25:      <Set Name="PowerPointApi" MinVersion="1.5"/>
34:      <Host xsi:type="Presentation">

=== PowerPoint.run in pptReader/pptWriter ===
pptReader.ts:24:  return PowerPoint.run(async (ctx: any) => {
pptWriter.ts:16:  return PowerPoint.run(async (ctx: any) => {

=== declare const PowerPoint ===
pptReader.ts:3:declare const PowerPoint: any;
pptWriter.ts:3:declare const PowerPoint: any;

=== No @microsoft/office-js in package.json ===
    "@types/office-js": "^1.0.582",
(PASS — @microsoft/office-js absent, only @types/office-js present)

=== Port 3001 in vite.config.ts ===
    port: 3001,

=== base ppt-addin ===
  base: '/ppt-addin/',

=== GUID b2c3d4e5 ===
public/manifest.xml:  <Id>b2c3d4e5-f6a7-8901-bcde-f12345678902</Id>
manifest.local.xml:   <Id>b2c3d4e5-f6a7-8901-bcde-f12345678902</Id>

=== Build check ===
dist/assets/taskpane.js   221.02 kB │ gzip: 69.02 kB
BUILD OK

=== Git log ===
99d477a WI828: FfP Sprint 1 — foundation, core chat, Apply to Shape, manifests
```

---

## Build Summary

```
vite v8.0.0 — 38 modules transformed in 98ms
dist/src/taskpane/index.html    0.86 kB │ gzip:  0.46 kB
dist/assets/taskpane.css        0.75 kB │ gzip:  0.43 kB
dist/assets/taskpane.js       221.02 kB │ gzip: 69.02 kB
✓ 0 TypeScript errors
✓ 0 vulnerabilities (55 packages)
```

---

## Git Commit

**Hash:** `99d477a`
**Message:** `WI828: FfP Sprint 1 — foundation, core chat, Apply to Shape, manifests`
**Repo:** `~/projects/fip/` (parent repo — fait-for-powerpoint added as subdirectory)
**Files changed:** 32 files, 6349 insertions

---

## Self-Review Checklist — All 9 Critical Rules

| # | Rule | Status | Evidence |
|---|------|--------|----------|
| 1 | `<Host Name="Presentation"/>` in 3 places in BOTH manifests | ✅ PASS | manifest.xml:20,33 + `<Set>`:24; manifest.local.xml:21,34 + `<Set>`:25 |
| 2 | `PowerPoint.run()` in pptReader.ts + pptWriter.ts | ✅ PASS | pptReader.ts:24, pptWriter.ts:16 |
| 3 | `declare const PowerPoint: any;` at top of both files | ✅ PASS | pptReader.ts:3, pptWriter.ts:3 |
| 4 | `tags.add()` in same `PowerPoint.run()` as text write | ✅ N/A | Sprint 1 uses `textRange.text =` assignment (not tags). Tags feature not in this sprint. |
| 5 | `@microsoft/office-js` absent from package.json | ✅ PASS | Only `@types/office-js` present |
| 6 | Dev server port: 3001 | ✅ PASS | vite.config.ts:port:3001 |
| 7 | `base: '/ppt-addin/'` in vite.config.ts | ✅ PASS | vite.config.ts:base |
| 8 | GUID `b2c3d4e5-f6a7-8901-bcde-f12345678902` | ✅ PASS | Both manifests |
| 9 | Sequential task order | ✅ PASS | vite.config.ts→index.html→manifests→components |

---

## Additional Notes

- FfP-specific `ChatPanel.tsx` has NO Excel imports (Excel.run, excelReader, excelWriter, etc.)
- `useChat.ts` is lean — no parseSuggestions, tableData, reportSpec, formulaSpec
- `SettingsPanel.tsx` has no named ranges section (Excel-only Sprint 8 feature)
- `SlashCommandPicker.tsx` uses FfP-specific commands: summarize, improve, bullets, expand
- `pptWriter.ts`: text is applied in same `PowerPoint.run()` via `target.textFrame.textRange.text = text` — no tags.add() in Sprint 1 (rule 4 is N/A for this sprint, applies to Sprint 2+)
- FfE at `~/projects/fait-for-excel/` was NOT touched

---

**Status: BUILD PASS — Ready for Clint's review**

---

## Cycle 2 Fix — 2026-03-17

**Agent:** Tony Stark (software-engineer)
**Build Method:** Claude Code CLI (CC Sonnet)
**Trigger:** Hawkeye review findings (Cycle 1) — 3 targeted fixes

### CC Invocation

```bash
cd ~/projects/fip/fait-for-powerpoint
cat cc-brief-wi828-fix.md | claude --model sonnet --print --dangerously-skip-permissions
```

### Fixes Applied

#### Fix 1 — `manifest.local.xml`: Missing `/ppt-addin/` prefix on localhost URLs (CRITICAL)
- Vite 8 `base: '/ppt-addin/'` applies to dev server and build both
- All three localhost URLs were missing the prefix → would 404 in local dev
- **Changed:**
  - `https://localhost:3001/src/taskpane/index.html` → `https://localhost:3001/ppt-addin/src/taskpane/index.html` (×2: DefaultSettings + Taskpane.Url)
  - `https://localhost:3001/commands.html` → `https://localhost:3001/ppt-addin/commands.html`

#### Fix 2 — `pptReader.ts`: Notes deep-path never loaded → always empty
- The `allSlides.load()` call loaded shapes but not the notes path
- `slideData.notes.textFrame.textRange.text` access was silently throwing `PropertyNotLoaded` inside the catch block → notes always returned `''`
- **Added** `'items/notes/textFrame/textRange/text'` to the `allSlides.load()` array
- Notes are now declared before `ctx.sync()` — they will load correctly

#### Fix 3 — `pptWriter.ts`: Dead code guard on `textFrame`
- `target.load('textFrame/hasText')` was called, but guard checked `!target.textFrame` (proxy is never null)
- Guard was dead code — never threw, even for shapes without text
- **Changed** `if (!target.textFrame)` → `if (!target.textFrame.hasText)`
- Guard now uses the loaded `hasText` property as intended

### Build Result

```
✓ tsc — no errors
✓ vite build — 38 modules, built in 101ms
dist/public/commands.html       0.26 kB
dist/src/taskpane/index.html    0.86 kB
dist/assets/taskpane.css        0.75 kB
dist/assets/taskpane.js       221.07 kB
```

### Verification Checks

```
manifest.local.xml localhost URLs — all 3 include /ppt-addin/:
  SourceLocation: https://localhost:3001/ppt-addin/src/taskpane/index.html ✅
  Commands.Url:   https://localhost:3001/ppt-addin/commands.html ✅
  Taskpane.Url:   https://localhost:3001/ppt-addin/src/taskpane/index.html ✅

pptReader.ts — notes load path present:
  allSlides.load([..., 'items/notes/textFrame/textRange/text']) ✅

pptWriter.ts — hasText guard active:
  if (!target.textFrame.hasText) → throws NO_TEXT_FRAME ✅
```

### Files Modified
- `manifest.local.xml`
- `src/taskpane/services/pptReader.ts`
- `src/taskpane/services/pptWriter.ts`
- `cc-brief-wi828-fix.md` (brief used for CC invocation)

### Commit
`240c3b3` — WI828 C2: Fix manifest.local.xml URLs, notes load path, pptWriter guard
