# Security Report: WI858 — FfE Entra Auth Refactor
## Verdict: PASS
## Scoped: Changed files in fait-for-excel/ + fip/fait/ (high risk — new auth scheme)
## Scanned: 2026-03-17 ~21:56 EDT

---

## Findings

None blocking. One informational note:

| ID | Severity | Finding | File | Action |
|----|----------|---------|------|--------|
| SEC-1 | 📝 NOTE | CLIENT_ID and SCOPE are hardcoded in authService.ts (`887206bc-fac1-436a-a8ed-2150418d76c0`). These are public values (MSAL public client flow — no secret involved). Not a security issue. | `authService.ts:2,6` | No action needed |

---

## Passed Checks

| Check | Result | Evidence |
|-------|--------|----------|
| Token in OfficeRuntime.storage, NOT localStorage | ✅ PASS | No localStorage references in authService.ts |
| Token never logged | ✅ PASS | No console.log of token/key/secret |
| Auth dialog doesn't store token itself | ✅ PASS | auth-dialog.html has no storage calls — uses messageParent() |
| No client secret in taskpane (public client flow) | ✅ PASS | No clientSecret anywhere in src/ |
| Entra JWT: ValidateIssuer + ValidateAudience = true | ✅ PASS | Program.cs:192-194 |
| Entra JWT: correct authority (login.microsoftonline.com/{tenantId}/v2.0) | ✅ PASS | Program.cs:188 |
| OnTokenValidated fails open (not reject on user-not-found) | ✅ PASS | Program.cs:224 — leaves claims as-is, comment explains provisioning path |
| ExcelAddinAccess policy: both AppKeyAuth + EntraBearer | ✅ PASS | Program.cs:236-238 |
| AppKeyOnly policy retained for backward compat | ✅ PASS | Program.cs:233-235 |
| AppKey path: no cross-contamination with Entra path | ✅ PASS | Conditional claims in AppKeyAuthHandler.cs:55-76 |
| whoami: Entra auth required (not AllowAnonymous) | ✅ PASS | ExcelAddinController.cs:34 — [Authorize(AuthenticationSchemes = "EntraBearer")] |
| HavenChatController: ExcelAddinAccess (both schemes) | ✅ PASS | HavenChatController.cs:22 |

---

## Decision

**PASS** — proceed to DEPLOY. Infrastructure prereqs (Entra scope + redirect URIs) are Rhodey's responsibility pre-deploy.
