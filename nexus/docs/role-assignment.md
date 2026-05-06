# NEXUS Role Assignment

## Overview

NEXUS uses a DB-backed role system. Roles are stored in the `nexus_user_roles` table and injected as `ClaimTypes.Role` claims on each authenticated request via `NexusClaimsTransformation : IClaimsTransformation`.

The shared FAIT auth cookie only carries `NameIdentifier` (FAIT user ID). NEXUS enriches the claims principal at runtime using the user's UPN (`preferred_username` Entra claim) to look up their assigned roles.

## Roles

| Role | Constant | Capabilities |
|------|----------|-------------|
| NexusAdmin | `NexusRoles.Admin` = `"NexusAdmin"` | Approve specs, trigger decomp, edit any submission, view all submissions |
| NexusReviewer | `NexusRoles.Reviewer` = `"NexusReviewer"` | Edit specs (not approve), trigger decomp, view assigned submissions |
| NexusUser | `NexusRoles.User` = `"NexusUser"` | Submit requests, view own submissions |

## How It Works

1. User authenticates via the shared `.FortressAI.Session` cookie (set by FAIT).
2. On each request, ASP.NET Core calls `NexusClaimsTransformation.TransformAsync`.
3. The transformation reads the `preferred_username` claim (Entra UPN).
4. It queries `nexus_user_roles` for matching rows.
5. It clones the `ClaimsPrincipal` and adds `ClaimTypes.Role` claims.
6. `User.IsInRole("NexusAdmin")` now returns `true` for Fred.

## Assigning a Role

### Option 1 — Direct DB insert (admin operations)

```sql
INSERT INTO nexus_user_roles (user_upn, role, assigned_at, assigned_by)
VALUES ('user@fortressaffinitygroup.com', 'NexusAdmin', NOW(), 'admin');
```

### Option 2 — Seed in `DatabaseInitializationService` (bootstrap users)

Add a seed block in `StartAsync` after the migration call. See Fred's seed as a reference.

### Option 3 — Admin UI (planned)

A role management UI for NexusAdmins is planned for a future work item.

## User UPN

The UPN used for role matching comes from the `preferred_username` claim (Entra — the user's login email). For Fortress Affinity Group users this is typically `firstname@fortressaffinitygroup.com` or `f.lastname@fortressaffinitygroup.com`.

## Removing a Role

```sql
DELETE FROM nexus_user_roles
WHERE user_upn = 'user@fortressaffinitygroup.com' AND role = 'NexusAdmin';
```

## Bootstrap: Fred White

Fred White (`fwhite@fortressaffinitygroup.com`) is seeded as `NexusAdmin` on first startup by `DatabaseInitializationService`. This is idempotent — the seed only runs if the row doesn't exist.
