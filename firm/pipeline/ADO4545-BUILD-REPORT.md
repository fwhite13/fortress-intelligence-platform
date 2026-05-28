# Build Report: ADO#4545

## Summary
Added JWT Bearer authentication (`AddJwtBearer`) alongside the existing cookie auth scheme in FIRM's `Program.cs`. Created a new `CookieOrBearer` named authorization policy that accepts either scheme. Applied that policy to the 4 mobile API endpoints so the React Native companion app can authenticate with Entra PKCE Bearer tokens. Blazor routes and all other endpoints are unaffected.

## CC Invocation
```
cat firm/pipeline/ADO4545-build-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Single CC session, sequential (no parallelization needed — all changes in 3 files with a clear dependency order).

## Files Modified
- `firm/src/FortressIntelligenceRM.Web/Program.cs` — Added JwtBearer using, AddJwtBearer config, CookieOrBearer policy
- `firm/src/FortressIntelligenceRM.Web/Controllers/MeetingsApiController.cs` — 4 mobile endpoint attributes updated
- `firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj` — Added `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.*` package ref (not bundled by default in this project)

## Changes Made
- **Program.cs: AddJwtBearer added**
  - Authority: `https://login.microsoftonline.com/{AzureAd:TenantId}/v2.0`
  - Audience: `api://{AzureAd:ClientId}`
  - Full token validation: issuer, audience, lifetime, signing key
- **Program.cs: CookieOrBearer policy added**
  - Accepts both `CookieAuthenticationDefaults.AuthenticationScheme` and `"Bearer"`
  - Requires authenticated user
- **MeetingsApiController.cs: 4 endpoints swapped to CookieOrBearer policy**
  - `GET /api/firm/me` (GetMe)
  - `POST /api/firm/register-push-token` (RegisterPushToken)
  - `POST /api/meetings/mobile-upload` (MobileUpload)
  - `GET /api/meetings/list` (ListMeetings)

## Build Result
**Build succeeded — 0 errors, 22 warnings** (all 22 warnings are pre-existing; 0 new warnings introduced)

## Self-Review Checklist
- [x] AC1: /api/firm/me accepts Bearer token — uses `CookieOrBearer` policy which accepts Bearer scheme
- [x] AC2: Meeting list loads for mobile — `GET /api/meetings/list` updated to `CookieOrBearer`
- [x] AC3: Mobile upload works — `POST /api/meetings/mobile-upload` updated to `CookieOrBearer`
- [x] AC4: Blazor web UI unaffected — `DefaultScheme` and `DefaultChallengeScheme` both remain `CookieAuthenticationDefaults.AuthenticationScheme`; `FallbackPolicy` untouched
- [x] AC5: No regression on existing `[Authorize]` endpoints — all non-mobile endpoints left as `[Authorize]` (cookie-only); VpCallback/VpGetOrgContext remain `[AllowAnonymous]`

## Known Edge Cases / Things Clint Should Scrutinize
1. **`AzureAd:TenantId` and `AzureAd:ClientId` config keys** — These must exist in the ECS task environment or appsettings. If they're missing at runtime, JWT validation will fail silently or throw. Clint should verify these are already in the FIRM ECS task definition (they were added for other reasons, but worth confirming).
2. **NuGet package version pinning** — CC added `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.*` which will resolve to the latest 8.0.x. This is correct for .NET 8 but confirm the restore resolves cleanly in CI/CD.
3. **Token audience** — The audience is `api://{ClientId}`, which is the standard Entra app registration format for mobile PKCE. If the mobile app registered a different audience scope, this won't match. Needs QA with an actual mobile token.

## How to Test Locally
1. Get a valid Entra Bearer token from the mobile app or Postman (PKCE flow against the FIRM app registration)
2. `curl -H "Authorization: Bearer <token>" https://firm.dev.fortressam.ai/api/firm/me`
3. Expect: `200 OK` with JSON user object (not an HTML redirect)
4. Test cookie flow still works: open FIRM in browser — should still redirect to FAIT for login as usual

## ADO Comment
Build complete — commit d6f1442d.

---

## Fix Round (post-QA)

### CC Invocation
```
cat firm/pipeline/ADO4545-fix-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Single CC session.

### Change: OnRedirectToLogin suppression for /api/ paths

Added `options.Events.OnRedirectToLogin` handler inside the existing `AddCookie` options lambda in `Program.cs`.

When a request path starts with `/api`, the handler sets `StatusCode = 401` and returns immediately — no redirect header appended. All other paths receive the normal `Location: /auth/redirect-to-login` redirect as before.

**Only `Program.cs` was changed.** No other files touched.

### Build Result
**dotnet build — 0 errors, 22 warnings** (all 22 pre-existing; 0 new warnings introduced)

### Commit
`1fe3cd1c` — "FIRM ADO#4545 fix: suppress OnRedirectToLogin for /api/ paths"
