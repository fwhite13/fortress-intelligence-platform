# BUILD BRIEF — ADO#2887 — FORGE KB Integration Service
**Tony Stark — BUILD cycle 2 (review fixes)**

## Review verdict: NEEDS-CHANGES
Clint found 3 issues. Fix ONLY these — no scope creep.

---

## Fix 1 (BLOCKING): `FipTokenProvider` — wrong claim name, always returns null

**File:** `Services/FipTokenProvider.cs`

The FIP shared cookie does NOT store `access_token` as a claim. The Entra bearer token is stored in the DB (`fip_dev.user_microsoft_tokens`), not in the cookie principal. Current code returns null on every call → all fip-mcp requests go out without Bearer → 401s.

**Fix:** Mirror FIRM's pattern — query `user_microsoft_tokens` table by `entraOid` from the principal:

```csharp
public class FipTokenProvider : IFipTokenProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDbContextFactory<FipDbContext> _fipDbFactory;  // FIP portal's DB context

    public FipTokenProvider(IHttpContextAccessor httpContextAccessor, IDbContextFactory<FipDbContext> fipDbFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _fipDbFactory = fipDbFactory;
    }

    public async Task<string?> GetAccessTokenAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return null;

        var entraOid = user.FindFirst("oid")?.Value
                    ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return null;

        await using var db = await _fipDbFactory.CreateDbContextAsync();
        var tokenRecord = await db.UserMicrosoftTokens
            .FirstOrDefaultAsync(t => t.EntraOid == entraOid);

        if (tokenRecord == null) return null;

        // Check expiry — if expired within 5 minutes, return null and let caller degrade gracefully
        if (tokenRecord.AccessTokenExpiry < DateTime.UtcNow.AddMinutes(5))
            return null;

        return tokenRecord.AccessToken;
    }
}
```

**Important:** Check what the actual FIP shared DB context class is called (it may be `FortressDbContext` or similar — look at FAIT v1's `fred-dev` service / `Program.cs` for how it references the token store). Use the correct type. Register `IDbContextFactory<FipDbContext>` (or whatever the type is) if it isn't already registered. The connection string for FIP's DB is available via `FORTRESS_DB_*` env vars — same ones already in FAIT v2's task def.

Also update DI registration in `Program.cs` accordingly.

---

## Fix 2 (BLOCKING): Remove Design Agent tables from `AddMcpTables` migration

**File:** `Data/Migrations/20260507125357_AddMcpTables.cs`

This migration currently creates 4 tables: `mcp_servers`, `mcp_user_tokens`, `design_agent_sessions`, `design_agent_artifacts`. The last two are owned by WI #2865 — having them here causes a double-create failure when migrations run in order.

**Fix:** Edit the migration file to remove `design_agent_sessions`, `design_agent_artifacts`, and all their associated indexes from both `Up()` and `Down()`. Leave only `mcp_servers` and `mcp_user_tokens`.

Also update the migration snapshot (`FaitV2DbContextModelSnapshot.cs`) to remove those two entity entries if they're present.

**Coordinate:** WI #2865 (Lane 2) owns those tables. Either #2865 creates them in its own migration, or — if #2865 already has them in a migration too — simply remove them from this one (no new migration needed for #2865).

---

## Fix 3 (NITPICK): Hardcoded `height: 28px` in ChatView.razor

**File:** `Components/Chat/ChatView.razor` (style block and/or C# helpers for KB pills)

Replace `height: 28px` (and any other hardcoded pixel values in that same style block) with a CSS variable. Example:

In `fortress.css`:
```css
--pill-height-sm: 28px;
```

In `ChatView.razor`:
```css
height: var(--pill-height-sm);
```

Check for any other hardcoded numeric values in that same style block while you're there.

---

## Process
1. Use CC: `CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 cat pipeline/brief-c2-2887.md | claude --model sonnet --print --dangerously-skip-permissions`
2. Verify build: `dotnet build` (0 errors, 0 warnings)
3. Pull and rebase: `git pull --rebase origin main`
4. Commit: `git add -A && git commit -m "fix(fait-v2#2887): FipTokenProvider DB lookup, strip design-agent tables from migration, CSS var"`
5. Push: `git push origin main`
6. Post ADO comment: `mcporter call devops.add_comment --args '{"project":"Fortress","id":2887,"text":"**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: Fixed FipTokenProvider (DB lookup via entraOid), removed design_agent tables from AddMcpTables migration, replaced hardcoded 28px with CSS variable. Build: SUCCEEDED."}'`
7. Write updated Build Report to `pipeline/ADO2887-BUILD-REPORT.md`
8. Reply with your Build Report so Maria can send back to Clint.
