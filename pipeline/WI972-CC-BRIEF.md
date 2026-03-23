# WI972 Build Brief — Task Center fix

## Context

The Task Center is broken in production because `FAMOS_QA_BYPASS=true` is set in the ECS task definition,
which activates a QA middleware that overrides all auth claims with `qa@fortressam.ai`. Real users
(fred.white@fortressam.ai) get no tasks because the filter compares against the wrong identity.

Investigation: ~/projects/fip/pipeline/WI972-INVESTIGATION.md

## Fix 2 (P1) — Guard QA bypass middleware with IsDevelopment() AND check in Program.cs

File: `~/projects/fip/famos/src/FamOs.Web/Program.cs`

The current QA bypass block (around line 355-380) has this condition:

```csharp
// QA bypass — dev/staging only (FAMOS_QA_BYPASS=true env var required)
// MUST be after UseAuthorization() so the bypass identity is not clobbered by the cookie auth check
if (app.Environment.IsDevelopment() ||
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
```

The `||` (OR) is WRONG — it allows QA bypass to activate in production if the env var is set.
Change it to `&&` (AND) so bypass ONLY activates in Development AND the env var is set:

```csharp
// QA bypass — dev only (FAMOS_QA_BYPASS=true env var required)
// MUST be after UseAuthorization() so the bypass identity is not clobbered by the cookie auth check
if (app.Environment.IsDevelopment() &&
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
```

Also update the comment from "dev/staging only" to "dev only".

There is a SECOND occurrence of the same pattern around line 401 for /qa/login:

```csharp
if (!((app.Environment.IsDevelopment() ||
       Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true") &&
```

Change that `||` to `&&` as well:

```csharp
if (!((app.Environment.IsDevelopment() &&
       Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true") &&
```

## Fix 3 (P1) — Backfill OwnerUserId in DB init block in Program.cs

File: `~/projects/fip/famos/src/FamOs.Web/Program.cs`

60 of 71 opportunities have `OwnerUserId = ''` (empty string). Tasks on those opps are invisible
to everyone. Add a backfill SQL to the DB init block, after the existing migration statements,
before `app.Run()`.

Find the end of the DB init try/catch block (look for the logger.LogError and closing brace around
line 340), and add AFTER it (still before app.Run()):

```csharp
// WI972: Backfill OwnerUserId — empty string treated as unowned, breaks task filter
try
{
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE opportunities SET OwnerUserId = NULL WHERE OwnerUserId = ''");
    logger.LogInformation("WI972: Backfilled empty OwnerUserId to NULL");
}
catch (Exception ex)
{
    logger.LogWarning("WI972: OwnerUserId backfill skipped: {Msg}", ex.Message);
}
```

IMPORTANT: The `db` variable is available in that scope (it's an `await using var db = ...` block
that's used for the migrations). Place this new try/catch INSIDE the same scope where `db` is
accessible but AFTER the existing catch block that handles migration errors.

Look at the structure around lines 170-345 — find where `db` is declared and where it goes
out of scope, and place the new block appropriately (after migrations, before app.Run()).

## Fix 4 (P2) — Null guard in GetOpenTasksForUserAsync, GetOpenTasksPagedAsync, GetOpenTaskCountForUserAsync

File: `~/projects/fip/famos/src/FamOs.Web/Services/TaskService.cs`

There are THREE places with the same pattern that need a null guard added:

### GetOpenTasksForUserAsync (around line 26):
BEFORE:
```csharp
.Where(t => t.Status == "open"
    && t.Opportunity.OwnerUserId == userId
    && !t.Opportunity.IsClosed)
```
AFTER:
```csharp
.Where(t => t.Status == "open"
    && t.Opportunity.OwnerUserId != null
    && t.Opportunity.OwnerUserId == userId
    && !t.Opportunity.IsClosed)
```

### GetOpenTasksPagedAsync (around line 91):
BEFORE:
```csharp
.Where(t => t.Status == "open"
    && t.Opportunity.OwnerUserId == userId
    && !t.Opportunity.IsClosed);
```
AFTER:
```csharp
.Where(t => t.Status == "open"
    && t.Opportunity.OwnerUserId != null
    && t.Opportunity.OwnerUserId == userId
    && !t.Opportunity.IsClosed);
```

### GetOpenTaskCountForUserAsync (around line 120):
BEFORE:
```csharp
.Where(t => t.Status == "open"
    && t.Opportunity.OwnerUserId == userId
    && !t.Opportunity.IsClosed)
```
AFTER:
```csharp
.Where(t => t.Status == "open"
    && t.Opportunity.OwnerUserId != null
    && t.Opportunity.OwnerUserId == userId
    && !t.Opportunity.IsClosed)
```

## Summary of all changes

1. `Program.cs`: Change `||` to `&&` in QA bypass condition (~line 357) + update comment
2. `Program.cs`: Change `||` to `&&` in /qa/login guard (~line 401)
3. `Program.cs`: Add WI972 OwnerUserId backfill try/catch after migrations block
4. `TaskService.cs`: Add `t.Opportunity.OwnerUserId != null &&` to all 3 Where clauses

Do NOT touch anything else. Surgical changes only.
