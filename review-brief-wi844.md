# Review Brief: WI844 — FIRM v1 Gap Fixes

You are Hawkeye (Clint Barton), code reviewer. Review commit `dff2e61` in the FIRM web app.

**Working directory:** `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/`

## Files to review (read each carefully):
1. `Services/MeetingService.cs`
2. `Services/FirmKbService.cs`
3. `Controllers/MeetingsApiController.cs`
4. `Components/Pages/MeetingDetail.razor`
5. `Data/FirmDbContext.cs`
6. `Data/DatabaseInitializationService.cs`
7. `Models/FirmMeetingKbPush.cs`

---

## CHECK 1: ResolveFaitUserIdAsync guard — MeetingService.cs

In `GetOrCreateUserAsync`, find the section that calls `ResolveFaitUserIdAsync`. Confirm:

**a)** The guard is `if (string.IsNullOrEmpty(user.FaitUserId))` — NOT running on every login, only when null/empty.

**b)** If `FaitUserId` is populated, the method returns early (i.e., the `ResolveFaitUserIdAsync` code block is inside the `if` check, not outside it).

**c)** The `ResolveFaitUserIdAsync` call is inside a `try/catch` block. On failure it logs a **warning** and does NOT rethrow. The warning message should mention KB push being unavailable.

**d)** In `ResolveFaitUserIdAsync`, check the HTTP call to FAIT's `/api/firm/resolve-user`. Confirm the `Firm:SharedSecret` config value is read and added as a request header (e.g., `X-Firm-Secret`).

---

## CHECK 2: PushDocumentAsync dedup — FirmKbService.cs

In `PushDocumentAsync`, verify the EXACT sequence of operations inside the `foreach (var scope in scopeList)` loop:

**a)** `FirstOrDefaultAsync` query against `db.FirmMeetingKbPushes` on `(meetingId, docType, kbScope)` is called BEFORE any S3 upload.

**b)** If `existing != null` (dedup hit) → logs info message, calls `continue` → NO S3 upload, NO Bedrock ingestion happens.

**c)** S3 `PutObjectAsync` only happens AFTER the dedup check passes (i.e., when `existing == null`).

**d)** The dedup logic uses `FirmMeetingKbPushes` table, NOT the boolean `TranscriptKbPushed`/`SummaryKbPushed` columns on the meeting entity.

---

## CHECK 3: GetAudio return type — MeetingsApiController.cs

In the `GetAudio` method:

**a)** Confirm it returns `return Redirect(url)` — NOT `return Ok(new { url = ... })` or `return Json(...)`.

**b)** Check `S3Service.GeneratePresignedUrlAsync`. Does it include `ResponseContentDisposition` (e.g., `Content-Disposition: attachment; filename="recording.mp4"`) in the `GetPreSignedUrlRequest`?

**c)** Auth + ownership check: does the method call `ResolveOwnedMeeting` before doing anything else?

---

## CHECK 4: Obsolete endpoints — MeetingsApiController.cs

**a)** Does `push-transcript-to-kb` endpoint have `[Obsolete("...")]` attribute immediately before (or together with) its `[HttpPost]` attribute?

**b)** Does `push-summary-to-kb` endpoint have `[Obsolete("...")]` attribute?

**c)** Both methods: are their FULL working bodies intact? (Check that they actually call `_firmKbService.PushTranscriptAsync` / `PushSummaryAsync`, have proper auth checks, proper error handling — NOT gutted/empty/returning 410.)

**d)** Does the new `push-to-kb` endpoint exist alongside the old ones?

---

## CHECK 5: firm_meeting_kb_pushes in extraTables — DatabaseInitializationService.cs

**a)** Is `("firm_meeting_kb_pushes", @"CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes ...")` in the `extraTables` array?

**b)** Does the SQL include at minimum: `id`, `meeting_id`, `doc_type`, `kb_scope`, `pushed_at`?

**c)** Does the SQL have a `UNIQUE KEY` on `(meeting_id, doc_type, kb_scope)`?

**d)** The service uses `ExecuteSqlRawAsync` directly — NOT relying solely on EF Core `CreateTablesAsync`.

---

## CHECK 6: FirmDbContext — FirmDbContext.cs

**a)** Does `DbSet<FirmMeetingKbPush> FirmMeetingKbPushes` property exist?

**b)** Is `FirmMeetingKbPush` configured with `entity.ToTable("firm_meeting_kb_pushes")` in `OnModelCreating`? (Or does the model have a `[Table]` attribute?)

---

## CHECK 7: MeetingDetail.razor — HttpClient injection

**a)** Is `@inject HttpClient Http` used (NOT `@inject IHttpClientFactory`)?

**b)** Are API calls using `Http.PostAsJsonAsync(...)` or `Http.GetFromJsonAsync(...)` with relative paths (e.g., `/api/meetings/{Id}/push-to-kb`)?

---

## CHECK 8: No files outside fip/firm/ modified

The commit stats show:
- `firm/Dockerfile.debian.bak` — new file (backup, inside fip/firm/)
- `firm/FIRM-V1-SPEC.md` — new file (spec, inside fip/firm/)
- All modified .cs/.razor files inside `firm/src/FortressIntelligenceRM.Web/`

Confirm: zero changes to `fip/fait/`, `fip/cowork/`, `fip/firm-vpbot/`, `fip/shared/`.

---

## ADDITIONAL CONCERN: EF Core index on FirmMeetingKbPush

In `FirmDbContext.cs`, the `HasIndex(e => new { e.MeetingId, e.DocType, e.KbScope })` for `FirmMeetingKbPush` — is it marked as `IsUnique()`? Without `.IsUnique()` in EF config, EF won't generate a UNIQUE constraint via migrations. (The raw SQL DDL in DatabaseInitializationService DOES have the UNIQUE KEY — is that sufficient?)

---

## Expected Output

For each check, state:
- ✅ PASS with line reference
- ❌ FAIL with specific issue and line reference
- ⚠️ WARN with concern but not a blocker

Overall verdict: PASS / NEEDS-CHANGES / FAIL
