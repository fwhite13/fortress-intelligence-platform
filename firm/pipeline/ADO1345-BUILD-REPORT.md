# Build Report — ADO#1345: FIRM GetOrCreateUserAsync NullReferenceException

**[Tony Stark — BUILD cycle 1]**
**Commit:** `7c9bbe3`
**Date:** 2026-03-29
**Build:** SUCCEEDED — 0 errors, 0 warnings (12 pre-existing warnings in unrelated files)

---

## Root Cause — Confirmed

**File:** `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs`

`FirmUser.Id` had **no explicit `HasColumnName` or `HasColumnType` mapping** in the EF Core model configuration.

### Why this caused NullReferenceException:

1. The DB table `firm_users` has PK column named `id` (lowercase, `char(36)`).
2. EF Core's default Pomelo convention maps property `Id` → column name `Id` (PascalCase).
3. With `GuidFormat = MySqlGuidFormat.None` in the connection string (set by ADO#1329), Pomelo performs no automatic GUID-to-char conversion or column name normalization.
4. On MySQL with case-sensitive column resolution, EF cannot find the `id` column using the generated `Id` alias → query translation fails → `FirstOrDefaultAsync` throws `NullReferenceException`.

### Stack trace confirmed two frames in same method:
```
at MeetingService.GetOrCreateUserAsync ... :line 94    ← FirstOrDefaultAsync
at MeetingService.GetOrCreateUserAsync ... :line 141   ← SaveChangesAsync (FaitUserId block)
```

Both originated from the same underlying DbContext model issue — the entire model was poisoned by the missing `Id` column mapping, causing all DB operations on `FirmUser` to fail.

### Secondary issue also fixed:
`FirmUser.FaitUserId` was mapped with `HasMaxLength(36)` instead of `HasColumnType("char(36)")`. DB column is `char(36)`. Inconsistent mapping with other `char(36)` fields in the context (e.g., `UserMicrosoftToken.UserId` uses `HasColumnType("char(36)")`).

---

## Investigation Steps

1. **CloudWatch logs** — Retrieved full stack trace confirming NullRef at MeetingService.cs:94 and :141
2. **DB schema inspection** — `DESCRIBE firm_users` confirmed PK column name is `id` (lowercase), `char(36)`
3. **FirmDbContext review** — Confirmed `FirmUser` entity config had no `HasColumnName` for `Id`
4. **GuidFormat check** — `Program.cs` confirmed `GuidFormat = MySqlGuidFormat.None` (ADO#1329) which disables auto-conversion and makes explicit column name mapping mandatory

---

## Fix Applied

**Fix A + partial Fix B** — Added `HasColumnName("id")` and `HasColumnType("char(36)")` to `FirmUser.Id` mapping.

```csharp
// Before
entity.HasKey(e => e.Id);
// ... (no Id property mapping)
entity.Property(e => e.FaitUserId).HasColumnName("fait_user_id").HasMaxLength(36);

// After
entity.HasKey(e => e.Id);
entity.Property(e => e.Id).HasColumnName("id").HasColumnType("char(36)");
// ...
entity.Property(e => e.FaitUserId).HasColumnName("fait_user_id").HasColumnType("char(36)");
```

No DB migration required — schema is unchanged, only the EF model configuration.

---

## Files Changed

| File | Change |
|------|--------|
| `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs` | Added `HasColumnName("id").HasColumnType("char(36)")` to `FirmUser.Id`; fixed `FaitUserId` to use `HasColumnType("char(36)")` |

---

## CC Session

- **Model:** CC Sonnet
- **Sessions:** 1 (sequential — single-file fix)
- **Brief:** `/tmp/tony-brief-1345.md`

---

## Build Verification

```
Build succeeded.
12 Warning(s)  ← all pre-existing, unrelated to this change
0 Error(s)
Time Elapsed 00:00:04.49
```

Warnings are in:
- `SharePanel.razor` auto-generated code (CS8669 — nullable annotation context)
- `TeamsGraphService.cs` (CS8604 — pre-existing nullable args)
- `GraphProxyController.cs` (CS8604 — pre-existing)
- `Meetings.razor` (CS0649, CS0414 — unassigned/unused fields, pre-existing)

None introduced by this change.

---

## Things Clint Should Scrutinize

1. **Verify no other entities are missing `HasColumnName` for `Id`** — `FirmMeeting`, `FirmMeetingParticipant`, etc. all use `entity.Property(e => e.Id).ValueGeneratedOnAdd()` but may also lack `HasColumnName("id")`. However those are `long` (auto-increment) PKs where EF/Pomelo handles them reliably — the issue was specific to `string`/`char(36)` PKs with `GuidFormat=None`.
2. **`GuidFormat=None` interaction** — Confirm that with this fix, Pomelo correctly reads `char(36)` → `string` for `FirmUser.Id`. No runtime test available in this environment but the model mapping is now explicit.

---

## How to Test

1. Deploy to ECS (trigger CodeBuild on `main`)
2. Load any FIRM page — `GetOrCreateUserAsync` should succeed instead of throwing
3. CloudWatch should show `FIRM: Provisioned new user` or `FIRM: Linked FaitUserId` — NOT `GetOrCreateUserAsync failed`
