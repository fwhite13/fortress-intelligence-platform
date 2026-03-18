# Review Report: WI858
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

## CC Invocation
```bash
cat ~/projects/fait-for-excel/review-brief-wi858.md | claude --model sonnet -p
```
CC completed frontend checks (1–5, 9–12, 14). Backend checks (6–8, 13) completed by reviewer from direct file reads.

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| auth-dialog in manifest.xml AppDomains | ✅ | `public/manifest.xml` line 18 — `<AppDomain>https://login.microsoftonline.com</AppDomain>` |
| auth-dialog in manifest.local.xml AppDomains | ✅ | `manifest.local.xml` line 19 — same entry present |
| auth-dialog Vite entry (output path match) | ❌ | **CRITICAL — path mismatch** (see Issues) |
| getAuthHeader() replaces apiKey param everywhere | ✅ | `faitApi.ts` — all exports take `authHeader: Record<string, string>`, no `apiKey: string` param |
| No apiKey prop in App.tsx/ChatPanel | ✅ | `App.tsx` passes `authHeader` to `ChatPanel` (line 44); no `apiKey` prop |
| ExcelAddinAccess policy: both schemes | ✅ | `Program.cs` lines ~195–198 — `.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")` |
| AppKeyAuthHandler.cs intact | ✅ | Handler intact; reads `x-api-key` header; returns `NoResult()` when absent |
| OnTokenValidated: OID→userId mapping | ✅ w/ NOTE | Email lookup + claim injection present; fail-open (see Notes) |
| authDialog.tsx: messageParent() used | ✅ | `authDialog.tsx` line ~46 (success) and ~63 (error) — both call `messageParent()` |
| authDialog.tsx: token storage in parent not dialog | ✅ | No `OfficeRuntime.storage` or `localStorage` calls in authDialog.tsx |
| AuthGate.tsx: displayDialogAsync flow | ✅ w/ NOTE | Correct flow via `signIn()` → `displayDialogAsync`; minor loop-edge-case noted |
| Token in OfficeRuntime.storage (not localStorage) | ✅ | `authService.ts` `getStorage()` — prefers `OfficeRuntime.storage`, localStorage fallback for dev |
| whoami endpoint response shape | ⚠️ WARN | Returns `userId`, `email`, `name` — **missing `authScheme`** (spec requires 4 fields) |
| TS clean | ⚠️ WARN | Storage duplication, hardcoded URL, minor issues (see Issues) |

## Issues Found

### 🔴 Critical

**C1 — Auth dialog 404 at runtime (CHECK 3)**

`vite.config.ts` entry:
```ts
'auth-dialog': 'src/taskpane/auth/auth-dialog.html'  // line 24
```

Vite preserves the source-relative path, so the output is:
```
dist/src/taskpane/auth/auth-dialog.html
```

With `base: '/excel-addin/'`, this serves at:
```
https://fait.dev.fortressam.ai/excel-addin/src/taskpane/auth/auth-dialog.html
```

But `authService.ts` line 10 constructs:
```ts
const DIALOG_URL_BASE = `${window.location.origin}/excel-addin/auth-dialog.html`;
// → https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html  ← WRONG
```

**The dialog will 404.** The MSAL redirect URI in `authDialog.tsx` line 16 has the same wrong path:
```ts
const redirectUri = `${window.location.origin}/excel-addin/auth-dialog.html`;
```

This will also fail MSAL's redirect URI validation against the app registration.

The comment in `vite.config.ts` line 24 (`// auth dialog — outputs to dist/auth-dialog.html`) confirms the developer had the wrong mental model — the output is NOT `dist/auth-dialog.html`.

**Fix:** Move `auth-dialog.html` to project root so Vite outputs `dist/auth-dialog.html` (matching the current DIALOG_URL_BASE), OR update `DIALOG_URL_BASE` and `redirectUri` to the actual nested path `/excel-addin/src/taskpane/auth/auth-dialog.html` AND update the Azure app registration redirect URI to match.

Easiest fix: move `src/taskpane/auth/auth-dialog.html` to root as `auth-dialog.html` and update `vite.config.ts` entry to `'auth-dialog': 'auth-dialog.html'`. That makes Vite output `dist/auth-dialog.html` → served at `/excel-addin/auth-dialog.html` → matches DIALOG_URL_BASE and redirectUri. Also update `authDialog.tsx` import (currently `src="./authDialog.js"` — after the move, this path is broken too).

---

### 🟡 Important

**I1 — `whoami` missing `authScheme` in response**

`ExcelAddinController.cs` `WhoAmI()` returns:
```csharp
return Ok(new { userId = user.Id, email = user.Email, name = user.DisplayName ?? user.Email });
```

The spec requires `authScheme` in the response. Add it:
```csharp
return Ok(new { userId = user.Id, email = user.Email, name = user.DisplayName ?? user.Email, authScheme = "EntraBearer" });
```

**I2 — Duplicate `getStorage()` / `APIKEY_KEY` / `getApiKey()` between authService.ts and storage.ts**

`authService.ts` defines its own inline `getStorage()` with a localStorage fallback shim (lines 17–22), plus `APIKEY_KEY` and `getApiKey()`. `storage.ts` likely has its own version (both files were modified). These are now duplicated. If one is updated without the other, divergence bugs follow. Choose one location and remove the other.

**I3 — Hardcoded production URL in `resolveUserIdentity()`**

`authService.ts` `resolveUserIdentity()` hardcodes `https://fait.dev.fortressam.ai/api/excel/whoami`. All other API calls in `faitApi.ts` use `const FAIT_BASE`. This breaks local dev (where the dev server at localhost:3000 calls the prod backend) and diverges from the established pattern.

Fix: import and use `FAIT_BASE` from `faitApi.ts` (or export it from a shared constants file).

**I4 — OnTokenValidated fails open but doesn't emit `userNotFound` flag**

`Program.cs` `OnTokenValidated`: if no matching `AppUser` is found, it leaves claims as-is (fail-open ✅). But the spec says it should emit a `userNotFound` flag so controllers can handle the distinction. Currently there's no such claim. The `whoami` endpoint will provision the user on first hit, so this is low-risk in practice — but the `ExcelAddinController.WhoAmI` is decorated `[Authorize(AuthenticationSchemes = "EntraBearer")]` directly (not `ExcelAddinAccess` policy), meaning it only accepts Entra tokens. A user whose email isn't yet in the DB will still reach WhoAmI successfully (and get provisioned), so the fail-open behavior + provisioning-on-demand is functionally correct. Low severity but worth documenting.

---

### 🔵 Nitpick

**N1 — `vite.config.ts` line 24 comment is wrong** — says `outputs to dist/auth-dialog.html` but actual output is `dist/src/taskpane/auth/auth-dialog.html`. Once the path is fixed, this comment will be accurate.

**N2 — `manifest.local.xml` has a duplicate `<DisplayName>` element** — XML lint will flag this. Harmless but untidy.

**N3 — `AuthGate.tsx` edge case**: if `signIn()` returns `{ success: true, user: undefined }`, the user stays on the sign-in screen with no error and no feedback. Should add a fallback: `if (result.success && !result.user) setError('Identity resolution failed')`.

**N4 — `authDialog.tsx` script src path**: `auth-dialog.html` has `<script type="module" src="./authDialog.js">`. After the path fix (moving auth-dialog.html to root), this relative path will need to be updated.

**N5 — `settings.ts` `authMode` is a point-in-time snapshot**, not reactive. If a token expires between settings load and display, the shown authMode will be stale. Cosmetic issue.

---

## Notes

### ExcelAddinAccess policy — CORRECT
`Program.cs` `AddPolicy("ExcelAddinAccess")`:
```csharp
policy.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")
      .RequireAuthenticatedUser()
```
Both schemes listed. ✅

### AppKeyAuthHandler — CORRECTLY MODIFIED
Handler was modified to support per-key claims. The FfE Excel Addin key now gets a service account identity (`FfE Service Account`, `00000000-0000-0000-0000-000000000001`). The Haven key retains Fred White's claims. `NoResult()` returned when header absent. Backward compat preserved. ✅

### OnTokenValidated — FAIL OPEN, CORRECT
If Entra user not found in DB:
```csharp
// If user not found, leave claims as-is; whoami endpoint will provision them
```
Fail-open behavior confirmed. The whoami endpoint handles first-login provisioning. ✅

### HavenChatController — CORRECTLY MIGRATED
Was on `AppKeyOnly` (or similar), now on `ExcelAddinAccess`. Both schemes work. ✅

### WhoAmI endpoint — ONLY Entra, not both schemes
`[Authorize(AuthenticationSchemes = "EntraBearer")]` — this is intentional: the whoami endpoint is only for Entra users. AppKey users don't use it. ✅

---

## Verdict

**NEEDS-CHANGES**

One critical blocker: the auth-dialog path mismatch (C1) will make the sign-in flow 404 at runtime. Everything else is correctly implemented — MSAL v3 flow, messageParent pattern, token storage separation, policy setup, AppKey fallback, OID→userId mapping. Fix C1 + I1 (authScheme), clean up I2 (storage duplication) and I3 (hardcoded URL), then re-submit for Cycle 2.

**Return to Tony with:**
- C1 (critical): Fix Vite entry path vs DIALOG_URL_BASE mismatch
- I1 (important): Add `authScheme` to whoami response
- I2 (important): Remove storage.ts/authService.ts duplication
- I3 (important): Replace hardcoded URL in resolveUserIdentity() with FAIT_BASE
- N1–N5 (nitpick): Fix comment, duplicate XML element, edge cases
