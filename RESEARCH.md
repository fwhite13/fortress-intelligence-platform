# FAIT for Excel — Office Add-in Research Report

**Date:** 2026-03-16  
**Researcher:** Bruce Banner  
**Stack:** React 19 + TypeScript + Vite + REST backend (`https://fait.dev.fortressam.ai`)  
**Purpose:** Establish correct patterns before writing more code, based on last night's build failures.

---

## Table of Contents

1. [Correct Vite Setup for Office Add-ins](#1-correct-vite-setup-for-office-add-ins)
2. [OfficeRuntime vs Office Namespace](#2-officeruntime-vs-office-namespace)
3. [Reading Selected Range Data](#3-reading-selected-range-data)
4. [Writing Data to Cells](#4-writing-data-to-cells)
5. [CORS Considerations](#5-cors-considerations)
6. [Manifest Requirements](#6-manifest-requirements)
7. [Local Dev Workflow](#7-local-dev-workflow)
8. [What To Change](#8-what-to-change)

---

## 1. Correct Vite Setup for Office Add-ins

### The Core Problem with IIFE

The current `vite.config.ts` uses `format: 'iife'` with `input: 'src/taskpane/index.tsx'`. This is **wrong** for a Vite-based Office Add-in for several reasons:

1. **No taskpane HTML served**: The manifest points to a URL (e.g., `https://fait.dev.fortressam.ai/excel-addin/`) which must return an HTML page. With `input` pointing to a `.tsx` file, Vite builds a `.js` bundle but produces no HTML.
2. **Office.js load ordering**: When Vite bundles your `.tsx` as the entry, `Office.onReady(...)` runs before the CDN script in `index.html` has fully loaded. This is a race condition.
3. **IIFE wrapping**: IIFE is intended for browser script tags, not for module-based React apps with dynamic imports. Vite's preferred pattern is **HTML entry points with ES modules**.

### The Correct Pattern: HTML Entry Points

Vite natively understands `index.html` as the entry point. The build input should be the **HTML file**, not the `.tsx` file. Vite scans the HTML, finds the `<script type="module" src="...">` tag, bundles it, and emits a complete HTML + assets package.

This is exactly how Office add-in templates (including ExtraBB's well-known Vite template) work.

**Current structure** (has `src/taskpane/index.html` — good!):
```
src/taskpane/index.html   ← has Office.js CDN script + <script type="module" src="./index.tsx">
src/taskpane/index.tsx    ← calls Office.onReady(...)
public/commands.html      ← ribbon command file
```

**Correct vite.config.ts pattern:**

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert'; // for HTTPS in dev

export default defineConfig({
  plugins: [react(), mkcert()],

  server: {
    port: 3000,
    host: 'localhost',
    https: true,  // REQUIRED — Office Add-ins must be served over HTTPS
  },

  build: {
    outDir: 'dist',
    target: 'es2017',
    rollupOptions: {
      input: {
        // HTML files as entry points — NOT .tsx files
        taskpane: 'src/taskpane/index.html',
        commands: 'public/commands.html',
      },
      // No 'output.format' needed — defaults to ES modules, which is correct
    },
  },

  // base must match your deployment URL path
  base: '/excel-addin/',
});
```

**Why no IIFE format needed:**
- ES module output (`type="module"`) is supported in all modern browsers and in Excel's WebView2/WKWebView/Edge runtime
- Vite's default ES module output handles code splitting, tree-shaking, and HMR correctly
- The `type="module"` script in `index.html` ensures correct loading order

### What `vite-plugin-office-addin` Does

`vite-plugin-office-addin` (npm) is a thin wrapper that:
- Configures HTTPS with `office-addin-dev-certs` (Microsoft's own cert tool)
- Injects Office.js script tag into HTML during dev
- Handles `manifest.xml` path configuration

**Verdict:** It's optional. Using `vite-plugin-mkcert` + HTML entry points achieves the same result with less magic. The ExtraBB template (most popular Vite+Office addin template) doesn't use it and works perfectly.

### Official Microsoft Template

The official OfficeDev template (`Office-Addin-TaskPane-React`) still uses **webpack**, not Vite. Microsoft hasn't officially published a Vite template, but their documentation confirms the universal pattern works:

> "Reference Office.js from the CDN in your HTML `<head>`. Call `Office.onReady()` and wait for it to complete. Initialize your framework after Office.js is ready."

— [Connect Office.js to any JavaScript framework](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/connect-to-javascript-frameworks)

**The @microsoft/office-js npm package is NOT recommended** — per the official README, "The NPM package associated with this repo is no longer officially supported. Your add-in should get the JavaScript library from the Office CDN." The `@types/office-js` package for TypeScript types is fine to keep.

---

## 2. OfficeRuntime vs Office Namespace

### The Two Namespaces

| | `Office` (office.js) | `OfficeRuntime` |
|---|---|---|
| **Source** | CDN: `appsforoffice.microsoft.com/lib/1/hosted/office.js` | Built into Office application |
| **Available in** | All task panes and content add-ins (after `onReady`) | Task panes AND custom functions (JS-only runtime) |
| **Purpose** | Document interaction (Excel, Word, Outlook APIs) | Cross-runtime data sharing, auth dialogs |
| **When available** | After CDN script loads + `Office.onReady()` resolves | Available immediately in task pane; **shared** with custom function runtime |
| **Key APIs** | `Excel`, `Word`, `Office.context`, `Office.EventType` | `OfficeRuntime.storage`, `OfficeRuntime.auth` |

### The Root Cause of Last Night's Bug

```typescript
// ❌ WRONG — OfficeRuntime accessed at module scope
declare const OfficeRuntime: any;
const getStorage = () => OfficeRuntime.storage; // can throw if not yet available
```

`OfficeRuntime` is a global injected by the Office host environment. In a **task pane**, it IS available at page load — but only if:
1. The page is actually loaded inside Excel (not in a plain browser)
2. The Office.js script has initialized the runtime bindings

When running in a plain browser tab (for dev), `OfficeRuntime` doesn't exist at all.

### Correct Pattern: Safe OfficeRuntime.storage Access

For a task pane that must work **both inside Excel and standalone in a browser**:

```typescript
// services/storage.ts

// A localStorage-backed shim for when OfficeRuntime isn't available
const localStorageShim = {
  getItem: (key: string): Promise<string | null> =>
    Promise.resolve(localStorage.getItem(key)),
  setItem: (key: string, value: string): Promise<void> =>
    Promise.resolve(void localStorage.setItem(key, value)),
  removeItem: (key: string): Promise<void> =>
    Promise.resolve(void localStorage.removeItem(key)),
};

function getStorage(): typeof localStorageShim {
  // Check at call time, not module load time
  const officeRuntime = (window as any).OfficeRuntime;
  if (officeRuntime?.storage) {
    return officeRuntime.storage;
  }
  // Fallback to localStorage for browser dev / testing
  return localStorageShim;
}

export async function storageGet(key: string): Promise<string | null> {
  return getStorage().getItem(key);
}

export async function storageSet(key: string, value: string): Promise<void> {
  return getStorage().setItem(key, value);
}

export async function storageRemove(key: string): Promise<void> {
  return getStorage().removeItem(key);
}
```

**Key insight:** `OfficeRuntime.storage` in a task pane context (not custom functions) is essentially synced with `localStorage` on Excel Online anyway — per the OfficeDev team's own GitHub issue response:

> "For add-in on Excel Online, the data created with OfficeRuntime.storage is stored in local storage."

So the localStorage fallback is semantically equivalent for the web scenario.

### OfficeRuntime.storage API Reference

Available methods on the `storage` object:
- `getItem(key: string): Promise<string | null>`
- `getItems(keys: string[]): Promise<{ [key: string]: string | null }>`
- `setItem(key: string, value: string): Promise<void>`
- `setItems(items: { [key: string]: string }): Promise<void>`
- `removeItem(key: string): Promise<void>`
- `removeItems(keys: string[]): Promise<void>`
- `getKeys(): Promise<string[]>`

**Limit:** 10 MB per domain. No `clear()` method — use `removeItems(await getKeys())` if needed.

📖 Docs: [OfficeRuntime.storage](https://learn.microsoft.com/en-us/javascript/api/office-runtime/officeruntime.storage)  
📖 Docs: [Custom Functions Runtime](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/custom-functions-runtime)

---

## 3. Reading Selected Range Data

### The Correct API

Use `Excel.run()` with `context.workbook.getSelectedRange()`. The key pattern is:
1. Call `getSelectedRange()` to get a proxy object
2. Call `.load()` to declare which properties you want
3. Call `context.sync()` to fetch the data from Excel
4. Read the properties after sync

```typescript
// Read the currently selected range (address + all values)
export async function readSelectedRange(): Promise<{
  address: string;
  values: (string | number | boolean)[][];
  rowCount: number;
  columnCount: number;
}> {
  return Excel.run(async (context) => {
    const range = context.workbook.getSelectedRange();

    // Declare what you need BEFORE sync
    range.load(['address', 'values', 'rowCount', 'columnCount']);

    await context.sync();

    // Properties are now populated
    return {
      address: range.address,
      values: range.values as (string | number | boolean)[][],
      rowCount: range.rowCount,
      columnCount: range.columnCount,
    };
  });
}
```

**Important notes:**
- `range.values` is a 2D array: `values[row][col]` — even for a single cell it's `[[value]]`
- Empty cells return `""` (empty string), not `null`
- Numbers return as JS `number`, dates return as Excel serial number (a float)
- Call from within `Office.onReady` callback or after it resolves

### Listening for Selection Changes

If you want to react when the user changes selection:

```typescript
// React hook example
import { useEffect, useState } from 'react';

export function useSelectedRange() {
  const [address, setAddress] = useState<string | null>(null);

  useEffect(() => {
    let handler: Office.EventHandlerResult | null = null;

    async function registerHandler() {
      await Excel.run(async (context) => {
        handler = context.workbook.onSelectionChanged.add(async () => {
          const result = await readSelectedRange();
          setAddress(result.address);
        });
        await context.sync();
      });
    }

    registerHandler().catch(console.error);

    return () => {
      if (handler) {
        handler.remove().catch(console.error);
      }
    };
  }, []);

  return address;
}
```

### Permissions Required

For **reading** the selected range:
- Manifest: `<Permissions>ReadDocument</Permissions>` is sufficient
- But since we also write: `<Permissions>ReadWriteDocument</Permissions>` (already in manifest ✅)
- API requirement set: `ExcelApi 1.1` minimum (manifest currently requires `1.13` ✅)

📖 Docs: [Set and get selected range](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-ranges-set-get)  
📖 Docs: [Get a range](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-ranges-get)

---

## 4. Writing Data to Cells

### Writing to a Specific Range

```typescript
// Write a 2D array of values to a specific range address
export async function writeRangeValues(
  sheetName: string,
  rangeAddress: string,
  values: (string | number | boolean)[][]
): Promise<void> {
  return Excel.run(async (context) => {
    const sheet = context.workbook.worksheets.getItem(sheetName);
    const range = sheet.getRange(rangeAddress);

    // range.values accepts a 2D array — dimensions must match rangeAddress
    range.values = values;

    await context.sync();
  });
}
```

### Writing to the Active Sheet at a Specific Cell (Common Pattern)

```typescript
// Write starting at a given cell address on the active sheet
export async function writeAtCell(
  cellAddress: string,
  data: (string | number | boolean)[][]
): Promise<void> {
  return Excel.run(async (context) => {
    const sheet = context.workbook.worksheets.getActiveWorksheet();

    // getRange with exact dimensions matching your data
    const rows = data.length;
    const cols = data[0]?.length ?? 1;
    // Excel range notation: start cell resized to fit data
    const range = sheet.getRange(cellAddress).getResizedRange(rows - 1, cols - 1);

    range.values = data;
    range.format.autofitColumns();

    await context.sync();
  });
}
```

### Writing to the Currently Selected Range

```typescript
// Paste data into whatever range the user has selected
export async function writeToSelection(
  data: (string | number | boolean)[][]
): Promise<void> {
  return Excel.run(async (context) => {
    const selectedRange = context.workbook.getSelectedRange();
    selectedRange.load(['rowCount', 'columnCount', 'address']);
    await context.sync();

    // Optionally validate dimensions here
    selectedRange.values = data;
    await context.sync();
  });
}
```

### Single Cell Write (Simplest Case)

```typescript
await Excel.run(async (context) => {
  const sheet = context.workbook.worksheets.getActiveWorksheet();
  const cell = sheet.getRange('A1');
  cell.values = [['Hello from FAIT!']];
  await context.sync();
});
```

**Critical:** `range.values` is always a 2D array. `[['value']]` for a single cell, `[['a', 'b'], ['c', 'd']]` for a 2×2 range.

📖 Docs: [Set and get range values](https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-ranges-set-get-values)

---

## 5. CORS Considerations

### How Office Add-ins Make HTTP Requests

Office Add-ins run in a **webview** (WebView2 on Windows, WKWebView on Mac, browser engine on Excel Online). The same-origin policy applies exactly as it does in a browser. From within the add-in's taskpane:

- **Excel Online**: Your page is served from `https://fait.dev.fortressam.ai`, so calls to `https://fait.dev.fortressam.ai/api/...` are **same-origin** — no CORS needed ✅
- **Excel Desktop (Windows/Mac)**: The taskpane is a separate WebView instance. The origin will be your add-in's URL (`https://fait.dev.fortressam.ai`). Calls to `https://fait.dev.fortressam.ai/api/...` are same-origin ✅

### For Our Specific Setup (fait.dev.fortressam.ai calling itself)

Since the taskpane is served from `https://fait.dev.fortressam.ai/excel-addin/` and the API is at `https://fait.dev.fortressam.ai/api/...`, this is **same origin**. No special CORS headers are needed.

However, for **local development** (taskpane at `https://localhost:3000`):

The server at `https://fait.dev.fortressam.ai` will see the origin as `https://localhost:3000`. The backend must return:
```
Access-Control-Allow-Origin: https://localhost:3000
Access-Control-Allow-Headers: Content-Type, x-api-key, Accept
Access-Control-Allow-Methods: POST, GET, OPTIONS
```

Or more permissively during development:
```
Access-Control-Allow-Origin: *
```

### CORS and Custom Functions (Important Distinction)

**Task pane (what we're building):** Full CORS support, standard fetch works fine.

**Custom functions in JS-only runtime:** Only "simple CORS" is supported — no preflight, only simple headers (`Content-Type: application/x-www-form-urlencoded`, `text/plain`, `multipart/form-data`). Our `Content-Type: application/json` with `x-api-key` header would **fail** in the JS-only custom functions runtime. However, since we're building a task pane, this doesn't apply to us.

### Special Headers for Office Add-ins

There are **no special CORS headers** required exclusively for Office Add-ins. Standard CORS headers suffice. Just ensure:
1. `Access-Control-Allow-Origin` includes your taskpane origin
2. `Access-Control-Allow-Headers` includes `x-api-key`, `Content-Type`, `Accept`
3. Preflight `OPTIONS` requests are handled

📖 Docs: [Addressing same-origin policy limitations](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/addressing-same-origin-policy-limitations)

---

## 6. Manifest Requirements

### Current Manifest Analysis

The manifest already has the correct permission level:

```xml
<Permissions>ReadWriteDocument</Permissions>
```

This is the **highest permission level** in the XML manifest and covers everything we need:
- Read document content ✅
- Write document content ✅
- Read selected range values ✅
- Write to cells ✅
- Access `OfficeRuntime.storage` ✅

### Permission Levels (for reference)

| Permission | Read | Write | Use case |
|---|---|---|---|
| `Restricted` | ❌ | ❌ | Non-document add-ins |
| `ReadDocument` | ✅ | ❌ | Read-only inspection |
| `ReadAllDocument` | ✅ (all sheets) | ❌ | Full workbook read |
| `WriteDocument` | ❌ | ✅ | Write-only |
| `ReadWriteDocument` | ✅ | ✅ | **Full access — use this** |

### ExcelApi Requirement Set

```xml
<Set Name="ExcelApi" MinVersion="1.13"/>
```

This is fine for modern Excel. The APIs we're using (`getSelectedRange`, `range.values`) are available since ExcelApi 1.1. Setting MinVersion to 1.13 means the add-in won't load on very old Excel versions, which is acceptable.

### Manifest URL — Local Dev Issue

The manifest currently has:
```xml
<bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
```

For local development, you need a **separate local manifest** or to temporarily override this URL to `https://localhost:3000/src/taskpane/index.html`.

**Recommendation:** Create `manifest.local.xml` with `https://localhost:3000/` URLs for dev use, and keep `manifest.xml` for production.

📖 Docs: [Permissions element](https://learn.microsoft.com/en-us/javascript/api/manifest/permissions?view=common-js-preview)  
📖 Docs: [Office Add-ins manifest overview](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/add-in-manifests)

---

## 7. Local Dev Workflow

### The Required Stack

Office Add-ins **must be served over HTTPS** — even for local development. Excel will refuse to load the taskpane from `http://localhost`. Two options:

**Option A: `vite-plugin-mkcert` (recommended)**
```bash
npm install -D vite-plugin-mkcert
```
```typescript
// vite.config.ts
import mkcert from 'vite-plugin-mkcert';
export default defineConfig({
  plugins: [react(), mkcert()],
  server: { https: true, port: 3000, host: 'localhost' },
});
```
`mkcert` creates a locally-trusted CA + certificate. Works transparently. No browser warnings.

**Option B: `office-addin-dev-certs`**
```bash
npx office-addin-dev-certs install
```
Then reference the generated certs in vite.config.ts. More setup, but uses Microsoft's official cert tool.

### Full Local Dev Workflow

```
1. npm run dev          → starts Vite HTTPS dev server at https://localhost:3000
2. Create manifest.local.xml pointing to https://localhost:3000/src/taskpane/index.html
3. Sideload manifest.local.xml into Excel
4. Edit code → Vite HMR updates the taskpane in real time
```

### Sideloading for Testing

**Excel Desktop (Windows):**
- Go to `%APPDATA%\Microsoft\Excel\XLSTART` 
- Or use: Insert → Get Add-ins → Upload My Add-in → browse to manifest.xml

**Excel Online:**
1. Open Excel Online
2. Insert → Office Add-ins
3. Upload My Add-in
4. Browse to your `manifest.xml`

**Using `office-addin-debugging` (official CLI tool):**
```json
// package.json scripts
{
  "start": "office-addin-debugging start manifest.xml",
  "stop": "office-addin-debugging stop manifest.xml"
}
```
This handles: dev certs, manifest sideloading, browser launch, and Chrome DevTools attachment automatically.

### HMR in Excel — How It Works

When Vite HMR updates a module:
1. The taskpane's webview receives the update via WebSocket
2. React components re-render
3. **Caveat:** `Office.onReady()` is NOT re-called on HMR updates (it only fires once per page load)
4. Changes to `index.tsx` structure may require a full page reload

**Tip:** Keep Office.js initialization code minimal in `index.tsx`. Put all your React component logic in `App.tsx` and sub-components — those hot-reload fine.

### Dev vs Production URL Strategy

```
Dev:   https://localhost:3000/src/taskpane/index.html
Prod:  https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html
       (or wherever Vite's dist/ is deployed)
```

The `base` in vite.config.ts controls the URL prefix for all assets in production:
```typescript
base: '/excel-addin/',   // → dist assets will be at /excel-addin/assets/...
```

📖 Docs: [office-addin-debugging npm](https://www.npmjs.com/package/office-addin-debugging)  
📖 Docs: [Sideload Office Add-ins to Office on the web](https://learn.microsoft.com/en-us/office/dev/add-ins/testing/sideload-office-add-ins-for-testing)

---

## 8. What To Change

### Priority 1 — vite.config.ts (Critical, blocks build correctness)

**Current:**
```typescript
build: {
  rollupOptions: {
    input: 'src/taskpane/index.tsx',       // ❌ .tsx file, not HTML
    output: {
      format: 'iife',                      // ❌ Wrong format
      inlineDynamicImports: true,          // ❌ Unnecessary
      name: 'TaskpaneApp',                 // ❌ IIFE-specific
      entryFileNames: 'assets/taskpane.js',
      assetFileNames: 'assets/[name][extname]',
    },
  },
},
```

**Replace with:**
```typescript
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [react(), mkcert()],

  server: {
    port: 3000,
    host: 'localhost',
    https: true,
  },

  build: {
    outDir: 'dist',
    target: 'es2017',
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html',  // ✅ HTML entry point
        commands: 'public/commands.html',      // ✅ Commands HTML entry point
      },
      // No output.format — defaults to esm, which is correct
    },
  },

  base: '/excel-addin/',
});
```

### Priority 2 — index.html (Verify structure)

The existing `src/taskpane/index.html` looks correct! It has:
```html
<script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" ...></script>  ✅
<script type="module" src="/src/taskpane/index.tsx"></script>  ✅
```

**One potential issue:** Office.js nullifies `window.history.replaceState` and `pushState`. If using React Router or any router, add this workaround:
```html
<script>
  window._historyCache = {
    replaceState: window.history.replaceState,
    pushState: window.history.pushState
  };
</script>
<script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js"></script>
<script>
  window.history.replaceState = window._historyCache.replaceState;
  window.history.pushState = window._historyCache.pushState;
</script>
```

### Priority 3 — settings.ts (OfficeRuntime fallback)

**Current:**
```typescript
declare const OfficeRuntime: any;
const getStorage = () => (window as any).OfficeRuntime?.storage ?? OfficeRuntime.storage;
```

This is close but the fallback to `OfficeRuntime.storage` at the end will throw a `ReferenceError` when running in a plain browser (no Office context).

**Replace `getStorage()` with:**
```typescript
const localStorageShim = {
  getItem: (key: string) => Promise.resolve(localStorage.getItem(key)),
  setItem: (key: string, value: string) => Promise.resolve(void localStorage.setItem(key, value)),
  removeItem: (key: string) => Promise.resolve(void localStorage.removeItem(key)),
};

function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? localStorageShim;
}
```

### Priority 4 — Remove @microsoft/office-js from dependencies

```json
// package.json — REMOVE this:
"@microsoft/office-js": "^1.1.110",
```

The `@microsoft/office-js` npm package is **officially unsupported**. Office.js is loaded from the CDN via the script tag in `index.html`. The types package is fine:
```json
// KEEP in devDependencies:
"@types/office-js": "^1.0.582"  ✅
```

### Priority 5 — Create manifest.local.xml for dev

```bash
cp manifest.xml manifest.local.xml
```

In `manifest.local.xml`, change:
```xml
<!-- Dev source location -->
<DefaultSettings>
  <SourceLocation DefaultValue="https://localhost:3000/src/taskpane/index.html"/>
</DefaultSettings>

<!-- Dev taskpane URL -->
<bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3000/src/taskpane/index.html"/>
<bt:Url id="Commands.Url" DefaultValue="https://localhost:3000/public/commands.html"/>
```

### Priority 6 — Install missing dev dependency

```bash
npm install -D vite-plugin-mkcert
```

### Summary of Changes

| File | Change | Impact |
|---|---|---|
| `vite.config.ts` | HTML entry points, remove IIFE, add mkcert, add HTTPS | Fixes build output + local dev |
| `src/taskpane/index.html` | Verify already correct (it is) | None needed |
| `src/taskpane/services/settings.ts` | Replace broken OfficeRuntime fallback | Fixes browser dev testing |
| `package.json` | Remove `@microsoft/office-js` dep | Removes unsupported package |
| `manifest.local.xml` | New file: localhost URLs for dev | Enables local sideloading |
| Install | `npm install -D vite-plugin-mkcert` | Required for HTTPS dev |

---

## Reference Links

| Topic | URL |
|---|---|
| Connect Office.js to any JS framework | https://learn.microsoft.com/en-us/office/dev/add-ins/develop/connect-to-javascript-frameworks |
| Reference Office.js from CDN | https://learn.microsoft.com/en-us/office/dev/add-ins/develop/referencing-the-javascript-api-for-office-library-from-its-cdn |
| Excel JS API — get range | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-ranges-get |
| Excel JS API — set/get selected range | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-ranges-set-get |
| Excel JS API — set/get values | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/excel-add-ins-ranges-set-get-values |
| OfficeRuntime.storage | https://learn.microsoft.com/en-us/javascript/api/office-runtime/officeruntime.storage |
| Custom functions runtime | https://learn.microsoft.com/en-us/office/dev/add-ins/excel/custom-functions-runtime |
| Manifest permissions | https://learn.microsoft.com/en-us/javascript/api/manifest/permissions |
| Same-origin / CORS | https://learn.microsoft.com/en-us/office/dev/add-ins/develop/addressing-same-origin-policy-limitations |
| Sideload for testing | https://learn.microsoft.com/en-us/office/dev/add-ins/testing/sideload-office-add-ins-for-testing |
| ExtraBB Vite+React template | https://github.com/ExtraBB/Office-Addin-React-Vite-Template |
| office-addin-debugging npm | https://www.npmjs.com/package/office-addin-debugging |
| vite-plugin-mkcert | https://www.npmjs.com/package/vite-plugin-mkcert |
