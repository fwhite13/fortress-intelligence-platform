# Review Report — ADO#1345: FIRM FirmUser.Id HasColumnName fix

**[Hawkeye — REVIEW cycle 1]**
**Date:** 2026-03-29
**Commit:** `7c9bbe3`
**Reviewer:** Clint Barton (Hawkeye)

---

## Verdict: ✅ PASS

---

## Spec Compliance Check

No formal developer brief for this targeted hotfix. Review focused on the stated fix objectives from the build report.

**Files changed:** `FirmDbContext.cs` only — +2/-1 lines. No scope creep. ✅

**Fix objectives met:**
- [x] `FirmUser.Id` has explicit `HasColumnName("id")` + `HasColumnType("char(36)")` ✅
- [x] `FaitUserId` updated from `HasMaxLength(36)` to `HasColumnType("char(36)")` ✅
- [x] No other files touched ✅

---

## Consistency Audit

All nine mapped properties in `FirmUser` verified against the entity config block:

| Property | HasColumnName | HasColumnType / HasMaxLength | Status |
|---|---|---|---|
| `Id` | `"id"` | `char(36)` | ✅ Fixed in this PR |
| `EntraOid` | `"entra_oid"` | `HasMaxLength(128)` | ✅ |
| `Email` | `"email"` | `HasMaxLength(256)` | ✅ |
| `DisplayName` | `"display_name"` | `HasMaxLength(255)` | ✅ |
| `IsActive` | `"is_active"` | — | ✅ |
| `CreatedAt` | `"created_at"` | — | ✅ |
| `UpdatedAt` | `"updated_at"` | — | ✅ |
| `LastLoginAt` | `"last_login_at"` | — | ✅ |
| `FaitUserId` | `"fait_user_id"` | `char(36)` | ✅ Fixed in this PR |
| `Meetings` | navigation property | — | ✅ |

**Nothing missing.** Every column-mapped property has an explicit `HasColumnName`. No additional NullRef landmines lurking.

---

## CC Review Summary

CC Sonnet confirmed PASS with one low-risk observation (noted below as N1). No false positives dismissed — all CC findings were directionally correct.

Key confirmations from CC:
- `ValueGeneratedNever()` is NOT needed — EF Core defaults to `ValueGeneratedNever` for `string` PKs, unlike `int`/`long`/`Guid`. The fix is complete without it.
- `IsRequired()` is NOT needed — PK properties are implicitly required.
- Long-PK entities (`FirmMeeting`, etc.) are safe without `HasColumnName("id")` — Pomelo uses `LAST_INSERT_ID()` for auto-increment PKs, bypassing column-name read. MySQL case-insensitivity covers the rest.
- `UserMicrosoftToken` PascalCase column names vs. FirmUser snake_case: intentional, different table conventions, not a problem.

---

## Critical Issues

**None.**

---

## Important Issues

**None.**

---

## Nitpicks

**N1 — FaitUserId application-layer length validation removed** (non-blocking)
- **File:** `FirmDbContext.cs`
- **Issue:** Replacing `HasMaxLength(36)` with `HasColumnType("char(36)")` removes EF Core's application-layer length guard. If something ever writes a non-GUID string to `FaitUserId`, EF won't reject it before the DB does.
- **Risk:** Effectively zero. `FaitUserId` is exclusively written from FAIT user ID lookups. DB will enforce `char(36)` anyway.
- **Action:** None required. Worth knowing.

**N2 — FirmMeeting.CreatedBy has no HasColumnType("char(36)")** (pre-existing, not in scope)
- **File:** `FirmDbContext.cs`, `FirmMeeting` entity
- **Issue:** `CreatedBy` is a `string` FK to `FirmUser.Id` (char(36)) but has no explicit `HasColumnType`. Pre-existing inconsistency, not introduced by this PR.
- **Risk:** MySQL handles implicit varchar↔char comparison fine at runtime. Not a NullRef risk.
- **Action:** Track for a future cleanup pass, not this PR.

---

## Positive Observations

- Fix placement is correct: `HasKey` → `Property(e => e.Id)` mapping is the established EF Core pattern, consistent with how `UserMicrosoftToken.UserId` is configured.
- Tony's root cause diagnosis is accurate — `GuidFormat=None` disabling Pomelo's auto-normalization, combined with MySQL result-set column name matching, is exactly why the explicit `HasColumnName("id")` is required.
- Full property scan was clean. Tony audited all mapped properties when writing the fix; nothing was missed.

---

## Acceptance Criteria Verification

| Criterion | Status |
|---|---|
| `FirmUser.Id` mapped with `HasColumnName("id")` | ✅ Verified in source |
| `FirmUser.Id` mapped with `HasColumnType("char(36)")` | ✅ Verified in source |
| `FaitUserId` uses `HasColumnType("char(36)")` instead of `HasMaxLength(36)` | ✅ Verified in source |
| All other FirmUser properties have HasColumnName (no other NullRef bombs) | ✅ Verified — full property audit clean |
| No other files modified (no scope creep) | ✅ Single file, +2/-1 lines |
| Build passes 0 errors | ✅ Per build report |

---

## Ship Recommendation

**PASS. Ready to deploy.** No changes required. The fix correctly resolves the NullReferenceException in `GetOrCreateUserAsync`. N2 is a pre-existing issue tracked for future cleanup.
