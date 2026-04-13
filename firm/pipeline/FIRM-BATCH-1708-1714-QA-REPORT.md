# QA Report: FIRM Batch ADOs #1708–#1714
**Analyst:** Natasha Romanoff (Black Widow — qa-analyst)
**Image Verified:** `firm-web:83 (:latest)`
**Repo:** `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/`
**Method:** Code-verification (static analysis against HEAD)
**Date:** 2026-04-13

---

## Overall Verdict: ✅ PASS (7/7)

---

## Test Cases

### TC1 — ADO#1708: Join Now CF error (direct service injection)
**File:** `Components/Pages/JoinMeetingDialog.razor`
**Check:** No `HttpClientFactory.CreateClient("local")` — uses direct service injection.

**Result:** ✅ PASS

`JoinMeetingDialog.razor` has **zero** occurrences of `HttpClientFactory` or `CreateClient`. The dialog closes with a `DialogResult` tuple passed back to the caller; all service calls are handled upstream via injected services. No HTTP anti-pattern present.

---

### TC2 — ADO#1709: Meeting FK cascade (5 child tables ON DELETE CASCADE)
**File:** `Data/DatabaseInitializationService.cs`
**Check:** All 5 child tables have `ON DELETE CASCADE` FK constraints.

**Result:** ✅ PASS

Lines 189–193 confirm all 5 ALTER TABLE statements with `ON DELETE CASCADE`:
```
ALTER TABLE firm_meeting_participants  ADD CONSTRAINT fk_fmp_meeting_id  ... ON DELETE CASCADE
ALTER TABLE firm_meeting_transcripts   ADD CONSTRAINT fk_fmt_meeting_id  ... ON DELETE CASCADE
ALTER TABLE firm_meeting_summaries     ADD CONSTRAINT fk_fms_meeting_id  ... ON DELETE CASCADE
ALTER TABLE firm_meeting_kb_pushes     ADD CONSTRAINT fk_fmkp_meeting_id ... ON DELETE CASCADE
ALTER TABLE firm_meeting_channel_posts ADD CONSTRAINT fk_fmcp_meeting_id ... ON DELETE CASCADE
```

All 5 required tables covered. ✅

---

### TC3 — ADO#1710: UTC timezone display via JS interop
**Files:** `Components/Pages/MeetingDetail.razor`, `Components/Pages/Meetings.razor`, `wwwroot/js/firm-utils.js`
**Check:** Uses `Intl.DateTimeFormat` JS interop — NOT `DateTime.ToLocalTime()`.

**Result:** ✅ PASS

`firm-utils.js` implements `firmUtils.formatLocalDateTime`, `firmUtils.formatLocalTime`, and `firmUtils.formatLocalTimeOnly` — all using `new Intl.DateTimeFormat(undefined, ...)` which resolves to the browser's local timezone.

Blazor calls confirmed:
- `MeetingDetail.razor:303` — `JS.InvokeAsync<string>("firmUtils.formatLocalDateTime", ...)`
- `Meetings.razor:709` — `JS.InvokeAsync<string>("firmUtils.formatLocalDateTime", utcStr)`
- `Meetings.razor:721,726` — `JS.InvokeAsync<string>("firmUtils.formatLocalTime/Only", ...)`

No `DateTime.ToLocalTime()` calls found in either razor file. ✅

---

### TC4 — ADO#1711: Action items JSON deserialization (`[JsonPropertyName]` on `ActionItemDisplay`)
**File:** `Components/Pages/MeetingDetail.razor`
**Check:** `ActionItemDisplay` record has `[JsonPropertyName("description")]` and `[JsonPropertyName("owner")]`.

**Result:** ✅ PASS

Lines 409–412 of `MeetingDetail.razor`:
```csharp
private record ActionItemDisplay(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("owner")] string? Owner,
    [property: JsonPropertyName("deadline")] string? Deadline);
```

Both required attributes present, plus `deadline`. ✅

---

### TC5 — ADO#1712: Summary download missing sections
**File:** `Controllers/MeetingsApiController.cs`
**Check:** Summary download appends Key Decisions, Action Items, Follow-ups; `Owner ?? "TBD"` guard on action items.

**Result:** ✅ PASS

Lines 425–462 of the download endpoint confirm all three structured sections are appended from `KeyDecisionsJson`, `ActionItemsJson`, and `FollowUpsJson` fields:

- **Key Decisions** — `## Decisions Made` section, deserializes `KeyDecisionsJson` → `List<string>`
- **Action Items** — `## Action Items` section, deserializes `ActionItemsJson` → `List<ActionItem>`, uses `i.Owner ?? "TBD"` guard ✅
- **Follow-ups** — `## Follow-ups` section, deserializes `FollowUpsJson` → `List<string>`

Note: heading label is "Decisions Made" (not "Key Decisions") — functionally equivalent, all three sections present.

---

### TC6 — ADO#1713: KB push Blazor Server HTTP anti-pattern
**File:** `Components/Pages/MeetingDetail.razor`
**Check:** Uses `@inject FirmKbService` directly — NO `HttpClientFactory.CreateClient("local")`.

**Result:** ✅ PASS

Line 8: `@inject FirmKbService FirmKbService`

KB push calls at lines 342 and 371 use `FirmKbService.PushDocumentAsync(...)` directly. No `HttpClientFactory` or `CreateClient` anywhere in the file. ✅

---

### TC7 — ADO#1714: Org context wiki data model
**Files:** `Data/DatabaseInitializationService.cs`, `Services/OrgContextService.cs`
**Check:** `firm_org_context` table DDL and `OrgContextService.cs` exists.

**Result:** ✅ PASS

- `DatabaseInitializationService.cs:150` — `("firm_org_context", @"CREATE TABLE IF NOT EXISTS firm_org_context (...)")` DDL confirmed.
- `OrgContextService.cs` exists and implements `IOrgContextService`. Methods `GetContextAsync`, `UpsertContextAsync` confirmed. SQL references `firm_org_context` table directly (lines 82+). ✅

---

## Summary

| ADO | TC | Description | Verdict |
|-----|-----|-------------|---------|
| #1708 | TC1 | Join Now — direct service injection, no HttpClientFactory | ✅ PASS |
| #1709 | TC2 | Meeting FK — ON DELETE CASCADE on 5 child tables | ✅ PASS |
| #1710 | TC3 | UTC timezone via Intl.DateTimeFormat JS interop | ✅ PASS |
| #1711 | TC4 | ActionItemDisplay [JsonPropertyName] attributes | ✅ PASS |
| #1712 | TC5 | Summary download — all 3 sections + Owner ?? "TBD" guard | ✅ PASS |
| #1713 | TC6 | KB push — direct FirmKbService injection, no HttpClientFactory | ✅ PASS |
| #1714 | TC7 | firm_org_context DDL + OrgContextService exists | ✅ PASS |

**All 7 test cases: PASS. Batch deployment firm-web:83 verified clean.**

---

_Trust nothing. Verify everything._
