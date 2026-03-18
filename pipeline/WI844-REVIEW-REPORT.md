# Review Report: WI844
## Verdict: PASS
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web
cat ~/projects/fait-for-excel/review-brief-wi844.md | claude --model sonnet -p
```

First 20 lines of output:
```
# Code Review: WI844 — FIRM v1 Gap Fixes (`dff2e61`)
**Reviewer:** Hawkeye (Clint Barton)

---

## CHECK 1: ResolveFaitUserIdAsync guard — MeetingService.cs

**1a)** ✅ PASS — `if (string.IsNullOrEmpty(user.FaitUserId))` at line 120. Correct guard, not running on every login.

**1b)** ✅ PASS — The entire `try/catch` block including the `ResolveFaitUserIdAsync` call is nested inside the `if` block (lines 121–136). Early exit by virtue of the `if` — if `FaitUserId` is populated the block is skipped entirely.

**1c)** ✅ PASS — `try/catch (Exception ex)` at lines 122–136. On failure: `_logger.LogWarning(...)`. Does NOT rethrow. Warning message explicitly mentions KB push unavailability.

**1d)** ✅ PASS — `ResolveFaitUserIdAsync` reads `_config["Firm:SharedSecret"]`, early-returns null if empty. Adds it as `request.Headers.Add("X-Firm-Secret", sharedSecret)`.
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| FaitUserId null guard — only fires when empty/null | ✅ | `MeetingService.cs` line 120: `if (string.IsNullOrEmpty(user.FaitUserId))` |
| ResolveFaitUserIdAsync in try/catch, never throws | ✅ | `MeetingService.cs` lines 122–136: `catch (Exception ex)` → `LogWarning`, no rethrow |
| Firm:SharedSecret in resolve-user HTTP call | ✅ | `MeetingService.cs` `ResolveFaitUserIdAsync`: `request.Headers.Add("X-Firm-Secret", sharedSecret)` |
| PushDocumentAsync — FirstOrDefaultAsync BEFORE S3 | ✅ | `FirmKbService.cs`: `FirstOrDefaultAsync` on `FirmMeetingKbPushes` is first op in loop, before `PutObjectAsync`; comment confirms "DEDUP CHECK FIRST" |
| Dedup hit → skip S3 + Bedrock (continue/return) | ✅ | `FirmKbService.cs`: `if (existing != null)` → log → `continue`; S3/Bedrock path unreachable |
| GetAudio returns Redirect(url) not Ok({url}) | ✅ | `MeetingsApiController.cs` `GetAudio`: `return Redirect(url)` |
| Presigned URL has ResponseContentDisposition | ❌ | `S3Service.cs` `GeneratePresignedUrlAsync`: `GetPreSignedUrlRequest` has no `ResponseContentDisposition` — audio URL won't set download filename |
| push-transcript-to-kb has [Obsolete] + full body | ✅ | `MeetingsApiController.cs` line 346: `[Obsolete("Use /push-to-kb instead")]`; full body calls `PushTranscriptAsync` with try/catch |
| push-summary-to-kb has [Obsolete] + full body | ✅ | `MeetingsApiController.cs` line 372: `[Obsolete("Use /push-to-kb instead")]`; full body calls `PushSummaryAsync` with try/catch |
| firm_meeting_kb_pushes in extraTables | ✅ | `DatabaseInitializationService.cs` lines 115–124: entry present in `extraTables` array |
| extraTables SQL has UNIQUE KEY on (meeting_id, doc_type, kb_scope) | ✅ | DDL line 122: `UNIQUE KEY uq_push (meeting_id, doc_type, kb_scope)` |
| DbSet\<FirmMeetingKbPush\> in FirmDbContext | ✅ | `FirmDbContext.cs` line 17: `public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();` |
| MeetingDetail uses HttpClient not IHttpClientFactory | ✅ | `MeetingDetail.razor` line 8: `@inject HttpClient Http`; calls use `Http.GetFromJsonAsync` / `Http.PostAsJsonAsync` with relative paths |
| No files outside fip/firm/ | ✅ | Commit stats: 9 files, all under `firm/` — zero touches to fait/, cowork/, firm-vpbot/, shared/ |

---

## Issues Found

### ❌ Important — Missing `ResponseContentDisposition` in presigned URL

**File:** `Services/S3Service.cs`, `GeneratePresignedUrlAsync` method  
**Issue:** `GetPreSignedUrlRequest` does not set `ResponseHeaderOverrides.ContentDisposition` (e.g., `attachment; filename="recording.mp4"`). The audio redirect will work, but the browser will not receive a suggested download filename. On some browsers/clients the URL path component (the S3 key) becomes the default filename, which is a UUID/path slug — confusing UX.  
**Not a functional blocker for v1.** Audio download/redirect works correctly.  
**Fix:** Add `ResponseHeaderOverrides = new ResponseHeaderOverrides { ContentDisposition = $"attachment; filename=\"recording.mp4\"" }` to the `GetPreSignedUrlRequest` in `S3Service.GeneratePresignedUrlAsync`, or pass a filename parameter.

### ⚠️ Nitpick — EF Core `HasIndex` on `FirmMeetingKbPush` missing `.IsUnique()`

**File:** `Data/FirmDbContext.cs` line 151  
**Issue:** `.HasIndex(e => new { e.MeetingId, e.DocType, e.KbScope }).HasDatabaseName("idx_fmkp_lookup")` — no `.IsUnique()` call. The raw SQL DDL in `DatabaseInitializationService` correctly has `UNIQUE KEY uq_push (meeting_id, doc_type, kb_scope)`, so the runtime constraint is correct. However, if EF Core migrations are ever generated, the index will be non-unique — diverging silently from the actual schema intent.  
**Not a blocker.** The `DatabaseInitializationService` raw DDL approach is authoritative for this project.  
**Fix:** Add `.IsUnique()` to the `HasIndex` call so EF config is self-consistent.

---

## Verdict

**PASS** — All functional priority checks pass. Commit `dff2e61` correctly implements:
- `FaitUserId` null guard fires only when empty/null, wrapped in try/catch, never throws, includes `Firm:SharedSecret` header
- `PushDocumentAsync` dedup check against `firm_meeting_kb_pushes` occurs BEFORE S3 upload, using the new table (not legacy booleans)
- `GetAudio` returns `Redirect(url)` with auth/ownership enforcement
- Legacy KB endpoints marked `[Obsolete]` with full working bodies preserved
- `firm_meeting_kb_pushes` table in `extraTables` with raw DDL including UNIQUE KEY
- `DbSet<FirmMeetingKbPush>` wired in `FirmDbContext` with correct table mapping
- `MeetingDetail.razor` uses `@inject HttpClient Http` with relative-path API calls
- No files outside `fip/firm/` touched

Two non-blocking follow-up items logged (Important: missing ContentDisposition in presigned URL; Nitpick: EF index missing `.IsUnique()`). Neither blocks v1 deployment.
