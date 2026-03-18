# CC Brief: WI813 — Fix Vite Build Foundation

You are working in `/home/fredw/projects/fait-for-excel/`.

This is a focused refactor fixing 5 root problems with the build config:
1. Vite uses IIFE format instead of HTML entry points
2. settings.ts has a broken OfficeRuntime fallback (ReferenceError in plain browser)
3. storage.ts has NO OfficeRuntime fallback at all (ReferenceError guaranteed)
4. @microsoft/office-js is in npm dependencies (unsupported — CDN only)
5. No HTTPS for local dev (Office refuses http://)

Make ALL of the following changes. Touch ONLY the files listed. Do NOT modify anything in
~/projects/fip/ or any other directory outside ~/projects/fait-for-excel/.

---

## CHANGE 1: package.json — reorganize deps, remove office-js, add mkcert, add build:copy

Replace the entire package.json with:

```json
{
  "name": "fait-for-excel",
  "version": "1.0.0",
  "description": "",
  "main": "index.js",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "build:copy": "tsc && vite build && cp -r dist/* ../fip/fait/src/FortressAI.Web/wwwroot/excel-addin/",
    "preview": "vite preview"
  },
  "keywords": [],
  "author": "",
  "license": "ISC",
  "dependencies": {
    "react": "^19.2.4",
    "react-dom": "^19.2.4"
  },
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
}
```

Key changes:
- Removed `@microsoft/office-js` (officially unsupported npm package — Office.js is loaded via CDN in index.html)
- Moved all build-time packages from dependencies to devDependencies
- Added `vite-plugin-mkcert` to devDependencies
- Added `build:copy` script for deployment convenience

---

## CHANGE 2: vite.config.ts — full replacement

Replace the entire vite.config.ts with:

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

Key changes:
- Removed `format: 'iife'`, `inlineDynamicImports: true`, `name: 'TaskpaneApp'`, `entryFileNames`, `assetFileNames` overrides
- input changed from `'src/taskpane/index.tsx'` to HTML entry points object
- Added mkcert() plugin and https: true on server
- Changed host from '127.0.0.1' to 'localhost'
- Raised target from 'es2015' to 'es2017'

---

## CHANGE 3: src/taskpane/services/settings.ts — fix OfficeRuntime fallback

The current lines 1-5 are:
```typescript
/* eslint-disable @typescript-eslint/no-explicit-any */
declare const OfficeRuntime: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

// Lazy accessor — OfficeRuntime is not available at module load time
const getStorage = () => (window as any).OfficeRuntime?.storage ?? OfficeRuntime.storage;
```

The `?? OfficeRuntime.storage` fallback throws `ReferenceError: OfficeRuntime is not defined`
when running in a plain browser, because OfficeRuntime is a host-injected global.

Replace ONLY those first 6 lines (the declare + getStorage function) with:

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

Everything else in settings.ts (FaitSettings interface, loadSettings, saveSetting) stays unchanged.

---

## CHANGE 4: src/taskpane/services/storage.ts — apply same localStorage shim fix

The current storage.ts is:
```typescript
/* eslint-disable @typescript-eslint/no-explicit-any */
declare const OfficeRuntime: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

const KEY = 'fait_api_key';

export async function getApiKey(): Promise<string | null> {
  try {
    const value = await OfficeRuntime.storage.getItem(KEY);
    return value ?? null;
  } catch {
    return null;
  }
}

export async function setApiKey(key: string): Promise<void> {
  try {
    await OfficeRuntime.storage.setItem(KEY, key);
  } catch {
    throw new Error('STORAGE_UNAVAILABLE');
  }
}

export async function clearApiKey(): Promise<void> {
  try {
    await OfficeRuntime.storage.removeItem(KEY);
  } catch {
    // ignore
  }
}
```

This file has NO fallback at all — direct `OfficeRuntime.storage.*` calls throw ReferenceError
in any plain browser. Replace the entire file with:

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
function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? localStorageShim;
}

const KEY = 'fait_api_key';

export async function getApiKey(): Promise<string | null> {
  try {
    const storage = getStorage();
    const value = await storage.getItem(KEY);
    return value ?? null;
  } catch {
    return null;
  }
}

export async function setApiKey(key: string): Promise<void> {
  try {
    const storage = getStorage();
    await storage.setItem(KEY, key);
  } catch {
    throw new Error('STORAGE_UNAVAILABLE');
  }
}

export async function clearApiKey(): Promise<void> {
  try {
    const storage = getStorage();
    await storage.removeItem(KEY);
  } catch {
    // ignore
  }
}
```

---

## CHANGE 5: src/taskpane/index.html — verify only

Read src/taskpane/index.html and confirm it has:
- `<script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>` in <head>
- `<script type="module" src="/src/taskpane/index.tsx"></script>` in <body>

If it already has both of these, make NO changes to this file. Report whether it was correct as-is.

---

## CHANGE 6: manifest.xml — update 2 URLs to include /taskpane/index.html

When Vite's input is `src/taskpane/index.html`, it preserves the relative path structure in output.
The built HTML will be at `dist/taskpane/index.html`, so the URL is:
`https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html`

In manifest.xml, update EXACTLY these two elements:

1. In `<DefaultSettings>`, change:
   ```xml
   <SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
   ```
   to:
   ```xml
   <SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html"/>
   ```

2. In `<Resources><bt:Urls>`, change:
   ```xml
   <bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
   ```
   to:
   ```xml
   <bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/excel-addin/taskpane/index.html"/>
   ```

All other lines in manifest.xml stay unchanged.

---

## CHANGE 7: manifest.local.xml — create new file

Create a NEW file at manifest.local.xml (in the project root, same directory as manifest.xml).

Write this EXACT content:

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

## Summary of All Changes

1. package.json — remove @microsoft/office-js, reorganize deps/devDeps, add vite-plugin-mkcert, add build:copy script
2. vite.config.ts — full replacement: HTML entry points, mkcert plugin, HTTPS, es2017 target, no IIFE format
3. src/taskpane/services/settings.ts — replace broken OfficeRuntime fallback with localStorage shim
4. src/taskpane/services/storage.ts — same localStorage shim fix (no fallback exists currently)
5. src/taskpane/index.html — verify only, report if correct as-is (no changes expected)
6. manifest.xml — update SourceLocation and Taskpane.Url to .../taskpane/index.html
7. manifest.local.xml — create new file with localhost:3000 URLs

After making all changes, confirm completion by listing the files modified.
Do NOT run npm install or npm run build — the calling script will do that.
