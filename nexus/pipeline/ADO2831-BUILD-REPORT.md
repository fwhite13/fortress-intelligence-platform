# Build Report — ADO#2831
## NEXUS: DB-backed NexusUserRoles + ClaimsTransformation

**Build cycle:** 1
**Commit:** `629ed8d`
**Date:** 2026-05-06
**Build result:** ✅ SUCCEEDED — 0 errors, 1 pre-existing unrelated warning

---

## What was built

Added a DB-backed role system to NEXUS so `User.IsInRole("NexusAdmin")` works correctly.
The shared FAIT cookie only carries `NameIdentifier`; the new `NexusClaimsTransformation` queries the `nexus_user_roles` table on each authenticated request and injects `ClaimTypes.Role` claims. Fred's UPN is seeded as `NexusAdmin` on first startup.

---

## Files changed

| File | Action | Notes |
|------|--------|-------|
| `src/FortressNexus.Web/Models/Entities/NexusUserRole.cs` | **CREATE** | Entity: id, user_upn, role, assigned_at, assigned_by |
| `src/FortressNexus.Web/Data/NexusDbContext.cs` | **MODIFY** | Added `NexusUserRoles` DbSet + `nexus_user_roles` entity config (unique index on user_upn+role) |
| `src/FortressNexus.Web/Services/NexusClaimsTransformation.cs` | **CREATE** | `IClaimsTransformation` impl; reads `preferred_username` claim, queries DB, injects roles into cloned principal |
| `src/FortressNexus.Web/Program.cs` | **MODIFY** | Added `using Microsoft.AspNetCore.Authentication;`, registered `IClaimsTransformation → NexusClaimsTransformation` after AddAuthorization block |
| `src/FortressNexus.Web/Services/DatabaseInitializationService.cs` | **MODIFY** | Added seed: `fwhite@fortressaffinitygroup.com` → `NexusAdmin` if not present (idempotent) |
| `src/FortressNexus.Web/Migrations/20260506172635_AddNexusUserRoles.cs` | **CREATE** | EF migration — creates `nexus_user_roles` table |
| `src/FortressNexus.Web/Migrations/20260506172635_AddNexusUserRoles.Designer.cs` | **CREATE** | EF migration snapshot |
| `docs/role-assignment.md` | **CREATE** | Documents roles, how transformation works, SQL patterns for assigning/removing roles |

---

## Migration name

`20260506172635_AddNexusUserRoles`

---

## Parallelization used

No — all changes are sequential (each step depends on the prior entity/context changes).

---

## CC sessions run

1 CC session (Sonnet). Single-pass — all steps executed in order.

---

## Key decisions

- **`AddDbContextFactory` already registered** — `Program.cs` already had `AddDbContextFactory<NexusDbContext>`. No change needed. This was confirmed before writing the spec.
- **`IClaimsTransformation` uses `IDbContextFactory`** (not `IDbContext`) — avoids scoping issues since `IClaimsTransformation` is registered as scoped but called by the auth middleware.
- **Duplicate injection guard** — `TransformAsync` checks if any Nexus role claim already exists before querying DB, preventing double-injection on re-authentication events.
- **Migration flag** — `dotnet ef migrations add` required `--context NexusDbContext` due to multiple DbContext types in the project (`NexusDbContext` + `SharedKeyRingDbContext`).
- **Pre-existing DI warnings** — build output contains `CS2002` / scoped-from-singleton warnings for pre-existing services (`SpecGenerationService`, `DiscoveryService`). Not introduced by this WI.

---

## Acceptance criteria verification

- [x] `nexus_user_roles` table — migration `AddNexusUserRoles` creates it with unique index on `(user_upn, role)`
- [x] Fred's UPN seeded as NexusAdmin — `DatabaseInitializationService.StartAsync` seeds `fwhite@fortressaffinitygroup.com` → `NexusAdmin` idempotently
- [x] `NexusClaimsTransformation` registered — `IClaimsTransformation` → `NexusClaimsTransformation` scoped in DI
- [x] Roles injected from DB — `TransformAsync` queries `nexus_user_roles` by `preferred_username` claim, adds `ClaimTypes.Role` claims to cloned principal
- [x] `IDbContextFactory<NexusDbContext>` registered — already present, confirmed
- [x] `docs/role-assignment.md` created — includes roles table, how it works, SQL patterns, bootstrap note
- [x] Build: 0 errors

---

## How to test

1. Deploy to staging (ECS task update)
2. Log in as Fred (`fwhite@fortressaffinitygroup.com`)
3. On startup, migration runs + seed inserts row into `nexus_user_roles`
4. Navigate to any `NexusAdmin`-gated page — should render instead of redirect to `/access-denied`
5. CloudWatch: look for `[NEXUS] Seeded NexusAdmin role for fwhite@fortressaffinitygroup.com` on first deploy

To manually verify in DB:
```sql
SELECT * FROM nexus_user_roles WHERE user_upn = 'fwhite@fortressaffinitygroup.com';
```

---

## Things for Clint to scrutinize

1. **`TransformAsync` clone logic** — We clone the principal and cast `clone.Identity` as `ClaimsIdentity`. Verify the cast is safe (it will be for cookie auth, where identity is always `ClaimsIdentity`).
2. **Duplicate-injection guard** — The guard checks for `NexusRoles.Admin`, `NexusRoles.Reviewer`, or `NexusRoles.User`. If a user has none of these roles, the DB is still queried every request. For now acceptable (single SELECT by indexed column); could be cached in future.
3. **UPN claim fallback chain** — `preferred_username` → `ClaimTypes.Email` → `ClaimTypes.Upn`. If none present, transformation skips silently. This is correct behavior (unauthenticated pass-through).
