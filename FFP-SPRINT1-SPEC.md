# FfP Sprint 1 Spec — Foundation + Core Chat

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What Was Already Read

This spec is informed by:
- `FFP-ARCHITECTURE-SPEC.md` — all decisions, lessons L1–L7, API baseline, deployment path
- FfE source tree (`~/projects/fait-for-excel/src/`) — exact files to copy verbatim vs. adapt
- FAIT backend (`Program.cs` lines 280–305) — the `MapGet + AllowAnonymous` pattern for static file serving
- FfE `manifest.xml` / `manifest.local.xml` — exact XML structure to adapt for PowerPoint

**Nothing is guessed. Everything in this spec is derived from reading live code.**

---

## Objectives

Sprint 1 delivers a working FfP add-in that:
1. Loads in PowerPoint Online without a blank screen (correct Vite config from day 1 — L1)
2. Accepts and stores an API key
3. Sends chat messages to the FAIT API and streams responses
4. Reads the selected slide's context (shape text, slide title, speaker notes)
5. Injects that context into every chat prompt
6. Offers an "Apply to Shape" flow — preview AI text in a confirm dialog, then write it

---

## Repo Location Decision

The arch spec raised the question: standalone repo or inside `fip/` monorepo?

**Decision: `~/projects/fip/fait-for-powerpoint/`** — inside the `fip/` monorepo.

Rationale:
- The Docker buildspec for FAIT already needed expansion for FfE (WI#813); the pattern exists
- Avoids a third GitHub repo
- `fip/fait-for-powerpoint/` is parallel to `fip/fait/`, `fip/firm/`, `fip/forms/`
- `fip/fait-for-powerpoint/dist/` can be COPY'd in the FAIT Dockerfile the same way FfE dist would be

This is the repo path Tony uses. If Fred decides on a standalone repo, the internal structure is identical — only the root path changes.

---

## Single CC Session — Sequential Tasks

All 18 tasks below run in one CC session, in order. No parallelism — each task builds on the previous. Estimated: 1–2 hours of CC time.

The most important rule: **Task 1 (vite.config.ts) and Task 2 (index.html) must be committed before any component is written.** This is L1 from the FfE lessons — the blank screen crisis was caused by writing components before validating the build foundation.

---

## Task List (Complete, Sequential)

```
Task 1:  vite.config.ts          ← first file committed; zero tolerance for deviation
Task 2:  src/taskpane/index.html ← HTML entry point with CDN office.js script tag
Task 3:  public/commands.html    ← ribbon commands page
Task 4:  tsconfig.json           ← exact copy from FfE with one change
Task 5:  package.json            ← exact copy from FfE with two changes (name, port comment)
Task 6:  .gitignore
Task 7:  public/manifest.xml     ← prod manifest (committed once; Presentation host)
         manifest.local.xml      ← dev manifest (localhost:3001)
Task 8:  src/taskpane/styles/global.css     ← exact copy from FfE
Task 9:  src/taskpane/services/settings.ts  ← exact copy from FfE (shim intact)
Task 10: src/taskpane/services/faitApi.ts   ← exact copy from FfE
Task 11: src/taskpane/hooks/useChat.ts      ← exact copy from FfE (no changes needed S1)
Task 12: src/taskpane/services/pptReader.ts ← NEW: getSlideContext(), context formatter
Task 13: src/taskpane/services/pptWriter.ts ← NEW: applyTextToShape()
Task 14: src/taskpane/hooks/usePptContext.ts ← NEW: polling hook (2s interval)
Task 15: src/taskpane/components/SettingsPanel.tsx ← port from FfE (minor text changes)
Task 16: src/taskpane/components/ShapePreview.tsx  ← NEW: confirm dialog for Apply to Shape
Task 17: src/taskpane/components/ChatPanel.tsx      ← port from FfE (major adaptations)
Task 18: src/taskpane/App.tsx              ← port from FfE (minor changes)
         src/taskpane/index.tsx            ← exact copy from FfE (Office.onReady wrapper)
```

---

## File-Level Spec

### Task 1: `vite.config.ts`

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [
    react(),
    mkcert(), // generates locally-trusted HTTPS cert — required for Office Add-ins
  ],

  server: {
    port: 3001,          // 3001, NOT 3000 — avoid collision with FfE dev server (L7)
    host: '127.0.0.1',   // FfE uses 'localhost'; use '127.0.0.1' for HTTPS cert compatibility
    https: true,         // Office Add-ins reject http:// (L1)
  },

  build: {
    outDir: 'dist',
    target: 'es2017',
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html',   // HTML entry point — NOT index.tsx (L1)
        commands: 'public/commands.html',
      },
      // No format: 'iife' — default ES modules (L1)
      output: {
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },

  base: '/ppt-addin/',  // Must match deployment URL prefix and manifest URLs
});
```

**Do NOT add:** `format: 'iife'`, `inlineDynamicImports: true`, `input: 'src/taskpane/index.tsx'`. Any deviation from this config must be justified in writing before implementation.

---

### Task 2: `src/taskpane/index.html`

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>FAIT for PowerPoint</title>
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet" />
    <!-- Office JS — CDN only. Never install @microsoft/office-js from npm. (L3) -->
    <script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/taskpane/index.tsx"></script>
  </body>
</html>
```

---

### Task 3: `public/commands.html`

Exact copy from FfE. No changes needed — ribbon command handler is app-agnostic.

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8" />
    <meta http-equiv="X-UA-Compatible" content="IE=Edge" />
    <title>Commands Page</title>
</head>
<body>
<script type="text/javascript">
    Office.onReady(function() {});
</script>
</body>
</html>
```

---

### Task 4: `tsconfig.json`

Exact copy from FfE. No changes needed — same TypeScript target, same React JSX settings.

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "useDefineForClassFields": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": false,
    "noUnusedParameters": false,
    "noFallthroughCasesInSwitch": true
  },
  "include": ["src"]
}
```

---

### Task 5: `package.json`

Copy from FfE with two changes: `name` and `build:copy` target path.

```json
{
  "name": "fait-for-powerpoint",
  "version": "1.0.0",
  "description": "FAIT for PowerPoint — Office Add-in taskpane",
  "main": "index.js",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "build:copy": "tsc && vite build && cp -r dist/* ../fip/fait/src/FortressAI.Web/wwwroot/ppt-addin/",
    "preview": "vite preview"
  },
  "keywords": [],
  "author": "Fortress Asset Management",
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

**Confirm: `@microsoft/office-js` is NOT in dependencies or devDependencies.** Only `@types/office-js` (type definitions, no runtime) is in devDependencies. (L3)

---

### Task 6: `.gitignore`

```
node_modules/
dist/
.env
.env.local
*.local
.DS_Store
```

---

### Task 7: Manifests

**`public/manifest.xml`** — Production manifest. Committed once. Never edited during development.

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
           xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
           xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
           xmlns:ov="http://schemas.microsoft.com/office/taskpaneappversionoverrides"
           xsi:type="TaskPaneApp">
  <Id>b2c3d4e5-f6a7-8901-bcde-f12345678902</Id>
  <Version>1.0.0.0</Version>
  <ProviderName>Fortress Asset Management</ProviderName>
  <DefaultLocale>en-US</DefaultLocale>
  <DisplayName DefaultValue="FAIT for PowerPoint"/>
  <Description DefaultValue="Fortress AI assistant for presentations — data sovereignty guaranteed"/>
  <IconUrl DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-32.png"/>
  <HighResolutionIconUrl DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-80.png"/>
  <SupportUrl DefaultValue="https://fait.dev.fortressam.ai"/>
  <AppDomains>
    <AppDomain>https://fait.dev.fortressam.ai</AppDomain>
  </AppDomains>
  <Hosts>
    <Host Name="Presentation"/>
  </Hosts>
  <Requirements>
    <Sets>
      <Set Name="PowerPointApi" MinVersion="1.5"/>
    </Sets>
  </Requirements>
  <DefaultSettings>
    <SourceLocation DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/src/taskpane/index.html"/>
  </DefaultSettings>
  <Permissions>ReadWriteDocument</Permissions>
  <VersionOverrides xmlns="http://schemas.microsoft.com/office/taskpaneappversionoverrides" xsi:type="VersionOverridesV1_0">
    <Hosts>
      <Host xsi:type="Presentation">
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
        <bt:Image id="Icon.16x16" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-16.png"/>
        <bt:Image id="Icon.32x32" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-32.png"/>
        <bt:Image id="Icon.80x80" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-80.png"/>
      </bt:Images>
      <bt:Urls>
        <bt:Url id="Commands.Url" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/commands.html"/>
        <bt:Url id="Taskpane.Url" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/src/taskpane/index.html"/>
      </bt:Urls>
      <bt:ShortStrings>
        <bt:String id="GetStarted.Title" DefaultValue="FAIT for PowerPoint"/>
        <bt:String id="CommandsGroup.Label" DefaultValue="FAIT"/>
        <bt:String id="TaskpaneButton.Label" DefaultValue="Open FAIT"/>
        <bt:String id="GetStarted.Description" DefaultValue="AI-powered presentation assistant grounded in your firm's knowledge"/>
        <bt:String id="GetStarted.LearnMoreUrl" DefaultValue="https://fait.dev.fortressam.ai"/>
      </bt:ShortStrings>
      <bt:LongStrings>
        <bt:String id="TaskpaneButton.Tooltip" DefaultValue="Open the FAIT for PowerPoint assistant"/>
      </bt:LongStrings>
    </Resources>
  </VersionOverrides>
</OfficeApp>
```

**Key differences from FfE manifest:**
- `<Id>`: different GUID — `b2c3d4e5-f6a7-8901-bcde-f12345678902` (FfE is `...ef1234567890`)
- `<Host Name="Presentation"/>` — not `Workbook`
- `<Set Name="PowerPointApi" MinVersion="1.5"/>` — not ExcelApi 1.13
- `<Host xsi:type="Presentation">` — not `Workbook`
- All URLs use `/ppt-addin/` — not `/excel-addin/`
- `<SourceLocation>` path: `/ppt-addin/src/taskpane/index.html`

**`manifest.local.xml`** — Dev manifest. localhost:3001.

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<OfficeApp xmlns="http://schemas.microsoft.com/office/appforoffice/1.1"
           xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
           xmlns:bt="http://schemas.microsoft.com/office/officeappbasictypes/1.0"
           xmlns:ov="http://schemas.microsoft.com/office/taskpaneappversionoverrides"
           xsi:type="TaskPaneApp">
  <Id>b2c3d4e5-f6a7-8901-bcde-f12345678902</Id>
  <Version>1.0.0.0</Version>
  <ProviderName>Fortress Asset Management</ProviderName>
  <DefaultLocale>en-US</DefaultLocale>
  <DisplayName DefaultValue="FAIT for PowerPoint (Local Dev)"/>
  <Description DefaultValue="Fortress AI assistant — local dev build"/>
  <IconUrl DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-32.png"/>
  <HighResolutionIconUrl DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-80.png"/>
  <SupportUrl DefaultValue="https://fait.dev.fortressam.ai"/>
  <AppDomains>
    <AppDomain>https://fait.dev.fortressam.ai</AppDomain>
    <AppDomain>https://localhost:3001</AppDomain>
  </AppDomains>
  <Hosts>
    <Host Name="Presentation"/>
  </Hosts>
  <Requirements>
    <Sets>
      <Set Name="PowerPointApi" MinVersion="1.5"/>
    </Sets>
  </Requirements>
  <DefaultSettings>
    <SourceLocation DefaultValue="https://localhost:3001/src/taskpane/index.html"/>
  </DefaultSettings>
  <Permissions>ReadWriteDocument</Permissions>
  <VersionOverrides xmlns="http://schemas.microsoft.com/office/taskpaneappversionoverrides" xsi:type="VersionOverridesV1_0">
    <Hosts>
      <Host xsi:type="Presentation">
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
        <bt:Image id="Icon.16x16" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-16.png"/>
        <bt:Image id="Icon.32x32" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-32.png"/>
        <bt:Image id="Icon.80x80" DefaultValue="https://fait.dev.fortressam.ai/ppt-addin/assets/icon-80.png"/>
      </bt:Images>
      <bt:Urls>
        <bt:Url id="Commands.Url" DefaultValue="https://localhost:3001/commands.html"/>
        <bt:Url id="Taskpane.Url" DefaultValue="https://localhost:3001/src/taskpane/index.html"/>
      </bt:Urls>
      <bt:ShortStrings>
        <bt:String id="GetStarted.Title" DefaultValue="FAIT for PowerPoint (Dev)"/>
        <bt:String id="CommandsGroup.Label" DefaultValue="FAIT"/>
        <bt:String id="TaskpaneButton.Label" DefaultValue="Open FAIT"/>
        <bt:String id="GetStarted.Description" DefaultValue="Local dev build"/>
        <bt:String id="GetStarted.LearnMoreUrl" DefaultValue="https://fait.dev.fortressam.ai"/>
      </bt:ShortStrings>
      <bt:LongStrings>
        <bt:String id="TaskpaneButton.Tooltip" DefaultValue="Open the FAIT for PowerPoint assistant (local dev)"/>
      </bt:LongStrings>
    </Resources>
  </VersionOverrides>
</OfficeApp>
```

---

### Task 8: `src/taskpane/styles/global.css`

**Exact copy from FfE.** No changes. Same design tokens, same scrollbar, same keyframes.

---

### Task 9: `src/taskpane/services/settings.ts`

**Exact copy from FfE.** The `OfficeRuntime.storage` shim is correct and must not be changed. (L5)

The storage keys (`fait_api_key`, `fait_model`, etc.) are identical — scoped to the FfP add-in by `OfficeRuntime.storage`'s per-add-in isolation. No key conflicts with FfE.

---

### Task 10: `src/taskpane/services/faitApi.ts`

**Exact copy from FfE.** Same backend (`https://fait.dev.fortressam.ai`), same `/api/haven/chat` endpoint, same streaming logic. No changes.

---

### Task 11: `src/taskpane/hooks/useChat.ts`

**Exact copy from FfE** for Sprint 1. The hook is API-agnostic — it only calls `sendChat()` / `sendChatStreaming()`. No PowerPoint-specific logic needed.

**One modification only:** remove the `tableData` and `reportSpec` and `formulaSpec` fields from the `Message` interface — those are FfE Sprint 6–11 additions not needed in Sprint 1. Keep the interface lean:

```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
}
```

Everything else in `useChat.ts` is copied verbatim.

**Do NOT import** `parseSuggestions` — FfP Sprint 1 has no structured JSON block parsing. FAIT responses are plain text only in Sprint 1.

---

### Task 12 (NEW): `src/taskpane/services/pptReader.ts`

This is the PowerPoint equivalent of FfE's `excelReader.ts`. It reads the current slide context for injection into the chat prompt.

```typescript
/* global PowerPoint */

declare const PowerPoint: any;

export interface ShapeContext {
  id: string;
  name: string;
  text: string;         // textFrame.textRange.text (empty string if no text)
  isSelected: boolean;  // true if this shape is the selected shape
  hasText: boolean;     // true if textFrame exists and has text
}

export interface SlideContext {
  slideIndex: number;        // 0-based index
  slideNumber: number;       // 1-based (for display)
  title: string;             // first shape with type "title" text, or first shape text
  shapes: ShapeContext[];    // all shapes with text
  notes: string;             // speaker notes text (empty string if none)
  selectedShapeId: string | null;  // ID of currently selected shape, or null
  selectedShapeText: string;       // text of selected shape (empty string if none)
}

/**
 * Read the context of the currently selected slide.
 * Requires PowerPointApi 1.5 for getSelectedSlides() + getSelectedShapes().
 * Speaker notes require 1.6 — guarded with try/catch for graceful degradation.
 */
export async function getSlideContext(): Promise<SlideContext> {
  return PowerPoint.run(async (ctx: any) => {
    // ── 1. Get selected slides ───────────────────────────────────────────
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    const slides = selectedSlides.items;
    if (!slides || slides.length === 0) {
      return emptySlideContext();
    }

    // Use first selected slide
    const slide = slides[0];
    slide.load('id');

    // ── 2. Get slide index (position in deck) ────────────────────────────
    // slide.index is 0-based position in the presentation
    // Not directly available — use presentation.slides to find position
    const allSlides = ctx.presentation.slides;
    allSlides.load(['items/id', 'items/shapes/items/id',
                    'items/shapes/items/name',
                    'items/shapes/items/textFrame/textRange/text',
                    'items/shapes/items/type']);
    await ctx.sync();

    const slideItems = allSlides.items as any[];
    const slideIndex = slideItems.findIndex((s: any) => s.id === slide.id);
    const slideData = slideIndex >= 0 ? slideItems[slideIndex] : null;

    if (!slideData) {
      return emptySlideContext();
    }

    // ── 3. Get selected shapes ──────────────────────────────────────────
    const selectedShapes = ctx.presentation.getSelectedShapes();
    selectedShapes.load('items/id');
    await ctx.sync();

    const selectedShapeIds = new Set(
      (selectedShapes.items as any[]).map((s: any) => s.id as string)
    );

    // ── 4. Extract shapes with text from the slide ─────────────────────
    const shapeContexts: ShapeContext[] = [];
    let titleText = '';
    let selectedShapeId: string | null = null;
    let selectedShapeText = '';

    for (const shape of (slideData.shapes?.items ?? []) as any[]) {
      const text: string = shape.textFrame?.textRange?.text ?? '';
      const hasText = text.trim().length > 0;
      const isSelected = selectedShapeIds.has(shape.id);

      if (isSelected) {
        selectedShapeId = shape.id;
        selectedShapeText = text;
      }

      // Heuristic: first shape with 'title' in the name or type is the slide title
      const shapeName: string = (shape.name ?? '').toLowerCase();
      if (!titleText && (shapeName.includes('title') || shape.type === 'title')) {
        titleText = text;
      }

      if (hasText) {
        shapeContexts.push({
          id: shape.id,
          name: shape.name ?? '',
          text,
          isSelected,
          hasText: true,
        });
      }
    }

    // Fallback: if no title shape found, use first shape text
    if (!titleText && shapeContexts.length > 0) {
      titleText = shapeContexts[0].text;
    }

    // ── 5. Read speaker notes (PowerPointApi 1.6 — graceful degradation) ─
    let notesText = '';
    try {
      const notes = slideData.notes;
      if (notes?.textFrame?.textRange?.text) {
        notesText = notes.textFrame.textRange.text;
      }
    } catch {
      // Notes API not available on this version — silently omit
    }

    return {
      slideIndex,
      slideNumber: slideIndex + 1,
      title: titleText,
      shapes: shapeContexts,
      notes: notesText,
      selectedShapeId,
      selectedShapeText,
    };
  }).catch((): SlideContext => emptySlideContext());
}

function emptySlideContext(): SlideContext {
  return {
    slideIndex: 0,
    slideNumber: 1,
    title: '',
    shapes: [],
    notes: '',
    selectedShapeId: null,
    selectedShapeText: '',
  };
}

/**
 * Format a SlideContext into a prompt context block for injection into FAIT.
 */
export function formatSlideContext(ctx: SlideContext): string {
  let out = `[PRESENTATION CONTEXT]\n`;
  out += `Slide: ${ctx.slideNumber}`;
  if (ctx.title) out += ` — ${ctx.title}`;
  out += `\n`;

  if (ctx.selectedShapeId && ctx.selectedShapeText) {
    out += `Selected shape text:\n${ctx.selectedShapeText.slice(0, 800)}\n`;
  }

  if (ctx.shapes.length > 0) {
    const otherShapes = ctx.shapes.filter(
      (s) => !s.isSelected && s.text.trim()
    );
    if (otherShapes.length > 0) {
      out += `Other shapes on this slide:\n`;
      for (const s of otherShapes.slice(0, 5)) {
        // Cap each shape at 200 chars to stay within token budget
        out += `  • ${s.name}: ${s.text.slice(0, 200).replace(/\n/g, ' ')}\n`;
      }
    }
  }

  if (ctx.notes) {
    out += `Speaker notes:\n${ctx.notes.slice(0, 500)}\n`;
  }

  out += `[END PRESENTATION CONTEXT]`;
  return out;
}
```

**Gotchas documented in `pptReader.ts`:**
- `getSelectedSlides()` requires PowerPointApi **1.5** — correct baseline
- `slide.notes` for speaker notes requires **1.6** — guarded with try/catch
- `getSelectedShapes()` requires **1.5** — correct baseline
- Loading `items/shapes/items/textFrame/textRange/text` is a deep-path load — must use string notation, not chained `.load()` calls on nested objects in the PowerPoint JS API

---

### Task 13 (NEW): `src/taskpane/services/pptWriter.ts`

```typescript
/* global PowerPoint */

declare const PowerPoint: any;

export class PptWriteError extends Error {
  constructor(
    message: string,
    public readonly code: 'SHAPE_NOT_FOUND' | 'NO_TEXT_FRAME' | 'PPT_ERROR'
  ) {
    super(message);
    this.name = 'PptWriteError';
  }
}

/**
 * Write text to a specific shape's text frame.
 * Replaces all text in the shape's first text range.
 *
 * @param shapeId   The shape's ID from SlideContext.shapes[n].id
 * @param text      The text to write
 */
export async function applyTextToShape(shapeId: string, text: string): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
    // Find the shape on the active slide
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items/id');
    await ctx.sync();

    if (!selectedSlides.items || selectedSlides.items.length === 0) {
      throw new PptWriteError('No slide selected', 'SHAPE_NOT_FOUND');
    }

    const slide = selectedSlides.items[0];
    const shapes = slide.shapes;
    shapes.load('items/id');
    await ctx.sync();

    const target = (shapes.items as any[]).find((s: any) => s.id === shapeId);
    if (!target) {
      throw new PptWriteError(`Shape ${shapeId} not found on active slide`, 'SHAPE_NOT_FOUND');
    }

    // Load the textFrame to verify it exists
    target.load('textFrame/hasText');
    await ctx.sync();

    if (!target.textFrame) {
      throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
    }

    // Write the text — replaces the entire text range
    target.textFrame.textRange.text = text;
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof PptWriteError) throw e;
    throw new PptWriteError(
      e?.message ?? 'PowerPoint write failed',
      'PPT_ERROR'
    );
  });
}
```

---

### Task 14 (NEW): `src/taskpane/hooks/usePptContext.ts`

Polls for slide context every 2 seconds. Equivalent to FfE's `useExcelContext.ts` polling loop.

```typescript
import { useState, useEffect, useRef } from 'react';
import { getSlideContext } from '../services/pptReader';
import type { SlideContext } from '../services/pptReader';

export interface UsePptContextReturn {
  slideContext: SlideContext | null;
  refreshing: boolean;
  error: string | null;
  refresh: () => Promise<void>;
}

/**
 * Polls getSlideContext() every 2 seconds to keep slide context fresh.
 * Used by ChatPanel to know the current slide + selected shape.
 *
 * Polling (not events) because onSlideSelectionChanged is Preview-only in PowerPointApi 1.5.
 * This mirrors FfE's useExcelContext.ts approach.
 */
export function usePptContext(): UsePptContextReturn {
  const [slideContext, setSlideContext] = useState<SlideContext | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const refresh = async () => {
    setRefreshing(true);
    try {
      const ctx = await getSlideContext();
      setSlideContext(ctx);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to read slide context');
    } finally {
      setRefreshing(false);
    }
  };

  useEffect(() => {
    // Initial read
    refresh();

    // Poll every 2 seconds
    intervalRef.current = setInterval(() => {
      refresh();
    }, 2000);

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return { slideContext, refreshing, error, refresh };
}
```

---

### Task 15: `src/taskpane/components/SettingsPanel.tsx`

Port from FfE. Three text changes only:

1. `"FAIT for Excel"` → `"FAIT for PowerPoint"`
2. `"Excel add-in settings"` → `"PowerPoint add-in settings"` (or similar label)
3. Remove the "Named Ranges" section (Sprint 8 FfE feature — not in FfP Sprint 1)

**Everything else is identical:** API key input, model selector, KB toggles, project selector. Keep all existing logic, styling, and button handlers verbatim.

**Do NOT change:** `OfficeRuntime.storage` calls, `saveSetting()` usage, `loadSettings()` call, or the existing prop interface.

---

### Task 16 (NEW): `src/taskpane/components/ShapePreview.tsx`

The FfP equivalent of FfE's `WriteSuggestionsDialog`. Shows the AI-generated text before writing it to a shape, with Accept/Reject buttons.

```typescript
import React from 'react';

interface ShapePreviewProps {
  pendingText: string;           // AI-generated text to apply
  targetShapeName: string;       // Name of the target shape (for display)
  onAccept: () => void;          // User clicked Apply — fire the write
  onReject: () => void;          // User clicked Discard
  loading?: boolean;             // Write in progress
}

const ShapePreview: React.FC<ShapePreviewProps> = ({
  pendingText,
  targetShapeName,
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
      {/* Header */}
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
        <span>▶</span>
        <span>Apply to: {targetShapeName || 'selected shape'}</span>
      </div>

      {/* Preview text */}
      <div
        style={{
          background: '#131f2e',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          padding: '8px 10px',
          fontSize: '12px',
          color: '#e8edf3',
          lineHeight: 1.6,
          maxHeight: '120px',
          overflowY: 'auto',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
        }}
      >
        {pendingText}
      </div>

      {/* Action buttons */}
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
          {loading ? 'Applying…' : '✓ Apply to Shape'}
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

export default ShapePreview;
```

---

### Task 17: `src/taskpane/components/ChatPanel.tsx`

Port from FfE `ChatPanel.tsx` with the following changes. Do NOT start from scratch — port the existing file and apply these modifications surgically.

**Remove (FfE-specific, not in FfP Sprint 1):**
- All Sprint 4–11 handlers and state (chart, pivot, CF, sort/filter, watch mode, named ranges, formula, report)
- `WriteSuggestionsDialog` import and usage
- `writeRangeData`, `writeToTable`, `insertChart`, etc. imports
- `parseSuggestions` import (Sprint 1 has no structured JSON parsing)
- `selectionInfo` state and `getSelectedRange()` call (replaced by `usePptContext`)
- `ContextIndicator` component (replaced by a minimal `SlideContextBar`)
- FORGE search panel (Sprint 2 FfP feature)

**Keep (port verbatim):**
- Header bar structure and `headerBtnStyle`
- `useChat` hook integration
- `ChatInput` component and `SlashCommandPicker` (no slash commands yet, but keep the infra)
- Message list + streaming rendering
- Settings gear icon and `onOpenSettings` callback
- Error display and loading state
- Session persistence (custom XML via `sessionStorage.ts` — port from FfE)

**Add (FfP-specific):**

1. Import `usePptContext` and `getSlideContext` + `formatSlideContext`:

```typescript
import { usePptContext } from '../hooks/usePptContext';
import { formatSlideContext } from '../services/pptReader';
import { applyTextToShape, PptWriteError } from '../services/pptWriter';
import ShapePreview from './ShapePreview';
```

2. Use the context hook:

```typescript
const { slideContext, refresh: refreshSlideContext } = usePptContext();
```

3. Add state for the Apply to Shape flow:

```typescript
// Apply to Shape state
const [pendingApplyText, setPendingApplyText] = useState<string | null>(null);
const [applyLoading, setApplyLoading] = useState(false);
const [applyError, setApplyError] = useState<string | null>(null);
```

4. Update `handleSend` to inject slide context:

```typescript
const handleSend = async (text: string) => {
  let context: string | undefined;

  // Always include slide context (no toggle — the whole slide IS the context in PPT)
  try {
    const ctx = await getSlideContext();
    if (ctx.slideNumber > 0) {
      context = formatSlideContext(ctx);
    }
  } catch {
    // Non-fatal
  }

  await send(text, context);
};
```

5. After FAIT responds (via `useEffect` watching `messages`), check for Apply to Shape:

The user may explicitly ask FAIT to write to the current shape ("write this to my shape"). Rather than a structured JSON block (Sprint 2+), Sprint 1 uses a simple rule: if the user's last message contained "apply to shape" or "write to shape" or "apply to slide", offer the Apply button with the full FAIT response text.

```typescript
// Watch messages for Apply trigger (Sprint 1: keyword-based, Sprint 2: ppt_text_spec)
useEffect(() => {
  const lastMsg = messages[messages.length - 1];
  if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
    // Check the preceding user message for apply intent
    const prevUserMsg = [...messages].reverse().find((m) => m.role === 'user');
    if (prevUserMsg) {
      const lower = prevUserMsg.content.toLowerCase();
      if (
        lower.includes('apply') ||
        lower.includes('write to shape') ||
        lower.includes('write to slide') ||
        lower.includes('update shape') ||
        lower.includes('put this in')
      ) {
        setPendingApplyText(lastMsg.content);
        setApplyError(null);
      }
    }
  }
}, [messages]);
```

6. Add apply handlers:

```typescript
const handleApplyToShape = async () => {
  if (!pendingApplyText || !slideContext?.selectedShapeId) {
    setApplyError(
      slideContext?.selectedShapeId
        ? 'No text to apply.'
        : 'Select a shape in PowerPoint first.'
    );
    return;
  }

  setApplyLoading(true);
  setApplyError(null);

  try {
    await applyTextToShape(slideContext.selectedShapeId, pendingApplyText);
    setPendingApplyText(null);
    await refreshSlideContext();
  } catch (e) {
    if (e instanceof PptWriteError) {
      if (e.code === 'SHAPE_NOT_FOUND') {
        setApplyError('Shape not found — re-select the shape and try again.');
      } else if (e.code === 'NO_TEXT_FRAME') {
        setApplyError('Selected shape cannot hold text.');
      } else {
        setApplyError('Write failed — try again.');
      }
    } else {
      setApplyError('Write failed — try again.');
    }
  } finally {
    setApplyLoading(false);
  }
};

const handleApplyDiscard = () => {
  setPendingApplyText(null);
  setApplyError(null);
};
```

7. Add a minimal `SlideContextBar` (inline, not a separate component) below the header:

```typescript
{/* Slide context indicator */}
{slideContext && (
  <div
    style={{
      padding: '4px 12px',
      borderBottom: '1px solid #2e3f54',
      background: '#0f1720',
      fontSize: '11px',
      color: slideContext.selectedShapeId ? '#d4af37' : '#556677',
      display: 'flex',
      alignItems: 'center',
      gap: '6px',
      flexShrink: 0,
    }}
  >
    <span>🖼</span>
    <span>
      Slide {slideContext.slideNumber}
      {slideContext.title ? ` — ${slideContext.title.slice(0, 40)}` : ''}
      {slideContext.selectedShapeId
        ? ` · ✓ shape selected`
        : ` · no shape selected`}
    </span>
  </div>
)}
```

8. Render `ShapePreview` at the bottom of the chat panel (above `ChatInput`):

```typescript
{pendingApplyText && (
  <ShapePreview
    pendingText={pendingApplyText}
    targetShapeName={
      slideContext?.shapes.find((s) => s.isSelected)?.name ?? 'selected shape'
    }
    onAccept={handleApplyToShape}
    onReject={handleApplyDiscard}
    loading={applyLoading}
  />
)}
{applyError && (
  <div
    style={{
      padding: '4px 12px',
      background: '#1a0f0f',
      color: '#e07070',
      fontSize: '11px',
      flexShrink: 0,
    }}
  >
    {applyError}
  </div>
)}
```

9. Header text change: `"🏰 FAIT"` label stays; sub-label changes from `"for Excel"` to `"for PowerPoint"`.

---

### Task 18: `src/taskpane/App.tsx` + `src/taskpane/index.tsx`

**`index.tsx`:** Exact copy from FfE. `Office.onReady()` wrapper is app-agnostic.

**`App.tsx`:** Port from FfE with one text change: `"FAIT for Excel"` in the loading screen → `"FAIT for PowerPoint"`. Settings gate logic is identical. ChatPanel props are nearly identical (remove `kbToggles` and `projectId` if desired for simplicity in Sprint 1, or keep them for forward compatibility with SettingsPanel — recommended to keep).

---

## FAIT Backend Change: AllowAnonymous for `/ppt-addin/`

**File:** `fip/fait/src/FortressAI.Web/Program.cs`

Add the following block immediately after the existing `/excel-addin/` block (around line 306):

```csharp
// Serve /ppt-addin/ static files publicly (Office Add-in — no auth required)
// Same pattern as /excel-addin/ — FallbackPolicy intercepts UseStaticFiles
app.MapGet("/ppt-addin/{**path}", async (HttpContext ctx, string? path) =>
{
    var webRoot = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
    var filePath = string.IsNullOrEmpty(path)
        ? Path.Combine(webRoot, "ppt-addin", "index.html")
        : Path.Combine(webRoot, "ppt-addin", path.Replace("/", Path.DirectorySeparatorChar.ToString()));

    if (!File.Exists(filePath))
        return Results.NotFound();

    var contentType = Path.GetExtension(filePath) switch
    {
        ".html" => "text/html",
        ".js"   => "application/javascript",
        ".css"  => "text/css",
        ".png"  => "image/png",
        ".svg"  => "image/svg+xml",
        ".json" => "application/json",
        ".xml"  => "application/xml",
        _       => "application/octet-stream"
    };

    return Results.File(filePath, contentType);
}).AllowAnonymous();
```

**This is the only backend change required for Sprint 1.** The FAIT API endpoints (`/api/haven/chat`, etc.) are already auth-protected by the `x-api-key` header — no change needed there.

---

## Build + Deploy Path

### Development

```bash
cd ~/projects/fip/fait-for-powerpoint
npm install
npm run dev
# Server starts at https://127.0.0.1:3001/
# Upload manifest.local.xml to PowerPoint Online → Insert → Get Add-ins → Upload Custom Manifest
```

### Production Build + Copy

```bash
npm run build:copy
# Compiles TypeScript, builds to dist/, copies to:
# fip/fait/src/FortressAI.Web/wwwroot/ppt-addin/
```

The `build:copy` script copies `dist/*` into `wwwroot/ppt-addin/`. After copy, the FAIT Docker build context includes the FfP static files at the correct path.

### FAIT Dockerfile Addition

In `fip/fait/Dockerfile`, after the FfE copy step (or wherever static files are assembled):

```dockerfile
# Build FfP static assets
FROM node:22-alpine AS ffp-build
WORKDIR /app/fait-for-powerpoint
COPY fait-for-powerpoint/package.json fait-for-powerpoint/package-lock.json ./
RUN npm ci
COPY fait-for-powerpoint/ ./
RUN npm run build

# In the final FAIT image:
COPY --from=ffp-build /app/fait-for-powerpoint/dist ./wwwroot/ppt-addin
```

This mirrors the FfE build pattern (WI#813 reference: FAIT Dockerfile already expanded for FfE). The Docker build context must include `fait-for-powerpoint/` — the buildspec `docker build -f fait/Dockerfile` context must be expanded from `fait/` to `.` (the `fip/` root), the same change already made for FfE.

**Simpler alternative for initial deploy:** Use `build:copy` locally before building the Docker image. FfP dist files land in `wwwroot/ppt-addin/` as part of the local FAIT build, then the existing Docker pipeline picks them up. This is the immediate path. The multi-stage Dockerfile approach is the production-clean path.

---

## Files Changed Summary

### New repo: `~/projects/fip/fait-for-powerpoint/`

| File | Type | Source |
|------|------|--------|
| `vite.config.ts` | New | Written from scratch per spec |
| `src/taskpane/index.html` | New | Port from FfE (title change) |
| `public/commands.html` | New | Exact copy from FfE |
| `tsconfig.json` | New | Exact copy from FfE |
| `package.json` | New | Port from FfE (name + path change) |
| `.gitignore` | New | Standard |
| `public/manifest.xml` | New | Adapted from FfE (Presentation host, new GUID) |
| `manifest.local.xml` | New | Adapted from FfE (port 3001) |
| `src/taskpane/styles/global.css` | New | Exact copy from FfE |
| `src/taskpane/services/settings.ts` | New | Exact copy from FfE |
| `src/taskpane/services/faitApi.ts` | New | Exact copy from FfE |
| `src/taskpane/hooks/useChat.ts` | New | Port from FfE (lean Message interface) |
| `src/taskpane/services/pptReader.ts` | New | Written from scratch per spec |
| `src/taskpane/services/pptWriter.ts` | New | Written from scratch per spec |
| `src/taskpane/hooks/usePptContext.ts` | New | Written from scratch per spec |
| `src/taskpane/components/SettingsPanel.tsx` | New | Port from FfE (text changes) |
| `src/taskpane/components/ShapePreview.tsx` | New | Written from scratch per spec |
| `src/taskpane/components/ChatPanel.tsx` | New | Port from FfE (major adaptations per spec) |
| `src/taskpane/App.tsx` | New | Port from FfE (text change) |
| `src/taskpane/index.tsx` | New | Exact copy from FfE |
| `public/assets/icon-16.png` | New | Copy FfE icons (or use same icons) |
| `public/assets/icon-32.png` | New | Copy FfE icons |
| `public/assets/icon-80.png` | New | Copy FfE icons |

### Modified: `fip/fait/src/FortressAI.Web/Program.cs`

| Change | Where |
|--------|-------|
| Add `/ppt-addin/` MapGet + AllowAnonymous block | After line 305 (the excel-addin block) |

**Total: 23 new files + 1 modified file. No new npm packages beyond what FfE already uses.**

---

## Acceptance Criteria

1. **Add-in loads:** PowerPoint Online → Insert → Get Add-ins → upload `manifest.local.xml` → "FAIT for PowerPoint" button appears in the Home tab ribbon → clicking it opens a taskpane
2. **No blank screen:** The taskpane renders the chat UI (not a blank white page) — validates L1 Vite config
3. **Settings:** Clicking ⚙ opens the settings panel. Entering an API key and closing persists the key across page reload
4. **Chat:** Typing a message and sending it produces a FAIT response (streamed). The response appears in the chat thread
5. **Slide context:** The context indicator shows "Slide 1 — [title]" (or "no shape selected") — updates when the user navigates slides
6. **Shape apply:** User selects a text shape in PowerPoint, asks "write a bullet summary of Q1 performance for this slide", FAIT responds → `ShapePreview` panel appears → user clicks "✓ Apply to Shape" → the selected shape's text is updated with the AI-generated text
7. **Reject flow:** Clicking "Discard" in `ShapePreview` dismisses the preview without writing anything
8. **No shape selected:** If no shape is selected and "Apply to Shape" is triggered, an error message appears ("Select a shape in PowerPoint first.")
9. **`/ppt-addin/` serves correctly:** `https://fait.dev.fortressam.ai/ppt-addin/src/taskpane/index.html` returns 200 with the HTML content (validates the `Program.cs` AllowAnonymous block)

---

## Constraints for CC

- Create ALL files in `~/projects/fip/fait-for-powerpoint/` — do NOT touch any file in `~/projects/fait-for-excel/`
- `vite.config.ts` MUST be the first file created. Build it, validate the config looks right, then proceed.
- `@microsoft/office-js` must NOT appear in `package.json`. Check the final package.json before moving on. (L3)
- `public/manifest.xml` and `manifest.local.xml` must be created before any component — this catches URL configuration errors early (L2)
- `manifest.xml` `<Host Name="Presentation"/>` — verify this is "Presentation", not "Workbook". This is the single most likely mistake.
- `pptReader.ts` uses `PowerPoint.run()` — not `Excel.run()`. Declare `declare const PowerPoint: any;` at the top of each file that calls the PPT API.
- `usePptContext.ts` polling interval uses `setInterval`, not `setTimeout`. The cleanup in `useEffect` return clears the interval with `clearInterval`.
- Do NOT import `parseSuggestions` in `useChat.ts` or `ChatPanel.tsx` for Sprint 1. Remove or comment out all references to structured JSON block parsing.
- The `sessionStorage.ts` module (custom XML persistence for chat history) can be ported from FfE or omitted from Sprint 1 — it's not in the acceptance criteria. Include it if there's time; skip if not.
- Icons: copy from `~/projects/fait-for-excel/public/assets/` — same FIP branding. Or use placeholder 1x1 pixel PNGs if icon files don't exist yet.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify manifest.xml <Host Name="Presentation"/> — NOT "Workbook".
          And <Set Name="PowerPointApi" MinVersion="1.5"/> — NOT ExcelApi.
          And <Host xsi:type="Presentation"> in VersionOverrides — NOT Workbook.
          These three mismatches would cause PowerPoint to silently reject the manifest.

⚠️  HIGH: Verify vite.config.ts base is '/ppt-addin/' and the input uses
          'src/taskpane/index.html' (HTML entry point). Confirm there is NO
          format: 'iife' or inlineDynamicImports: true anywhere in the config.
          This is the single failure mode that caused FfE's blank screen crisis.

⚠️  HIGH: Confirm @microsoft/office-js is not in package.json dependencies
          or devDependencies. Only @types/office-js should be present.

⚠️  HIGH: Confirm pptReader.ts loads shape text using the correct deep path load:
          'items/shapes/items/textFrame/textRange/text' — this is a string path,
          not chained .load() calls. In the PowerPoint JS API, nested object
          properties must be loaded via dot-path strings. If shape.textFrame is
          loaded separately after the collection, it may be undefined.

⚠️  MEDIUM: Verify manifest.local.xml uses port 3001 (not 3000) in ALL URL fields:
            <SourceLocation DefaultValue="https://localhost:3001/...">, Commands.Url,
            Taskpane.Url, and in <AppDomains>.

⚠️  MEDIUM: Verify Program.cs AllowAnonymous block uses "ppt-addin" (hyphen) not
            "ppt_addin" (underscore) — must match the Vite base '/ppt-addin/' and
            the manifest URLs.

⚠️  MEDIUM: Confirm applyTextToShape() locates the shape on the ACTIVE slide
            (via getSelectedSlides()), not on all slides. Shape IDs may not be
            globally unique across slides — using getSelectedSlides() + shapes
            on that slide is the correct scoping.

⚠️  LOW: Confirm the manifest GUID b2c3d4e5-f6a7-8901-bcde-f12345678902 is different
         from FfE's GUID a1b2c3d4-e5f6-7890-abcd-ef1234567890. Both must be present
         in the test environment simultaneously — duplicate GUIDs would cause
         Office to merge or replace one with the other.
```

---

## Open Question for Fred

**Repo location decision:** This spec places the repo at `~/projects/fip/fait-for-powerpoint/`. The FAIT Dockerfile (in `fip/fait/`) would need the FfP directory within its build context — same pattern as WI#813 for FfE. 

If Fred prefers a standalone repo (`~/projects/fait-for-powerpoint/`), the spec is identical internally. Only the `build:copy` path and Docker context changes. Confirm before Tony starts Task 1.

---

_Spec by Reed Richards | FfP Sprint 1 is 23 new files + 1 backend edit. The foundation is FfE with PowerPoint surfaces. Apply all 7 FfE lessons from day 1._
