# CC Brief: WI858 — FfE Entra Auth Refactor (Taskpane)

You are making changes to the FAIT for Excel taskpane at `~/projects/fait-for-excel/`.

## MANDATORY: Read the full spec first
```
cat ~/projects/fait-for-excel/FFE-ENTRA-AUTH-SPEC.md
```

## Context
This is a full Entra auth refactor. We're replacing the API key auth with MSAL.js via the Office Dialog API. The spec has exact code for all files. Follow it precisely.

## Files to CREATE (new files):

### 1. `src/taskpane/services/authService.ts`
Create this file with EXACTLY the code from the spec section "### `src/taskpane/services/authService.ts`".
Key points:
- MSAL config with CLIENT_ID = '887206bc-fac1-436a-a8ed-2150418d76c0' and TENANT_ID = 'd2bf3425-f8ab-451c-83bd-1e0ebd9508fe'
- SCOPE = 'api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access'
- DIALOG_URL_BASE uses `${window.location.origin}/excel-addin/auth-dialog.html` (hyphenated!)
- Exports: `getStoredToken`, `storeToken`, `getStoredUser`, `storeUser`, `clearAuth`, `getApiKey`, `getAuthHeader`, `signIn`, `FaitUser` interface
- `getAuthHeader()` returns `{ 'Authorization': 'Bearer token' }` for Entra, `{ 'x-api-key': key }` for AppKey fallback

### 2. `src/taskpane/auth/` directory — create both files:

#### `src/taskpane/auth/auth-dialog.html` (HYPHENATED filename — critical!)
Use EXACTLY the code from spec section "### `src/taskpane/auth/authDialog.html`".
The script tag must reference `./authDialog.js` (the compiled TS output).

#### `src/taskpane/auth/authDialog.tsx`
Use EXACTLY the code from spec section "### `src/taskpane/auth/authDialog.tsx`".
Key points:
- Reads clientId, tenantId, scope from query string params
- redirectUri = `${window.location.origin}/excel-addin/auth-dialog.html`
- cacheLocation: 'sessionStorage' (NOT localStorage — critical for security)
- navigateToLoginRequestUrl: false
- On redirect result: posts JSON {type:'auth_success', accessToken, expiresIn, oid, email, name} via `Office.context.ui.messageParent()`
- On no result: calls `msalInstance.loginRedirect()`
- Wraps in `Office.onReady(() => run())`

### 3. `src/taskpane/components/AuthGate.tsx`
Use EXACTLY the code from spec section "### `components/AuthGate.tsx`".
Key points:
- On mount: checks `getStoredToken()` + `getStoredUser()`
- If no user: shows sign-in screen with "Sign in with Microsoft" button
- Calls `signIn()` from authService
- On success: sets user state
- Renders `<App user={user} />` when authenticated

## Files to MODIFY:

### 4. `src/taskpane/services/storage.ts`
Add these exports (keep existing code, just add):
```typescript
export const AUTH_TOKEN_KEY  = 'fait_entra_token';
export const AUTH_EXPIRY_KEY = 'fait_entra_expiry';
export const AUTH_USER_KEY   = 'fait_entra_user';
// Rename the existing KEY constant to APIKEY_KEY for clarity
export const APIKEY_KEY = 'fait_api_key';
```
Keep all existing functions (getApiKey, setApiKey, clearApiKey). The KEY constant can be renamed to APIKEY_KEY or kept as-is — just add the new exported constants.

### 5. `src/taskpane/services/settings.ts`
Add `authMode: 'entra' | 'appkey'` to the `FaitSettings` interface and `loadSettings()` return:
```typescript
export interface FaitSettings {
  apiKey: string | null;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
  authMode: 'entra' | 'appkey';  // NEW
}
```
In `loadSettings()`, detect authMode: if a stored Entra token exists (`fait_entra_token`), set `authMode: 'entra'`, otherwise `authMode: 'appkey'`.

### 6. `src/taskpane/services/faitApi.ts` — CRITICAL CHANGE
Replace `apiKey: string` parameter with `authHeader: Record<string, string>` in ALL exported functions.
Replace `'x-api-key': apiKey` headers with `...authHeader` spread.

Current functions that need updating:
- `sendChat(message, apiKey, model, signal, kbTypes, projectId)` → `sendChat(message, authHeader, model, signal, kbTypes, projectId)`
- `sendChatStreaming(message, apiKey, onChunk, model, signal, kbTypes, projectId)` → `sendChatStreaming(message, authHeader, onChunk, model, signal, kbTypes, projectId)`
- `searchKb(query, apiKey, projectId, kbTypes)` → `searchKb(query, authHeader, projectId, kbTypes)`
- `fetchKbList(apiKey)` → `fetchKbList(authHeader)`
- `fetchProjectList(apiKey)` → `fetchProjectList(authHeader)`

In headers, replace:
```typescript
// Before
{ 'Content-Type': 'application/json', 'x-api-key': apiKey }
// After
{ 'Content-Type': 'application/json', ...authHeader }
```

DO NOT change any other logic. Just replace the parameter name and header construction.

### 7. `src/taskpane/hooks/useChat.ts`
Replace `apiKey: string` parameter with `authHeader: Record<string, string>` in the `useChat` function signature.
Update all calls to `sendChatStreaming` and `sendChat` to pass `authHeader` instead of `apiKey`.

```typescript
// Before
export function useChat(apiKey: string, model, kbToggles, projectId, initialMessages)

// After  
export function useChat(authHeader: Record<string, string>, model, kbToggles, projectId, initialMessages)
```

### 8. `src/taskpane/components/ChatPanel.tsx`
- Change `apiKey: string` prop to `authHeader: Record<string, string>` in `ChatPanelProps`
- Import `getAuthHeader` from `'../services/authService'`
- Where `useChat(apiKey, ...)` is called, change to `useChat(authHeader, ...)`
- Where `searchKb(query, apiKey, ...)` is called directly, change to:
  ```typescript
  const authHeader = await getAuthHeader();
  searchKb(query, authHeader, ...)
  ```
- Where `fetchKbList(apiKey)` and `fetchProjectList(apiKey)` are called, use:
  ```typescript
  const authHeader = await getAuthHeader();
  fetchKbList(authHeader)
  ```

### 9. `src/taskpane/components/SettingsPanel.tsx`
Replace the API key section with Entra sign-in + sign-out UX. Keep AppKey as "Advanced" fallback.
- Remove the API key test logic that calls `sendChat(message, apiKey, ...)`
- Add "Signed in as: {user.name}" display when Entra token exists
- Add "Sign out" button that calls `clearAuth()` then `window.location.reload()`
- Keep `<input>` for AppKey under a collapsible "Advanced" section
- Remove `apiKey` and `onKeyChange` from props — these are no longer primary UX
- Add optional `user?: FaitUser` prop for displaying user info
- The SettingsPanel no longer needs to fetch KB list/projects on apiKey change; it can skip that or use getAuthHeader() internally

### 10. `src/taskpane/index.tsx`
Mount `<AuthGate>` instead of `<App>`:
```typescript
import { createRoot } from 'react-dom/client';
import AuthGate from './components/AuthGate';
import './styles/global.css';

declare const Office: any;

Office.onReady(() => {
  const container = document.getElementById('root');
  if (!container) throw new Error('Root element not found');
  const root = createRoot(container);
  root.render(<AuthGate />);
});
```

### 11. `src/taskpane/App.tsx`
- Remove `apiKey` state, `handleKeyChange`, and the "if no API key show settings" logic
- Add `user: FaitUser` prop (import FaitUser from authService)
- Remove `[apiKey, setApiKey]` state — auth is handled by AuthGate
- Pass `authHeader` via `getAuthHeader()` async call, or pass `user` prop down to ChatPanel
- Keep model, kbToggles, projectId, showSettings logic intact
- ChatPanel now receives `authHeader` instead of `apiKey`; compute it async in useEffect or pass user's identity

Here's the minimal App.tsx pattern to follow:
```tsx
import React, { useState, useEffect } from 'react';
import { loadSettings } from './services/settings';
import { getAuthHeader } from './services/authService';
import { FaitUser } from './services/authService';
import ChatPanel from './components/ChatPanel';
import SettingsPanel from './components/SettingsPanel';

interface AppProps {
  user: FaitUser;
}

const App: React.FC<AppProps> = ({ user }) => {
  const [authHeader, setAuthHeader] = useState<Record<string, string>>({});
  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');
  const [kbToggles, setKbToggles] = useState<Record<string, boolean>>({ corp: true, team: false });
  const [projectId, setProjectId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  useEffect(() => {
    Promise.all([loadSettings(), getAuthHeader()]).then(([s, hdr]) => {
      setAuthHeader(hdr);
      setModel(s.model);
      setKbToggles(s.kbToggles);
      setProjectId(s.projectId);
      setLoading(false);
    });
  }, []);

  if (loading) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center',
                    height: '100vh', background: '#1a2332' }}>
        <div style={{ color: '#d4af37', fontFamily: 'Inter, sans-serif', fontSize: '14px' }}>
          Loading FAIT…
        </div>
      </div>
    );
  }

  if (showSettings) {
    return (
      <SettingsPanel
        onClose={() => setShowSettings(false)}
        user={user}
      />
    );
  }

  return (
    <ChatPanel
      authHeader={authHeader}
      model={model}
      kbToggles={kbToggles}
      projectId={projectId}
      onOpenSettings={() => setShowSettings(true)}
    />
  );
};

export default App;
```

### 12. `public/manifest.xml`
Add `login.microsoftonline.com` to `<AppDomains>`:
```xml
<AppDomains>
  <AppDomain>https://fait.dev.fortressam.ai</AppDomain>
  <AppDomain>https://login.microsoftonline.com</AppDomain>
</AppDomains>
```

### 13. `manifest.local.xml`
Same change — add `login.microsoftonline.com`:
```xml
<AppDomains>
  <AppDomain>https://fait.dev.fortressam.ai</AppDomain>
  <AppDomain>https://login.microsoftonline.com</AppDomain>
  <AppDomain>https://localhost:3000</AppDomain>
</AppDomains>
```

### 14. `vite.config.ts`
Add the auth-dialog.html entry point (HYPHENATED key name → outputs to auth-dialog.html):
```typescript
rollupOptions: {
  input: {
    taskpane:    'src/taskpane/index.html',
    commands:    'public/commands.html',
    'auth-dialog': 'src/taskpane/auth/auth-dialog.html',  // NEW — hyphen key = hyphen filename
  },
},
```

### 15. Install package
```bash
cd ~/projects/fait-for-excel
npm install @azure/msal-browser
```

## Gate Checks (run after all changes)
```bash
# TypeScript clean
cd ~/projects/fait-for-excel && npx tsc --noEmit 2>&1 | head -20

# Vite entry point
grep -n "auth-dialog\|authDialog" ~/projects/fait-for-excel/vite.config.ts

# Both manifests
grep -n "login.microsoftonline" ~/projects/fait-for-excel/public/manifest.xml ~/projects/fait-for-excel/manifest.local.xml

# apiKey no longer in faitApi.ts function params
grep -n "apiKey.*string" ~/projects/fait-for-excel/src/taskpane/services/faitApi.ts

# getAuthHeader used
grep -n "getAuthHeader\|authHeader" ~/projects/fait-for-excel/src/taskpane/services/faitApi.ts | head -5

# AuthGate in index.tsx
grep -n "AuthGate" ~/projects/fait-for-excel/src/taskpane/index.tsx
```

## Critical Constraints
1. The auth-dialog.html filename MUST be hyphenated: `auth-dialog.html` — not `authDialog.html`
2. The vite.config.ts rollupOptions input key must be `'auth-dialog'` (hyphenated) to produce `auth-dialog.html` output
3. authDialog.tsx uses `cacheLocation: 'sessionStorage'` NOT localStorage
4. Every faitApi.ts function MUST use `authHeader: Record<string, string>` not `apiKey: string`
5. DO NOT add any npm packages other than `@azure/msal-browser`
6. SettingsPanel: keep the AppKey input field (under Advanced), just don't make it mandatory
