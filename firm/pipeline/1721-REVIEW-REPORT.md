# Review Report — ADO #1721
## FIRM: Remove Meeting Direct Service Injection

**Verdict: PASS**
**Cycle:** 1 | **Reviewer:** Hawkeye | **Date:** 2026-04-13
**Commit:** `e56d03d`

---

## Spec Compliance Check

**§ Codebase Map:**
- `Services/MeetingService.cs` — ✅ new `RemoveMeetingAsync` method added
- `Components/Pages/Meetings.razor` — ✅ `RemoveMeeting()` updated to use direct service call

**§ Out of Scope:**
- ✅ No out-of-scope changes detected — controller, models, interfaces untouched

**§ Acceptance Criteria:**
- [x] `RemoveMeetingAsync(long id, Guid userId)` added to MeetingService ✅
- [x] Ownership check via `GetMeetingAsync` ✅
- [x] In-progress guard present ✅
- [x] `Meetings.razor` calls service directly, not via HttpClientFactory ✅
- [x] `IHttpClientFactory` inject retained (StopRecording still works) ✅

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Status guard — MeetingService vs MeetingsApiController:**

| Status | Service blocks | Controller blocks |
|---|---|---|
| Scheduled | ✗ (permitted) | ✗ (permitted) |
| Pending | ✓ | ✓ |
| Joining | ✓ | ✓ |
| Recording | ✓ | ✓ |
| WaitingTranscript | ✓ | ✓ |
| Transcribing | ✓ | ✓ |
| Summarizing | ✓ | ✓ |
| Complete | ✗ (permitted) | ✗ (permitted) |
| Failed | ✗ (permitted) | ✗ (permitted) |

✅ **Exact match.** Service status guard is a direct copy of the controller logic.

**SQL DELETE pattern:**
✅ Identical `ExecuteSqlRawAsync("DELETE FROM firm_meetings WHERE id = {0}", id)` in both service and controller. Consistent.

---

## CC Review Summary

Ran CC adversarial review via:
```
cat /tmp/1721-review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC found 2 Important items and 0 Critical items. Both resolved below.

---

## Critical Issues — 0

None.

---

## Important Issues — 2

### I1: `_userId` null guard missing in `RemoveMeeting()`
- **File:** `Components/Pages/Meetings.razor` (~L619)
- **Issue:** `Guid.Parse(_userId!)` uses null-forgiving operator with no explicit guard. Every other call site in the component guards first: `if (string.IsNullOrEmpty(_userId)) return;`. The `!` suppresses the compiler warning but throws `ArgumentNullException` at runtime if `_userId` is null.
- **Practical risk:** Very low — `LoadMeetings()` returns early if `_userId == null`, so the meeting list is empty and the Remove button is never reachable. But it's a soft UI guarantee, not an explicit code contract.
- **CC verdict:** Flag as Important — inconsistent with component pattern, poor UX if triggered.
- **Hawkeye verdict:** ⚠️ **Does not block PASS.** Low real-world risk, but should be fixed for consistency in a follow-up. Not worth a NEEDS-CHANGES cycle on its own.
- **Recommended fix:**
  ```csharp
  private async Task RemoveMeeting(long meetingId)
  {
      if (string.IsNullOrEmpty(_userId)) return;  // add guard, matches other callers
      try
      {
          var (success, error) = await MeetingService.RemoveMeetingAsync(meetingId, Guid.Parse(_userId));
  ```

### I2: ON DELETE CASCADE — raw SQL delete relies on DB-level FKs
- **File:** `Services/MeetingService.cs`
- **Issue:** `ExecuteSqlRawAsync` bypasses EF change tracking. EF's `OnDelete(DeleteBehavior.Cascade)` in `FirmDbContext` only applies when EF tracks the entity deletion — it has no effect on raw SQL. Child table cleanup depends entirely on actual DB FK constraints.
- **Resolution:** ✅ **CONFIRMED SAFE.** `DatabaseInitializationService.cs` lines 189–193 explicitly add `ON DELETE CASCADE` to all five child tables:
  ```
  fk_fmp_meeting_id → firm_meeting_participants (CASCADE)
  fk_fmt_meeting_id → firm_meeting_transcripts (CASCADE)
  fk_fms_meeting_id → firm_meeting_summaries (CASCADE)
  fk_fmkp_meeting_id → firm_meeting_kb_pushes (CASCADE)
  fk_fmcp_meeting_id → firm_meeting_channel_posts (CASCADE)
  ```
  This was established in #1709. Raw SQL DELETE is safe.

---

## Nitpicks — 1

- **N1:** `RemoveMeetingAsync` has no XML doc comment. Other public service methods don't either — consistent, so not worth flagging.

---

## Positive Observations

- Status guard is a verbatim copy of the controller guard — zero risk of parity drift.
- Service method has **no try/catch** — correct. Exceptions propagate naturally to the Razor component's catch block, which owns user-facing error presentation. Clean separation.
- `(bool success, string? error)` tuple return is idiomatic for this codebase and avoids exception-for-flow-control.
- SQL is correctly parameterized via EF's `{0}` positional syntax (not string interpolation — this is safe).
- `IHttpClientFactory` inject untouched. `StopRecording()` at L647 still calls `HttpClientFactory.CreateClient("local")` correctly.

---

## Final Verdict

**✅ PASS**

No critical issues. Two important items: one (I1, null guard) is very low risk and doesn't block shipping; one (I2, cascade) is confirmed safe via DatabaseInitializationService. I1 should be cleaned up in a follow-up or bundled with the next PR touching Meetings.razor.

Ships as-is.
