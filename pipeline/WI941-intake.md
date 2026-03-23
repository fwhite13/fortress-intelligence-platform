# WI#941 — Task Center: newly added task doesn't appear after saving

**Priority:** High — core task management workflow broken
**Component:** FAMOS — Task Center / TaskService
**Repo:** fip monorepo (`fip/famos/`)

## What the User Sees
User opens Task Center, clicks "+ Add Task", fills in opportunity + title + due date, clicks "Add Task" in modal. Modal closes. Task Center shows no new task — appears as if nothing happened.

## Root Cause
Task IS saved to DB correctly (confirmed — `tasks` table has the row with Status="open").

The task doesn't appear because `GetOpenTasksForUserAsync(userId)` filters by `t.Opportunity.OwnerUserId == userId`.

The task was saved via `TaskService.CreateTaskAsync()` which does NOT check or require userId match — it just inserts. But the Task Center loads tasks via `GetOpenTasksForUserAsync(userId)` which filters by owner.

**The userId used for filtering comes from `UserSession.GetUserIdAsync()`** — if this returns an Entra object ID (GUID) but `OwnerUserId` on the opportunity is stored as an email address (`fred.white@fortressam.ai`), the filter will never match and no tasks appear.

## Fix (Tony — Clint verify first)

**Clint: check what `UserSessionService.GetUserIdAsync()` actually returns** (email vs OID vs UPN) and what format `OwnerUserId` is stored in on the `opportunities` table.

```sql
SELECT OwnerUserId FROM opportunities WHERE OwnerUserId IS NOT NULL LIMIT 5;
```

**If OwnerUserId is stored as email** and GetUserIdAsync() returns OID:
- Option A: Store OwnerUserId as OID in the DB (normalize everything to OID)
- Option B: Compare by email claim instead of OID in `GetOpenTasksForUserAsync`

**Most likely quick fix:** In `TaskService.GetOpenTasksForUserAsync`, also accept email-based match:
```csharp
.Where(t => t.Status == "open"
    && (t.Opportunity.OwnerUserId == userId || t.Opportunity.OwnerUserId == userEmail)
    && !t.Opportunity.IsClosed)
```

Or normalize OwnerUserId to always use email (preferred — consistent with current data).

## Acceptance Criteria
1. After adding a task via "+ Add Task" modal, the task appears in Task Center immediately
2. Existing tasks (created programmatically or via pipeline stage transitions) also visible to the owning user
3. Task count badge in nav reflects correct count
