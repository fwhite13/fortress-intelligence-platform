# FAIT M365 Callback Route Alias — Review Report

**Commit:** `b1cad64`
**Reviewer:** Hawkeye (Clint Barton) — code-reviewer
**Review Cycle:** 1 of 2
**Date:** 2026-03-12

---

## Verdict: ✅ PASS

Small, surgical change. All checklist items verified. No issues found.

---

## Checklist Results

### 1. Both routes registered and point to the same handler?
✅ **PASS**

```csharp
app.MapGet("/auth/microsoft-callback", msCallbackHandler);
app.MapGet("/auth/ms-callback", msCallbackHandler);
```
`Program.cs` lines 386–387 confirm both routes are registered. Same delegate reference — not separate lambdas.

---

### 2. Handler defined once (Option A — no duplication)?
✅ **PASS**

`msCallbackHandler` is declared as a single `Func<>` delegate at line 318. Both `MapGet` calls reference the identical variable. Zero code duplication.

---

### 3. `config` captured correctly — `MicrosoftGraph:RedirectUri` read inside the handler?
✅ **PASS**

```csharp
Func<HttpContext, IDbContextFactory<AppDbContext>, IHttpClientFactory, IConfiguration, Task<IResult>> msCallbackHandler = async (ctx, dbFactory, httpFactory, config) =>
{
    ...
    var redirectUri = config["MicrosoftGraph:RedirectUri"]
        ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/microsoft-callback";
```

`config` is an **injected parameter** of the delegate — not a closure capture from outer scope. It is read fresh on each request invocation. The `MicrosoftGraph:RedirectUri` key is read at call time, not at registration time. Correct.

---

### 4. No accidental closure capture of mutable variables?
✅ **PASS**

The delegate captures no outer-scope variables. All variables (`code`, `state`, `error`, `redirectUri`, `token`, `email`, etc.) are declared locally within the delegate body. No shared mutable state, no race condition risk.

---

### 5. `ExchangeCodeAsync` — `redirect_uri` in token exchange body matches configured `MicrosoftGraph:RedirectUri`?
✅ **PASS**

In `Program.cs`:
```csharp
var redirectUri = config["MicrosoftGraph:RedirectUri"]
    ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/microsoft-callback";
var token = await tokenService.ExchangeCodeAsync(userId, code, redirectUri);
```

In `MicrosoftTokenService.cs` line 67:
```csharp
["redirect_uri"] = redirectUri,
```

The configured value flows through unchanged. Not hardcoded. The fallback default is the legacy path (`/auth/microsoft-callback`), which is correct for backward compatibility when config is absent.

---

### 6. Token stored via EF — upsert pattern (not blind insert)?
✅ **PASS**

`MicrosoftTokenService.ExchangeCodeAsync` (lines 120–134):
```csharp
var existing = await db.UserMicrosoftTokens.FindAsync(userId);
if (existing != null)
{
    existing.AccessToken = token.AccessToken;
    existing.RefreshToken = token.RefreshToken;
    existing.ExpiresAt = token.ExpiresAt;
    existing.MicrosoftEmail = token.MicrosoftEmail;
    existing.UpdatedAt = DateTime.UtcNow;
}
else
{
    db.UserMicrosoftTokens.Add(token);
}
await db.SaveChangesAsync();
```

FindAsync-then-update-or-insert pattern. Safe upsert. No duplicate key risk.

---

### 7. Build: 0 errors?
✅ **PASS**

```
dotnet build → 0 Error(s), 29 Warning(s) (pre-existing MUD0002 analyzer warnings, unrelated to this change)
```

---

### 8. No regressions to existing `/auth/microsoft-callback` behavior?
✅ **PASS**

The legacy route is explicitly re-registered at line 386 with the identical delegate. The handler body is unchanged from the pre-refactor lambda (verified via diff — only the outer declaration syntax changed). Behavior is functionally identical.

---

## Issues Found

None.

---

## Notes

- The fallback value in the `redirectUri` null-coalesce (`/auth/microsoft-callback`) is the legacy path, not `/auth/ms-callback`. This is intentional and correct — if `MicrosoftGraph:RedirectUri` is missing from config, the old behavior is preserved. No action needed.
- Pre-existing MUD0002 warnings (29) are unrelated to this change and tracked separately.

---

## Summary

Three-line change. Clean refactor. Both routes live. Delegate is a single reference, not a copy. Config injection is per-request. Upsert is safe. Build is clean. Nothing to send back.

**Verdict: PASS — ready to advance to SECURITY.**
