# FfE Entra Auth Refactor — WI#831

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Repo:** `~/projects/fait-for-excel/` (taskpane) + `~/projects/fip/fait/` (backend)

---

## Pre-Read: What Was Confirmed

**Current state (taskpane):**
- `storage.ts` — reads/writes `fait_api_key` in `OfficeRuntime.storage` (falls back to `localStorage` in dev)
- `settings.ts` — `loadSettings()` returns `{ apiKey, model, kbToggles, projectId }`. All API calls pass `apiKey` as `x-api-key` header.
- `faitApi.ts` — every function (`sendChat`, `sendChatStreaming`, `searchKb`, etc.) takes `apiKey: string` as a parameter and sets `'x-api-key': apiKey`.
- `App.tsx` — on load, reads `apiKey` from storage; if empty, shows `SettingsPanel` for manual entry. The API key is the single auth gate.

**Current state (backend):**
- `AppKeyAuthHandler.cs` — validates `x-api-key` header against `AppKeys:Haven` and `AppKeys:ExcelAddin`. On success, emits **hardcoded Fred White claims** (userId `08de7605-3f7d-427d-858a-637777b41018`). Every FfE user is currently Fred White as far as the backend is concerned.
- `HavenChatController.cs` — decorated with `[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]`. Uses `User.FindFirstValue(ClaimTypes.NameIdentifier)` to scope personal KB queries.
- Personal KB retrieval is per-user via that userId. Since all FfE users are currently mapped to the same hardcoded ID, personal KB is effectively shared.

**Critical implication:** Adding Rob changes nothing unless each person gets their own token — but that's operationally messy (token management, rotation, revocation). The real fix is Entra auth so each person has their own identity automatically.

---

## Architecture Decision: Auth Strategy

**Option A — Per-user AppKeys:** Issue a unique `AppKeys:ExcelAddin_Rob`, `AppKeys:ExcelAddin_Len`, etc. Each user enters their personal token in SettingsPanel. Backend resolves user from token.

**Option B — Entra JWT (MSAL.js + Office Dialog API):** MSAL authenticates the user against Entra. Token is stored in `OfficeRuntime.storage`. Backend validates the Entra JWT.

**Decision: Option B.** Rationale:
- Rob, Len, Leslie all have Entra identities from the FIP platform login. No new credentials to manage.
- Entra tokens auto-expire and silently refresh — no manual key rotation.
- The `oid` claim in the Entra JWT is a stable per-user identifier that maps directly to FAIT's `AppUser.EntraOid` — personal KB scoping becomes real.
- Option A would require an admin to issue/track/rotate keys for every new FfE user.

**Keep AppKey as fallback:** The `AppKeyOnly` policy and `AppKeys:ExcelAddin` env var are retained for CI/CD, automated testing, and emergency access. The backend accepts EITHER auth scheme on the haven endpoints. FfE itself uses Entra only; the AppKey path is for non-browser callers.

---

## Office Add-in Auth Constraint: Why `displayDialogAsync`

Office add-in taskpanes run in a sandboxed iframe (Office Online) or a sandboxed WebView (Office Desktop). Both environments block:
- `window.open()` popups (blocked by iframe sandbox policy)
- `window.location.href =` redirects (would navigate away from the taskpane)
- Cross-origin postMessage from a child window back to the taskpane iframe

MSAL.js's default `loginPopup()` and `loginRedirect()` flows are both blocked.

**The correct pattern:** Use `Office.context.ui.displayDialogAsync()` to open a separate top-level dialog window (not an iframe — it's a full OS-level window). The dialog performs the MSAL auth flow. On success, the dialog uses `Office.context.ui.messageParent()` to post the token back to the taskpane. The taskpane listens via `addEventHandler(Office.EventType.DialogMessageReceived, ...)`.

This is an officially documented Microsoft pattern. MSAL.js supports it via `PublicClientApplication` with a custom navigation client that intercepts redirects and sends them to `displayDialogAsync` instead.

---

## Component Map

### New/Modified Files (Taskpane)

| File | New/Modified | Purpose |
|------|-------------|---------|
| `src/taskpane/services/authService.ts` | **New** | MSAL init, token acquisition (silent + dialog fallback), token storage in OfficeRuntime.storage |
| `src/taskpane/auth/authDialog.html` | **New** | Standalone HTML page loaded inside the Office dialog (not the taskpane) — runs MSAL redirect flow |
| `src/taskpane/auth/authDialog.tsx` | **New** | React entry for `authDialog.html` — calls `msalInstance.handleRedirectPromise()`, sends token to parent |
| `src/taskpane/services/storage.ts` | **Modified** | Rename `KEY` to `APIKEY_KEY`; add `AUTH_TOKEN_KEY`, `AUTH_USER_KEY` storage helpers |
| `src/taskpane/services/settings.ts` | **Modified** | `loadSettings()` returns `authMode: 'entra' | 'appkey'`; load user identity alongside settings |
| `src/taskpane/services/faitApi.ts` | **Modified** | Replace `apiKey: string` param with `getAuthHeader(): Promise<Record<string,string>>`; all functions call `getAuthHeader()` instead of accepting a key |
| `src/taskpane/components/SettingsPanel.tsx` | **Modified** | Replace API key entry with Entra sign-in button; keep AppKey field as "Advanced" fallback |
| `src/taskpane/components/AuthGate.tsx` | **New** | Wraps App — shows sign-in screen if no token; shows `<App>` when authenticated |
| `src/taskpane/index.tsx` | **Modified** | Mount `<AuthGate>` not `<App>` |
| `src/taskpane/App.tsx` | **Modified** | Remove `apiKey` state + `SettingsPanel` show-on-empty logic; receive `userId` prop |
| `public/manifest.xml` + `manifest.local.xml` | **Modified** | Register `authDialog.html` as an allowed URL in `<AppDomains>` |
| `vite.config.ts` | **Modified** | Add `authDialog.html` as a second Vite entry point |
| `package.json` | **Modified** | Add `@azure/msal-browser` |

### Modified Files (FAIT Backend)

| File | Change |
|------|--------|
| `Program.cs` | Add Entra JWT bearer scheme; update `HavenChatController` auth policy to accept either scheme |
| `Auth/AppKeyAuthHandler.cs` | Remove hardcoded Fred White claims; look up user from DB by `AppKeys:ExcelAddin` token; emit real user claims |
| `Controllers/HavenChatController.cs` | Update auth policy attribute to accept both schemes |

---

## Taskpane Implementation

### `src/taskpane/services/authService.ts`

```typescript
import * as msal from '@azure/msal-browser';

// FIP Entra app registration — same clientId as FAIT/FIRM
const CLIENT_ID  = '887206bc-fac1-436a-a8ed-2150418d76c0';
const TENANT_ID  = 'd2bf3425-f8ab-451c-83bd-1e0ebd9508fe';
const AUTHORITY  = `https://login.microsoftonline.com/${TENANT_ID}`;
// The FAIT backend validates tokens issued for this scope
// The scope must be registered on the FIP app registration as an exposed API scope
const SCOPE      = 'api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access';

// The dialog page URL — must be served from the same origin as the add-in
// In production: https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html
const DIALOG_URL_BASE = `${window.location.origin}/excel-addin/auth-dialog.html`;

const AUTH_TOKEN_KEY   = 'fait_entra_token';
const AUTH_EXPIRY_KEY  = 'fait_entra_expiry';
const AUTH_USER_KEY    = 'fait_entra_user';   // JSON: { userId, email, name, oid }
const APIKEY_KEY       = 'fait_api_key';

function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? {
    getItem: (k: string) => Promise.resolve(localStorage.getItem(k)),
    setItem: (k: string, v: string) => Promise.resolve(void localStorage.setItem(k, v)),
    removeItem: (k: string) => Promise.resolve(void localStorage.removeItem(k)),
  };
}

export interface FaitUser {
  userId: string;   // FAIT AppUser GUID (resolved from backend after first auth)
  email:  string;
  name:   string;
  oid:    string;   // Entra Object ID
}

// ── Silent token refresh ──────────────────────────────────────────────────────

/** Get a valid token from storage. Returns null if missing or expired. */
export async function getStoredToken(): Promise<string | null> {
  const storage = getStorage();
  const [token, expiry] = await Promise.all([
    storage.getItem(AUTH_TOKEN_KEY).catch(() => null),
    storage.getItem(AUTH_EXPIRY_KEY).catch(() => null),
  ]);
  if (!token || !expiry) return null;
  // Treat as expired 5 minutes before actual expiry (buffer for clock skew)
  if (Date.now() > parseInt(expiry, 10) - 5 * 60 * 1000) return null;
  return token;
}

/** Store a new token and its expiry. */
export async function storeToken(token: string, expiresInSeconds: number): Promise<void> {
  const storage = getStorage();
  const expiry = Date.now() + expiresInSeconds * 1000;
  await Promise.all([
    storage.setItem(AUTH_TOKEN_KEY, token),
    storage.setItem(AUTH_EXPIRY_KEY, String(expiry)),
  ]);
}

export async function getStoredUser(): Promise<FaitUser | null> {
  const storage = getStorage();
  const raw = await storage.getItem(AUTH_USER_KEY).catch(() => null);
  if (!raw) return null;
  try { return JSON.parse(raw) as FaitUser; }
  catch { return null; }
}

export async function storeUser(user: FaitUser): Promise<void> {
  const storage = getStorage();
  await storage.setItem(AUTH_USER_KEY, JSON.stringify(user));
}

export async function clearAuth(): Promise<void> {
  const storage = getStorage();
  await Promise.all([
    storage.removeItem(AUTH_TOKEN_KEY),
    storage.removeItem(AUTH_EXPIRY_KEY),
    storage.removeItem(AUTH_USER_KEY),
  ]);
}

// ── AppKey fallback ───────────────────────────────────────────────────────────

export async function getApiKey(): Promise<string | null> {
  return getStorage().getItem(APIKEY_KEY).catch(() => null);
}

// ── Auth header for faitApi.ts ────────────────────────────────────────────────

/**
 * Returns the correct auth header for FAIT API calls.
 * Priority: Entra token > AppKey > empty (will get 401).
 * Callers don't need to know which mode is active.
 */
export async function getAuthHeader(): Promise<Record<string, string>> {
  const token = await getStoredToken();
  if (token) return { 'Authorization': `Bearer ${token}` };

  const apiKey = await getApiKey();
  if (apiKey) return { 'x-api-key': apiKey };

  return {};
}

// ── Interactive sign-in via Office Dialog API ─────────────────────────────────

export interface SignInResult {
  success: boolean;
  user?: FaitUser;
  error?: string;
}

/**
 * Launch the Entra sign-in flow via Office.context.ui.displayDialogAsync.
 * Opens authDialog.html in a top-level dialog window (not an iframe).
 * The dialog runs the MSAL redirect flow and posts the result back via
 * Office.context.ui.messageParent().
 */
export function signIn(): Promise<SignInResult> {
  return new Promise((resolve) => {
    const dialogUrl = `${DIALOG_URL_BASE}?clientId=${CLIENT_ID}&tenantId=${TENANT_ID}&scope=${encodeURIComponent(SCOPE)}`;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const Office = (window as any).Office;
    Office.context.ui.displayDialogAsync(
      dialogUrl,
      { height: 60, width: 30, promptBeforeOpen: false },
      (asyncResult: any) => {
        if (asyncResult.status === Office.AsyncResultStatus.Failed) {
          resolve({ success: false, error: `Dialog failed to open: ${asyncResult.error.message}` });
          return;
        }

        const dialog = asyncResult.value;

        dialog.addEventHandler(
          Office.EventType.DialogMessageReceived,
          async (args: any) => {
            dialog.close();
            try {
              const msg = JSON.parse(args.message) as DialogMessage;
              if (msg.type === 'auth_success') {
                await storeToken(msg.accessToken, msg.expiresIn);
                // Resolve user identity from FAIT backend
                const user = await resolveUserIdentity(msg.accessToken, msg.oid, msg.email, msg.name);
                await storeUser(user);
                resolve({ success: true, user });
              } else {
                resolve({ success: false, error: msg.error ?? 'Sign-in cancelled' });
              }
            } catch (e: any) {
              resolve({ success: false, error: e.message });
            }
          }
        );

        dialog.addEventHandler(
          Office.EventType.DialogEventReceived,
          (args: any) => {
            if (args.error === 12006) {
              // User closed the dialog manually
              dialog.close();
              resolve({ success: false, error: 'Sign-in cancelled' });
            }
          }
        );
      }
    );
  });
}

interface DialogMessage {
  type: 'auth_success' | 'auth_error';
  accessToken: string;
  expiresIn: number;
  oid: string;
  email: string;
  name: string;
  error?: string;
}

/**
 * After successful sign-in, call FAIT backend to resolve the FAIT userId
 * (may create a new user on first login — mirrors FIP portal behaviour).
 * Returns a FaitUser with the FAIT-internal userId populated.
 */
async function resolveUserIdentity(accessToken: string, oid: string, email: string, name: string): Promise<FaitUser> {
  try {
    const resp = await fetch('https://fait.dev.fortressam.ai/api/excel/whoami', {
      method: 'GET',
      headers: { 'Authorization': `Bearer ${accessToken}` },
    });
    if (resp.ok) {
      const body = await resp.json() as { userId: string; email: string; name: string };
      return { userId: body.userId, email: body.email, name: body.name, oid };
    }
  } catch { /* Non-fatal — use Entra claims as fallback */ }
  // Fallback: use oid as userId (backend will reconcile on next API call)
  return { userId: oid, email, name, oid };
}
```

### `src/taskpane/auth/authDialog.html`

Standalone page loaded inside the Office dialog. Must be served from the same origin as the add-in (`https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html`).

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8" />
  <title>FAIT Sign In</title>
  <style>
    body { font-family: Inter, sans-serif; background: #1a2332; color: #d4af37;
           display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }
    p { font-size: 14px; text-align: center; }
  </style>
</head>
<body>
  <p id="status">Signing in to FAIT…</p>
  <script type="module" src="./authDialog.js"></script>
</body>
</html>
```

### `src/taskpane/auth/authDialog.tsx`

This is the entry point that Vite compiles to `authDialog.js`. It runs inside the dialog (not the taskpane). It must NOT import anything from the taskpane — no `OfficeRuntime.storage` access here.

```typescript
// src/taskpane/auth/authDialog.tsx
// Runs inside the Office dialog window (not the taskpane).
// Uses MSAL to perform the auth code redirect, then posts result back to taskpane.

import * as msal from '@azure/msal-browser';

// Read params passed in query string from signIn()
const params = new URLSearchParams(window.location.search);
const clientId  = params.get('clientId')  ?? '';
const tenantId  = params.get('tenantId')  ?? '';
const scope     = params.get('scope')     ?? '';
const authority = `https://login.microsoftonline.com/${tenantId}`;

// REDIRECT URI must point back to THIS page — the dialog completes the flow here
const redirectUri = `${window.location.origin}/excel-addin/auth-dialog.html`;

const msalConfig: msal.Configuration = {
  auth: {
    clientId,
    authority,
    redirectUri,
    navigateToLoginRequestUrl: false,  // Critical: don't navigate away from the dialog
  },
  cache: {
    cacheLocation: 'sessionStorage',   // sessionStorage only — dialog has its own session
    storeAuthStateInCookie: false,
  },
};

const msalInstance = new msal.PublicClientApplication(msalConfig);

async function run() {
  const statusEl = document.getElementById('status');

  try {
    // Step 1: Check if we're returning from a redirect (hash contains code/token)
    const result = await msalInstance.handleRedirectPromise();

    if (result) {
      // We got a token — extract claims and post back to taskpane
      const oid   = (result.idTokenClaims as any)?.oid ?? result.uniqueId;
      const email = result.account?.username ?? '';
      const name  = result.account?.name     ?? email;

      if (statusEl) statusEl.textContent = 'Signed in! Closing…';

      // Post result back to taskpane via Office messageParent
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Office.context.ui.messageParent(JSON.stringify({
        type:        'auth_success',
        accessToken: result.accessToken,
        expiresIn:   Math.floor(((result.expiresOn?.getTime() ?? Date.now() + 3600_000) - Date.now()) / 1000),
        oid,
        email,
        name,
      }));
    } else {
      // Step 2: No token yet — initiate login redirect
      if (statusEl) statusEl.textContent = 'Redirecting to Microsoft sign-in…';
      await msalInstance.loginRedirect({
        scopes: [scope, 'openid', 'profile', 'email'],
        prompt: 'select_account',
      });
      // loginRedirect() navigates away — rest of this function doesn't run
    }
  } catch (error: any) {
    if (statusEl) statusEl.textContent = 'Sign-in failed.';
    // Post error back
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Office?.context?.ui?.messageParent(JSON.stringify({
      type:  'auth_error',
      error: error.message ?? 'Unknown error',
    }));
  }
}

// Wait for Office.js to be ready in the dialog context
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(window as any).Office.onReady(() => run());
```

### `vite.config.ts` — Second Entry Point

```typescript
export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        taskpane:   'src/taskpane/index.html',
        authDialog: 'src/taskpane/auth/authDialog.html',  // <-- new
      },
    },
  },
  // ...rest unchanged
});
```

### `components/AuthGate.tsx` (new)

```tsx
// Wraps App. Shows sign-in UI if not authenticated. Shows App once signed in.

import React, { useState, useEffect } from 'react';
import { getStoredToken, getStoredUser, signIn, FaitUser } from '../services/authService';
import App from '../App';

const AuthGate: React.FC = () => {
  const [checking, setChecking]   = useState(true);
  const [user, setUser]           = useState<FaitUser | null>(null);
  const [signingIn, setSigningIn] = useState(false);
  const [error, setError]         = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      const token = await getStoredToken();
      if (token) {
        const storedUser = await getStoredUser();
        setUser(storedUser);
      }
      setChecking(false);
    })();
  }, []);

  const handleSignIn = async () => {
    setSigningIn(true);
    setError(null);
    const result = await signIn();
    setSigningIn(false);
    if (result.success && result.user) {
      setUser(result.user);
    } else {
      setError(result.error ?? 'Sign-in failed');
    }
  };

  if (checking) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center',
                    height: '100vh', background: '#1a2332' }}>
        <span style={{ color: '#d4af37', fontFamily: 'Inter, sans-serif', fontSize: '14px' }}>
          Loading FAIT…
        </span>
      </div>
    );
  }

  if (!user) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center',
                    justifyContent: 'center', height: '100vh', background: '#1a2332',
                    fontFamily: 'Inter, sans-serif', padding: '24px', gap: '16px' }}>
        <div style={{ color: '#d4af37', fontSize: '20px', fontWeight: 600 }}>FAIT</div>
        <div style={{ color: 'rgba(248,250,252,0.7)', fontSize: '13px', textAlign: 'center' }}>
          Sign in with your Fortress AM account to continue.
        </div>
        {error && (
          <div style={{ color: '#f87171', fontSize: '12px', textAlign: 'center' }}>{error}</div>
        )}
        <button
          onClick={handleSignIn}
          disabled={signingIn}
          style={{ background: '#d4af37', color: '#1a2332', border: 'none', borderRadius: '6px',
                   padding: '10px 24px', fontWeight: 600, fontSize: '14px', cursor: 'pointer',
                   opacity: signingIn ? 0.7 : 1 }}>
          {signingIn ? 'Opening sign-in…' : 'Sign in with Microsoft'}
        </button>
      </div>
    );
  }

  return <App user={user} />;
};

export default AuthGate;
```

### Updated `faitApi.ts`

Replace every `apiKey: string` parameter with `authHeader: Record<string, string>`. Callers get the header via `getAuthHeader()` from `authService.ts`.

```typescript
// Before:
export async function sendChatStreaming(
  message: string,
  apiKey: string,           // <-- remove
  onChunk: (text: string) => void,
  ...
): Promise<void> {
  const resp = await fetch(url, {
    headers: { 'x-api-key': apiKey, ... },
  });

// After:
export async function sendChatStreaming(
  message: string,
  authHeader: Record<string, string>,  // <-- new
  onChunk: (text: string) => void,
  ...
): Promise<void> {
  const resp = await fetch(url, {
    headers: { ...authHeader, 'Content-Type': 'application/json', ... },
  });
```

All callers (`useChat.ts`, `ChatPanel.tsx`, etc.) must be updated to call `await getAuthHeader()` and pass the result instead of `apiKey`.

**Constraints for CC:**
- Replace `apiKey: string` in ALL exported functions in `faitApi.ts` — `sendChat`, `sendChatStreaming`, `searchKb`, `sendChatWithKb`, `streamChatWithKb` (and any others).
- Update every call site that currently passes `apiKey` — search for `faitApi.` and `sendChat(` and `searchKb(` across all files.
- Do NOT remove the `'x-api-key'` header path — `getAuthHeader()` returns `{ 'x-api-key': key }` when AppKey is active. The header key changes automatically.

---

## Manifest Changes

Both `manifest.xml` and `manifest.local.xml` must be updated together.

**Add `<AppDomains>` entry for the auth dialog redirect URI:**

```xml
<AppDomains>
  <AppDomain>https://login.microsoftonline.com</AppDomain>
</AppDomains>
```

`login.microsoftonline.com` must be whitelisted so the dialog's MSAL redirect to `login.microsoftonline.com/...` is allowed. Without this, Office blocks the navigation inside the dialog.

**No manifest version bump required.** `AppDomains` is a baseline feature.

---

## Backend Changes

### 1. Entra JWT Validation in `Program.cs`

Add JWT Bearer authentication scheme alongside the existing `AppKeyAuth` scheme:

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options => { /* unchanged */ })
// Existing AppKey scheme (unchanged)
.AddScheme<AppKeyAuthOptions, AppKeyAuthHandler>("AppKeyAuth", options =>
{
    options.ApiKey  = builder.Configuration["AppKeys:Haven"];
    options.ApiKeys = new List<string> { builder.Configuration["AppKeys:ExcelAddin"] ?? "" };
})
// NEW: Entra JWT Bearer for FfE
.AddJwtBearer("EntraBearer", options =>
{
    var tenantId = builder.Configuration["Azure:TenantId"]
                   ?? "d2bf3425-f8ab-451c-83bd-1e0ebd9508fe";
    var clientId = builder.Configuration["Azure:ClientId"]
                   ?? "887206bc-fac1-436a-a8ed-2150418d76c0";
    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    options.Audience  = $"api://{clientId}";  // Must match the scope's audience
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer           = true,
        ValidIssuer              = $"https://login.microsoftonline.com/{tenantId}/v2.0",
        ValidateAudience         = true,
        ValidateLifetime         = true,
        ClockSkew                = TimeSpan.FromMinutes(5),
    };
});
```

Update the `AppKeyOnly` policy to also accept `EntraBearer`:

```csharp
options.AddPolicy("AppKeyOnly", policy =>
    policy.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")  // <-- add "EntraBearer"
          .RequireAuthenticatedUser());
```

Rename the policy to `"ExcelAddinAccess"` for clarity (update `HavenChatController` attribute to match):

```csharp
options.AddPolicy("ExcelAddinAccess", policy =>
    policy.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")
          .RequireAuthenticatedUser());
```

### 2. `AppKeyAuthHandler.cs` — Fix Hardcoded Claims

The current handler emits hardcoded Fred White claims for any valid AppKey. This must be fixed to either emit generic service claims (for system-level AppKey usage) or look up the actual user.

For `AppKeys:ExcelAddin` (the FfE key), there is no per-user identity — it's a shared static key. After this spec ships, the FfE key is used only for CI/testing, not by real users. Emit generic service claims:

```csharp
// Replace the hardcoded Fred White block with:
var isExcelAddinKey = Options.ApiKeys.Contains(apiKey);
var claims = isExcelAddinKey
    ? new[]
      {
        // Service-level identity for CI/testing — no personal KB access
        new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
        new Claim(ClaimTypes.Name,           "FfE Service Account"),
        new Claim(ClaimTypes.Email,          "ffe-service@internal"),
      }
    : new[]
      {
        // Haven key — existing Fred White claims (unchanged for backward compat)
        new Claim(ClaimTypes.NameIdentifier, "08de7605-3f7d-427d-858a-637777b41018"),
        new Claim("oid",                     "08de7605-3f7d-427d-858a-637777b41018"),
        new Claim(ClaimTypes.Email,          "fwhite@refugems.com"),
        new Claim(ClaimTypes.Name,           "Fred White"),
        new Claim("preferred_username",      "fwhite@refugems.com"),
        new Claim("groups",                  "FIP-Users"),
        new Claim("groups",                  "FAIT-Users"),
      };
```

### 3. New Endpoint: `GET /api/excel/whoami`

Called by `authService.ts` after first sign-in to resolve the FAIT userId for the Entra user. Gets or creates the FAIT `AppUser` record for this Entra identity.

```csharp
// Add to a new ExcelAddinController.cs (or append to FirmIntegrationController — separate is cleaner)

[ApiController]
[Route("api/excel")]
public class ExcelAddinController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ExcelAddinController> _logger;

    [HttpGet("whoami")]
    [Authorize(AuthenticationSchemes = "EntraBearer")]  // Entra only — not AppKey
    public async Task<IActionResult> WhoAmI()
    {
        var oid   = User.FindFirst("oid")?.Value
                    ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        var name  = User.FindFirst("name")?.Value
                    ?? User.FindFirst(ClaimTypes.Name)?.Value ?? email;

        if (string.IsNullOrEmpty(oid))
            return Unauthorized(new { error = "No OID claim in token" });

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Get or create AppUser by EntraOid
        var user = await db.Users.FirstOrDefaultAsync(u => u.IsEntraUser && u.Email == email);
        if (user == null)
        {
            // Check by OID if available in a custom field — for now, match by email
            user = new AppUser
            {
                Id          = Guid.NewGuid(),
                Email       = email,
                DisplayName = name,
                IsEntraUser = true,
                IsActive    = true,
                Role        = "user",
                CreatedAt   = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            _logger.LogInformation("ExcelAddin: Provisioned new Entra user {Email} as FAIT user {Id}", email, user.Id);
        }

        return Ok(new { userId = user.Id, email = user.Email, name = user.DisplayName });
    }
}
```

### 4. `HavenChatController.cs` — Update Auth Attribute

```csharp
// Before:
[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]

// After:
[Authorize(Policy = "ExcelAddinAccess")]
```

The `ExcelAddinAccess` policy accepts both `AppKeyAuth` and `EntraBearer`. The `ClaimTypes.NameIdentifier` claim is present in both — for Entra JWT, it comes from the `sub` or `oid` claim (ASP.NET Core's JWT handler maps `oid` to `NameIdentifier` via the `Microsoft.Identity.Web` conventions — verify this mapping). For personal KB scoping, the NameIdentifier GUID must match the `AppUser.Id` in FAIT's DB.

**Constraint for CC:** The Entra JWT's `sub` claim is a per-app unique identifier (not the tenant-wide OID). Use the `oid` claim for user identity, not `sub`. Configure `TokenValidationParameters` to use OID for `NameIdentifier` mapping, or override the claim mapping after token validation:

```csharp
options.Events = new JwtBearerEvents
{
    OnTokenValidated = async ctx =>
    {
        // Map the Entra OID to the FAIT AppUser.Id for personal KB scoping
        var oid = ctx.Principal?.FindFirst("oid")?.Value
                  ?? ctx.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(oid)) return;

        var dbFactory = ctx.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var user = await db.Users.FirstOrDefaultAsync(u => u.IsEntraUser
            && u.Email == (ctx.Principal?.FindFirst("preferred_username")?.Value ?? ""));
        if (user != null)
        {
            // Inject the FAIT userId as NameIdentifier so HavenChatController gets the right userId
            var identity = ctx.Principal!.Identity as System.Security.Claims.ClaimsIdentity;
            identity?.RemoveClaim(identity.FindFirst(ClaimTypes.NameIdentifier));
            identity?.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        }
    }
};
```

---

## Entra App Registration Changes

**Expose an API scope on the FIP app registration:**

In Entra admin center → App registrations → FIP platform app → Expose an API:
- Application ID URI: `api://887206bc-fac1-436a-a8ed-2150418d76c0` (set if not already set)
- Add a scope: `FfE.Access`, admin + user consent, description: "Access FAIT from the Excel Add-in"

Without this, `msalInstance.loginRedirect({ scopes: ['api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access'] })` will fail with `AADSTS70011: invalid scope`.

**Allowed redirect URIs** (add to the Web platform in the registration):
- `https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html`
- `http://localhost:3000/excel-addin/auth-dialog.html` (local dev)

**This is an admin portal operation (Rhodey).** No code change.

---

## Silent Refresh Flow

After the first sign-in, `OfficeRuntime.storage` holds the access token with an expiry timestamp. On every `getAuthHeader()` call, `getStoredToken()` checks expiry (with a 5-minute buffer). If expired, it returns null and `getAuthHeader()` falls back to AppKey or returns empty headers.

**Silent refresh without re-prompting the user:** The MSAL refresh token flow requires a call to `msalInstance.acquireTokenSilent()`. However, `acquireTokenSilent` in a taskpane context requires the iframe to have a session cookie from the prior login — which may not be present in all Office environments (especially Desktop).

**Sprint 1 approach (pragmatic):** Don't attempt silent refresh in the taskpane. When `getStoredToken()` returns null (token expired), `AuthGate` detects the missing user in storage and shows the sign-in screen again. Access tokens from Entra are valid for **1 hour** by default. For typical Excel sessions (< 1 hour), this never triggers. For long sessions: user sees the sign-in prompt again. This is acceptable for v1.

**Sprint 2 (proper silent refresh):** Use `acquireTokenSilent()` inside a try/catch. If it fails (requires interaction), show a non-blocking banner "Session expired — click to re-authenticate" and open the dialog again without blocking the existing chat history. This is a UX improvement, not a correctness issue.

---

## Multi-Tenant Note

The current design uses a **single-tenant configuration** (`authority = https://login.microsoftonline.com/<tenantId>/v2.0`). This means only users from the Fortress AM Entra tenant (`d2bf3425-f8ab-451c-83bd-1e0ebd9508fe`) can sign in.

**What changes for multi-tenant:**
1. `authority` → `https://login.microsoftonline.com/common/v2.0` (accepts any tenant)
2. JWT validator: `ValidateIssuer = false` (or use a custom issuer validator that checks an allowlist of tenant IDs)
3. `AppUser` provisioning: must store `tenantId` alongside `oid` to differentiate users from different orgs with the same email
4. Personal KB scoping must be tenant-aware (a user at Tenant A and a user at Tenant B with the same email are different people)
5. The `FfE.Access` API scope becomes a multi-tenant API scope — requires admin consent in each tenant

For v1 (Fortress AM only), single-tenant is correct and simpler. Multi-tenant is a future architectural decision for Fred.

---

## Files Changed Summary

### New Files (Taskpane)

| File | Purpose |
|------|---------|
| `src/taskpane/services/authService.ts` | MSAL config, token storage, dialog sign-in, `getAuthHeader()` |
| `src/taskpane/auth/authDialog.html` | Dialog page (HTML entry) |
| `src/taskpane/auth/authDialog.tsx` | Dialog page (TS entry) — runs MSAL redirect, posts token |
| `src/taskpane/components/AuthGate.tsx` | Auth wrapper component |

### Modified Files (Taskpane)

| File | Change |
|------|--------|
| `src/taskpane/services/storage.ts` | Rename key constant; add `AUTH_TOKEN_KEY`, `AUTH_USER_KEY` helpers |
| `src/taskpane/services/settings.ts` | `loadSettings()` returns `authMode`; no longer required to return `apiKey` |
| `src/taskpane/services/faitApi.ts` | Replace `apiKey: string` param with `authHeader: Record<string,string>` in all exported functions |
| `src/taskpane/index.tsx` | Mount `<AuthGate>` not `<App>` |
| `src/taskpane/App.tsx` | Accept `user: FaitUser` prop; remove API key state + settings-show-on-empty logic |
| `src/taskpane/components/SettingsPanel.tsx` | Replace API key entry with "Sign out" + user info display; keep AppKey under "Advanced" |
| `src/taskpane/hooks/useChat.ts` | Replace `apiKey` param with `authHeader` from `getAuthHeader()` |
| `src/taskpane/components/ChatPanel.tsx` | Pass `authHeader` instead of `apiKey` to `useChat` |
| `public/manifest.xml` | Add `<AppDomains>` for `login.microsoftonline.com` |
| `manifest.local.xml` | Same `<AppDomains>` addition |
| `vite.config.ts` | Add `authDialog` as second `rollupOptions.input` entry |
| `package.json` | Add `@azure/msal-browser` |

### New Files (FAIT Backend)

| File | Purpose |
|------|---------|
| `Controllers/ExcelAddinController.cs` | `GET /api/excel/whoami` — resolve FAIT userId for Entra user |

### Modified Files (FAIT Backend)

| File | Change |
|------|--------|
| `Program.cs` | Add `EntraBearer` JWT scheme; rename `AppKeyOnly` policy to `ExcelAddinAccess`; add both schemes to policy |
| `Auth/AppKeyAuthHandler.cs` | Fix hardcoded Fred White claims; emit service-account claims for FfE AppKey |
| `Controllers/HavenChatController.cs` | Update `[Authorize]` attribute to use `ExcelAddinAccess` policy |

**Total: 4 new files + 12 modified (taskpane) + 1 new file + 3 modified (backend). One new npm package.**

**Infrastructure (Rhodey):**
- Expose `FfE.Access` scope on FIP app registration
- Add redirect URIs for `authDialog.html` (prod + local)
- No new AWS resources

---

## Acceptance Criteria

1. **First launch — no stored token:** `AuthGate` shows the "Sign in with Microsoft" button. Clicking it opens a top-level Office dialog navigating to `login.microsoftonline.com`. User signs in. Dialog closes. `AuthGate` transitions to `<App>`. No manual API key entry.

2. **Token stored:** After sign-in, close and reopen the taskpane. `AuthGate` reads the token from `OfficeRuntime.storage`, skips the sign-in screen, and shows `<App>` directly.

3. **Per-user identity:** Rob signs in. Claude's "personal KB" results come from Rob's personal KB (not Fred's). Len signs in on a different machine. Len gets Len's KB. Verify via `GET /api/excel/whoami` response showing the correct userId for each user.

4. **AppKey fallback:** A CI pipeline sends `POST /api/haven/chat` with `x-api-key: <AppKeys:ExcelAddin>`. It gets 200. The response does NOT contain personal KB results (service-account identity has no personal KB).

5. **Sign out:** User opens SettingsPanel, clicks "Sign out". `OfficeRuntime.storage` is cleared. Next time the taskpane opens, the sign-in screen appears.

6. **Token expiry:** Manually set the stored token's expiry to 1 minute ago. Reload the taskpane. The sign-in screen appears (token expired, no auto-refresh in Sprint 1).

7. **No popup blocked:** On Excel Desktop (not Online), sign-in completes without any "popup blocked" error. The dialog is a top-level window, not a browser popup.

8. **Dialog cancel:** User opens the sign-in dialog and closes it manually (X button). `AuthGate` shows an error "Sign-in cancelled" and the sign-in button again. App does not crash.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify authDialog.html is listed in manifest.xml <AppDomains> and that
          login.microsoftonline.com is ALSO listed. Missing either blocks the dialog
          from navigating to the sign-in page (Office shows "Navigation blocked").
          Both manifests (manifest.xml AND manifest.local.xml) must be updated.

⚠️  HIGH: Verify authDialog.tsx uses sessionStorage for MSAL cache
          (cacheLocation: 'sessionStorage'), NOT localStorage. The dialog window
          has a different localStorage origin than the taskpane on Desktop.
          Using localStorage in the dialog can leak tokens between different users
          on a shared machine.

⚠️  HIGH: Verify OnTokenValidated maps the Entra OID → FAIT AppUser.Id as
          NameIdentifier BEFORE HavenChatController reads it. If the FAIT userId
          is not injected, User.FindFirstValue(ClaimTypes.NameIdentifier) returns
          the Entra sub (a per-app random GUID, not the FAIT userId), and personal
          KB retrieval returns nothing or wrong results.

⚠️  HIGH: Verify the FfE.Access scope is exposed on the FIP app registration
          before testing. If the scope doesn't exist in Entra, MSAL will throw
          AADSTS70011 and the dialog will show "Sign-in failed" with no clear
          reason. This is an admin portal operation (Rhodey), not a code change.
          Confirm it's done before merging.

⚠️  MEDIUM: Verify vite.config.ts authDialog entry point output path matches
            what manifest.xml expects. If Vite outputs to dist/authDialog.html
            but manifest says /excel-addin/auth-dialog.html, the dialog 404s.
            The rollupOptions input key determines the output filename. Use key
            'auth-dialog' (hyphenated) to match the manifest URL convention.

⚠️  MEDIUM: Verify getAuthHeader() is awaited at every call site in faitApi.ts.
            It's async (reads OfficeRuntime.storage). Forgetting await returns
            a Promise object, which becomes the header value — the backend gets
            'Authorization: [object Promise]' and returns 401.

⚠️  MEDIUM: Verify ChatPanel.tsx and useChat.ts don't still pass apiKey as a
            string argument to faitApi functions. Search the entire codebase for
            'apiKey' after the refactor — any remaining string usages are bugs.
            The only remaining 'apiKey' should be in SettingsPanel (Advanced field)
            and in authService.ts's fallback path.

⚠️  LOW: Verify the dialog URL query string encodes the scope correctly.
         scope='api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access' contains
         slashes and colons. It must be encodeURIComponent()-encoded in the
         DIALOG_URL_BASE construction in authService.ts, and decoded via
         new URLSearchParams(window.location.search) in authDialog.tsx.
```

---

_Spec by Reed Richards | WI#831: FfE Entra Auth. 4 new files + 12 modified (taskpane) + 1 new + 3 modified (backend). Core pattern: Office Dialog API → MSAL redirect → messageParent → taskpane stores token. Per-user identity via OnTokenValidated OID→FAIT userId mapping._
