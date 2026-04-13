# Review Report — ADO #1709: Remove Meeting Button Fix

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `ac9f1a5`  
**Cycle:** 1  
**Date:** 2026-04-13  

---

## Verdict: NEEDS-CHANGES

One confirmed Critical data integrity bug. Everything else passes.

---

## Spec Compliance Check

No developer brief on file for this WI — reviewed against task description and ADO WI context.

**Files changed (verified):**
- `Controllers/MeetingsApiController.cs` ✅ modified as expected
- `Components/Pages/Meetings.razor` ✅ modified as expected

**Out-of-scope changes:** None detected.

**Acceptance criteria from task description:**
- [x] Absolute route applied to DELETE endpoint ✅
- [x] Status constraint relaxed to allow Complete/Failed removal ✅
- [x] Error body surfaced in Snackbar ✅
- [x] Success toast added ✅
- [x] Remove button shown for Scheduled/Complete/Failed ✅

**Spec compliance verdict:** ✅ COMPLIANT — but one Critical correctness issue blocks PASS.

---

## Consistency Audit

**Files cross-referenced:**
- `MeetingStatus.cs` ↔ `MeetingsApiController.cs` blocked states — ✅ All 9 enum values accounted for
- `MeetingsApiController.cs` allowed states ↔ `Meetings.razor` button visibility — ✅ Exact match
- `FirmDbContext.cs` `OnDelete(Cascade)` ↔ `DatabaseInitializationService.cs` schema — ❌ EF cascade ≠ DB cascade (see Critical #1)

---

## Critical Issues [1]

### C1: Raw SQL Delete Orphans All Child Records

- **File:** `Controllers/MeetingsApiController.cs` (line 724)
- **Category:** Correctness / Data integrity
- **Issue:** The delete is issued via `ExecuteSqlRawAsync`, which bypasses EF's change tracker entirely. `OnDelete(DeleteBehavior.Cascade)` in `FirmDbContext.OnModelCreating` **only fires when EF issues deletes through its own pipeline** — not for raw SQL. For cascade to work here, the MySQL schema itself must define `ON DELETE CASCADE` on the FK constraints.

  Checked all five child tables in `DatabaseInitializationService.cs` — none define a FOREIGN KEY with ON DELETE CASCADE:

  | Child table | Schema FK? | Cascade? |
  |---|---|---|
  | `firm_meeting_participants` | INDEX only | **NO** |
  | `firm_meeting_transcripts` | INDEX only | **NO** |
  | `firm_meeting_summaries` | UNIQUE only | **NO** |
  | `firm_meeting_kb_pushes` | INDEX only | **NO** |
  | `firm_meeting_channel_posts` | INDEX only | **NO** |

  Note: `FirmDbContext.cs` line 6 explicitly states *"FIRM does NOT use EF migrations — all schema is managed by DatabaseInitializationService (raw SQL)."* This confirms EF's cascade config creates no actual DB constraints.

- **Impact:** Every successful `RemoveMeeting` call silently deletes the parent row but leaves all child records orphaned — participants, transcript segments, summaries, KB push records, and channel post records. These accumulate indefinitely with no parent, polluting the database.

- **Evidence:**
  ```csharp
  // Line 724 — raw SQL, EF cascade does NOT fire
  await db.Database.ExecuteSqlRawAsync("DELETE FROM firm_meetings WHERE id = {0}", id);
  ```

  ```csharp
  // FirmDbContext.cs — cascade configured only in EF, never reaches DB
  entity.HasOne(e => e.Meeting)
      .WithMany(m => m.Participants)
      .HasForeignKey(e => e.MeetingId)
      .OnDelete(DeleteBehavior.Cascade)   // ← only fires via EF, not raw SQL
      .HasConstraintName("fk_fmp_meeting");
  ```

  ```sql
  -- DatabaseInitializationService.cs — no FK constraint defined, only an index
  CREATE TABLE IF NOT EXISTS firm_meeting_participants (
      id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
      meeting_id BIGINT NOT NULL,
      ...
      INDEX idx_fmp_meeting (meeting_id)   -- no FOREIGN KEY, no ON DELETE CASCADE
  ) ...
  ```

- **Fix (Option A — preferred, keep raw SQL):** Add `ON DELETE CASCADE` FK constraints to child table schemas in `DatabaseInitializationService.cs`. Use ALTER TABLE statements (consistent with the existing `alterStatements` block) to add FKs to existing tables without recreating them:

  ```csharp
  // Add to alterStatements array in DatabaseInitializationService.cs:
  "ALTER TABLE firm_meeting_participants ADD CONSTRAINT fk_fmp_meeting FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE",
  "ALTER TABLE firm_meeting_transcripts ADD CONSTRAINT fk_fmt_meeting FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE",
  "ALTER TABLE firm_meeting_summaries ADD CONSTRAINT fk_fms_meeting FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE",
  "ALTER TABLE firm_meeting_kb_pushes ADD CONSTRAINT fk_fmkp_meeting FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE",
  "ALTER TABLE firm_meeting_channel_posts ADD CONSTRAINT fk_fmcp_meeting FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE"
  ```

  Wrap these in `try/catch (MySqlException ex) when (ex.Number == 1826)` to handle "duplicate FK constraint name" for idempotency (MySQL error 1826).

- **Fix (Option B — alternative, use EF):** Replace the raw SQL delete with an EF-tracked delete so the existing `OnDelete(Cascade)` config actually fires:

  ```csharp
  // Replace lines 723-724 with:
  await using var db = await _dbFactory.CreateDbContextAsync();
  var meetingToDelete = await db.Meetings.FindAsync(id);
  if (meetingToDelete != null)
  {
      db.Meetings.Remove(meetingToDelete);
      await db.SaveChangesAsync();
  }
  return NoContent();
  ```

  Note: With Option B, EF's cascade will delete child records that it knows about (Participants, Transcripts, Summaries, KbPushes), but `firm_meeting_channel_posts` is NOT registered as a DbSet in `FirmDbContext` — it would still orphan. Option A (DB-level FK cascade) is therefore more complete.

---

## Important Issues [0]

None.

---

## Nitpicks [1]

### N1: Mixed absolute/relative routes in MeetingsApiController — inconsistency

Four routes still use relative paths (`{id}/post-to-channel`, `{id}/channel-post-history`, `{id}/join`, `{id}/reprocess-summary`) while most use absolute paths. The relative routes resolve correctly via the class-level `[Route("api/meetings")]`, so this is not a bug — but the inconsistency makes it harder to audit routing at a glance. Not blocking; clean up opportunistically.

---

## Positive Observations

- **Route change:** Technically a no-op (relative `{id}` with class-level `[Route("api/meetings")]` = absolute `/api/meetings/{id}`), but the cleanup is sensible and the intent is correct. No conflicts introduced.
- **Enum coverage:** All 9 MeetingStatus values are accounted for across blocked and allowed lists. No gaps.
- **Ownership check preserved:** `GetMeetingAsync(id, firmUser.Id)` correctly scopes by `Id AND CreatedBy == userId`. No IDOR vector.
- **UI/API alignment:** Razor shows Remove button for exactly `Scheduled | Complete | Failed` — matches what the API permits. Clean.
- **Error surfacing:** Reading response body on failure is a meaningful UX improvement. `Logger.LogError` added on both failure paths — good operational hygiene.
- **LoadMeetings() order:** Correctly called before success toast. List refreshes before the user sees confirmation. ✅

---

## What To Fix (NEEDS-CHANGES)

Tony: one fix needed before this ships.

**C1 — Cascade delete orphan records**

The raw SQL `DELETE FROM firm_meetings WHERE id = {0}` does not cascade because the DB schema has no FK constraints with ON DELETE CASCADE — only plain indexes. EF's `OnDelete(DeleteBehavior.Cascade)` config is a no-op for raw SQL.

**Recommended fix (Option A):** In `DatabaseInitializationService.cs`, add FK ALTER TABLE statements to the `alterStatements` array for all five child tables. This repairs the schema on next startup and makes the raw SQL delete work correctly going forward.

See C1 above for the exact ALTER TABLE statements. Remember to wrap them in the `MySqlException` catch block with error code `1826` (duplicate FK name) for idempotency — same pattern as the existing `1060`/`1061` catches.

If the `alterStatements` block already has a try-catch pattern that only handles 1060/1061, add 1826 to the list or add a separate try-catch for FK additions.

---

_Hawkeye — REVIEW cycle 1 — 2026-04-13_

---

## Review Report — Cycle 2 — ADO #1709

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `fa68ab1`  
**Cycle:** 2 (Targeted — CASCADE FK fix)  
**Date:** 2026-04-13  

---

## Verdict: PASS

All 6 targeted checks pass. The fix is correct, complete, syntactically valid, idempotent, and scoped appropriately.

---

## Targeted Review Summary

### CHECK 1: All 5 child tables present ✅
All five FK ALTER statements are present in `alterStatements` (lines 181–185):
- `firm_meeting_participants`
- `firm_meeting_transcripts`
- `firm_meeting_summaries`
- `firm_meeting_kb_pushes`
- `firm_meeting_channel_posts`

### CHECK 2: FK column name correctness ✅
All ALTER statements use `FOREIGN KEY (meeting_id)`. Cross-referenced against each table's `CREATE TABLE` block — `meeting_id BIGINT NOT NULL` confirmed in all 5 DDL definitions. Column name consistent throughout.

### CHECK 3: Idempotency — error 1826 catch ✅
- `ex.Number == 1826` added to the catch clause
- Catch wraps `ExecuteSqlRawAsync` **inside** the `foreach` loop body — per-statement, not wrapping the whole loop
- On 1826: logs "already applied (idempotent)" and continues to next statement — correct behavior

### CHECK 4: MySQL ALTER TABLE syntax validity ✅
All 5 statements follow the correct pattern:
`ALTER TABLE <table> ADD CONSTRAINT <name> FOREIGN KEY (meeting_id) REFERENCES firm_meetings(id) ON DELETE CASCADE`

| Table | Constraint Name | Column | References | CASCADE |
|---|---|---|---|---|
| firm_meeting_participants | fk_fmp_meeting_id | meeting_id | firm_meetings(id) | ✅ |
| firm_meeting_transcripts | fk_fmt_meeting_id | meeting_id | firm_meetings(id) | ✅ |
| firm_meeting_summaries | fk_fms_meeting_id | meeting_id | firm_meetings(id) | ✅ |
| firm_meeting_kb_pushes | fk_fmkp_meeting_id | meeting_id | firm_meetings(id) | ✅ |
| firm_meeting_channel_posts | fk_fmcp_meeting_id | meeting_id | firm_meetings(id) | ✅ |

No trailing comma on last entry. Array properly closed.

### CHECK 5: Scope ✅
Fix is contained entirely in `DatabaseInitializationService.cs`. No other service files, controllers, or domain classes modified.

### CHECK 6: Regression risk ✅
5 new strings appended cleanly after the 9 existing `alterStatements` entries. No prior entries disturbed. Array termination correct.

---

## Issues Found

None. All Cycle 1 findings addressed.

---

_Hawkeye — REVIEW cycle 2 — 2026-04-13_
