# Review Report — ADO#1352

**[Hawkeye — REVIEW cycle 1]**
**Date:** 2026-03-29
**Reviewer:** Clint Barton (Hawkeye)
**Verdict:** NEEDS-CHANGES

---

## Spec Compliance Check

**Brief:** `/home/fredw/.openclaw/workspace/memory/projects/fip-specs/firm-calendar-auth-brief.md`

**§ Codebase Map:**
- `fip/src/.../Data/FipDbContext.cs` — ✅ created as specified
- `fip/src/.../Program.cs` — ✅ modified (FipDbContext registration, OIDC scopes, OnTokenValidated)
- `firm/src/.../Data/FipDbContext.cs` — ✅ created as specified
- `firm/src/.../Services/FipTokenService.cs` — ✅ created as specified
- `firm/src/.../Services/CalendarService.cs` — ✅ modified (FipTokenService injection)
- `firm/src/.../Components/Pages/Meetings.razor` — ✅ MS365 UI removed
- `firm/src/.../Program.cs` — ✅ modified (services swapped, hosted services commented)
- `firm/src/.../Data/FirmDbContext.cs` — ✅ CreatedBy fix applied
- `firm/src/.../Models/FirmMeeting.cs` — ✅ CreatedBy Guid fix applied
- `FirmMicrosoftTokenService.cs`, `IFirmMicrosoftTokenService.cs`, `MicrosoftTokenService.cs` — ✅ deleted

**Out of Scope:**
- ✅ No out-of-scope changes detected

**Acceptance Criteria (pre-deploy verifiable):**
- [x] No "Connect Microsoft 365" button in FIRM — ✅ Verified in Meetings.razor
- [x] `TranscriptPollingService`/`TeamsGraphService` do not appear in startup — ✅ Commented out in FIRM Program.cs
- [x] `fip_dev.user_microsoft_tokens` table created — ✅ Per build report (DDL in spec)
- [ ] Live login / CloudWatch / calendar events — ⚠️ Requires deploy (post-deploy, Natasha's scope)

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Cross-repo FipDbContext identity:**
- FIP `Data/FipDbContext.cs` ↔ FIRM `Data/FipDbContext.cs` — ✅ Identical entity mappings (except namespace). Same table name, same column names, same PK, same property types.

**Column names vs DDL:**
- `entra_oid`, `access_token`, `refresh_token`, `expires_at`, `microsoft_email`, `created_at`, `updated_at` — ✅ All match spec DDL exactly.

**No HasColumnType violations on FipDbContext:**
- ✅ No `HasColumnType("longtext")`, `HasColumnType("TEXT")`, or `HasColumnType("datetime(6)")` anywhere in either FipDbContext. MySQL/Pomelo infers correctly.

**OIDC scopes — FIP Program.cs vs spec:**
- `offline_access` — ✅ present
- `https://graph.microsoft.com/Calendars.Read` — ✅ present (full URI, not bare scope name)
- `https://graph.microsoft.com/User.Read` — ✅ present

**FirmMeeting.CreatedBy Guid fix — full chain:**
- `FirmMeeting.CreatedBy: Guid` — ✅
- `FirmDbContext.HasColumnType("char(36)")` on CreatedBy — ✅
- `MeetingService.*Async(Guid userId, ...)` — ✅ all method signatures use Guid
- `Meetings.razor: Guid.Parse(_userId)` — ✅ callers fixed

**Old token service references — fully purged:**
- `IFirmMicrosoftTokenService` — ✅ no references in any .cs/.razor file
- `FirmMicrosoftTokenService` — ✅ no references
- `MicrosoftTokenService` — ✅ no references
- Deleted service files — ✅ confirmed absent from `Services/` directory

---

## Issues Found

### Important Issues [1]

#### I1: FIRM Program.cs — `dbHost` null guard missing for FipDbContext registration
- **File:** `firm/src/FortressIntelligenceRM.Web/Program.cs` (~line 54)
- **Category:** Correctness / local dev crash
- **Issue:** FirmDbContext registration (line ~26) has a `if (!string.IsNullOrEmpty(dbHost))` guard with a fallback to a local connection string. The FipDbContext registration immediately below (~line 54) uses `Server = dbHost` **unconditionally** — `dbHost` is `null` when `FORTRESS_DB_HOST` is not set (local dev).
- **Impact:** In local dev, `MySqlConnectionStringBuilder` will receive `Server = null`, producing a broken connection string and crashing FipDbContext instantiation. No impact in production (ECS always sets `FORTRESS_DB_HOST`). Breaks local dev workflows.
- **Fix:**
  ```diff
  var fipCsb = new MySqlConnectionStringBuilder
  {
  -   Server = dbHost,
  +   Server = dbHost ?? "localhost",
      Port = uint.Parse(dbPort),
  ```

---

### Nitpicks [3]

**N1: `_calendarPendingMsg` is declared but never set — dead UI code path**
(`firm/.../Components/Pages/Meetings.razor`)
The `else if (!_calendarLoading && !string.IsNullOrEmpty(_calendarPendingMsg))` block that renders "Calendar integration pending" can never be reached — `_calendarPendingMsg` is never assigned. If a user has no token in `fip_dev` (never logged in via FIP, or token was deleted), the calendar section shows nothing with no guidance. Also, the pending message copy references "FAIT" — should say "FIP". Consider setting this in `LoadUpcomingMeetingsAsync` when `calendarMeetings` is empty, or remove the dead block entirely. Not a blocker.

**N2: Dead `using` directives in CalendarService**
(`firm/.../Services/CalendarService.cs`, lines 3-4)
`using Microsoft.EntityFrameworkCore` and `using FortressIntelligenceRM.Web.Data` survived the refactor but are unused. No runtime impact. Clean them up.

**N3: Legacy `UserMicrosoftToken` entity still in FirmDbContext**
(`firm/.../Data/FirmDbContext.cs`)
The old `firm_dev.user_microsoft_tokens` mapping (keyed by `Guid UserId`, FK to `firm_users`) is still registered with a DbSet and `OnModelCreating` block. No service touches it anymore — all token access goes through `FipDbContext/FipUserMicrosoftToken`. Dead code, no runtime harm. Tony flagged this in the build report (intentionally left for migration safety). Follow-up cleanup PR when confirmed safe.

---

## CC Review Summary

Ran Claude Code Opus adversarial review against all changed files. CC examined:
- Entity config against DDL spec
- HasColumnType violations (banned pattern from past bugs ADO#1341, ADO#1343)
- OnTokenValidated correctness: ResponseType=code/SaveTokens=true, ExpiresIn string parsing, try/catch safety
- FipTokenService refresh logic: rotating token support, failure handling, config key names
- Null safety across Program.cs FipDbContext registration
- Complete purge of old token service stack

CC surfaced 5 findings: 1 Important (confirmed real — I1 above), 4 Nitpicks (confirmed accurate). No false positives this round.

---

## What to fix (NEEDS-CHANGES)

**One required fix before PASS:**

Tony: `firm/src/FortressIntelligenceRM.Web/Program.cs` — add `?? "localhost"` fallback on the `Server = dbHost` line inside the FipDbContext `MySqlConnectionStringBuilder` block. One line. See I1 above.

The three nitpicks are optional/follow-up. Not blocking.

---

## Spec Fidelity

Architecture fully matches the spec. FIP captures token at OIDC login via `ctx.TokenEndpointResponse` (correct — works because `ResponseType="code"` + `SaveTokens=true` are already set). FIRM reads via `FipTokenService` without any FirmUser lookup. Delegated `/me/calendarview` graph call is correct. MS365 stack fully removed. `FirmMeeting.CreatedBy` Guid fix is clean end-to-end.

---

## Positive Observations

- `OnTokenValidated` try/catch is exactly right — login never fails on DB write error.
- `FipTokenService` handles rotating refresh tokens (updates refresh_token if a new one is returned) — good defensive coding.
- FipTokenService deletes the stale token record on refresh failure — correct behavior (forces re-login to seed fresh token, prevents infinite retry on a dead refresh token).
- The 5-minute expiry buffer is correct defensive margin.
- Both FipDbContext files are byte-for-byte identical in mappings — no drift between repos.

---

---

# Review Report — ADO#1352 — Cycle 2

**[Hawkeye — REVIEW cycle 2]**
**Date:** 2026-03-29
**Reviewer:** Clint Barton (Hawkeye)
**Commit reviewed:** `a1c6c2c`
**Verdict:** ✅ PASS

---

## Cycle 1 Findings — Verification Status

| # | Finding | Status |
|---|---------|--------|
| I1 | `firm/Program.cs` FipDbContext: `Server = dbHost ?? "localhost"` | ✅ FIXED — confirmed at Program.cs:56 |
| N1 | `Meetings.razor`: dead `_calendarPendingMsg` field + unreachable render block | ✅ FIXED — zero occurrences in file |
| N2 | `CalendarService.cs`: 2 unused `using` directives | ✅ FIXED — exactly 2 usings remain, both actively used (`System.Text.Json`, `System.Text.RegularExpressions`) |
| N3 | `FirmDbContext.cs`: `DbSet<UserMicrosoftToken>` + `OnModelCreating` block removed | ✅ FIXED — no `UserMicrosoftToken` references anywhere in FirmDbContext |
| N3 | `Models/UserMicrosoftToken.cs`: deleted | ✅ FIXED — file does not exist |

---

## Regression Check

**FipDbContext connection string (Program.cs lines 54–67):**
All env vars null-safe: `dbHost ?? "localhost"`, `dbPort ?? "3306"`, `dbUser ?? "fortress_mysql"`, `dbPass ?? "dev"`, `fipDbName ?? "fip_dev"`. Well-formed throughout. ✅

**FipTokenService compilation:**
Injects `IDbContextFactory<FipDbContext>` (not `FirmDbContext`). Accesses `db.UserMicrosoftTokens` → resolves to `FipDbContext.UserMicrosoftTokens` (property still present). `FirmDbContext` not referenced in this service. No compilation break. ✅

**No live references to deleted `UserMicrosoftToken`:**
Grep across `firm/src/` confirms only `FipUserMicrosoftToken` (the current class) is referenced. Deleted model has zero surviving references. ✅

---

## New Issues

None. No regressions, no new dead code, no new hardcoded values. The `FortressTenantPrefix` constant in `CalendarService.cs:11` predates this cycle and is not a new issue.

---

## Summary

All 4 cycle 1 findings correctly addressed. Connection string is now fully null-safe for local dev. Cleanup is complete — no dead model, no dead DbSet, no dead UI code, no dead imports. FipTokenService unaffected. Codebase is cleaner than before. Ready to ship.

**Cycles: 2. All clear.**
