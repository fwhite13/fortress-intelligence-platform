# FfE Refactor Spec — Build Foundation Fix

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Deploy:** Rhodey (after Clint sign-off) → fred-dev → fait-prod

---

## Objective

The add-in is currently working (chat functional in Excel Online as of ~03:00 EDT 2026-03-16) but the build config is fundamentally wrong and will cause ongoing maintenance problems. This refactor fixes the foundation **without adding any Sprint 2 features**. Chat in Excel Online must still work after this lands.

**Strictly in scope:**
- Fix Vite build config (IIFE → HTML entry points)
- Fix `settings.ts` OfficeRuntime fallback bug
- Remove unsupported `@microsoft/office-js` npm package
- Add HTTPS for local dev (`vite-plugin-mkcert`)
- Create `manifest.local.xml` for dev sideloading

**Strictly out of scope:**
- Any new UI components or features
- Sprint 2 Excel read/write capabilities
- Changes to FAIT backend, FIRM, FORMS, or FIP

---

## Root Problems (from Bruce's Research)

| # | Problem | Symptom | Fix |
|---|---------|---------|-----|
| 1 | `vite.config.ts` uses `input: 'src/taskpane/index.tsx'` + `format: 'iife'` | No HTML in build output; manifest `<SourceLocation>` gets a directory URL that returns nothing | Switch to HTML entry points |
| 2 | `settings.ts` fallback: `OfficeRuntime.storage` (undeclared variable) | `ReferenceError` crash when running in plain browser for dev/testing | Replace with `localStorage` shim |
| 3 | `@microsoft/office-js` in `dependencies` | Officially unsupported npm package; CDN is the correct source | Remove from package.json |
| 4 | No HTTPS in dev server | Excel refuses to load taskpane from `http://` | Add `vite-plugin-mkcert` |
| 5 | No `manifest.local.xml` | Developers must hand-edit manifest.xml to test locally | Create local dev manifest |

---

## Parallelization Map

All changes are in `fait-for-excel/` only. No shared files between tasks.

```
Single sequential CC session (all changes are small, 6 files total):
  1. package.json          — remove @microsoft/office-js, add vite-plugin-mkcert devDep
  2. vite.config.ts        — HTML entry points, mkcert plugin, HTTPS, raise target
  3. settings.ts           — fix OfficeRuntime fallback
  4. manifest.local.xml    — new file: copy of manifest.xml with localhost URLs
  5. index.html            — verify only (no changes expected — already correct)
  6. (npm install)         — after package.json changes
```

No parallelization needed — this is a fast, focused session.

---

## File-Level Spec

### 1. `package.json` — 2 changes

**Remove from `dependencies`:**
```json
"@microsoft/office-js": "^1.1.110"
```
Reason: officially unsupported per Microsoft. Office.js is loaded from CDN in `index.html`. The npm package is a stale mirror that is no longer maintained.

**Add to `devDependencies`** (create the section if it doesn't exist — currently everything is in `dependencies`):
```json
"devDependencies": {
  "vite-plugin-mkcert": "^1.17.6"
}
```

**Also move these from `dependencies` to `devDependencies`** (they are build-time only, not runtime):
```json
"devDependencies": {
  "@types/node": "^25.5.0",
  "@types/office-js": "^1.0.582",
  "@types/react": "^19.2.14",
  "@types/react-dom": "^19.2.3",
  "@vitejs/plugin-react": "^6.0.1",
  "typescript": "^5.9.3",
  "vite": "^8.0.0",
  "vite-plugin-mkcert": "^1.17.6"
}
```

**`dependencies` after change** (runtime only):
```json
"dependencies": {
  "react": "^19.2.4",
  "react-dom": "^19.2.4"
}
```

**`scripts` — add `build:copy`** (convenience script for deployment, see §6):
```json
"scripts": {
  "dev": "vite",
  "build": "tsc && vite build",
  "build:copy": "tsc && vite build && cp -r dist/* ../fip/fait/src/FortressAI.Web/wwwroot/excel-addin/",
  "preview": "vite preview"
}
```

---

### 2. `vite.config.ts` — full replacement

**Replace the entire file with:**

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [
    react(),
    mkcert(), // generates locally-trusted HTTPS cert for dev
  ],

  server: {
    port: 3000,
    host: 'localhost',
    https: true, // required — Office Add-ins reject http://
  },

  build: {
    outDir: 'dist',
    target: 'es2017', // raised from es2015; WebView2/WKWebView/Edge all support es2017
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html', // HTML entry point — Vite handles bundling
        commands: 'public/commands.html',    // ribbon commands page
      },
      // No output.format override — defaults to ES modules, which is correct
    },
  },

  base: '/excel-addin/', // must match deployment URL prefix and manifest URLs
});
```

**What changed and why:**
- `input` is now `src/taskpane/index.html` (not `index.tsx`) — Vite reads the HTML, finds the `<script type="module">` tag, bundles everything, and emits a complete HTML+assets package
- `format: 'iife'` removed — ES module output is correct for modern Office runtimes (WebView2, WKWebView, Edge)
- `inlineDynamicImports: true` removed — not needed with HTML entry points
- `name: 'TaskpaneApp'` removed — IIFE-specific, not needed
- `entryFileNames`/`assetFileNames` overrides removed — Vite's defaults are fine
- `mkcert()` added — auto-generates trusted HTTPS cert on first `npm run dev`
- `https: true` on server
- `host: 'localhost'` (was `127.0.0.1` — `localhost` works better with mkcert)
- `target: 'es2017'` (was `es2015` — unnecessary restriction)

---

### 3. `src/taskpane/services/settings.ts` — fix OfficeRuntime fallback

**Current broken code:**
```typescript
declare const OfficeRuntime: any;
const getStorage = () => (window as any).OfficeRuntime?.storage ?? OfficeRuntime.storage;
```

The `?? OfficeRuntime.storage` fallback throws `ReferenceError: OfficeRuntime is not defined` when running in a plain browser (local dev), because `OfficeRuntime` is a global injected by the Office host — it doesn't exist in a plain browser tab.

**Replace lines 1–5 (the declare + getStorage function) with:**

```typescript
// localStorage shim — used when OfficeRuntime is not available (plain browser / dev)
const localStorageShim = {
  getItem: (key: string): Promise<string | null> =>
    Promise.resolve(localStorage.getItem(key)),
  setItem: (key: string, value: string): Promise<void> =>
    Promise.resolve(void localStorage.setItem(key, value)),
  removeItem: (key: string): Promise<void> =>
    Promise.resolve(void localStorage.removeItem(key)),
};

// Safe accessor — checks at call time, not module load time.
// In Excel Online, OfficeRuntime.storage IS backed by localStorage anyway,
// so the shim is semantically equivalent for the web scenario.
function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? localStorageShim;
}
```

**Everything else in `settings.ts` stays unchanged** — `FaitSettings` interface, `loadSettings()`, `saveSetting()` all work correctly as-is.

---

### 4. `src/taskpane/index.html` — verify only, no changes expected

The existing file is already correct:
```html
<script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>
...
<script type="module" src="/src/taskpane/index.tsx"></script>
```

✅ Office.js loaded from CDN (not npm package)  
✅ `type="module"` on the app script  
✅ Office.js loads before app script due to DOM order

**Tony: verify the file matches the above. If it does, no changes needed.**

---

### 5. `manifest.local.xml` — new file for local dev

Create `manifest.local.xml` as a copy of `manifest.xml` with three URL substitutions. The production `manifest.xml` is **not modified**.

```bash
cp manifest.xml manifest.local.xml
```

Then in `manifest.local.xml`, make these replacements:

| Element | Production value | Local dev value |
|---------|-----------------|-----------------|
| `<SourceLocation>` in `<DefaultSettings>` | `https://fait.dev.fortressam.ai/excel-addin/` | `https://localhost:3000/src/taskpane/index.html` |
| `<bt:Url id="Taskpane.Url">` | `https://fait.dev.fortressam.ai/excel-addin/` | `https://localhost:3000/src/taskpane/index.html` |
| `<bt:Url id="Commands.Url">` | `https://fait.dev.fortressam.ai/excel-addin/commands.html` | `https://localhost:3000/public/commands.html` |
| Icon URLs | `https://fait.dev.fortressam.ai/excel-addin/assets/icon-*.png` | can stay as prod URLs (icons load fine from prod during local dev) |

**Full `manifest.local.xml` for reference** — all other elements identical to `manifest.xml`:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
           xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
           xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
           xmlns:ov="http://schemas.microsoft.com/office/taskpaneappversionoverrides"
           xsi:type="TaskPaneApp">
  <Id>a1b2c3d4-e5f6-7890-abcd-ef1234567890</Id>
  <Version>1.0.0.0</Version>
  <ProviderName>Fortress Asset Management</ProviderName>
  <DefaultLocale>en-US</DefaultLocale>
  <DisplayName DefaultValue="FAIT for Excel (Local Dev)"/>
  <Description DefaultValue="Fortress AI assistant — local dev build"/>
  <IconUrl DefaultValue="https://fait.dev.fortressam.ai/excel-addin/assets/icon-32.png"/>
  <HighResolutionIconUrl DefaultValue="https://fait.dev.fortressam.ai/excel-addin/assets/icon-80.png"/>
  <SupportUrl DefaultValue="https://fait.dev.fortressam.ai"/>
  <AppDomains>
    <AppDomain>https://fait.dev.fortressam.ai</AppDomain>
    <AppDomain>https://localhost:3000</AppDomain>
  </AppDomains>
  <Hosts>
    <Host Name="Workbook"/>
  </Hosts>
  <Requirements>
    <Sets>
      <Set Name="ExcelApi" MinVersion="1.13"/>
    </Sets>
  </Requirements>
  <DefaultSettings>
    <SourceLocation DefaultValue="https://localhost:3000/src/taskpane/index.html"/>
  </DefaultSettings>
  <Permissions>ReadWriteDocument</Permissions>
  <VersionOverrides xmlns="http://schemas.microsoft.com/office/taskpaneappversionoverrides" xsi:type="VersionOverridesV1_0">
    <Hosts>
      <Host xsi:type="Workbook">
        <DesktopFormFactor>
          <GetStarted>
            <Title resid="GetStarted.Title"/>
            <Description resid="GetStarted.Description"/>
            <LearnMoreUrl resid="GetStarted.LearnMoreUrl"/>
          </GetStarted>
          <FunctionFile resid="Commands.Url"/>
          <ExtensionPoint xsi:type="PrimaryCommandSurface">
            <OfficeTab id="TabHome">
              <Group id="CommandsGroup">
                <Label resid="CommandsGroup.Label"/>
                <Icon>
                  <bt:Image size="16" resid="Icon.16x16"/>
                  <bt:Image size="32" resid="Icon.32x32"/>
                  <bt:Image size="80" resid="Icon.80x80"/>
                </Icon>
                <Control xsi:type="Button" id="TaskpaneButton">
                  <Label resid="TaskpaneButton.Label"/>
                  <Supertip>
                    <Title resid="TaskpaneButton.Label"/>
                    <Description resid="TaskpaneButton.Tooltip"/>
                  </Supertip>
                  <Icon>
                    <bt:Image size="16" resid="Icon.16x16"/>
                    <bt:Image size="32" resid="Icon.32x32"/>
                    <bt:Image size="80" resid="Icon.80x80"/>
                  </Icon>
                  <Action xsi:type="ShowTaskpane">
                    <TaskpaneId>ButtonId1</TaskpaneId>
                    <SourceLocation resid="Taskpane.Url"/>
                  </Action>
                </Control>
              </Group>
            </OfficeTab>
          </ExtensionPoint>
        </DesktopFormFactor>
      </Host>
    </Hosts>
    <Resources>
      <bt:Images>
        <bt:Image id="Icon.16x16" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/assets/icon-16.png"/>
        <bt:Image id="Icon.32x32" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/assets/icon-32.png"/>
        <bt:Image id="Icon.80x80" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/assets/icon-80.png"/>
      </bt:Images>
      <bt:Urls>
        <bt:Url id="Commands.Url" DefaultValue="https://localhost:3000/public/commands.html"/>
        <bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3000/src/taskpane/index.html"/>
        <bt:Url id="GetStarted.LearnMoreUrl" DefaultValue="https://fait.dev.fortressam.ai"/>
      </bt:Urls>
      <bt:ShortStrings>
        <bt:String id="GetStarted.Title" DefaultValue="FAIT for Excel"/>
        <bt:String id="CommandsGroup.Label" DefaultValue="Fortress AI"/>
        <bt:String id="TaskpaneButton.Label" DefaultValue="Open FAIT"/>
      </bt:ShortStrings>
      <bt:LongStrings>
        <bt:String id="GetStarted.Description" DefaultValue="Your Fortress AI assistant — ask questions, analyze data, write formulas."/>
        <bt:String id="TaskpaneButton.Tooltip" DefaultValue="Open FAIT — Fortress AI assistant for Excel (local dev)"/>
        <bt:String id="GetStarted.LearnMoreUrl" DefaultValue="https://fait.dev.fortressam.ai"/>
      </bt:LongStrings>
    </Resources>
  </VersionOverrides>
</OfficeApp>
```

---

## Build Output — What Does Correct Look Like?

### Before (current IIFE build)
```
dist/
  assets/
    taskpane.js          ← single bundled JS file, no HTML
    taskpane.css
    icon-16.png
    icon-32.png
    icon-80.png
  commands.html
  manifest.xml
```

The manifest `<SourceLocation>` points to `https://fait.dev.fortressam.ai/excel-addin/` (a directory). Without an `index.html` in the dist root, Excel gets a directory listing or 404. The current production deploy happens to have an `index.html` from a manual or earlier step — this is fragile.

### After (HTML entry point build)
```
dist/
  assets/
    taskpane-[hash].js   ← hashed ES module bundle
    taskpane-[hash].css  ← hashed CSS
    icon-16.png
    icon-32.png
    icon-80.png
  taskpane/
    index.html           ← complete HTML page referencing the hashed JS/CSS
  commands.html          ← ribbon commands page
  manifest.xml           ← (if copied from public/)
```

**Wait — important path detail.** When Vite's input is `src/taskpane/index.html`, it preserves the relative path structure in output. The built HTML will be at `dist/taskpane/index.html`, not `dist/index.html`.

This means:
- The taskpane URL becomes: `https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html`
- The manifest `<SourceLocation>` and `<bt:Url id="Taskpane.Url">` in `manifest.xml` must be updated to match

**`manifest.xml` URL update required in this refactor:**

```xml
<!-- BEFORE -->
<SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
<bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>

<!-- AFTER -->
<SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html"/>
<bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html"/>
```

This is a **required change** — without it, the production manifest still points to the old directory URL and the add-in breaks after refactor.

---

## Deployment Path — Does It Change?

**Short answer: No. The deployment destination is unchanged.**

```
npm run build
→ dist/ produced in ~/projects/fait-for-excel/dist/

Deploy step (manual or via build:copy script):
cp -r dist/* ~/projects/fip/fait/src/FortressAI.Web/wwwroot/excel-addin/
```

After this copy:
```
wwwroot/excel-addin/
  taskpane/
    index.html            ← taskpane served at /excel-addin/taskpane/index.html
  assets/
    taskpane-[hash].js
    taskpane-[hash].css
    icon-*.png
  commands.html
```

The FAIT web server (ASP.NET Core with `UseStaticFiles()`) serves everything under `wwwroot/` with no additional config needed. The `base: '/excel-addin/'` in `vite.config.ts` ensures all asset references in the built HTML use the correct absolute paths.

**The only change to the deployment process:** `manifest.xml` must be updated with the new taskpane URL before deploying (see §File Changes above). After first deploy with updated manifest, users sideload the updated `manifest.xml`.

---

## Local Dev Workflow

After this refactor, the complete local dev setup:

```bash
# 1. Install new dependency
cd ~/projects/fait-for-excel
npm install

# 2. Start dev server (first run: mkcert auto-generates a trusted HTTPS cert)
npm run dev
# → Server starts at https://localhost:3000
# → On first run, mkcert installs a local CA into your system trust store
#   (may prompt for sudo/admin password once, never again)

# 3. Verify the taskpane loads in a plain browser
# Open: https://localhost:3000/src/taskpane/index.html
# Should see: FAIT chat UI (Office.js falls back gracefully outside Excel)

# 4. Sideload into Excel Online for testing
# a. Open any workbook at https://excel.office.com (or office.com → Excel)
# b. Insert → Add-ins → Upload My Add-in
# c. Upload: ~/projects/fait-for-excel/manifest.local.xml
# d. The FAIT taskpane button appears in the Home tab ribbon
# e. Click it → taskpane opens and loads from https://localhost:3000

# 5. Develop with HMR
# Edit any component file → Vite hot-reloads in the Excel taskpane
# Note: changes to index.tsx may require a full taskpane reload (close + reopen)
```

**CORS during local dev:** The taskpane at `https://localhost:3000` calls `https://fait.dev.fortressam.ai/api/haven/`. This is a cross-origin request. The FAIT backend must allow `https://localhost:3000` as an origin. Verify with the team that FAIT's CORS config includes `localhost:3000` for dev. If not, a temporary `Access-Control-Allow-Origin: *` on the dev backend unblocks this.

---

## Acceptance Criteria

Tony must verify all of these before handing to Clint:

1. **`npm install` completes** with no warnings about `@microsoft/office-js`
2. **`npm run build` produces** `dist/taskpane/index.html` (not just `dist/assets/taskpane.js`)
3. **`dist/taskpane/index.html` is a valid HTML page** with Office.js CDN script tag and a `<script type="module">` reference to the hashed bundle
4. **`npm run dev` starts** at `https://localhost:3000` with a valid HTTPS cert (no browser security warning)
5. **`https://localhost:3000/src/taskpane/index.html` loads** in a plain browser without a `ReferenceError` in the console
6. **Chat still works in Excel Online** using the production manifest (smoke test: open add-in → send a message → get a response)
7. **`manifest.local.xml` exists** and sideloads successfully into Excel Online, loading from localhost
8. **`manifest.xml` contains updated Taskpane.Url** pointing to `/excel-addin/taskpane/index.html`

---

## Regression Risk

**What currently works and must keep working:**
- Chat in Excel Online at `https://fait.dev.fortressam.ai`
- API key entry and storage in settings panel
- Message history within a session
- Model picker (haiku/sonnet)

**Risk areas:**

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Manifest URL change breaks sideloaded add-in | HIGH if manifest not updated | Spec requires manifest.xml URL update — Clint's #1 review flag |
| `storage.ts` (separate from `settings.ts`) may also have OfficeRuntime reference | MEDIUM | Tony must check `storage.ts` for same pattern and apply same fix |
| Hashed asset filenames break if FAIT static file server has aggressive caching headers | LOW | ASP.NET Core `UseStaticFiles` has no aggressive cache by default |
| `public/commands.html` path in build output | LOW | Vite copies `public/` to dist root — `commands.html` ends up at `dist/commands.html` ✅ |

**One additional file Tony must check:**

`src/taskpane/services/storage.ts` — a separate storage service file exists alongside `settings.ts`. It may have the same OfficeRuntime fallback pattern. Read it and apply the same `localStorageShim` fix if needed.

---

## Files Changed Summary

| File | Action | What Changes |
|------|--------|-------------|
| `package.json` | Edit | Remove `@microsoft/office-js`; reorganize deps/devDeps; add `vite-plugin-mkcert`; add `build:copy` script |
| `vite.config.ts` | Full replace | HTML entry points; mkcert plugin; HTTPS; es2017 target |
| `src/taskpane/services/settings.ts` | Edit lines 1–5 | Replace broken OfficeRuntime fallback with localStorage shim |
| `src/taskpane/services/storage.ts` | Inspect + edit if needed | Same OfficeRuntime fallback fix if present |
| `src/taskpane/index.html` | Verify only | Should need no changes |
| `manifest.xml` | Edit 2 URLs | `<SourceLocation>` and `<bt:Url id="Taskpane.Url">` → `.../taskpane/index.html` |
| `manifest.local.xml` | Create (new) | Full file per spec above |

**Run after edits:**
```bash
npm install   # picks up new vite-plugin-mkcert, drops @microsoft/office-js
npm run build # verify correct dist/ output
```

---

## Constraints for CC

- Touch only the files listed above
- Do NOT modify any files in `src/taskpane/components/`, `hooks/`, or services other than `settings.ts` and `storage.ts`
- Do NOT add any new React components or features
- Do NOT modify anything in `~/projects/fip/` — this is fait-for-excel only
- After editing `vite.config.ts`, run `npm run build` and confirm `dist/taskpane/index.html` exists before marking complete

---

## Clint Review Priorities

```
⚠️  HIGH: Verify manifest.xml Taskpane.Url is updated to .../taskpane/index.html
          — if this is missed, the production add-in breaks immediately on deploy

⚠️  HIGH: Verify dist/taskpane/index.html exists in build output and contains
          a valid <script type="module"> reference (not a bare JS bundle)

⚠️  HIGH: Verify @microsoft/office-js is gone from package.json (both deps and devDeps)
          and NOT referenced anywhere in source files

⚠️  MEDIUM: Check storage.ts for same OfficeRuntime fallback bug as settings.ts

⚠️  MEDIUM: Verify vite.config.ts has NO output.format override — if 'iife' or 'cjs'
            appears anywhere in the rollupOptions, the fix is incomplete

⚠️  LOW: Confirm manifest.local.xml has localhost:3000 in <AppDomains> — without this,
         Office rejects the cross-domain taskpane URL
```

---

_Spec by Reed Richards | Single CC session. Build verification required before Clint review._
