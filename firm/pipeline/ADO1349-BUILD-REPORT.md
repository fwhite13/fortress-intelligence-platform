# Build Report — ADO#1349
**[Tony Stark — BUILD cycle 1]**
**Date:** 2026-03-29
**Commit:** `29748d3`

---

## What was built

Removed conflicting `[Column(TypeName = "json")]` data annotations from `FirmMeetingSummary.cs` that were poisoning `FirmDbContext` model construction at startup, causing a cascade NullRef in `GetOrCreateUserAsync` and every other EF query.

---

## Root Cause Confirmed

`FirmMeetingSummary` had `[Column(TypeName = "json")]` data annotations on three properties **AND** `FirmDbContext.OnModelCreating` also had `.HasColumnType("JSON")` fluent config for the same three properties.

**The conflict:** Pomelo's `ElementMappingConvention` sees `[Column(TypeName = "json")]` and tries to treat those columns as JSON collections, then throws `NullReferenceException` inside `RelationalTypeMappingSource.FindCollectionMapping` during `OnModelCreating`. This poisoned the entire `FirmDbContext` model — EF never finished building it. Every subsequent query, including `Users.FirstOrDefaultAsync` in `GetOrCreateUserAsync`, failed with NullRef because the model was broken.

The fluent `HasColumnType("JSON")` in `FirmDbContext` is sufficient — Pomelo handles it correctly from there. The data annotations caused the conflict.

---

## Fix Applied

**File:** `firm/src/FortressIntelligenceRM.Web/Models/FirmMeetingSummary.cs`

**Removed (4 lines):**
```csharp
using System.ComponentModel.DataAnnotations.Schema;   // line 2 — no longer needed

[Column(TypeName = "json")]   // before ActionItemsJson
[Column(TypeName = "json")]   // before KeyDecisionsJson
[Column(TypeName = "json")]   // before FollowUpsJson
```

**Kept:**
- `using System.ComponentModel.DataAnnotations;` — still needed for `[MaxLength]`
- `[MaxLength(100)]` on `ModelUsed` — untouched
- All fluent config in `FirmDbContext.OnModelCreating` — untouched

**Self-check:**
- ✅ `ActionItemsJson`, `KeyDecisionsJson`, `FollowUpsJson` are `string?` — Pomelo stores as JSON text via fluent config
- ✅ `FirmDbContext` still has `.HasColumnType("JSON")` for all three — left in place
- ✅ No `[Column]` attributes remain on those properties — conflict eliminated

---

## CC Invocation

```bash
cat /tmp/tony-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC exit code: 0. Output confirmed all 4 lines removed correctly.

---

## Build Result

```
Build succeeded.
0 Error(s)
12 Warning(s) — all pre-existing (Razor CS8669 auto-generated, TeamsGraphService CS8604, pre-existing field warnings)
Zero warnings introduced by this change.
Time Elapsed: 00:00:04.39
```

---

## 🚨 Note for Rhodey — CloudWatch logDriver Fix (Deploy-Time)

**Do NOT skip this.** When registering the new task def revision for ADO#1349, you MUST add `awslogs` logging config to the container definition. Current task def has `logDriver: null`.

Add this to the container definition in the task def JSON:

```json
"logConfiguration": {
    "logDriver": "awslogs",
    "options": {
        "awslogs-group": "/ecs/firm-web",
        "awslogs-region": "us-east-1",
        "awslogs-stream-prefix": "ecs"
    }
}
```

This is a deploy-time config change — no code change required. Rhodey owns this step.

---

## Files Changed

| File | Change |
|------|--------|
| `firm/src/FortressIntelligenceRM.Web/Models/FirmMeetingSummary.cs` | Removed 3x `[Column(TypeName = "json")]` + removed unused `using System.ComponentModel.DataAnnotations.Schema;` |

---

## Known Edge Cases / Things Clint Should Scrutinize

- None — this is a pure annotation removal. The fluent config in `FirmDbContext` was already correct and remains untouched.
- The 12 pre-existing warnings are not in scope for this WI.

## How to Test Locally

1. `dotnet build` → 0 errors (verified ✅)
2. Run FIRM locally → `GetOrCreateUserAsync` should complete without NullRef
3. Navigate to any page that triggers user lookup — should not 500
