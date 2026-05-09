# Build Report — ADO#3119 + ADO#3120 + ADO#3122

**Agent:** Tony Stark (software-engineer)
**Date:** 2026-05-09
**Commit:** `1bb5e191`
**WIs:** ADO#3119 (entra_oid backfill), ADO#3120 (chat v1 parity tracking), ADO#3122 (full chat UI v1 parity rebuild)

---

## Summary

Two bugs fixed in a single commit:

1. **ADO#3119** — Middleware added to `Program.cs` to backfill `entra_oid` on every authenticated request for existing users who have NULL in the column (pre-schema users / users who bypassed onboarding).

2. **ADO#3120 / ADO#3122** — Full chat UI v1 parity rebuild: `MessageBubble.razor` upgraded from stub to full v1-equivalent component with Markdig rendering, avatar, token meta, and `UserInitial` parameter. `ChatView.razor` updated to pass `UserInitial` and add mic button stub. `fortress.css` updated with v1-parity message structure styles.

---

## Changes by File

### `src/FortressAI.V2.Web/Program.cs`

**ADO#3119 — `entra_oid` backfill middleware**

Middleware added between `UseAuthentication()` and `UseAuthorization()`:

```csharp
// ADO#3119 — Backfill entra_oid for authenticated users (cookie consumer — no OIDC callback)
app.Use(async (context, next) =>
{
    try
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var oid = context.User.FindFirst("oid")?.Value
                   ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
            if (!string.IsNullOrEmpty(oid))
            {
                var dbFactory = context.RequestServices.GetRequiredService<IDbContextFactory<FaitV2DbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == oid);
                if (user == null)
                {
                    // Try email fallback for stale/pre-schema users
                    var email = context.User.FindFirst(ClaimTypes.Email)?.Value
                             ?? context.User.FindFirst("preferred_username")?.Value;
                    if (!string.IsNullOrEmpty(email))
                    {
                        var staleUser = await db.Users.FirstOrDefaultAsync(
                            u => u.Email == email && (u.EntraOid == null || u.EntraOid == ""));
                        if (staleUser != null)
                        {
                            staleUser.EntraOid = oid;
                            staleUser.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync();
                            logger.LogInformation("[ADO#3119] Backfilled entra_oid for user {UserId}", staleUser.Id);
                        }
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        logger?.LogWarning(ex, "[ADO#3119] entra_oid backfill failed — continuing");
    }
    await next(context);
});
```

**Design decisions:**
- Runs only when user is already authenticated (no overhead on anonymous requests)
- Fast path: if `entra_oid` already set for this OID, no DB write — just calls `next()`
- Fallback: matches on email for stale users (null/empty `entra_oid`)
- Non-fatal: all exceptions are caught + logged, request always continues
- Uses scoped `IDbContextFactory` (safe — no captured scoped service)

### `src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor`

**ADO#3120/3122 — Full v1-parity component rebuild**

Upgraded from minimal stub (plain text + line-break encoding) to full v1-equivalent:

| Feature | Before | After |
|---------|--------|-------|
| Markdown rendering | `HtmlEncode + <br/>` | Markdig `UseAdvancedExtensions()` pipeline |
| Avatar (user) | None | `UserInitial` span with `var(--color-primary)` circle |
| Avatar (assistant) | None | MudIcon Shield with `var(--color-gold)` |
| Token meta | Present but minimal | Full `message-meta` with Memory icon |
| Streaming cursor | Absent | `cursor-blink` span |
| Structure | `.message-bubble` | `.message` (v1 class — matches v1 CSS) |
| `UserInitial` param | Absent | `[Parameter] public string? UserInitial` |

### `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

- Passes `UserInitial="@_userInitial"` to `MessageBubble`
- Derives `_userInitial` from `_userDisplayName` (first char, uppercase)
- Added mic button stub (disabled, title="Voice input (coming soon)")

### `src/FortressAI.V2.Web/wwwroot/css/fortress.css`

CSS updated to match v1 message structure (`.message`, `.message-avatar`, `.message-body`, `.message-content`, `.message-user`, `.message-assistant`) which the rebuilt MessageBubble now emits.

### `src/FortressAI.V2.Web/FortressAI.V2.Web.csproj`

Added `<PackageReference Include="Markdig" Version="0.37.0" />` (same version as FAIT v1).

---

## Build

```
dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## CC Invocation

```
cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Self-Review Checklist

- [x] `entra_oid` backfill middleware is non-fatal — exception never blocks request
- [x] Backfill only runs when user has no matching `EntraOid` row — no unnecessary DB writes
- [x] Markdig version matches FAIT v1 (`0.37.0`)
- [x] `MessageBubble` uses `.message` class (v1 structure) not `.message-bubble` (stub class)
- [x] `UserInitial` gracefully defaults to `"U"` if display name is unavailable
- [x] Build passes: 0 errors, 0 warnings
- [x] No scope creep beyond the three WIs
