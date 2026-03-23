# WI#972 Investigation
**Investigator:** Hawkeye (Clint Barton)  
**Date:** 2026-03-20

---

## DB State

- **OwnerUserId format:** **email** (`fred.white@fortressam.ai`) — confirmed by JOIN of tasks → opportunities
- **Total tasks in DB:** 4 (all `open`, all on opportunity `fcab7f04-bdd5-455a-aec0-3045a3733d14`)
- **Opportunity IsClosed:** `0` (open) ✓
- **Total opportunities:** 71 — **60 have empty string `''` OwnerUserId**, 11 have a real email value
- **Filter preconditions on the 4 tasks:** All green — `Status = 'open'`, correct `OwnerUserId`, `IsClosed = 0`

---

## GetUserIdAsync returns: **`qa@fortressam.ai`** (in the deployed environment)

`UserSessionService.GetUserIdAsync()` reads `preferred_username` first → returns email.

**BUT:** The ECS task definition for `famos-dev` has `FAMOS_QA_BYPASS=true` in production environment. This activates the QA middleware in `Program.cs` which **overrides all auth claims** with:

```csharp
new Claim("preferred_username", "qa@fortressam.ai")
```

So `GetUserIdAsync()` returns `"qa@fortressam.ai"` for every request — not Fred's real identity.

---

## Filter query (exact WHERE clause)

```csharp
// GetOpenTasksPagedAsync (TaskCenter.razor calls this)
db.Tasks
  .Include(t => t.Opportunity)
  .Where(t => t.Status == "open"
      && t.Opportunity.OwnerUserId == userId   // "qa@fortressam.ai" != "fred.white@fortressam.ai"
      && !t.Opportunity.IsClosed)
```

The filter is structurally correct. It fails because `userId = "qa@fortressam.ai"` but `OwnerUserId = "fred.white@fortressam.ai"`.

---

## Root Cause: **QA Bypass Active in Production ECS**

**This is not Scenario A, B, or C from the brief — it's Scenario D: QA bypass override.**

**Evidence:**
- ECS task def `famos-dev:3` has `FAMOS_QA_BYPASS=true` set as an environment variable
- `Program.cs` lines 355-379: when `FAMOS_QA_BYPASS=true`, a middleware intercepts every request and replaces the authenticated user's claims with hardcoded QA claims (`preferred_username = "qa@fortressam.ai"`)
- All 4 tasks in DB are owned by `fred.white@fortressam.ai`
- `GetUserIdAsync()` returns `qa@fortressam.ai` → no tasks returned → Task Center shows empty

**Secondary issue:** 60 of 71 opportunities have `OwnerUserId = ''` (empty string) — these were created without an owner (likely via HubSpot sync or bulk import before owner tracking). Any tasks on those 60 opps would be invisible to everyone.

---

## Fix

### Fix 1 — Remove `FAMOS_QA_BYPASS=true` from ECS task definition (IMMEDIATE — 2 min fix)

This is a deployment config change. Remove or set to `false` in the ECS task def, then redeploy.

```bash
# Via fip-deploy.sh or direct ECS task def update
# Remove: FAMOS_QA_BYPASS=true (or set to false)
```

**Result:** Real Entra auth claims flow through, `GetUserIdAsync()` returns `fred.white@fortressam.ai`, Task Center shows 4 tasks.

### Fix 2 — Add guard: never set FAMOS_QA_BYPASS in non-dev environments

In `Program.cs`, scope the QA bypass to development environments only:

```csharp
// BEFORE (current — dangerous):
if (Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")

// AFTER (safe):
if (app.Environment.IsDevelopment() && 
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
```

### Fix 3 — Backfill OwnerUserId on 60 empty-owner opportunities (data hygiene)

```sql
-- Verify scope
SELECT COUNT(*) FROM opportunities WHERE OwnerUserId = '' AND IsClosed = 0;
-- Result: ~50 open opps with no owner

-- Backfill if all Fred's
UPDATE opportunities SET OwnerUserId = 'fred.white@fortressam.ai' 
WHERE OwnerUserId = '' AND IsClosed = 0;
```

### Fix 4 — Add null/empty guard in CreateOpportunityAsync (prevents recurrence)

```csharp
// OpportunityService.cs
public async Task<Guid> CreateOpportunityAsync(string name, string ownerUserId, ...)
{
    if (string.IsNullOrWhiteSpace(ownerUserId))
        throw new ArgumentException("ownerUserId required", nameof(ownerUserId));
    ...
}
```

---

## Priority Order

| Priority | Fix | Effort | Impact |
|----------|-----|--------|--------|
| 🔴 P0 | Remove FAMOS_QA_BYPASS=true from ECS task def | 2 min | Immediately fixes Task Center |
| 🟡 P1 | Scope QA bypass to Development only (code guard) | 5 min | Prevents regression |
| 🟡 P1 | Backfill empty OwnerUserId on 60 opps | SQL | Makes all opp tasks visible |
| 🟢 P2 | CreateOpportunityAsync guard | 5 min | Prevents future data issues |

---

## Files to change

| File | Change |
|------|--------|
| ECS `famos-dev` task definition | Remove `FAMOS_QA_BYPASS=true` env var |
| `src/FamOs.Web/Program.cs` | Add `app.Environment.IsDevelopment()` guard on QA bypass |
| `src/FamOs.Web/Services/OpportunityService.cs` | Add empty guard on `ownerUserId` in `CreateOpportunityAsync` |
| DB migration | Backfill `OwnerUserId` on 60 empty-string opportunities |

---

## Additional Finding: UserSessionService Caching Bug

`UserSessionService` caches `_user` in a field:

```csharp
private ClaimsPrincipal? _user;
```

With `FAMOS_QA_BYPASS=true` removed, this is fine since the service is likely scoped. But if it's registered as Singleton (check DI registration), the first user's identity would be cached for all subsequent requests — a separate auth bug. Confirm it's registered as `Scoped`.

```bash
grep -n "UserSessionService" ~/projects/fip/famos/src/FamOs.Web/Program.cs
```
