# Build Report: FAIT M365 Callback Route Alias

**Task:** Add `/auth/ms-callback` route alias for Microsoft OAuth redirect URI
**Date:** 2026-03-12
**Agent:** Tony Stark (software-engineer)

---

## Summary

Fixed the 404 on Microsoft OAuth redirect by adding a second route alias `/auth/ms-callback` that points to the same handler as the existing `/auth/microsoft-callback` endpoint.

---

## Implementation

**Option Used: A (preferred — DRY)**

The existing `app.MapGet("/auth/microsoft-callback", ...)` inline lambda was refactored into a named local delegate and registered under both routes:

```csharp
// Program.cs — lines 318–387
Func<HttpContext, IDbContextFactory<AppDbContext>, IHttpClientFactory, IConfiguration, Task<IResult>> msCallbackHandler = async (ctx, dbFactory, httpFactory, config) =>
{
    // ... existing handler body unchanged ...
};

app.MapGet("/auth/microsoft-callback", msCallbackHandler);
app.MapGet("/auth/ms-callback", msCallbackHandler);
```

The handler body was not modified. It already reads `config["MicrosoftGraph:RedirectUri"]` (fixed in `9359b54`) to pass the correct redirect URI to `ExchangeCodeAsync`.

**File modified:** `src/FortressAI.Web/Program.cs`

---

## ExchangeCodeAsync Verification (`MicrosoftTokenService.cs`)

All three verification points confirmed **correct — no fixes needed**:

| Check | Status | Detail |
|-------|--------|--------|
| POSTs to correct token endpoint | ✅ Correct | `https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token` (line 61) |
| `redirect_uri` included in body | ✅ Correct | `["redirect_uri"] = redirectUri` in `FormUrlEncodedContent` dict (line 67) — value passed in from `config["MicrosoftGraph:RedirectUri"]` at call site |
| Result stored in `user_microsoft_tokens` | ✅ Correct | Upserts via `db.UserMicrosoftTokens` — updates existing record if found, inserts new otherwise (lines 119–133) |

No changes were needed to `MicrosoftTokenService.cs`.

---

## Build Result

```
cd ~/projects/fip/fait/src/FortressAI.Web && dotnet build 2>&1 | tail -3
    29 Warning(s)
    0 Error(s)
Time Elapsed 00:00:03.18
```

**✅ 0 Error(s)** — build clean. Warnings are pre-existing MudBlazor analyzer warnings unrelated to this change.

---

## Commit

```
SHA:     b1cad64
Branch:  main
Message: feat(m365): add /auth/ms-callback route alias for Microsoft OAuth redirect URI
Files:   src/FortressAI.Web/Program.cs (+6 -2)
Remote:  pushed → github.com:fwhite13/fortress-intelligence-platform.git
```

---

## Acceptance Criteria

- [x] `/auth/ms-callback` route registered and resolves to the Microsoft OAuth callback handler
- [x] `/auth/microsoft-callback` still works (backward compatible)
- [x] Handler reads `config["MicrosoftGraph:RedirectUri"]` for token exchange — matches ECS env var
- [x] `ExchangeCodeAsync` verified correct: endpoint, `redirect_uri` in body, storage in `user_microsoft_tokens`
- [x] Build: 0 errors
- [x] Committed and pushed
