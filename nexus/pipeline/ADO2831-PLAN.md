# BUILD Plan — ADO#2831
## NEXUS: Assign NexusAdmin role to Fred White + document role assignment mechanism

**WI:** ADO#2831 | Feature #2803 | Epic #2793
**Repo:** `/home/fredw/projects/fip/nexus/`

---

## Context

NEXUS uses `User.IsInRole("NexusAdmin")` throughout (controllers, services, Razor components) but NO role claims are ever injected into the shared FAIT auth cookie. The cookie only carries `NameIdentifier` (FAIT user ID) and standard Entra claims. `IsInRole("NexusAdmin")` always returns `false` for all users — Fred cannot edit specs, approve, or trigger decomp.

**Root cause:** FAIT's `OnTokenValidated` does not inject `ClaimTypes.Role`. The Portal (Cognito path) does inject roles from `cognito:groups`, but NEXUS is Entra-only.

**Required fix:** Add a DB-backed role table in NEXUS + an `IClaimsTransformation` that injects role claims from DB on each authenticated request.

---

## Implementation

### 1. New entity: `NexusUserRole`

```csharp
// src/FortressNexus.Web/Models/Entities/NexusUserRole.cs
public class NexusUserRole
{
    public int Id { get; set; }
    public string UserUpn { get; set; } = "";   // e.g. fwhite@fortressaffinitygroup.com
    public string Role { get; set; } = "";       // e.g. "NexusAdmin", "NexusReviewer"
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string AssignedBy { get; set; } = "system";
}
```

### 2. Add to `NexusDbContext`

```csharp
public DbSet<NexusUserRole> NexusUserRoles => Set<NexusUserRole>();
```

Model config in `OnModelCreating`:
```csharp
modelBuilder.Entity<NexusUserRole>(entity =>
{
    entity.ToTable("nexus_user_roles");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
    entity.Property(e => e.UserUpn).HasColumnName("user_upn").HasMaxLength(200).IsRequired();
    entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(50).IsRequired();
    entity.Property(e => e.AssignedAt).HasColumnName("assigned_at").IsRequired();
    entity.Property(e => e.AssignedBy).HasColumnName("assigned_by").HasMaxLength(200).IsRequired();
    entity.HasIndex(e => new { e.UserUpn, e.Role }).IsUnique();
});
```

### 3. EF Migration

```bash
cd /home/fredw/projects/fip/nexus/src/FortressNexus.Web
dotnet ef migrations add AddNexusUserRoles --output-dir Migrations
```

### 4. `IClaimsTransformation` — inject roles from DB

```csharp
// src/FortressNexus.Web/Services/NexusClaimsTransformation.cs
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

public class NexusClaimsTransformation : IClaimsTransformation
{
    private readonly IDbContextFactory<NexusDbContext> _dbFactory;

    public NexusClaimsTransformation(IDbContextFactory<NexusDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        // Get UPN from preferred_username or email claim
        var upn = principal.FindFirst("preferred_username")?.Value
               ?? principal.FindFirst(ClaimTypes.Email)?.Value
               ?? principal.FindFirst(ClaimTypes.Upn)?.Value;

        if (string.IsNullOrEmpty(upn))
            return principal;

        // Skip if roles already injected (avoid duplicate injection on re-auth)
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role && 
            (c.Value == NexusRoles.Admin || c.Value == NexusRoles.Reviewer)))
            return principal;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var roles = await db.NexusUserRoles
            .Where(r => r.UserUpn == upn)
            .Select(r => r.Role)
            .ToListAsync();

        if (roles.Count == 0)
            return principal;

        var clone = principal.Clone();
        var identity = clone.Identity as ClaimsIdentity
                       ?? new ClaimsIdentity();
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        return clone;
    }
}
```

### 5. Register in `Program.cs`

Add after `builder.Services.AddAuthorization(...)`:
```csharp
builder.Services.AddScoped<IClaimsTransformation, NexusClaimsTransformation>();
```

Also register `IDbContextFactory<NexusDbContext>` if not already present. Check `Program.cs` — NEXUS currently uses `AddDbContext`, not `AddDbContextFactory`. Need to add:
```csharp
builder.Services.AddDbContextFactory<NexusDbContext>(options => /* same connection string */);
```

Check the existing `AddDbContext` call and mirror the options lambda for `AddDbContextFactory`.

### 6. DB Seed — Fred's NexusAdmin record

Add a `DatabaseInitializationService` call or a one-time seed in `DatabaseInitializationService.cs` to insert Fred's role if not present:

Find `DatabaseInitializationService.cs` and add after migrations:
```csharp
// Seed NexusAdmin role for Fred White
const string fredUpn = "fwhite@fortressaffinitygroup.com";
var hasAdminRole = await db.NexusUserRoles
    .AnyAsync(r => r.UserUpn == fredUpn && r.Role == NexusRoles.Admin);
if (!hasAdminRole)
{
    db.NexusUserRoles.Add(new NexusUserRole
    {
        UserUpn = fredUpn,
        Role = NexusRoles.Admin,
        AssignedAt = DateTime.UtcNow,
        AssignedBy = "system-seed"
    });
    await db.SaveChangesAsync();
    logger.LogInformation("[NEXUS] Seeded NexusAdmin role for {Upn}", fredUpn);
}
```

**Verify Fred's UPN first:** Check `UserContextService.GetUpnAsync()` — it reads `preferred_username` claim. Fred's Entra UPN should be `fwhite@fortressaffinitygroup.com`. Confirm in existing code or comments.

### 7. Documentation

Create `docs/role-assignment.md` in the nexus repo:

```markdown
# NEXUS Role Assignment

## Roles

| Role | Value | Capabilities |
|------|-------|-------------|
| NexusAdmin | `NexusAdmin` | Approve specs, trigger decomp, edit any submission, view all submissions |
| NexusReviewer | `NexusReviewer` | Edit specs (not approve), trigger decomp, view assigned submissions |

## How Roles Work

Roles are stored in the `nexus_user_roles` DB table and injected as `ClaimTypes.Role` claims on each authenticated request via `NexusClaimsTransformation`.

## Assigning a Role

### Option 1 — Direct DB insert (current mechanism)
```sql
INSERT INTO nexus_user_roles (user_upn, role, assigned_at, assigned_by)
VALUES ('user@fortressaffinitygroup.com', 'NexusAdmin', NOW(), 'admin');
```

### Option 2 — DatabaseInitializationService seed (for known bootstrap users)
Add to the seed block in `DatabaseInitializationService.cs`.

### Option 3 — Admin UI (future)
A role management UI is planned for a future WI.

## User UPN

The UPN used for matching is the `preferred_username` claim from Entra (the user's email/login). For Fortress Affinity Group users this is typically `firstname.lastname@fortressaffinitygroup.com` or `fwhite@fortressaffinitygroup.com`.
```

---

## Acceptance Criteria

- [ ] `nexus_user_roles` table exists in DB after migration
- [ ] Fred's `fwhite@fortressaffinitygroup.com` seeded as `NexusAdmin` on first startup
- [ ] `NexusClaimsTransformation` injects role claims from DB on each authenticated request
- [ ] `User.IsInRole("NexusAdmin")` returns `true` for Fred (verifiable via CloudWatch logs or `/nexus` behavior)
- [ ] `IDbContextFactory<NexusDbContext>` registered (required by ClaimsTransformation)
- [ ] `docs/role-assignment.md` created documenting the mechanism and SQL pattern
- [ ] Build compiles with 0 errors

---

## Files to create/modify

- `src/FortressNexus.Web/Models/Entities/NexusUserRole.cs` — new
- `src/FortressNexus.Web/Data/NexusDbContext.cs` — add DbSet + model config
- `src/FortressNexus.Web/Services/NexusClaimsTransformation.cs` — new
- `src/FortressNexus.Web/Program.cs` — register IClaimsTransformation + IDbContextFactory
- `src/FortressNexus.Web/Services/DatabaseInitializationService.cs` — seed Fred's role
- Migrations — new AddNexusUserRoles
- `docs/role-assignment.md` — new

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```
