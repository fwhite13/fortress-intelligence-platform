# Build Report: WI844 — FIRM v1: Fix 5 Blocking Gaps

**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-17  
**CC Model:** Claude Sonnet (via `cat cc-brief-wi844.md | claude --model sonnet -p --dangerously-skip-permissions`)  
**Commit:** `dff2e61`  
**Branch:** main  
**Build Result:** ✅ SUCCESS — 0 errors, 0 new warnings  

---

## Summary

Implemented all 5 tasks from Reed's FIRM-V1-SPEC.md. The primary blocking gaps were:
1. `FaitUserId` never populated → KB push always failed with "FAIT user ID not linked"
2. Audio download returned JSON instead of triggering browser download
3. `HttpClientFactory.CreateClient()` with no base address broke Blazor push calls
4. No `firm_meeting_kb_pushes` table → no per-KB push tracking
5. No Team KB support → only personal KB was available

All 5 gaps are now fixed. Build compiled clean.

---

## Files Changed

| File | Action | Task |
|------|--------|------|
| `Services/MeetingService.cs` | Modified | T1 |
| `Controllers/MeetingsApiController.cs` | Modified | T2 |
| `Components/Pages/MeetingDetail.razor` | Modified | T3, T5C |
| `Models/FirmMeetingKbPush.cs` | **NEW** | T4 |
| `Data/FirmDbContext.cs` | Modified | T4 |
| `Data/DatabaseInitializationService.cs` | Modified | T4 |
| `Services/FirmKbService.cs` | Modified | T5A |

**Total: 1 new file + 6 modified.**

---

## Task 1: FaitUserId Resolution — MeetingService.cs ✅

**Root cause fixed:** `GetOrCreateUserAsync` never called FAIT's `resolve-user` endpoint.

**Changes:**
- Added `IConfiguration _config` and `IHttpClientFactory _httpClientFactory` to constructor
- Added null guard: `if (string.IsNullOrEmpty(user.FaitUserId))` — ONLY calls FAIT when FaitUserId is not set
- Added `ResolveFaitUserIdAsync(entraOid)` — calls `GET /api/firm/resolve-user?entraOid=...` with `X-Firm-Secret` header
- Wrapped in try/catch — best-effort, logs warning on failure, never throws to caller
- Added `ResolveFaitUserResponse` record with `[JsonPropertyName("userId")]`

**Gate check verified:**
```
Line 120: if (string.IsNullOrEmpty(user.FaitUserId))
Line 122: try
Line 124: var faitId = await ResolveFaitUserIdAsync(entraOid);
Line 133: catch (Exception ex)
```

---

## Task 2: Audio Redirect + New KB Endpoints — MeetingsApiController.cs ✅

**GetAudio fix:**
- Changed `return Ok(new { url })` → `return Redirect(url)`
- Browser now follows 302 to S3 presigned URL and downloads the audio file directly

**Old endpoints preserved with [Obsolete]:**
- `[Obsolete("Use /push-to-kb instead")]` added to `PushTranscriptToKb`
- `[Obsolete("Use /push-to-kb instead")]` added to `PushSummaryToKb`
- Both method bodies kept UNCHANGED — no 410, no gutting

**New endpoints added:**
- `POST /api/meetings/{id}/push-to-kb` — accepts `{ docType, kbScopes[] }`, validates, calls `PushDocumentAsync`
- `GET /api/meetings/{id}/kb-status` — returns `{ transcript: ["personal"], summary: [] }` etc.
- `PushToKbRequest` record: `(string DocType, List<string> KbScopes)`

**Gate check verified:**
```
Line 343: return Redirect(url);
Line 346: [Obsolete("Use /push-to-kb instead")]
Line 372: [Obsolete("Use /push-to-kb instead")]
```

---

## Task 3: HttpClient Base Address Fix — MeetingDetail.razor ✅

**Root cause fixed:** `IHttpClientFactory.CreateClient()` returns a client with no base address; relative `/api/...` URLs fail.

**Changes:**
- Removed `@inject IHttpClientFactory HttpClientFactory`
- Added `@inject HttpClient Http` (Blazor's default registered HttpClient has base address set to the server URL)
- All `HttpClientFactory.CreateClient()` calls removed — both push methods now use `Http` directly

**Gate check verified:**
```
Line 8: @inject HttpClient Http
```
No `IHttpClientFactory` injection remaining.

---

## Task 4: Schema + Model + DbContext — firm_meeting_kb_pushes ✅

**New file: `Models/FirmMeetingKbPush.cs`**
```csharp
public class FirmMeetingKbPush
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string DocType { get; set; } = "";    // "transcript" | "summary"
    public string KbScope { get; set; } = "";   // "personal" | "team"
    public string? KbId { get; set; }
    public DateTime PushedAt { get; set; } = DateTime.UtcNow;
    public FirmMeeting? Meeting { get; set; }
}
```

**FirmDbContext.cs:**
- Added `public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();`
- Added full `OnModelCreating` config: table mapping, column names, FK to `firm_meetings`, composite index

**DatabaseInitializationService.cs:**
- Added `firm_meeting_kb_pushes` to `extraTables` array with `CREATE TABLE IF NOT EXISTS` SQL
- Schema includes: `UNIQUE KEY uq_push (meeting_id, doc_type, kb_scope)`, `INDEX idx_meeting`
- Old boolean columns (`transcript_kb_pushed`, `summary_kb_pushed`) and their ALTER TABLE statements preserved

**Gate check verified:**
```
Line 17 (DbContext): public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();
Line 115 (DbInit): ("firm_meeting_kb_pushes", @"CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes (
Line 136 (DbContext): modelBuilder.Entity<FirmMeetingKbPush>(entity =>
```

---

## Task 5: Multi-KB Service + UI ✅

### FirmKbService.cs — New methods added (existing methods preserved)

**`PushDocumentAsync`:**
- Builds document content once (transcript or summary)
- For each scope, checks `db.FirmMeetingKbPushes.FirstOrDefaultAsync(...)` BEFORE any S3 upload
- Personal KB: uploads to `kb-docs/personal/{faitUserId}/firm-{docType}-{id}.{ext}` + metadata.json
- Team KB: uploads to `kb-docs/team/firm/firm-{docType}-{id}.{ext}`
- Calls `StartIngestionAsync(kbId, dsId)` for the appropriate KB
- Records push in `firm_meeting_kb_pushes` table

**`GetPushedScopesAsync`:** Returns `HashSet<string>` of scopes already pushed for a doc type.

**`BuildTranscriptContentAsync`:** Assembles `[HH:mm:ss] Speaker: text` lines from DB segments.

**`BuildSummaryContentAsync`:** Assembles markdown with Overview / Key Decisions / Action Items / Follow-ups sections.

**`StartIngestionAsync`:** Triggers Bedrock ingestion; handles `ConflictException` gracefully (non-fatal).

**`PushTranscriptAsync` and `PushSummaryAsync` preserved** — still used by auto-complete FAIT integration flow.

**Gate check verified:**
```
Line 228-233: FirstOrDefaultAsync dedup check BEFORE S3
Line 286: db.FirmMeetingKbPushes.Add(...)
Line 310: db.FirmMeetingKbPushes.Where(...)
```

### MeetingDetail.razor — Multi-KB UI

**State added:**
- `_transcriptPushedTo` / `_summaryPushedTo` — `HashSet<string>` loaded from `/kb-status` on init
- `_transcriptSelectPersonal/Team` / `_summarySelectPersonal/Team` — checkbox binding

**KB status loaded in `OnInitializedAsync`** after meeting load (non-fatal try/catch).

**New UI panel** replaces old two-button pattern:
- Transcript row: My KB checkbox (disabled if already pushed), Team KB checkbox, Add button
- Summary row: My KB checkbox, Team KB checkbox, Add button
- Green ✓ shown for already-pushed KBs
- `TranscriptHasSelection()` / `SummaryHasSelection()` helpers disable Add button when nothing new to push

**New push methods** call `POST /api/meetings/{id}/push-to-kb` with `{ docType, kbScopes }`.

---

## Self-Review Checklist

- [x] FaitUserId null guard fires ONLY when FaitUserId is null (line 120 confirmed)
- [x] `ResolveFaitUserIdAsync` wrapped in try/catch — never throws to caller
- [x] Dedup check in `PushDocumentAsync` runs BEFORE S3 upload (lines 228-233)
- [x] `GetAudio` returns `Redirect(url)` not JSON (line 343)
- [x] Old `push-transcript-to-kb` and `push-summary-to-kb` endpoints kept with full body + `[Obsolete]`
- [x] `firm_meeting_kb_pushes` in `extraTables` array (line 115 DatabaseInitializationService)
- [x] `FirmMeetingKbPushes` DbSet in FirmDbContext (line 17)
- [x] `IHttpClientFactory` removed from MeetingDetail.razor; `HttpClient Http` injected
- [x] No files modified outside `fip/firm/`
- [x] Build: 0 errors, 0 new warnings

---

## Gate Checks Output

```
=== ResolveFaitUserIdAsync null guard ===
120: if (string.IsNullOrEmpty(user.FaitUserId))

=== ResolveFaitUserIdAsync try/catch ===
122: try
124: var faitId = await ResolveFaitUserIdAsync(entraOid);
133: catch (Exception ex)

=== PushDocumentAsync dedup check BEFORE S3 ===
228: var existing = await db.FirmMeetingKbPushes
229:     .FirstOrDefaultAsync(p => p.MeetingId == meetingId && p.DocType == docType && p.KbScope == scope);
230: if (existing != null)
233:     continue;

=== GetAudio returns Redirect ===
343: return Redirect(url);

=== Old endpoints kept with [Obsolete] ===
346: [Obsolete("Use /push-to-kb instead")]
347: [HttpPost("/api/meetings/{id}/push-transcript-to-kb")]
372: [Obsolete("Use /push-to-kb instead")]
373: [HttpPost("/api/meetings/{id}/push-summary-to-kb")]

=== firm_meeting_kb_pushes in extraTables ===
115: ("firm_meeting_kb_pushes", @"CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes (

=== DbSet in FirmDbContext ===
17: public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();

=== HttpClient injection ===
8: @inject HttpClient Http

=== No files outside fip/firm/ ===
(clean — no output)
```

---

## Commit

```
dff2e61 WI844: FIRM v1 — fix 5 blocking gaps (FaitUserId, Team KB, audio redirect, HttpClient, kb_pushes schema)
```

9 files changed, 1476 insertions(+), 50 deletions(-)  
(includes FIRM-V1-SPEC.md and Dockerfile.debian.bak tracked alongside firm/ changes)

---

## Known Limitations (from Spec — v2 scope)

- **FAIT multi-user resolve:** `GET /api/firm/resolve-user` in FAIT still has single-user workaround. Works for v1 single-tenant. Fix is a FAIT-side `AppUser` change.
- **Multiple team KBs:** Schema supports multiple team KBs (kb_id stored), but UI has single Team KB toggle. v2.
- **Scheduled meetings:** `MeetingStatus.Scheduled` enum exists; scheduled join UI is v2.

---

**Clint: ready for review. Focus areas per Reed's spec: null guard, dedup-before-S3, Redirect on audio, [Obsolete] bodies intact.**
