# Review Report: ADO#1344 (cycle 1)

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `33d1104`  
**Date:** 2026-03-29  
**Cycle:** 1 of 2

---

### Verdict: NEEDS-CHANGES

Two changes required before PASS:
1. Fix `CreatedAt` = `DateTime.MinValue` on every new token insert (data corruption)
2. Fix `/auth/ms-callback` state binding — no check that the authenticated user matches `firmUserId` in the state parameter (security)

---

### CC Invocation

```bash
cd /home/fredw/projects/fip && cat review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Brief written by Clint at: `/home/fredw/projects/fip/review-brief.md`

---

### Spec Compliance

**Spec:** `memory/projects/fip-specs/firm-standalone-token-management.md`

| Requirement | Status | Notes |
|---|---|---|
| `IFirmMicrosoftTokenService` interface created | ✅ | |
| `FirmMicrosoftTokenService` uses `FirmDbContext` only | ✅ | No cross-DB access |
| No `FaitSharedDbContext` dependency | ✅ | File deleted, no live references |
| Token key is `firm_users.id` (FIRM GUID, string) | ✅ | `FindAsync(firmUserId)` |
| Config keys: `Firm:GraphClientId/TenantId/GraphClientSecret` | ✅ | |
| Log prefix: `[FirmMicrosoftTokenService]` | ✅ | All log calls use this prefix |
| Interface methods: `GetValidAccessTokenAsync`, `ExchangeCodeAsync`, `RevokeTokenAsync`, `HasToken` | ✅ | All 4 present |
| `ExchangeCodeAsync` return type — spec says `Task<bool>` | ⚠️ | Returns `Task<UserMicrosoftToken>` — see Note 1 |
| `UserMicrosoftToken.UserId`: string (not Guid) | ✅ | |
| `FirmDbContext` has `UserMicrosoftTokens` DbSet | ✅ | |
| Fluent config: `ToTable`, `HasKey`, `HasColumnName` for all props | ✅ | |
| `HasColumnType("char(36)")` for UserId | ✅ | |
| `HasColumnType("longtext")` for AccessToken, RefreshToken | ✅ | |
| `/auth/ms-callback` OAuth endpoint added | ✅ | |
| Callback resolves `firmUserId` from state | ✅ | State format `"firmUserId:random"` parsed correctly |
| Callback redirects to `/meetings` on success | ✅ | Via JS auto-redirect |
| `CalendarService` uses `IFirmMicrosoftTokenService` | ✅ | Constructor injection, no FAIT service |
| `CalendarService` calls `GetValidAccessTokenAsync(firmUser.Id)` | ✅ | Uses `firmUser.Id`, not `fait_user_id` |
| `Program.cs` registers `IFirmMicrosoftTokenService, FirmMicrosoftTokenService` | ✅ | `AddScoped` |
| `FaitSharedDbContext` factory removed from `Program.cs` | ✅ | |
| `Data/FaitSharedDbContext.cs` deleted | ✅ | Not present in Data/ directory |
| No leftover `FaitSharedDbContext` in live code | ✅ | grep confirms only pipeline docs reference it |
| `firm_users.fait_user_id` column untouched | ✅ | Not referenced anywhere in changed code |
| `GuidFormat=None` remains in FIRM connection string | ✅ | `Program.cs:36` |
| Scope creep | ✅ | Only expected files changed |

**Note 1 — ExchangeCodeAsync return type:** Spec says `Task<bool>`, implementation returns `Task<UserMicrosoftToken>`. This is a spec wording gap, not an impl bug — the caller in Program.cs uses `token.MicrosoftEmail` to display the connected email in the success page, which requires the richer return type. A `bool` would have required a separate DB read. The richer return type is strictly better and the caller correctly uses it. I'm treating this as spec document drift, not a code defect. Tony should note this deviation in the WI for tracking.

---

### Issues Found

| # | Severity | File | Issue | Fix Required |
|---|----------|------|-------|--------------|
| 1 | **Important** | `Services/FirmMicrosoftTokenService.cs` ~L181-189 | `CreatedAt` not set when creating new `UserMicrosoftToken`. EF has no `HasDefaultValueSql` or `ValueGeneratedOnAdd` for this column. MySQL's `DEFAULT CURRENT_TIMESTAMP(6)` is **overridden** by EF's explicit INSERT with `DateTime.MinValue` (0001-01-01). Every new token row will have `CreatedAt = 0001-01-01 00:00:00`. | Fix A (preferred): Add `CreatedAt = DateTime.UtcNow` to the object initializer in `ExchangeCodeAsync`. Fix B: Add `.HasDefaultValueSql("CURRENT_TIMESTAMP(6)").ValueGeneratedOnAdd()` to the EF config AND add `.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore)` to prevent EF from sending the default value column in INSERT. Fix A is 1 line — do that. |
| 2 | **Important** | `Program.cs` `/auth/ms-callback` endpoint ~L218-231 | `/auth/ms-callback` is `AllowAnonymous`. It extracts `firmUserId` from the `state` parameter and exchanges the code without verifying the currently-authenticated user's `firmUserId` matches. An attacker who knows a victim's `firmUserId` can initiate their own OAuth consent flow, inject the victim's ID in the state, and have the attacker's Microsoft token stored under the victim's FIRM account. Victim then sees attacker's calendar data. | After parsing `firmUserId` from state, read the auth cookie and verify the authenticated user's `firmUserId` matches. If the user has no valid session, reject. Example: `var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme); if (!authResult.Succeeded || authResult.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value != firmUserId) return Results.Redirect("/meetings");` — adjust the claim type to match whatever claim holds the FIRM user ID. |
| 3 | Nitpick | `Data/FirmDbContext.cs` L165 | `MicrosoftEmail` missing `HasColumnType("varchar(255)")`. DB schema is `VARCHAR(255)`. EF would default to `longtext` in a migration. No runtime impact on existing schema but causes migration drift. | Add `.HasColumnType("varchar(255)")` to the `MicrosoftEmail` mapping. |
| 4 | Nitpick | `Data/FirmDbContext.cs` L164 | `ExpiresAt` missing `DATETIME(6)` precision. DB schema is `DATETIME(6)`. Sub-second precision on expiry timestamps could be lost in migrations. | Add `.HasColumnType("datetime(6)")` to the `ExpiresAt` mapping. |
| 5 | Nitpick | `Program.cs` `/auth/ms-callback` ~L254 | `ex.Message` rendered directly in HTML response. Internal exception details (including potential DB/server info) visible to users. | Replace with a generic message: `"An unexpected error occurred. Please try again."` Log the exception (already done). |

---

### Positive Observations

- **Clean upsert logic** in `ExchangeCodeAsync` — FindAsync first, update in place if exists, preserves `CreatedAt` on update. Correct.
- **`HasToken` uses `using var`** — no connection leak risk.
- **`GetValidAccessTokenAsync` token deletion on bad refresh** — removes stale token and returns null rather than cycling. `CalendarService` handles null gracefully.
- **State parsing** — `state.Split(':')` with `Length < 2` guard is appropriate. UUID keys never contain colons.
- **All `FirmMicrosoftTokenService` log lines use `[FirmMicrosoftTokenService]` prefix** — consistent with spec requirement, makes CloudWatch filtering easy.
- **`GuidFormat=None` confirmed** — correctly prevents Pomelo from auto-converting CHAR(36) to binary Guid on the main FirmDbContext connection.
- **Scope discipline** — `FaitSharedDbContext` cleanly deleted with zero live code remnants.

---

### Notes for Rhodey

1. **`Firm:MsCallbackUrl` env var is required** — The `/auth/ms-callback` endpoint reads `config["Firm:MsCallbackUrl"]` as the redirect URI for token exchange. This value **must** match the registered redirect URI in the Azure App Registration exactly. At deploy time, set this ECS task def env var to `https://firm.dev.fortressam.ai/auth/ms-callback` (or prod equivalent). Without it, the code falls back to constructing from `ctx.Request.Scheme`+`ctx.Request.Host` which may work but is brittle behind a load balancer.

2. **Same Azure App Registration as FAIT** — No new app registration or admin consent needed. `Firm__GraphClientId`, `Firm__GraphTenantId`, and `Firm__GraphClientSecret` are already in the ECS task def per Tony.

3. **`firm_dev.user_microsoft_tokens` table** — Created by ADO#1341. Confirm it exists before first deployment. Schema expected: `CHAR(36) PRIMARY KEY` on `UserId`.

4. **No FAIT dependency at runtime** — FIRM no longer reads from FAIT's DB for tokens. The only remaining cross-DB connection is `SharedKeyRingDbContext` (reads `DataProtectionKeys` from `fred_dev`/`fait_dev` for the shared auth cookie). That is correct and expected.

---

### What Needs to Fix (Tony)

**Fix 1 — CreatedAt on insert (1 line):**
In `FirmMicrosoftTokenService.cs`, in the `ExchangeCodeAsync` method, the `new UserMicrosoftToken { ... }` object initializer is missing `CreatedAt`. Add:
```csharp
var token = new UserMicrosoftToken
{
    UserId = firmUserId,
    AccessToken = accessToken,
    RefreshToken = refreshToken,
    ExpiresAt = expiresAt,
    MicrosoftEmail = email,
    CreatedAt = DateTime.UtcNow,   // ← ADD THIS
    UpdatedAt = DateTime.UtcNow
};
```

**Fix 2 — State binding in /auth/ms-callback:**
After extracting `firmUserId = stateParts[0]`, add an auth check to confirm the current session's user matches. The exact implementation depends on how the FIRM user ID is stored in the claims principal — check what claim the FIRM auth cookie populates and verify `firmUserId` against it. If it's not in claims (FIRM derives user from cookie's Entra OID then DB lookup), then look up the FirmUser by Entra OID from the current principal and verify `firmUser.Id == firmUserId`. Example skeleton:
```csharp
var firmUserId = stateParts[0];

// Verify authenticated user matches firmUserId from state
var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
if (authResult.Succeeded)
{
    var entraOid = authResult.Principal?.FindFirst("oid")?.Value
                   ?? authResult.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    if (entraOid != null)
    {
        await using var verifyDb = await dbFactory.CreateDbContextAsync();
        var currentUser = await verifyDb.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (currentUser == null || currentUser.Id != firmUserId)
            return Results.Redirect("/meetings"); // silent fail — no info disclosure
    }
}
// If AllowAnonymous flow intentionally allows unauthenticated callbacks, document why.
```
If FIRM's OAuth flow always occurs in an authenticated session (user must be logged in to click "Connect M365"), enforce auth and reject unauthenticated callbacks. If the flow is intentionally anonymous (e.g., post-OIDC redirect before session is fully established), this needs a different approach (signed state with HMAC).

---

# Review Report: ADO#1344 (cycle 2)

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `0f6d962`  
**Date:** 2026-03-29  
**Cycle:** 2 of 2

---

### Verdict: PASS

All 5 cycle 1 findings confirmed fixed. Auth gate is not bypassable. No regressions. No new blocking issues.

---

### CC Invocation

```bash
cd /home/fredw/projects/fip && cat review-brief-cycle2.md | claude --model sonnet --print --dangerously-skip-permissions
```

Brief written by Clint at: `/home/fredw/projects/fip/review-brief-cycle2.md`

---

### Fix Verification

| # | Finding | Status | Evidence |
|---|---------|--------|----------|
| 1 | `CreatedAt = DateTime.UtcNow` in `ExchangeCodeAsync` | ✅ VERIFIED | `FirmMicrosoftTokenService.cs:188` |
| 2 | `/auth/ms-callback` auth check + Entra OID → FIRM user lookup + 403 on mismatch | ✅ VERIFIED | `Program.cs:222-244` |
| 3 | `MicrosoftEmail` → `HasColumnType("varchar(255)")` | ✅ VERIFIED | `FirmDbContext.cs:165` |
| 4 | `ExpiresAt` → `HasColumnType("datetime(6)")` | ✅ VERIFIED | `FirmDbContext.cs:164` |
| 5 | `ex.Message` removed from HTML; generic message used | ✅ VERIFIED | `Program.cs:270` |

---

### Fix 2 — Auth Robustness Detail

The `/auth/ms-callback` handler now:
1. Calls `AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)` — `Program.cs:222`
2. Rejects unauthenticated callers before reaching `ExchangeCodeAsync` — `Program.cs:223-231` — the gate holds; bypass is not possible
3. Extracts OID via both `"oid"` and full URI claim names — `Program.cs:233-234`
4. Looks up FIRM user by OID (trusted, from auth token) — `Program.cs:241`
5. Returns `Results.StatusCode(403)` on null user or ID mismatch — `Program.cs:244`
6. Reuses loaded user as `firmUser` (single DB round-trip) — `Program.cs:247`

---

### Minor Observations (Non-Blocking)

**M1 — `firmUser` variable unused** (`Program.cs:247`): `var firmUser = currentUser` is declared but never referenced downstream — only `firmUserId` (string) is used. Dead variable. Harmless but worth cleanup.

**M2 — Unauthenticated response is HTTP 200** (`Program.cs:223-231`): When `authResult.Succeeded == false`, the response body says "Unauthorized" but the status code is 200, not 401. Not a security gap — the gate holds and the attacker gets no useful information — but semantically incorrect. Low priority.

**M3 — Pre-existing reflected XSS risk (not introduced in this commit):** `Program.cs:188-195` reflects the `error` and `error_description` OAuth error parameters directly into HTML without HTML-encoding. An attacker who crafts a link to `/auth/ms-callback?error=<script>...` could execute script in the victim's browser. This code predates `0f6d962` and was not modified in this PR, but the endpoint is part of this feature. Recommend a follow-up ticket.

---

### Happy Path Verification

State parsed → `authResult.Succeeded` checked → OID extracted → DB lookup by OID → `currentUser.Id == firmUserId` verified → `ExchangeCodeAsync` called → JS redirect to `/meetings`. Flows without issue.

---

### Scope Check

Changes in `0f6d962` land in exactly the three expected files: `FirmMicrosoftTokenService.cs`, `Program.cs`, `FirmDbContext.cs`. No scope creep.

---

### Sign-off

PASS. Code is ready to proceed in the pipeline. Follow-up tickets recommended for M1 (dead variable cleanup) and M3 (reflected XSS on error branch).
