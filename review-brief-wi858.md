# Review Brief: WI858 — FfE Entra Auth Refactor
## Reviewer: Hawkeye (Clint Barton) — Cycle 1 of 2

You are performing a code review of WI858: MSAL.js Entra authentication for the FAIT for Excel add-in.
The implementation uses the Office Dialog API pattern for interactive sign-in, with per-user identity
via OID → FAIT userId mapping on the backend.

## Files to Review

### New files (taskpane — ~/projects/fait-for-excel/):
- src/taskpane/services/authService.ts
- src/taskpane/auth/auth-dialog.html
- src/taskpane/auth/authDialog.tsx
- src/taskpane/components/AuthGate.tsx

### Modified (taskpane):
- src/taskpane/services/faitApi.ts
- src/taskpane/services/storage.ts
- src/taskpane/services/settings.ts
- src/taskpane/components/SettingsPanel.tsx
- src/taskpane/index.tsx
- src/taskpane/App.tsx
- public/manifest.xml
- manifest.local.xml
- vite.config.ts

### New (backend — ~/projects/fip/fait/src/FortressAI.Web/):
- Controllers/ExcelAddinController.cs

### Modified (backend):
- Program.cs
- Auth/AppKeyAuthHandler.cs
- Controllers/HavenChatController.cs

## What to Verify

Read each file listed above. Then answer the following checks precisely:

### CHECK 1: manifest.xml AppDomains
Read public/manifest.xml. Does it contain `<AppDomain>https://login.microsoftonline.com</AppDomain>`?
If yes: PASS with line number. If no: FAIL.

### CHECK 2: manifest.local.xml AppDomains
Read manifest.local.xml. Does it contain `<AppDomain>https://login.microsoftonline.com</AppDomain>`?
If yes: PASS with line number. If no: FAIL.

### CHECK 3: Vite auth-dialog entry + path alignment
Read vite.config.ts.
- Is there an `auth-dialog` entry in rollupOptions.input?
- What output path does Vite generate? (based on entry path — if entry is `src/taskpane/auth/auth-dialog.html`, Vite outputs to `dist/src/taskpane/auth/auth-dialog.html`; if entry is just `auth-dialog.html` at root, it outputs `dist/auth-dialog.html`)
- What path does authService.ts use for DIALOG_URL_BASE? (check `${window.location.origin}/excel-addin/...`)
- With `base: '/excel-addin/'` configured, does Vite's output path + base = the URL authService expects?
- PASS only if the served URL matches DIALOG_URL_BASE. Document the exact path comparison.

### CHECK 4: getAuthHeader() replaces apiKey param
Read src/taskpane/services/faitApi.ts completely.
- Does any exported function still have `apiKey: string` as a parameter?
- Does every API call use the authHeader parameter (passed from callers that got it from getAuthHeader())?
- Note: faitApi.ts functions receive `authHeader: Record<string, string>` as a parameter — that IS the correct pattern (callers call getAuthHeader() and pass the result in). The wrong pattern would be functions taking `apiKey: string` directly.
- PASS if no function has `apiKey: string` param. FAIL if any do.

### CHECK 5: No apiKey prop in App.tsx or ChatPanel.tsx
Read src/taskpane/App.tsx and src/taskpane/components/ChatPanel.tsx (if it exists).
- Does App.tsx pass any `apiKey` prop to child components?
- Does ChatPanel.tsx receive an `apiKey` prop?
- App.tsx should call getAuthHeader() itself and pass `authHeader` to ChatPanel — NOT apiKey.
- PASS if no apiKey prop passing. FAIL if found.

### CHECK 6: ExcelAddinAccess policy — both schemes
Read Program.cs. Find the `ExcelAddinAccess` policy definition.
- Does it call `.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")`?
- Does it call `.RequireAuthenticatedUser()`?
- PASS if both schemes listed. FAIL if only one.

### CHECK 7: AppKeyAuthHandler.cs intact
Read Auth/AppKeyAuthHandler.cs.
- Does it still handle the `x-api-key` header?
- Does it return `NoResult()` when header is absent (not Fail)?
- Was it modified? If so, what changed?
- PASS if it still handles x-api-key properly.

### CHECK 8: OnTokenValidated OID→userId mapping
Read Program.cs, find the `OnTokenValidated` callback in the EntraBearer JWT configuration.
- Does it extract email from `preferred_username` claim?
- Does it query the DB for `u.IsEntraUser && u.Email == email`?
- Does it inject `ClaimTypes.NameIdentifier` with the FAIT userId?
- If user not found: does it fail open (leave claims as-is) or fail closed (reject)?
  The spec says fail open — check what the code actually does.
- PASS if email lookup + claim injection present. Note fail-open vs fail-closed behavior.

### CHECK 9: authDialog.tsx — messageParent() used correctly
Read src/taskpane/auth/authDialog.tsx.
- Does it call `msalInstance.handleRedirectPromise()` (or equivalent MSAL v3 flow)?
- On success: does it call `Office.context.ui.messageParent(JSON.stringify({...}))`?
- On failure: does it call `messageParent` with error info?
- Does it NOT store tokens itself (token storage must happen in parent/taskpane)?
- PASS if messageParent used correctly on both paths. FAIL if tokens stored in dialog.

### CHECK 10: authDialog.tsx — token storage in parent not dialog
Continuing from CHECK 9:
- Does authDialog.tsx call OfficeRuntime.storage.setItem() or localStorage.setItem()?
- It should NOT — token storage is the parent's responsibility.
- PASS if dialog does NOT store tokens. FAIL if it does.

### CHECK 11: AuthGate.tsx — displayDialogAsync flow
Read src/taskpane/components/AuthGate.tsx.
- On load: does it check stored token via getStoredToken()?
- If valid token: does it render `<App user={...} />`?
- If no/expired token: does it show a sign-in button?
- Clicking sign-in: does it call signIn() (which internally calls displayDialogAsync)?
- Dialog message handler: does it receive token and set user state?
- Is there any risk of infinite sign-in loops?
- PASS if flow is correct. Note any loop risks.

### CHECK 12: Token in OfficeRuntime.storage
Read src/taskpane/services/authService.ts.
- Does getStorage() use OfficeRuntime.storage (with localStorage fallback for dev)?
- Are storeToken() and storeUser() calling storage.setItem()?
- Does getStoredToken() check expiry with a 5-min buffer?
- PASS if OfficeRuntime.storage is primary. Note the localStorage fallback.

### CHECK 13: whoami endpoint response shape
Read Controllers/ExcelAddinController.cs.
- Does GET /api/excel/whoami return `userId`, `email`, `name`?
- Does it also return `authScheme`? (spec requires this field)
- PASS only if all four fields present: userId, email, name, authScheme.
- FAIL/WARN if authScheme is missing.

### CHECK 14: TypeScript cleanliness
Read all TypeScript files. Note any obvious type errors, missing type annotations, or `any` overuse.
Note: `eslint-disable @typescript-eslint/no-explicit-any` for Office.js interop is acceptable.

## Output Format

After reading all files, produce a structured review with:

1. A table with each check, PASS/FAIL/WARN, and evidence (file + line number)
2. A list of issues found, categorized as Critical / Important / Nitpick
3. An overall verdict: PASS | NEEDS-CHANGES | FAIL

Be precise about line numbers. Read the actual file content — do not reason about code you haven't read.
