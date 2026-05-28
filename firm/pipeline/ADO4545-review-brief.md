# ADO#4545 — Adversarial Code Review Brief

**Reviewer:** Clint Barton (Code Reviewer)
**Task:** FIRM JWT Bearer auth for mobile API endpoints
**Commit:** d6f1442d
**Model:** sonnet

---

## What Was Built

JWT Bearer authentication was added to FIRM's `Program.cs` alongside the existing cookie auth scheme. A `CookieOrBearer` named authorization policy was created that accepts either auth scheme. Four mobile API endpoints in `MeetingsApiController.cs` were updated from `[Authorize]` to `[Authorize(Policy = "CookieOrBearer")]`.

---

## Files to Read

1. `firm/src/FortressIntelligenceRM.Web/Program.cs` — **READ THE FULL FILE**
2. `firm/src/FortressIntelligenceRM.Web/Controllers/MeetingsApiController.cs` — read from line 1080 to end
3. `firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj` — read the file

---

## Review Tasks

### 1. Program.cs — JWT Bearer Configuration

Read the AddJwtBearer block and verify:

a) **Authority URL format**: Is `https://login.microsoftonline.com/{TenantId}/v2.0` the correct format for Entra v2.0 OIDC? (Expected: yes — this is the standard Entra v2.0 authority)

b) **Config key `AzureAd:TenantId`**: FIRM's `FipTokenService.cs` already uses `config["AzureAd:TenantId"]` (line 21 of FipTokenService.cs). Confirm the new JWT Bearer code uses the same config key. This is important for env var consistency.

c) **Config key `AzureAd:ClientId`**: Similarly, `FipTokenService.cs` already uses `config["AzureAd:ClientId"]` (line 19). Confirm the new JWT Bearer code uses the same key.

d) **Audience format**: `api://{ClientId}` — this is the correct Entra app registration audience for PKCE mobile flows. Verify this format is used.

e) **TokenValidationParameters**: Verify ALL four validation flags are `true`:
   - `ValidateIssuer = true`
   - `ValidateAudience = true`
   - `ValidateLifetime = true`
   - `ValidateIssuerSigningKey = true`
   **Any `false` here is a critical security issue.**

f) **RequireHttpsMetadata**: Check if this is set. Default is `true`. If it's explicitly set to `false`, that's a security issue. If not set, the default `true` is correct.

g) **SaveToken**: Check if this is set. Not required but verify it's not set to something problematic.

### 2. Program.cs — CookieOrBearer Policy

Read the `AddAuthorization` block and verify:

a) **Both schemes listed**: Does `policy.AddAuthenticationSchemes(...)` include BOTH `CookieAuthenticationDefaults.AuthenticationScheme` AND `"Bearer"`? If only one is listed, Bearer tokens will be rejected.

b) **RequireAuthenticatedUser()**: Is this present? Without it, unauthenticated requests would be allowed.

c) **FallbackPolicy preservation**: The existing `options.FallbackPolicy = options.DefaultPolicy;` must still be present. If removed, unauthenticated access to Blazor routes would be allowed. Confirm it's still there.

d) **Policy name consistency**: The policy is added as `"CookieOrBearer"`. The endpoints reference `Policy = "CookieOrBearer"`. Confirm exact case match.

### 3. Program.cs — Middleware Order

Locate `app.UseAuthentication()` and `app.UseAuthorization()`. Verify:
- `UseAuthentication()` appears BEFORE `UseAuthorization()`
- This is mandatory — wrong order causes 401s on all endpoints

### 4. Program.cs — DefaultScheme

Verify `DefaultScheme` and `DefaultChallengeScheme` both remain `CookieAuthenticationDefaults.AuthenticationScheme`. If either was changed to `"Bearer"`, Blazor pages would stop working.

### 5. MeetingsApiController.cs — Endpoint Coverage

Read the Mobile API Endpoints section (look for the comment `// ── Mobile API Endpoints`). Verify exactly these 4 endpoints use `[Authorize(Policy = "CookieOrBearer")]`:
- `GET /api/firm/me` (HttpGet + [Authorize(Policy="CookieOrBearer")])
- `POST /api/firm/register-push-token` (HttpPost + [Authorize(Policy="CookieOrBearer")])
- `POST /api/meetings/mobile-upload` (HttpPost + [Authorize(Policy="CookieOrBearer")])
- `GET /api/meetings/list` (HttpGet + [Authorize(Policy="CookieOrBearer")])

Also verify: are there any OTHER endpoints in the file that were accidentally changed to `CookieOrBearer`? Search for all `[Authorize` attributes in the file. Only the 4 mobile endpoints should use `CookieOrBearer`. All others should remain plain `[Authorize]` or `[AllowAnonymous]`.

### 6. Scheme Selection Behavior — Multi-Scheme Analysis

This is a subtle ASP.NET Core behavior question. Analyze:

When `DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme` and a request comes in with `Authorization: Bearer <token>`:
- For endpoints using `[Authorize]` (no policy): ASP.NET will use the `DefaultScheme` (cookie) to authenticate. If no cookie, it returns 401/redirect. Bearer token is IGNORED. This is the DESIRED behavior for non-mobile endpoints.
- For endpoints using `[Authorize(Policy = "CookieOrBearer")]`: the `AddAuthenticationSchemes(Cookie, Bearer)` call explicitly tells ASP.NET to try these schemes for this request. Both cookie and Bearer will be attempted. This is the DESIRED behavior.

Verify: Does the `CookieOrBearer` policy correctly use `AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme, "Bearer")`? If it uses `AddRequirements` instead without schemes, the scheme selection won't work.

### 7. csproj — Package Reference

Read the csproj file. Verify:
- `Microsoft.AspNetCore.Authentication.JwtBearer` package ref is present
- Version is `8.0.*` (correct for .NET 8)
- The project targets `net8.0` (confirm target framework)

---

## Pass/Fail Criteria

**FAIL conditions (any one = FAIL):**
- Any `ValidateXxx = false` in TokenValidationParameters
- `RequireHttpsMetadata = false`
- Missing `RequireAuthenticatedUser()` in CookieOrBearer policy
- `FallbackPolicy` removed or changed
- `DefaultScheme` changed away from Cookie
- `UseAuthentication` AFTER `UseAuthorization`
- Non-mobile endpoints accidentally using CookieOrBearer policy
- `AddAuthenticationSchemes` missing from the CookieOrBearer policy definition (breaks Bearer auth)
- Policy name mismatch between definition and usage

**NEEDS-CHANGES conditions:**
- Missing or wrong package version in csproj
- Config key inconsistency (using different key than FipTokenService)

**PASS conditions:**
- All validation flags true
- Both schemes in policy
- RequireAuthenticatedUser present
- FallbackPolicy preserved
- DefaultScheme unchanged
- Middleware in correct order
- Exactly 4 mobile endpoints updated (no more, no less)
- Package ref correct

---

## Output

Report findings for each numbered section above. For each check: ✅ (confirmed correct) or ❌ (issue found) with the specific code evidence. Then give a single verdict: PASS, NEEDS-CHANGES, or FAIL with severity of issues found.
