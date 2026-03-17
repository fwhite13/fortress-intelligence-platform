# CC Brief: WI828 — FfP Sprint 1: Foundation + Core Chat + Apply to Shape

You are building a NEW Office Add-in for PowerPoint from scratch at:
`~/projects/fip/fait-for-powerpoint/`

This is a complete 18-task scaffolding session. Create ALL files sequentially in order.
Do NOT modify anything in `~/projects/fait-for-excel/`.

---

## CRITICAL RULES (check each before completing)

1. `<Host Name="Presentation"/>` NOT "Workbook" — must appear in 3 places in BOTH manifests
2. `PowerPoint.run()` NOT `Excel.run()` — in pptReader.ts and pptWriter.ts
3. `declare const PowerPoint: any;` at top of pptReader.ts and pptWriter.ts
4. `tags.add()` must be in same `PowerPoint.run()` as text write (proxy lifetime)
5. `@microsoft/office-js` must NOT be in package.json (only `@types/office-js` in devDependencies)
6. Dev server port: 3001 (not 3000)
7. `base: '/ppt-addin/'` in vite.config.ts
8. GUID: `b2c3d4e5-f6a7-8901-bcde-f12345678902`
9. Tasks are sequential — vite.config.ts first, then index.html, then manifests, then components

---

## Task 1: Create `vite.config.ts`

Create `~/projects/fip/fait-for-powerpoint/vite.config.ts`:

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [
    react(),
    mkcert(),
  ],

  server: {
    port: 3001,
    host: '127.0.0.1',
    https: true,
  },

  build: {
    outDir: 'dist',
    target: 'es2017',
    rollupOptions: {
      input: {
        taskpane: 'src/taskpane/index.html',
        commands: 'public/commands.html',
      },
      output: {
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name][extname]',
      },
    },
  },

  base: '/ppt-addin/',
});
```

---

## Task 2: Create `src/taskpane/index.html`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/index.html`:

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
    <!-- Office JS — CDN only. Never install @microsoft/office-js from npm. -->
    <script src="https://appsforoffice.microsoft.com/lib/1/hosted/office.js" type="text/javascript"></script>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/taskpane/index.tsx"></script>
  </body>
</html>
```

---

## Task 3: Create `public/commands.html`

Create `~/projects/fip/fait-for-powerpoint/public/commands.html`:

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

## Task 4: Create `tsconfig.json`

Create `~/projects/fip/fait-for-powerpoint/tsconfig.json`:

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

## Task 5: Create `package.json`

Create `~/projects/fip/fait-for-powerpoint/package.json`:

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

IMPORTANT: Verify `@microsoft/office-js` is NOT present. Only `@types/office-js` is allowed.

---

## Task 6: Create `.gitignore`

Create `~/projects/fip/fait-for-powerpoint/.gitignore`:

```
node_modules/
dist/
.env
.env.local
*.local
.DS_Store
```

---

## Task 7: Create both manifests

### Create `~/projects/fip/fait-for-powerpoint/public/manifest.xml`:

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

### Create `~/projects/fip/fait-for-powerpoint/manifest.local.xml`:

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

## Task 8: Create `src/taskpane/styles/global.css`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/styles/global.css`:

```css
/* FAIT for PowerPoint — Global Styles */

*, *::before, *::after {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

html, body, #root {
  height: 100%;
  width: 100%;
  min-width: 300px;
}

body {
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  background-color: #1a2332;
  color: #e8edf3;
  font-size: 14px;
  line-height: 1.5;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

/* Scrollbar styling */
::-webkit-scrollbar {
  width: 6px;
}
::-webkit-scrollbar-track {
  background: #0f1720;
}
::-webkit-scrollbar-thumb {
  background: #2e3f54;
  border-radius: 3px;
}
::-webkit-scrollbar-thumb:hover {
  background: #d4af37;
}

/* Focus visible */
:focus-visible {
  outline: 2px solid #d4af37;
  outline-offset: 2px;
}

/* Keyframe: bouncing dots */
@keyframes bounce {
  0%, 80%, 100% { transform: translateY(0); }
  40% { transform: translateY(-6px); }
}

/* Keyframe: fade in */
@keyframes fadeIn {
  from { opacity: 0; transform: translateY(4px); }
  to   { opacity: 1; transform: translateY(0); }
}
```

---

## Task 9: Create `src/taskpane/services/settings.ts`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/services/settings.ts`:

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

export interface FaitSettings {
  apiKey: string | null;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
}

export async function loadSettings(): Promise<FaitSettings> {
  const storage = getStorage();
  const [apiKey, model, projectId, corpToggle, teamToggle] = await Promise.all([
    storage.getItem('fait_api_key').catch(() => null),
    storage.getItem('fait_model').catch(() => null),
    storage.getItem('fait_project_id').catch(() => null),
    storage.getItem('fait_kb_corp').catch(() => null),
    storage.getItem('fait_kb_team').catch(() => null),
  ]);
  return {
    apiKey: apiKey ?? null,
    model: model === 'haiku' ? 'haiku' : 'sonnet',
    kbToggles: {
      corp: corpToggle !== 'false',
      team: teamToggle === 'true',
    },
    projectId: projectId || null,
  };
}

export async function saveSetting(key: string, value: string): Promise<void> {
  const storage = getStorage();
  await storage.setItem(key, value).catch(() => {
    throw new Error('STORAGE_UNAVAILABLE');
  });
}

export async function setApiKey(key: string): Promise<void> {
  await saveSetting('fait_api_key', key);
}
```

---

## Task 10: Create `src/taskpane/services/faitApi.ts`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/services/faitApi.ts`:

```typescript
const FAIT_BASE = 'https://fait.dev.fortressam.ai';

export interface ChatResponse {
  answer: string;
  sources: string[];
}

export async function sendChat(
  message: string,
  apiKey: string,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal,
  kbTypes?: string[],
  projectId?: string | null
): Promise<ChatResponse> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 30_000);

  const combinedSignal = signal ?? controller.signal;

  try {
    const resp = await fetch(`${FAIT_BASE}/api/haven/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-api-key': apiKey,
      },
      body: JSON.stringify({
        message,
        model,
        kbTypes: kbTypes ?? undefined,
        projectId: projectId ?? undefined,
      }),
      signal: combinedSignal,
    });

    if (resp.status === 401) throw new Error('INVALID_KEY');
    if (resp.status === 502 || resp.status === 503) throw new Error('SERVICE_UNAVAILABLE');
    if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

    return await resp.json();
  } catch (err) {
    if (err instanceof Error && err.name === 'AbortError') {
      throw new Error('TIMEOUT');
    }
    throw err;
  } finally {
    clearTimeout(timeout);
  }
}

export async function sendChatStreaming(
  message: string,
  apiKey: string,
  onChunk: (text: string) => void,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal,
  kbTypes?: string[],
  projectId?: string | null
): Promise<void> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/chat`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
      Accept: 'text/event-stream',
    },
    body: JSON.stringify({
      message,
      model,
      kbTypes: kbTypes ?? undefined,
      projectId: projectId ?? undefined,
    }),
    signal,
  });

  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

  const contentType = resp.headers.get('content-type') ?? '';
  if (!contentType.includes('text/event-stream')) {
    const data: ChatResponse = await resp.json();
    if (data.answer) onChunk(data.answer);
    return;
  }

  const reader = resp.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() ?? '';

    for (const line of lines) {
      if (line.startsWith('data: ') && line.trim() !== 'data: [DONE]') {
        try {
          onChunk(JSON.parse(line.slice(6)));
        } catch {
          /* ignore parse errors */
        }
      }
    }
  }
}

export interface KbInfo {
  id: string;
  name: string;
  type: string;
  alwaysOn: boolean;
  available: boolean;
}

export interface ProjectInfo {
  id: string;
  name: string;
}

export async function fetchKbList(apiKey: string): Promise<KbInfo[]> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-list`, {
    headers: { 'x-api-key': apiKey },
  });
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.kbs ?? [];
}

export async function fetchProjectList(apiKey: string): Promise<ProjectInfo[]> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/project-list`, {
    headers: { 'x-api-key': apiKey },
  });
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.projects ?? [];
}
```

---

## Task 11: Create `src/taskpane/hooks/useChat.ts`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/hooks/useChat.ts`:

NOTE: This is a lean version for FfP Sprint 1. No parseSuggestions, no tableData, no reportSpec, no formulaSpec, no CellSuggestion. Keep the Message interface clean.

```typescript
import { useState } from 'react';
import { sendChat, sendChatStreaming } from '../services/faitApi';

export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
}

export interface UseChatReturn {
  messages: Message[];
  loading: boolean;
  error: string | null;
  send: (text: string, context?: string) => Promise<void>;
  clearError: () => void;
  setMessages: React.Dispatch<React.SetStateAction<Message[]>>;
}

export function useChat(
  apiKey: string,
  model: 'haiku' | 'sonnet',
  kbToggles?: Record<string, boolean>,
  projectId?: string | null,
  initialMessages?: Message[]
): UseChatReturn {
  const [messages, setMessages] = useState<Message[]>(initialMessages ?? []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const buildKbTypes = (): string[] => {
    if (!kbToggles) return ['corp', 'personal'];
    const types = Object.entries(kbToggles)
      .filter(([, v]) => v)
      .map(([k]) => k);
    if (!types.includes('personal')) types.push('personal');
    return types;
  };

  const send = async (text: string, context?: string) => {
    const fullMessage = context ? `${context}\n\nUser question: ${text}` : text;

    setMessages((prev) => [...prev, { role: 'user', content: text }]);
    setLoading(true);
    setError(null);

    const assistantIndex = await new Promise<number>((resolve) => {
      setMessages((prev) => {
        resolve(prev.length);
        return [...prev, { role: 'assistant', content: '', streaming: true }];
      });
    });

    const kbTypes = buildKbTypes();

    try {
      let rawText = '';

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 30_000);

      try {
        await sendChatStreaming(
          fullMessage,
          apiKey,
          (chunk) => {
            rawText += chunk;
            setMessages((prev) => {
              const next = [...prev];
              next[assistantIndex] = {
                role: 'assistant',
                content: rawText,
                streaming: true,
              };
              return next;
            });
          },
          model,
          controller.signal,
          kbTypes,
          projectId
        );
        clearTimeout(timeout);
      } catch (streamErr) {
        clearTimeout(timeout);
        const msg = streamErr instanceof Error ? streamErr.message : '';
        if (msg === 'INVALID_KEY' || msg.startsWith('HTTP_')) {
          throw streamErr;
        }
        rawText = '';
        const { answer } = await sendChat(fullMessage, apiKey, model, undefined, kbTypes, projectId);
        rawText = answer;
      }

      setMessages((prev) => {
        const next = [...prev];
        next[assistantIndex] = {
          role: 'assistant',
          content: rawText,
          streaming: false,
        };
        return next;
      });
    } catch (e) {
      setMessages((prev) => prev.filter((_, i) => i !== assistantIndex));

      const msg = e instanceof Error ? e.message : 'Unknown error';
      if (msg === 'INVALID_KEY') {
        setError('Invalid API key — check Settings');
      } else if (msg === 'TIMEOUT') {
        setError('FAIT took too long — try a shorter question');
      } else if (msg === 'SERVICE_UNAVAILABLE') {
        setError('FAIT service unavailable — try again');
      } else {
        setError('FAIT unavailable — try again');
      }
    } finally {
      setLoading(false);
      setMessages((prev) =>
        prev.map((m) => (m.streaming ? { ...m, streaming: false } : m))
      );
    }
  };

  const clearError = () => setError(null);

  return { messages, loading, error, send, clearError, setMessages };
}
```

---

## Task 12: Create `src/taskpane/services/pptReader.ts`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/services/pptReader.ts`:

CRITICAL: Use `PowerPoint.run()` NOT `Excel.run()`. Add `declare const PowerPoint: any;` at top.

```typescript
/* global PowerPoint */

declare const PowerPoint: any;

export interface ShapeContext {
  id: string;
  name: string;
  text: string;
  isSelected: boolean;
  hasText: boolean;
}

export interface SlideContext {
  slideIndex: number;
  slideNumber: number;
  title: string;
  shapes: ShapeContext[];
  notes: string;
  selectedShapeId: string | null;
  selectedShapeText: string;
}

export async function getSlideContext(): Promise<SlideContext> {
  return PowerPoint.run(async (ctx: any) => {
    const selectedSlides = ctx.presentation.getSelectedSlides();
    selectedSlides.load('items');
    await ctx.sync();

    const slides = selectedSlides.items;
    if (!slides || slides.length === 0) {
      return emptySlideContext();
    }

    const slide = slides[0];
    slide.load('id');

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

    const selectedShapes = ctx.presentation.getSelectedShapes();
    selectedShapes.load('items/id');
    await ctx.sync();

    const selectedShapeIds = new Set(
      (selectedShapes.items as any[]).map((s: any) => s.id as string)
    );

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

    if (!titleText && shapeContexts.length > 0) {
      titleText = shapeContexts[0].text;
    }

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

---

## Task 13: Create `src/taskpane/services/pptWriter.ts`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/services/pptWriter.ts`:

CRITICAL: Use `PowerPoint.run()` NOT `Excel.run()`. Add `declare const PowerPoint: any;` at top.

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

export async function applyTextToShape(shapeId: string, text: string): Promise<void> {
  return PowerPoint.run(async (ctx: any) => {
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

    target.load('textFrame/hasText');
    await ctx.sync();

    if (!target.textFrame) {
      throw new PptWriteError(`Shape ${shapeId} has no text frame`, 'NO_TEXT_FRAME');
    }

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

## Task 14: Create `src/taskpane/hooks/usePptContext.ts`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/hooks/usePptContext.ts`:

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
    refresh();

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

## Task 15: Create `src/taskpane/components/SettingsPanel.tsx`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/components/SettingsPanel.tsx`:

Port from FfE with these changes:
1. Remove all named ranges section (Sprint 8 FfE feature)
2. Remove namedRangeStorage, excelWriter imports
3. Change title text from "FAIT for Excel" to "FAIT for PowerPoint"
4. Keep all KB toggles, project selector, model picker, API key section

```typescript
import React, { useState, useEffect } from 'react';
import { setApiKey } from '../services/settings';
import { sendChat, fetchKbList, fetchProjectList } from '../services/faitApi';
import { saveSetting } from '../services/settings';
import ModelPicker from './ModelPicker';
import type { KbInfo, ProjectInfo } from '../services/faitApi';

interface SettingsPanelProps {
  onClose: () => void;
  apiKey: string;
  onKeyChange: (key: string) => void;
}

const SettingsPanel: React.FC<SettingsPanelProps> = ({
  onClose,
  apiKey,
  onKeyChange,
}) => {
  const [inputKey, setInputKey] = useState(apiKey ?? '');
  const [testing, setTesting] = useState(false);
  const [keyError, setKeyError] = useState<string | null>(null);
  const [keySuccess, setKeySuccess] = useState(false);

  const [kbList, setKbList] = useState<KbInfo[]>([]);
  const [kbToggles, setKbToggles] = useState<Record<string, boolean>>({});
  const [kbLoading, setKbLoading] = useState(false);

  const [projects, setProjects] = useState<ProjectInfo[]>([]);
  const [selectedProject, setSelectedProject] = useState<string>('');
  const [projectsLoading, setProjectsLoading] = useState(false);

  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');

  useEffect(() => {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const storage = (window as any).OfficeRuntime?.storage;
    /* eslint-enable @typescript-eslint/no-explicit-any */
    if (!storage) return;

    Promise.all([
      storage.getItem('fait_model').catch(() => null),
      storage.getItem('fait_project_id').catch(() => null),
      storage.getItem('fait_kb_corp').catch(() => null),
      storage.getItem('fait_kb_team').catch(() => null),
    ]).then(([storedModel, storedProject, corpToggle, teamToggle]) => {
      if (storedModel === 'haiku' || storedModel === 'sonnet') setModel(storedModel);
      if (storedProject) setSelectedProject(storedProject);
      setKbToggles({
        corp: corpToggle !== 'false',
        team: teamToggle === 'true',
      });
    });
  }, []);

  useEffect(() => {
    if (!apiKey) return;
    setKbLoading(true);
    fetchKbList(apiKey)
      .then((list) => {
        setKbList(list);
        setKbToggles((prev) => {
          const next = { ...prev };
          for (const kb of list) {
            if (!(kb.id in next)) {
              next[kb.id] = kb.alwaysOn || kb.type === 'corp';
            }
          }
          return next;
        });
      })
      .finally(() => setKbLoading(false));

    setProjectsLoading(true);
    fetchProjectList(apiKey)
      .then(setProjects)
      .finally(() => setProjectsLoading(false));
  }, [apiKey]);

  const handleSaveAndTest = async () => {
    const trimmed = inputKey.trim();
    if (!trimmed) {
      setKeyError('Please enter an API key');
      return;
    }
    setTesting(true);
    setKeyError(null);
    setKeySuccess(false);
    try {
      await sendChat('ping', trimmed);
      await setApiKey(trimmed);
      onKeyChange(trimmed);
      setKeySuccess(true);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      if (msg === 'INVALID_KEY') {
        setKeyError('Invalid API key — double-check and try again');
      } else if (msg === 'TIMEOUT') {
        setKeyError('Connection timed out — check your network');
      } else {
        setKeyError('FAIT service unavailable — try again later');
      }
    } finally {
      setTesting(false);
    }
  };

  const handleKbToggle = async (id: string, value: boolean) => {
    setKbToggles((prev) => ({ ...prev, [id]: value }));
    await saveSetting(`fait_kb_${id}`, String(value)).catch(() => null);
  };

  const handleProjectChange = async (id: string) => {
    setSelectedProject(id);
    await saveSetting('fait_project_id', id).catch(() => null);
  };

  const handleModelChange = async (m: 'haiku' | 'sonnet') => {
    setModel(m);
    await saveSetting('fait_model', m).catch(() => null);
  };

  const sectionStyle: React.CSSProperties = {
    background: '#1e2d3e',
    borderRadius: '8px',
    padding: '14px 16px',
    display: 'flex',
    flexDirection: 'column',
    gap: '10px',
  };

  const sectionHeadingStyle: React.CSSProperties = {
    color: '#d4af37',
    fontSize: '12px',
    fontWeight: '700',
    letterSpacing: '0.06em',
    textTransform: 'uppercase',
    marginBottom: '2px',
  };

  const labelStyle: React.CSSProperties = {
    color: '#8899aa',
    fontSize: '12px',
    lineHeight: 1.4,
  };

  const toggleRowStyle: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '6px 0',
    borderBottom: '1px solid #2e3f54',
  };

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        background: '#1a2332',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 12px',
          borderBottom: '1px solid #2e3f54',
          background: '#0f1720',
          flexShrink: 0,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ color: '#d4af37', fontWeight: '700', fontSize: '14px' }}>🏰 FAIT</span>
          <span style={{ color: '#556677', fontSize: '11px' }}>PowerPoint Settings</span>
        </div>
        <button
          onClick={onClose}
          title="Back to chat"
          aria-label="Back to chat"
          style={{
            background: 'none',
            border: 'none',
            color: '#8899aa',
            cursor: 'pointer',
            fontSize: '13px',
            padding: '2px 6px',
            borderRadius: '4px',
            fontFamily: 'Inter, sans-serif',
          }}
        >
          ← Chat
        </button>
      </div>

      {/* Scrollable body */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: '12px',
          display: 'flex',
          flexDirection: 'column',
          gap: '12px',
        }}
      >
        {/* API Key */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>API Key</div>
          <p style={labelStyle}>
            Enter your FAIT API key. Contact IT or check your onboarding email.
          </p>

          <input
            type="password"
            value={inputKey}
            onChange={(e) => { setInputKey(e.target.value); setKeySuccess(false); }}
            onKeyDown={(e) => { if (e.key === 'Enter') handleSaveAndTest(); }}
            placeholder="Paste your API key here…"
            autoComplete="off"
            style={{
              width: '100%',
              background: '#243447',
              border: `1px solid ${keyError ? '#e74c3c' : '#2e3f54'}`,
              borderRadius: '6px',
              color: '#e8edf3',
              fontFamily: 'Inter, sans-serif',
              fontSize: '13px',
              padding: '8px 10px',
              outline: 'none',
              boxSizing: 'border-box',
            }}
            onFocus={(e) => { if (!keyError) e.target.style.borderColor = '#d4af37'; }}
            onBlur={(e) => { if (!keyError) e.target.style.borderColor = '#2e3f54'; }}
          />

          {keyError && (
            <div
              role="alert"
              style={{
                color: '#e74c3c',
                fontSize: '12px',
                padding: '6px 10px',
                background: '#2d1515',
                borderRadius: '4px',
                border: '1px solid #e74c3c',
              }}
            >
              ⚠ {keyError}
            </div>
          )}

          {keySuccess && (
            <div
              style={{
                color: '#4caf50',
                fontSize: '12px',
                padding: '6px 10px',
                background: '#152d15',
                borderRadius: '4px',
                border: '1px solid #4caf50',
              }}
            >
              ✓ Key saved and verified
            </div>
          )}

          <button
            onClick={handleSaveAndTest}
            disabled={testing}
            style={{
              width: '100%',
              background: testing ? '#243447' : '#d4af37',
              border: 'none',
              borderRadius: '6px',
              color: testing ? '#8899aa' : '#1a2332',
              cursor: testing ? 'not-allowed' : 'pointer',
              fontFamily: 'Inter, sans-serif',
              fontWeight: '600',
              fontSize: '13px',
              padding: '9px',
              transition: 'background 0.15s',
            }}
          >
            {testing ? 'Testing connection…' : 'Save & Test Connection'}
          </button>
        </div>

        {/* Knowledge Bases */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Knowledge Bases</div>

          {kbLoading && (
            <div style={{ color: '#556677', fontSize: '12px' }}>Loading…</div>
          )}

          {!kbLoading && kbList.length === 0 && (
            <div style={{ color: '#556677', fontSize: '12px' }}>
              {apiKey ? 'No knowledge bases configured.' : 'Enter an API key above to load KBs.'}
            </div>
          )}

          {kbList.map((kb, idx) => (
            <div
              key={kb.id}
              style={{
                ...toggleRowStyle,
                borderBottom: idx < kbList.length - 1 ? '1px solid #2e3f54' : 'none',
              }}
            >
              <div>
                <div style={{ color: '#e8edf3', fontSize: '13px', fontWeight: '500' }}>
                  {kb.name}
                </div>
                {kb.alwaysOn && (
                  <div style={{ color: '#556677', fontSize: '11px' }}>Always on</div>
                )}
              </div>
              <button
                role="switch"
                aria-checked={kb.alwaysOn ? true : (kbToggles[kb.id] ?? false)}
                disabled={kb.alwaysOn}
                onClick={() => !kb.alwaysOn && handleKbToggle(kb.id, !(kbToggles[kb.id] ?? false))}
                title={kb.alwaysOn ? 'Always enabled' : (kbToggles[kb.id] ? 'Disable' : 'Enable')}
                style={{
                  width: '36px',
                  height: '20px',
                  borderRadius: '10px',
                  border: 'none',
                  cursor: kb.alwaysOn ? 'default' : 'pointer',
                  background: (kb.alwaysOn || kbToggles[kb.id]) ? '#d4af37' : '#2e3f54',
                  position: 'relative',
                  flexShrink: 0,
                  opacity: kb.alwaysOn ? 0.7 : 1,
                  transition: 'background 0.2s',
                  padding: 0,
                }}
              >
                <span
                  style={{
                    display: 'block',
                    width: '14px',
                    height: '14px',
                    borderRadius: '50%',
                    background: '#fff',
                    position: 'absolute',
                    top: '3px',
                    left: (kb.alwaysOn || kbToggles[kb.id]) ? '19px' : '3px',
                    transition: 'left 0.2s',
                  }}
                />
              </button>
            </div>
          ))}
        </div>

        {/* Active Project */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Active Project</div>
          <p style={labelStyle}>
            Select a project to include its knowledge base in searches.
          </p>

          {projectsLoading ? (
            <div style={{ color: '#556677', fontSize: '12px' }}>Loading projects…</div>
          ) : (
            <select
              value={selectedProject}
              onChange={(e) => handleProjectChange(e.target.value)}
              style={{
                width: '100%',
                background: '#243447',
                border: '1px solid #2e3f54',
                borderRadius: '6px',
                color: '#e8edf3',
                fontFamily: 'Inter, sans-serif',
                fontSize: '13px',
                padding: '8px 10px',
                cursor: 'pointer',
                outline: 'none',
              }}
              onFocus={(e) => { e.target.style.borderColor = '#d4af37'; }}
              onBlur={(e) => { e.target.style.borderColor = '#2e3f54'; }}
            >
              <option value="">— None —</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          )}
        </div>

        {/* Model */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Model</div>
          <p style={labelStyle}>
            Sonnet is more capable; Haiku is faster and cheaper.
          </p>
          <ModelPicker model={model} onChange={handleModelChange} />
        </div>

        {/* Footer */}
        <div
          style={{
            color: '#445566',
            fontSize: '11px',
            lineHeight: 1.5,
            padding: '0 4px',
          }}
        >
          Settings are stored in OfficeRuntime.storage on this device.
        </div>
      </div>
    </div>
  );
};

export default SettingsPanel;
```

---

## Task 16: Create `src/taskpane/components/ShapePreview.tsx`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/components/ShapePreview.tsx`:

```typescript
import React from 'react';

interface ShapePreviewProps {
  pendingText: string;
  targetShapeName: string;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
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

## Task 17: Create `src/taskpane/components/ChatPanel.tsx`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/components/ChatPanel.tsx`:

This is a FfP-specific ChatPanel. It does NOT include any Excel features (chart, pivot, CF, sort/filter, watch mode, named ranges, FORGE, formula). It includes:
- `usePptContext` for slide context
- `SlideContextBar` inline component
- `ShapePreview` component for Apply to Shape
- keyword-based apply trigger
- Settings gear and model indicator
- Clear history button
- MessageList, ChatInput, SlashCommandPicker

```typescript
import React, { useState, useEffect, useRef } from 'react';
import { useChat } from '../hooks/useChat';
import { usePptContext } from '../hooks/usePptContext';
import { getSlideContext, formatSlideContext } from '../services/pptReader';
import { applyTextToShape, PptWriteError } from '../services/pptWriter';
import MessageList from './MessageList';
import ChatInput from './ChatInput';
import ErrorBanner from './ErrorBanner';
import ShapePreview from './ShapePreview';
import SlashCommandPicker from './SlashCommandPicker';

interface ChatPanelProps {
  apiKey: string;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
  onOpenSettings: () => void;
}

const ChatPanel: React.FC<ChatPanelProps> = ({
  apiKey,
  model,
  kbToggles,
  projectId,
  onOpenSettings,
}) => {
  const { slideContext, refresh: refreshSlideContext } = usePptContext();

  // Apply to Shape state
  const [pendingApplyText, setPendingApplyText] = useState<string | null>(null);
  const [applyLoading, setApplyLoading] = useState(false);
  const [applyError, setApplyError] = useState<string | null>(null);

  // Input state (lifted for slash commands)
  const [inputText, setInputText] = useState('');
  const chatInputAreaRef = useRef<HTMLDivElement>(null);

  const showSlashPicker = inputText.startsWith('/');
  const slashQuery = showSlashPicker ? inputText.slice(1) : '';

  const {
    messages,
    loading,
    error,
    send,
    clearError,
    setMessages,
  } = useChat(apiKey, model, kbToggles, projectId);

  const handleSend = async (text: string) => {
    let context: string | undefined;

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

  // Watch messages for Apply to Shape trigger
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
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

  const handleClearHistory = () => {
    setMessages([]);
  };

  const modelLabel = model === 'haiku' ? 'Haiku' : 'Sonnet';

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minWidth: '300px',
        background: '#1a2332',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 12px',
          borderBottom: '1px solid #2e3f54',
          background: '#0f1720',
          flexShrink: 0,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ color: '#d4af37', fontWeight: '700', fontSize: '14px' }}>
            🏰 FAIT
          </span>
          <span style={{ color: '#556677', fontSize: '11px' }}>for PowerPoint</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
          {/* Clear History */}
          <button
            onClick={handleClearHistory}
            title="Clear conversation history"
            aria-label="Clear conversation history"
            style={headerBtnStyle}
          >
            🗑
          </button>

          {/* Model indicator */}
          <button
            onClick={onOpenSettings}
            title={`Model: ${modelLabel} — click to change in Settings`}
            style={{
              ...headerBtnStyle,
              fontSize: '11px',
              color: '#8899aa',
              display: 'flex',
              alignItems: 'center',
              gap: '3px',
            }}
          >
            <span style={{ color: '#556677' }}>Model:</span>{' '}
            <span style={{ color: '#d4af37' }}>{modelLabel}</span>
          </button>

          {/* Settings gear */}
          <button
            onClick={onOpenSettings}
            title="Settings"
            aria-label="Open settings"
            style={headerBtnStyle}
          >
            ⚙
          </button>
        </div>
      </div>

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

      {/* Error banner */}
      {error && <ErrorBanner message={error} onDismiss={clearError} />}

      {/* Scrollable message area */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <MessageList
          messages={messages}
          loading={loading}
        />
      </div>

      {/* Shape preview (Apply to Shape) */}
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

      {/* Input area */}
      <div ref={chatInputAreaRef} style={{ position: 'relative', flexShrink: 0 }}>
        {showSlashPicker && (
          <SlashCommandPicker
            query={slashQuery}
            onSelect={(prompt, _name) => {
              setInputText(prompt);
            }}
            onClose={() => setInputText('')}
          />
        )}

        <ChatInput
          value={inputText}
          onChange={setInputText}
          onSend={(text) => {
            setInputText('');
            handleSend(text);
          }}
          disabled={loading}
          includeSelection={true}
          onToggleSelection={() => {}}
        />
      </div>

      <style>{`
        @keyframes fadeIn {
          from { opacity: 0; transform: translateY(4px); }
          to   { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};

const headerBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: '#8899aa',
  cursor: 'pointer',
  fontSize: '14px',
  padding: '2px 4px',
  borderRadius: '4px',
  lineHeight: 1,
};

export default ChatPanel;
```

---

## Task 18a: Create `src/taskpane/App.tsx`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/App.tsx`:

```typescript
import React, { useState, useEffect } from 'react';
import { loadSettings } from './services/settings';
import ChatPanel from './components/ChatPanel';
import SettingsPanel from './components/SettingsPanel';

const App: React.FC = () => {
  const [apiKey, setApiKey] = useState<string>('');
  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');
  const [kbToggles, setKbToggles] = useState<Record<string, boolean>>({ corp: true, team: false });
  const [projectId, setProjectId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  useEffect(() => {
    loadSettings().then((s) => {
      setApiKey(s.apiKey ?? '');
      setModel(s.model);
      setKbToggles(s.kbToggles);
      setProjectId(s.projectId);
      if (!s.apiKey) setShowSettings(true);
      setLoading(false);
    });
  }, []);

  const handleKeyChange = (key: string) => {
    setApiKey(key);
    setShowSettings(false);
  };

  if (loading) {
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100vh',
          background: '#1a2332',
        }}
      >
        <div style={{ color: '#d4af37', fontFamily: 'Inter, sans-serif', fontSize: '14px' }}>
          Loading FAIT for PowerPoint…
        </div>
      </div>
    );
  }

  if (showSettings) {
    return (
      <SettingsPanel
        onClose={() => setShowSettings(false)}
        apiKey={apiKey}
        onKeyChange={handleKeyChange}
      />
    );
  }

  return (
    <ChatPanel
      apiKey={apiKey}
      model={model}
      kbToggles={kbToggles}
      projectId={projectId}
      onOpenSettings={() => setShowSettings(true)}
    />
  );
};

export default App;
```

---

## Task 18b: Create `src/taskpane/index.tsx`

Create `~/projects/fip/fait-for-powerpoint/src/taskpane/index.tsx`:

```typescript
import { createRoot } from 'react-dom/client';
import App from './App';
import './styles/global.css';

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Office: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

Office.onReady(() => {
  const container = document.getElementById('root');
  if (!container) throw new Error('Root element not found');
  const root = createRoot(container);
  root.render(<App />);
});
```

---

## Task 18c: Create shared UI components (exact copies from FfE)

These 4 components are portable — copy verbatim from FfE. The only dependency to note is that MessageList's `onWriteTable` prop should be optional and unused (FfP Sprint 1 has no write-table feature).

### Create `src/taskpane/components/MessageBubble.tsx`:

Copy exactly from FfE but strip out ParsedTable / tableData / TableRenderer references since FfP Sprint 1 doesn't use structured table parsing:

```typescript
import React from 'react';
import type { Message } from '../hooks/useChat';

interface MessageBubbleProps {
  message: Message;
  streaming?: boolean;
}

function simpleMarkdown(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/`([^`]+)`/g, '<code style="background:#0f1720;padding:1px 4px;border-radius:3px;font-size:12px;">$1</code>')
    .replace(/\n/g, '<br />');
}

const MessageBubble: React.FC<MessageBubbleProps> = ({ message, streaming }) => {
  const isUser = message.role === 'user';
  const isStreaming = streaming ?? message.streaming ?? false;

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: isUser ? 'flex-end' : 'flex-start',
        padding: '4px 8px',
        animation: 'fadeIn 0.2s ease-out',
      }}
    >
      <span
        style={{
          fontSize: '10px',
          fontWeight: '600',
          color: isUser ? '#8899aa' : '#d4af37',
          marginBottom: '2px',
          textTransform: 'uppercase',
          letterSpacing: '0.5px',
        }}
      >
        {isUser ? 'You' : 'FAIT'}
      </span>

      <div
        style={{
          maxWidth: '90%',
          padding: '8px 12px',
          borderRadius: isUser ? '12px 12px 4px 12px' : '12px 12px 12px 4px',
          background: isUser ? '#243447' : '#1e3a5f',
          border: `1px solid ${isUser ? '#2e3f54' : '#2e5080'}`,
          color: '#e8edf3',
          fontSize: '13px',
          lineHeight: 1.6,
          wordBreak: 'break-word',
          position: 'relative',
        }}
      >
        <span dangerouslySetInnerHTML={{ __html: simpleMarkdown(message.content) }} />

        {isStreaming && (
          <span
            aria-hidden="true"
            style={{
              display: 'inline-block',
              width: '2px',
              height: '13px',
              background: '#d4af37',
              marginLeft: '2px',
              verticalAlign: 'text-bottom',
              animation: 'blink 1s step-end infinite',
            }}
          />
        )}
      </div>

      <style>{`
        @keyframes blink {
          0%, 100% { opacity: 1; }
          50% { opacity: 0; }
        }
        @keyframes fadeIn {
          from { opacity: 0; transform: translateY(4px); }
          to   { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};

export default MessageBubble;
```

### Create `src/taskpane/components/MessageList.tsx`:

```typescript
import React, { useEffect, useRef } from 'react';
import type { Message } from '../hooks/useChat';
import MessageBubble from './MessageBubble';
import LoadingDots from './LoadingDots';

interface MessageListProps {
  messages: Message[];
  loading: boolean;
}

const MessageList: React.FC<MessageListProps> = ({ messages, loading }) => {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, loading]);

  if (messages.length === 0 && !loading) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          color: '#556677',
          fontSize: '13px',
          gap: '8px',
          padding: '16px',
          textAlign: 'center',
        }}
      >
        <span style={{ fontSize: '32px' }}>🏰</span>
        <span>Select a shape and ask FAIT anything</span>
        <span style={{ fontSize: '11px' }}>e.g. "Summarize this slide" or "Write a bullet list for the selected shape"</span>
      </div>
    );
  }

  return (
    <div
      style={{
        flex: 1,
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: '4px',
        padding: '8px 0',
        background: '#0f1720',
      }}
    >
      {messages.map((msg, idx) => (
        <MessageBubble
          key={idx}
          message={msg}
        />
      ))}
      {loading && <LoadingDots />}
      <div ref={bottomRef} />
    </div>
  );
};

export default MessageList;
```

### Create `src/taskpane/components/LoadingDots.tsx`:

Exact copy from FfE:

```typescript
import React from 'react';

const dotStyle = (delay: string): React.CSSProperties => ({
  display: 'inline-block',
  width: '6px',
  height: '6px',
  borderRadius: '50%',
  backgroundColor: '#d4af37',
  margin: '0 2px',
  animation: 'bounce 1.4s infinite ease-in-out',
  animationDelay: delay,
});

const LoadingDots: React.FC = () => (
  <div style={{
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    padding: '8px 12px',
    color: '#8899aa',
    fontSize: '12px',
    fontStyle: 'italic',
  }}>
    <span>FAIT is thinking</span>
    <span style={dotStyle('0s')} />
    <span style={dotStyle('0.2s')} />
    <span style={dotStyle('0.4s')} />
  </div>
);

export default LoadingDots;
```

### Create `src/taskpane/components/ErrorBanner.tsx`:

Exact copy from FfE:

```typescript
import React from 'react';

interface ErrorBannerProps {
  message: string;
  onDismiss: () => void;
}

const ErrorBanner: React.FC<ErrorBannerProps> = ({ message, onDismiss }) => (
  <div
    role="alert"
    style={{
      display: 'flex',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      gap: '8px',
      padding: '8px 12px',
      background: '#2d1515',
      border: '1px solid #e74c3c',
      borderRadius: '6px',
      margin: '4px 8px',
      fontSize: '12px',
      color: '#e74c3c',
      lineHeight: 1.4,
      animation: 'fadeIn 0.2s ease-out',
    }}
  >
    <span>⚠ {message}</span>
    <button
      onClick={onDismiss}
      aria-label="Dismiss error"
      style={{
        background: 'none',
        border: 'none',
        color: '#e74c3c',
        cursor: 'pointer',
        fontSize: '14px',
        lineHeight: 1,
        flexShrink: 0,
        padding: '0 2px',
      }}
    >
      ×
    </button>
  </div>
);

export default ErrorBanner;
```

### Create `src/taskpane/components/SlashCommandPicker.tsx`:

Exact copy from FfE (keep all commands including report/formula — they'll be connected in later sprints):

```typescript
import React, { useEffect, useRef, useState } from 'react';

interface SlashCommand {
  name: string;
  description: string;
  prompt: string;
}

const COMMANDS: SlashCommand[] = [
  {
    name: 'summarize',
    description: 'Summarize the current slide content',
    prompt: 'Please summarize the content of this slide. Describe what it covers, key points, and any notable data or claims.',
  },
  {
    name: 'improve',
    description: 'Suggest improvements for the selected shape text',
    prompt: 'Please review the selected shape text and suggest improvements. Focus on clarity, conciseness, and impact.',
  },
  {
    name: 'bullets',
    description: 'Convert selected shape text to bullet points',
    prompt: 'Please convert the selected shape text into clear, concise bullet points. Apply to shape when ready.',
  },
  {
    name: 'expand',
    description: 'Expand the selected shape text with more detail',
    prompt: 'Please expand the selected shape text with more detail and supporting context. Apply to shape when ready.',
  },
];

interface SlashCommandPickerProps {
  query: string;
  onSelect: (prompt: string, name?: string) => void;
  onClose: () => void;
}

const SlashCommandPicker: React.FC<SlashCommandPickerProps> = ({ query, onSelect, onClose }) => {
  const filtered = COMMANDS.filter((c) => c.name.startsWith(query.toLowerCase()));
  const [activeIndex, setActiveIndex] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setActiveIndex(0);
  }, [query]);

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (filtered.length === 0) return;
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setActiveIndex((i) => (i + 1) % filtered.length);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setActiveIndex((i) => (i - 1 + filtered.length) % filtered.length);
      } else if (e.key === 'Enter') {
        e.preventDefault();
        onSelect(filtered[activeIndex].prompt, filtered[activeIndex].name);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [filtered, activeIndex, onSelect, onClose]);

  if (filtered.length === 0) return null;

  return (
    <div
      ref={listRef}
      role="listbox"
      aria-label="Slash commands"
      style={{
        position: 'absolute',
        bottom: '100%',
        left: 0,
        right: 0,
        background: '#0f1720',
        border: '1px solid #2e3f54',
        borderRadius: '8px',
        boxShadow: '0 -4px 16px rgba(0,0,0,0.4)',
        overflow: 'hidden',
        zIndex: 1000,
        marginBottom: '4px',
      }}
    >
      <div
        style={{
          padding: '6px 10px',
          borderBottom: '1px solid #2e3f54',
          fontSize: '10px',
          color: '#556677',
          letterSpacing: '0.08em',
          textTransform: 'uppercase',
          fontWeight: '600',
        }}
      >
        Commands
      </div>

      {filtered.map((cmd, idx) => (
        <div
          key={cmd.name}
          role="option"
          aria-selected={idx === activeIndex}
          onClick={() => onSelect(cmd.prompt, cmd.name)}
          onMouseEnter={() => setActiveIndex(idx)}
          style={{
            padding: '8px 12px',
            cursor: 'pointer',
            background: idx === activeIndex ? '#1a2e45' : 'transparent',
            borderBottom: idx < filtered.length - 1 ? '1px solid #1a2332' : 'none',
            display: 'flex',
            flexDirection: 'column',
            gap: '2px',
            transition: 'background 0.1s',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span
              style={{
                color: '#d4af37',
                fontWeight: '600',
                fontSize: '13px',
                fontFamily: 'monospace',
              }}
            >
              /{cmd.name}
            </span>
          </div>
          <span style={{ color: '#8899aa', fontSize: '11px', lineHeight: 1.3 }}>
            {cmd.description}
          </span>
        </div>
      ))}
    </div>
  );
};

export default SlashCommandPicker;
```

### Create `src/taskpane/components/ModelPicker.tsx`:

Exact copy from FfE:

```typescript
import React from 'react';

interface ModelPickerProps {
  model: 'haiku' | 'sonnet';
  onChange: (model: 'haiku' | 'sonnet') => void;
}

const ModelPicker: React.FC<ModelPickerProps> = ({ model, onChange }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
    <label
      htmlFor="model-picker"
      style={{ fontSize: '11px', color: '#8899aa', whiteSpace: 'nowrap' }}
    >
      Model:
    </label>
    <select
      id="model-picker"
      value={model}
      onChange={(e) => onChange(e.target.value as 'haiku' | 'sonnet')}
      style={{
        background: '#243447',
        border: '1px solid #2e3f54',
        borderRadius: '4px',
        color: '#e8edf3',
        fontSize: '12px',
        padding: '2px 6px',
        cursor: 'pointer',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      <option value="haiku">Haiku (fast)</option>
      <option value="sonnet">Sonnet (best)</option>
    </select>
  </div>
);

export default ModelPicker;
```

---

## Task 18d: Copy icons from FfE

Copy the icon files:
```bash
mkdir -p ~/projects/fip/fait-for-powerpoint/public/assets
cp ~/projects/fait-for-excel/public/assets/icon-16.png ~/projects/fip/fait-for-powerpoint/public/assets/
cp ~/projects/fait-for-excel/public/assets/icon-32.png ~/projects/fip/fait-for-powerpoint/public/assets/
cp ~/projects/fait-for-excel/public/assets/icon-80.png ~/projects/fip/fait-for-powerpoint/public/assets/
```

---

## Final Verification Checklist

Before finishing, verify ALL of these:

1. **manifest.xml** — `<Host Name="Presentation"/>` (NOT Workbook), `<Set Name="PowerPointApi" MinVersion="1.5"/>`, `<Host xsi:type="Presentation">` in VersionOverrides
2. **manifest.local.xml** — Same 3 checks plus all URLs use `localhost:3001`
3. **vite.config.ts** — `port: 3001`, `base: '/ppt-addin/'`, input is `src/taskpane/index.html`
4. **package.json** — `@microsoft/office-js` NOT present, only `@types/office-js` in devDependencies
5. **pptReader.ts** — `declare const PowerPoint: any;` at top, uses `PowerPoint.run()`
6. **pptWriter.ts** — `declare const PowerPoint: any;` at top, uses `PowerPoint.run()`
7. **GUID** — `b2c3d4e5-f6a7-8901-bcde-f12345678902` in BOTH manifests
8. **ChatPanel.tsx** — No Excel.run(), no FfE-specific imports (excelReader, excelWriter, etc.)
9. **useChat.ts** — No parseSuggestions import, lean Message interface

All files are in `~/projects/fip/fait-for-powerpoint/`. Do NOT touch `~/projects/fait-for-excel/`.
