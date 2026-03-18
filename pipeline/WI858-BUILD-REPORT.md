# Build Report: WI858 — FfE Sprint 12: Entra Auth Refactor

**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-17  
**Build Method:** CC Sonnet (`cat brief.md | claude --model sonnet -p --dangerously-skip-permissions`)  
**Passes:** 2 (taskpane + backend)

---

## Summary

Full Entra auth refactor for the FAIT for Excel taskpane. Replaced API key authentication with MSAL.js + Office Dialog API flow. Per-user identity via OnTokenValidated OID→userId mapping. AppKey fallback retained for CI/testing.

---

## CC Invocations

### Pass 1: Taskpane
```bash
cd ~/projects/fait-for-excel
cat cc-brief-wi858-taskpane.md | claude --model sonnet -p --dangerously-skip-permissions
```
Result: All taskpane files created/modified. TypeScript clean (exit 0). CC hung briefly on msal-browser node test (killed blocking subprocess); all file writes were complete before hang.

### Pass 2: Backend
```bash
cd ~/projects/fip/fait/src/FortressAI.Web
cat ~/projects/fait-for-excel/cc-brief-wi858-backend.md | claude --model sonnet -p --dangerously-skip-permissions
```
Result: Build clean — 0 errors, 32 pre-existing MudBlazor warnings.

---

## Files Created (New)

### Taskpane
| File | Purpose |
|------|---------|
| `src/taskpane/services/authService.ts` | MSAL config, token storage, dialog sign-in, `getAuthHeader()` |
| `src/taskpane/auth/auth-dialog.html` | Dialog page HTML entry (hyphenated filename) |
| `src/taskpane/auth/authDialog.tsx` | Dialog page TS — MSAL redirect + messageParent |
| `src/taskpane/components/AuthGate.tsx` | Auth wrapper — shows sign-in or App |

### Backend
| File | Purpose |
|------|---------|
| `Controllers/ExcelAddinController.cs` | `GET /api/excel/whoami` — provision/resolve FAIT user for Entra identity |

---

## Files Modified

### Taskpane
| File | Change |
|------|--------|
| `src/taskpane/services/storage.ts` | Added AUTH_TOKEN_KEY, AUTH_EXPIRY_KEY, AUTH_USER_KEY constants |
| `src/taskpane/services/settings.ts` | Added `authMode: 'entra' \| 'appkey'` to FaitSettings interface |
| `src/taskpane/services/faitApi.ts` | Replaced `apiKey: string` with `authHeader: Record<string,string>` in ALL exported functions |
| `src/taskpane/hooks/useChat.ts` | Updated to pass `authHeader` instead of `apiKey` |
| `src/taskpane/components/ChatPanel.tsx` | Updated props: `authHeader` instead of `apiKey` |
| `src/taskpane/components/SettingsPanel.tsx` | Replaced API key entry with Entra sign-out + user info; AppKey under Advanced |
| `src/taskpane/index.tsx` | Mounts `<AuthGate>` not `<App>` |
| `src/taskpane/App.tsx` | Accepts `user: FaitUser` prop; uses `getAuthHeader()` for auth |
| `public/manifest.xml` | Added `<AppDomain>https://login.microsoftonline.com</AppDomain>` |
| `manifest.local.xml` | Same AppDomains entry |
| `vite.config.ts` | Added `'auth-dialog': 'src/taskpane/auth/auth-dialog.html'` entry point |
| `package.json` | Added `@azure/msal-browser` |

### Backend
| File | Change |
|------|--------|
| `Program.cs` | Added EntraBearer JWT scheme + OnTokenValidated OID→userId + ExcelAddinAccess policy |
| `Auth/AppKeyAuthHandler.cs` | Fixed hardcoded Fred White claims — ExcelAddin key → FfE Service Account; Haven key → unchanged |
| `Controllers/HavenChatController.cs` | `[Authorize]` updated to `Policy = "ExcelAddinAccess"` |
| `FortressAI.Web.csproj` | Added `Microsoft.AspNetCore.Authentication.JwtBearer` package ref |

---

## Gate Check Results

```
=== authDialog in Vite entry points ===
24: 'auth-dialog':  'src/taskpane/auth/auth-dialog.html', // auth dialog — outputs to dist/auth-dialog.html
✅ PASS

=== Both manifests have AppDomains ===
manifest.xml:18:    <AppDomain>https://login.microsoftonline.com</AppDomain>
manifest.local.xml:18:    <AppDomain>https://login.microsoftonline.com</AppDomain>
✅ PASS (both manifests)

=== getAuthHeader replaces apiKey param ===
faitApi.ts:10:  authHeader: Record<string, string>,
faitApi.ts:27:        ...authHeader,
faitApi.ts:60:  authHeader: Record<string, string>,
(no apiKey: string params found)
✅ PASS

=== AppKey fallback intact ===
AppKeyAuthHandler.cs verified — scheme registered, AllKeys validation intact
✅ PASS

=== Hardcoded Fred claims FIXED ===
AppKeyAuthHandler.cs:56: var isExcelAddinKey = Options.ApiKeys.Contains(apiKey);
AppKeyAuthHandler.cs:57: var claims = isExcelAddinKey
AppKeyAuthHandler.cs:62: new Claim(ClaimTypes.Name, "FfE Service Account"),
✅ PASS — conditional per-key identity

=== Entra JWT validation in Program.cs ===
Program.cs:182:.AddJwtBearer("EntraBearer", options =>
Program.cs:200:    OnTokenValidated = async ctx =>
Program.cs:236:    options.AddPolicy("ExcelAddinAccess", ...
✅ PASS

=== AuthGate wraps App ===
index.tsx:2: import AuthGate from './components/AuthGate';
index.tsx:13: root.render(<AuthGate />);
✅ PASS

=== whoami endpoint ===
ExcelAddinController.cs:33: [HttpGet("whoami")]
ExcelAddinController.cs:35: public async Task<IActionResult> WhoAmI()
✅ PASS

=== FfE TS clean ===
npx tsc --noEmit → EXIT: 0 → CLEAN
✅ PASS

=== .NET build clean ===
dotnet build → 0 errors, 32 pre-existing MudBlazor warnings
✅ PASS
```

---

## Commits

**FfE Taskpane:** `9d33305` — WI858: FfE Entra auth — MSAL.js + Office Dialog API + per-user identity  
**FAIT Backend:** `83011c0` — WI858: FAIT backend — Entra JWT validation + OID→userId mapping + whoami endpoint

---

## Acceptance Criteria Verification

| Criteria | Status | Notes |
|----------|--------|-------|
| AuthGate shows sign-in on first launch | ✅ Implemented | AuthGate checks stored token; shows sign-in button if missing |
| Token stored — skip sign-in on reopen | ✅ Implemented | getStoredToken() reads from OfficeRuntime.storage |
| Per-user identity | ✅ Implemented | OnTokenValidated maps Entra email → FAIT AppUser.Id |
| AppKey fallback for CI | ✅ Implemented | ExcelAddinAccess policy accepts both schemes; AppKey emits service account identity |
| Sign out | ✅ Implemented | SettingsPanel has sign-out button → clearAuth() + reload |
| Token expiry shows sign-in | ✅ Implemented | 5-min buffer; expired = null = AuthGate shows sign-in |
| No popup blocked | ✅ Implemented | Office.context.ui.displayDialogAsync() — top-level window |
| Dialog cancel handled | ✅ Implemented | DialogEventReceived error 12006 → resolve({success:false}) |

---

## Notable Implementation Details

1. **MSAL hang in non-browser env:** CC's test of `@azure/msal-browser` node instantiation hung (MSAL attempts browser API calls). Killed the blocking subprocess after confirming all file writes were complete. This is expected — MSAL only runs in browser context.

2. **AppUser.EntraOid not in model:** The spec references `AppUser.EntraOid` but the actual model has no such field. OnTokenValidated and whoami both use `IsEntraUser == true && Email == email` instead. This matches the existing DB schema.

3. **auth-dialog.html filename:** Correctly hyphenated throughout. Vite entry key is `'auth-dialog'` which produces `auth-dialog.html` output. Manifest references `/excel-addin/auth-dialog.html`.

4. **@azure/msal-browser:** Only new npm package added. All other deps unchanged.

---

## Infrastructure Note (Rhodey)

Before testing, the following admin portal operations are required:
- Expose `FfE.Access` scope on FIP app registration (`api://887206bc.../FfE.Access`)
- Add redirect URIs: `https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html` and `http://localhost:3000/excel-addin/auth-dialog.html`

These are NOT code changes. This is a prerequisite for Natasha's QA verification of the auth flow.

---

**Ready for Clint's review.**
