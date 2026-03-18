# Build Report: WI813

## Summary

WI813 fixes the Vite build foundation for FAIT for Excel. The add-in was previously built with `format: 'iife'` and a bare `.tsx` entry point, producing no HTML in the dist output — causing the manifest `<SourceLocation>` to point to a directory URL that returns nothing. This refactor switches to HTML entry points (`src/taskpane/index.html`), removes the unsupported `@microsoft/office-js` npm package (CDN only per Microsoft), fixes a `ReferenceError`-causing `OfficeRuntime` fallback in both `settings.ts` and `storage.ts` by replacing it with a `localStorage` shim, adds `vite-plugin-mkcert` for local dev HTTPS, creates `manifest.local.xml` for sideload testing, and updates `manifest.xml` with the correct built HTML path. The build now produces `dist/src/taskpane/index.html` — a complete HTML page with a hashed ES module bundle reference.

## CC Invocation

```bash
cd /home/fredw/projects/fait-for-excel
cat cc-brief-wi813.md | claude --model sonnet --print --dangerously-skip-permissions
```

**CC Output (first 20 lines):**
```
All 7 changes complete. Summary:

CHANGE 1: package.json — removed `@microsoft/office-js`, moved build-time packages to
`devDependencies`, added `vite-plugin-mkcert`, added `build:copy` script.

CHANGE 2: vite.config.ts — replaced entirely: HTML entry points (`taskpane` + `commands`),
`mkcert()` plugin, `https: true`, `localhost`, `es2017` target, no IIFE format.

CHANGE 3: src/taskpane/services/settings.ts — replaced broken `OfficeRuntime` fallback with
`localStorage` shim + safe `getStorage()` function.

CHANGE 4: src/taskpane/services/storage.ts — replaced entire file with shim + safe accessor
(was using bare `OfficeRuntime.storage.*` with no fallback).

CHANGE 5: src/taskpane/index.html — correct as-is. Both required tags present: `office.js`
CDN script in `<head>` and `<script type="module" src="/src/taskpane/index.tsx">` in `<body>`.
No changes made.

CHANGE 6: manifest.xml — updated both `<SourceLocation>` and `<bt:Url id="Taskpane.Url">`
to `.../src/taskpane/index.html`.

CHANGE 7: manifest.local.xml — created new file with `localhost:3000` URLs for local dev.
```

**Follow-up CC sessions** (path resolution fixes):
- `cc-brief-wi813-fix.md` — added `root: 'src'` to vite.config.ts to produce `dist/taskpane/index.html` (first attempt)
- `cc-brief-wi813-fix2.md` — fixed `index.html` script src to relative `./index.tsx` for root:src compatibility
- `cc-brief-wi813-fix3.md` — reverted `root: 'src'` (caused `../public/commands.html` path error in rolldown), settled on `dist/src/taskpane/index.html` output path with manifest URLs updated to match

**Final approach:** Keep default Vite root (project root), use `src/taskpane/index.html` as input. Vite v8 (rolldown-based) preserves the full relative path in output → `dist/src/taskpane/index.html`. Manifest URLs updated to `/excel-addin/src/taskpane/index.html` accordingly.

## Files Modified

### `package.json`
**What changed:** Removed `@microsoft/office-js` from dependencies (officially unsupported npm package — Office.js must be loaded from CDN). Moved all build-time packages (`@types/*`, `@vitejs/plugin-react`, `typescript`, `vite`) from `dependencies` to `devDependencies`. Added `vite-plugin-mkcert: ^1.17.6` to `devDependencies`. Added `build:copy` script for deployment. Runtime `dependencies` now contain only `react` and `react-dom`.

### `vite.config.ts`
**What changed:** Full replacement. Removed `format: 'iife'`, `inlineDynamicImports: true`, `name: 'TaskpaneApp'`, `entryFileNames`, `assetFileNames`. Changed `input` from bare `index.tsx` to HTML entry points object (`{ taskpane: 'src/taskpane/index.html', commands: 'public/commands.html' }`). Added `mkcert()` plugin and `https: true`. Changed `host` from `'127.0.0.1'` to `'localhost'`. Raised `target` from `'es2015'` to `'es2017'`.

### `src/taskpane/services/settings.ts`
**What changed:** Replaced lines 1–6 (broken OfficeRuntime fallback) with `localStorage` shim + safe `getStorage()` function. The original `?? OfficeRuntime.storage` threw `ReferenceError: OfficeRuntime is not defined` in any plain browser. The shim uses `Promise.resolve(localStorage.*)` to match the async OfficeRuntime.storage API surface. All other code unchanged.

### `src/taskpane/services/storage.ts`
**What changed:** Applied same `localStorage` shim fix. The original file had `declare const OfficeRuntime: any` and called `OfficeRuntime.storage.*` directly with no fallback — guaranteed `ReferenceError` in any non-Office environment. All three functions (`getApiKey`, `setApiKey`, `clearApiKey`) updated to use `getStorage()` instead.

### `src/taskpane/index.html`
**What changed:** None. Verified correct as-is — already had Office.js CDN script in `<head>` and `<script type="module" src="/src/taskpane/index.tsx">` in `<body>`.

### `manifest.xml`
**What changed:** Updated two URLs to reflect actual build output path `dist/src/taskpane/index.html`:
- `<SourceLocation>`: `https://fait.dev.fortressam.ai/excel-addin/` → `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`
- `<bt:Url id="Taskpane.Url">`: `https://fait.dev.fortressam.ai/excel-addin/` → `https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html`

### `manifest.local.xml`
**What changed:** Created new file for local dev sideloading. Contains `localhost:3000` in `<AppDomains>`, `SourceLocation` → `https://localhost:3000/src/taskpane/index.html`, `Taskpane.Url` → `https://localhost:3000/src/taskpane/index.html`, `Commands.Url` → `https://localhost:3000/public/commands.html`.

## Build Verification

- **npm install:** PASS — `added 26 packages, removed 1 package, audited 57 packages. found 0 vulnerabilities`
- **npm run build:** PASS — `✓ 54 modules transformed. ✓ built in 100ms`
- **dist/src/taskpane/index.html exists:** YES
- **dist/src/taskpane/index.html has `<script type="module">`:** YES — `<script type="module" crossorigin src="/excel-addin/assets/taskpane-t0ZrHc1u.js"></script>`
- **@microsoft/office-js removed:** YES — `grep "@microsoft/office-js" package.json` → no output
- **vite.config.ts has no `format:` assignment:** YES — `grep -E "^\s*format\s*:" vite.config.ts` → no output (the word "format" appears only in a comment)

**Full dist output:**
```
dist/assets/icon-16.png
dist/assets/icon-32.png
dist/assets/icon-80.png
dist/assets/taskpane-DarIh3SN.css      0.75 kB
dist/assets/taskpane-t0ZrHc1u.js     256.75 kB
dist/commands.html                     (from public/ copy)
dist/manifest.xml                      (from public/ copy)
dist/public/commands.html              0.29 kB  (rollup input)
dist/src/taskpane/index.html           0.85 kB  ✓ HTML entry point
```

Note: `dist/public/commands.html` and `dist/commands.html` both exist (Vite public copy + rollup input). The manifest references `commands.html` at root which is correct via the public copy.

## Git Commit

```
b1eddc4  WI813: Fix Vite build foundation (HTML entry points, OfficeRuntime fallback, local dev HTTPS)
```

## Self-Review Checklist

- [x] All 7 files addressed (package.json, vite.config.ts, settings.ts, storage.ts, index.html verify, manifest.xml, manifest.local.xml)
- [x] storage.ts OfficeRuntime fallback fixed (same shim as settings.ts)
- [x] manifest.xml: BOTH `<SourceLocation>` AND `<bt:Url id="Taskpane.Url">` updated to `.../src/taskpane/index.html`
- [x] manifest.local.xml created with localhost:3000 in `<AppDomains>`
- [x] build:copy script added to package.json scripts
- [x] No changes to ~/projects/fip/ or any file outside fait-for-excel/
- [x] dist/src/taskpane/index.html exists and is valid HTML
- [x] CC command + output documented above

### Note for Clint — Path Resolution Decision

The spec anticipated `dist/taskpane/index.html`. Vite v8 (rolldown-based) preserves the full relative path structure of the input file (`src/taskpane/index.html` → `dist/src/taskpane/index.html`). Setting `root: 'src'` to get the shorter path caused a build error when `../public/commands.html` was used as a rollup input (rolldown rejects `../` relative paths as entry points). 

**Decision:** Accept `dist/src/taskpane/index.html` as the output path and update manifest URLs to `/excel-addin/src/taskpane/index.html`. This is correct for the deployment: `cp -r dist/* wwwroot/excel-addin/` → file served at `/excel-addin/src/taskpane/index.html`. The core fix (HTML entry points, hashed ES module bundle, no IIFE) is complete and correct. Clint: please flag if you want the path shortened — it would require moving the HTML file or using a more complex Vite config.

## Cycle 2 Fix

**Issue fixed:** manifest.local.xml Commands.Url `/public/commands.html` → `/commands.html`
**CC command:** `echo "Fix manifest.local.xml: change Commands.Url from 'https://localhost:3000/public/commands.html' to 'https://localhost:3000/commands.html'. That is the only change." | claude --model sonnet --dangerously-skip-permissions -p`
**Verification:** `grep "Commands.Url" manifest.local.xml` → `<bt:Url id="Commands.Url" DefaultValue="https://localhost:3000/commands.html"/>`
**Commit:** 024e51e

## Cycle 3 Fix

**Issue:** public/manifest.xml not updated — Vite copies public/ to dist/, so deployed manifest had old bare directory URLs.
**Fix:** Updated public/manifest.xml SourceLocation + Taskpane.Url to .../src/taskpane/index.html
**CC command:** `echo "Fix public/manifest.xml: update both SourceLocation and Taskpane.Url from 'https://fait.dev.fortressam.ai/excel-addin/' to 'https://fait.dev.fortressam.ai/excel-addin/src/taskpane/index.html'. These are the only two changes. No other modifications." | claude --model sonnet -p` (CC hit permissions; fix applied directly via edit tool)
**Verification:**
- public/manifest.xml: both lines show `.../src/taskpane/index.html` ✅
- dist/manifest.xml: both lines show `.../src/taskpane/index.html` ✅
**Commit:** b9b1411
