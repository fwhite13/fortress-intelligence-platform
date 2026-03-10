# Code Review Report: FIRM KB Integration + FAIT Auto-Add Toggle

**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1 of 2
**Date:** 2026-03-10
**Commits:** FIRM `dd96bd9` · FAIT `d581bb5`

---

## Verdict: NEEDS-CHANGES

Two Important issues and one Nitpick. Nothing Critical. The core architecture is sound; these are fixable before cycle 2.

---

## Checklist Results

### FIRM — DB Schema

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | `FirmUser.FaitUserId` nullable `CHAR(36)`? EF maps to `fait_user_id`? | ✅ PASS | `string?` in model; `HasColumnName("fait_user_id").HasMaxLength(36)` in context. Not typed as `CHAR(36)` in EF but the ALTER TABLE DDL uses `CHAR(36) NULL` — correct. |
| 2 | `TranscriptKbPushed` / `SummaryKbPushed` bool, default false? EF maps correctly? | ✅ PASS | Both `bool` with no `= false` default in model (bool defaults to false in C# — fine), EF maps `HasDefaultValue(false)` and correct column names. |
| 3 | ALTER TABLE uses per-statement try/catch catching 1060? NOT shared? | ✅ PASS | Each ALTER is individually wrapped in its own `try/catch (MySqlException ex) when (ex.Number == 1060 \|\| ex.Number == 1061 \|\| ex.Number == 1091)`. Correct. |
| 4 | No DROP COLUMN or destructive schema changes? | ✅ PASS | Only `ADD COLUMN` statements. |

### FIRM — FirmKbService

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 5 | S3 key pattern correct? | ✅ PASS | `kb-docs/personal/{faitUserId}/firm-transcript-{meetingId}.txt` and `firm-summary-{meetingId}.md` — exact match. |
| 6 | Transcript format `[HH:mm:ss] SpeakerName: Text` from sorted rows? | ✅ PASS | `OrderBy(t => t.StartTimeMs)`, `TimeSpan.FromMilliseconds(...).ToString(@"hh\:mm\:ss")`, `[{ts}] {speaker}: {seg.Text}`. Correct. |
| 7 | Summary format: overview → key decisions → action items → follow-ups? | ✅ PASS | Sections rendered in correct order with `## Overview`, `## Key Decisions`, `## Action Items`, `## Follow-ups` headers. |
| 8 | `PutObjectAsync` uses `fortress-tools` or config `Firm:KbS3Bucket`? | ✅ PASS | `BucketName` property reads `_config["Firm:KbS3Bucket"] ?? "fortress-tools"`. |
| 9 | After S3 upload: `StartIngestionJobAsync` on `ZCEZCJGHQC` / `3X5E9L4HAC`? | ✅ PASS | `PersonalKbId` defaults to `ZCEZCJGHQC`, `PersonalDsId` to `3X5E9L4HAC`. Called via `StartPersonalIngestionAsync()` after each upload. |
| 10 | Null `faitUserId` guard: logs warning, returns without throwing? | ✅ PASS | `if (string.IsNullOrWhiteSpace(faitUserId)) { _logger.LogWarning(...); return; }` at top of both `PushTranscriptAsync` and `PushSummaryAsync`. |
| 11 | After successful push: updates `transcript_kb_pushed` / `summary_kb_pushed`? | ✅ PASS | Sets `meeting.TranscriptKbPushed = true` / `SummaryKbPushed = true` inside the success path of each method. |
| 12 | `IAmazonBedrockAgent` injected (not `IAmazonBedrockAgentRuntime`)? | ✅ PASS | Constructor takes `IAmazonBedrockAgent bedrockAgent`. `Program.cs` registers `Amazon.BedrockAgent.IAmazonBedrockAgent`. |

### FIRM — API Endpoints

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 13 | `POST push-transcript-to-kb` is `[Authorize]`? | ✅ PASS | `[HttpPost("/api/meetings/{id}/push-transcript-to-kb")] [Authorize]` — present on both push endpoints. |
| 14 | Returns 400 if meeting is not `Complete`? | ✅ PASS | `if (meeting.Status != MeetingStatus.Complete) return BadRequest(...)` |
| 15 | Returns 400 with descriptive error if `faitUserId` is null? | ✅ PASS | `if (string.IsNullOrEmpty(user.FaitUserId)) return BadRequest(new { error = "FAIT user ID not linked. Please log out and back in." })` |
| 16 | Uses `ResolveOwnedMeeting` pattern? | ⚠️ **IMPORTANT** | **No.** Both `PushTranscriptToKb` and `PushSummaryToKb` inline their own user/meeting resolution instead of calling `ResolveOwnedMeeting`. The method exists and is used correctly by the download/audio endpoints. The inline code achieves the same ownership check (`m.CreatedBy == user.Id`), but it's inconsistent with the established pattern, and any future hardening of `ResolveOwnedMeeting` won't cover these endpoints. **Must be refactored to use `ResolveOwnedMeeting` for consistency.** |
| 17 | Fire-and-forget FAIT notification uses `IHttpClientFactory`, not `new HttpClient()`? Wrapped in try/catch? | ✅ PASS | `_httpClientFactory.CreateClient()` — correct. The entire `Task.Run` body is wrapped in `try/catch (Exception ex) { _logger.LogWarning(...) }`. Cannot break the callback response. |

### FIRM — MeetingDetail.razor

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 18 | KB buttons only shown when `Status == Complete`? | ✅ PASS | Both KB buttons are inside `@if (_meeting.Status == MeetingStatus.Complete)` block. |
| 19 | Disabled when already pushed? | ✅ PASS | `Disabled="_pushingTranscript \|\| _meeting.TranscriptKbPushed"` / `_pushingSummary \|\| _meeting.SummaryKbPushed`. |
| 20 | Shows green "In My KB" state when pushed? | ✅ PASS | `Style="@(_meeting.TranscriptKbPushed ? "border-color: #4caf50; color: #4caf50;" : ...")"` and label switches to `"In My KB"`. |
| 21 | Uses `HttpClient` to call push endpoints (not direct service injection)? | ⚠️ **IMPORTANT** | **Uses `new HttpClient()` directly** — `var http = new HttpClient()` — in both `PushTranscriptToKb()` and `PushSummaryToKb()`. This is a textbook HttpClient misuse (socket exhaustion risk, no shared configuration, no lifecycle management). In a Blazor Server context this is particularly bad — each button click allocates a new socket. **Must be fixed: inject `IHttpClientFactory` via `@inject` and call `.CreateClient()`** — same pattern used in `VpCallback`. |
| 22 | Loading state prevents double-submission? | ✅ PASS | `if (_meeting == null \|\| _pushingTranscript) return;` guard at start, `_pushingTranscript = true` set before await, reset in `finally`. |

### FAIT — Schema

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 23 | `UserAssistantConfig` has `FirmAutoTranscript` / `FirmAutoSummary` with `= false` defaults? | ✅ PASS | Both present with `= false` in model. |
| 24 | `AppDbContext` maps to `firm_auto_transcript` / `firm_auto_summary`? | ✅ PASS | `HasColumnName("firm_auto_transcript").HasDefaultValue(false)` and same for summary. |
| 25 | FAIT DatabaseInitializationService ALTER TABLE per-statement try/catch with 1060 AND 1091? | ✅ PASS | The `firm_auto_transcript` and `firm_auto_summary` ALTER statements are in the shared `alterStatements` array, each wrapped individually in `catch (MySqlConnector.MySqlException ex) when (ex.Number == 1060 \|\| ex.Number == 1061 \|\| ex.Number == 1091)`. Per-statement loop — correct. |

### FAIT — FirmIntegrationController

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 26 | `GET resolve-user` protected by loopback IP check (NOT `[Authorize]`)? | ✅ PASS | `IPAddress.IsLoopback(remoteIp)` check, returns 403 for non-loopback. No `[Authorize]` attribute — correct for M2M. |
| 27 | `POST meeting-complete` validates `X-Firm-Secret`? Returns 401 on mismatch? | ✅ PASS | Reads `_config["Firm:SharedSecret"]`, compares to `Request.Headers["X-Firm-Secret"]`. Returns `Unauthorized(...)` if empty or mismatch. Fail-closed: empty config blocks all requests. |
| 28 | Auto-push only fires if `FirmAutoTranscript` / `FirmAutoSummary` is true? | ✅ PASS | `if (config.FirmAutoTranscript && !string.IsNullOrWhiteSpace(payload.TranscriptText))` — gated on toggle. |
| 29 | Uses `KbDocumentService` (not duplicating S3 logic)? | ⚠️ **NITPICK / ADVISORY** | `FirmIntegrationController` does **not** use `KbDocumentService` — it directly injects `IAmazonS3` and `IAmazonBedrockAgent` and reimplements the upload+ingestion pattern in a private `UploadToKbAsync` method. The checklist requirement says "must reuse existing KbDocumentService." However, the FAIT controller **does** add metadata file companion upload (`{s3Key}.metadata.json` with `ownerId` attribute) which the FIRM-side `FirmKbService` does not do. So this isn't pure duplication — there's a substantive reason for the separation. That said, if `KbDocumentService` is the canonical KB upload path, long-term maintenance will diverge. **Recommend:** confirm with the team whether `KbDocumentService` supports per-user personal KB uploads with metadata. If yes, refactor to use it. If no (or if it's in a different service scope), document the deliberate deviation. Blocking this as a hard fail is not warranted — calling it Important-Advisory rather than Critical. |
| 30 | Returns 200 OK even if auto-push fails? | ✅ PASS | `UploadToKbAsync` throws on failure, but `MeetingComplete` catches this via `Task.WhenAll` — wait, let me re-examine. `Task.WhenAll(tasks)` will rethrow if any task throws. The `UploadToKbAsync` rethrows exceptions. The outer `MeetingComplete` action method has no try/catch around `Task.WhenAll`. **This is a bug** — see Critical finding below. |

**Re-examining #30 more carefully:**

`UploadToKbAsync` has `try/catch { _logger.LogError; throw; }` — it rethrows. `Task.WhenAll(tasks)` propagates that exception to `MeetingComplete`. `MeetingComplete` has no try/catch. This means if S3 upload fails, the controller returns a 500 to FIRM, which would cause FIRM's fire-and-forget `Task.Run` to log a warning (non-fatal for FIRM). However, it also means that the "returns 200 OK even if auto-push fails" contract is violated — the controller will throw a 500 instead. **This needs a try/catch around the `Task.WhenAll` block in `MeetingComplete`.**

---

## Issues Summary

### ⚠️ IMPORTANT — Must Fix Before Deploy

#### Issue 1: `PushTranscriptToKb` / `PushSummaryToKb` don't use `ResolveOwnedMeeting`
**File:** `Controllers/MeetingsApiController.cs`
**Checklist:** #16

Both KB push endpoints duplicate the user resolution and ownership check inline instead of calling the existing `ResolveOwnedMeeting` pattern. This is a consistency violation. Any future hardening (rate limiting, audit logging, permission checks) applied to `ResolveOwnedMeeting` will silently miss these endpoints.

**Fix:**
```csharp
[HttpPost("/api/meetings/{id}/push-transcript-to-kb")]
[Authorize]
public async Task<IActionResult> PushTranscriptToKb(long id)
{
    var (meeting, error) = await ResolveOwnedMeeting(id);
    if (error != null) return error;

    await using var db = await _dbFactory.CreateDbContextAsync();
    var entraOid = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);

    if (meeting!.Status != MeetingStatus.Complete)
        return BadRequest(new { error = "Meeting is not complete" });

    if (string.IsNullOrEmpty(user?.FaitUserId))
        return BadRequest(new { error = "FAIT user ID not linked. Please log out and back in." });

    try
    {
        await _firmKbService.PushTranscriptAsync(id, user.Id, user.FaitUserId);
        return Ok(new { success = true });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "FIRM: Failed to push transcript for meeting {Id}", id);
        return StatusCode(500, new { error = "Failed to push transcript to KB" });
    }
}
```
Apply same pattern to `PushSummaryToKb`.

---

#### Issue 2: `MeetingDetail.razor` uses `new HttpClient()` directly
**File:** `Components/Pages/MeetingDetail.razor`
**Checklist:** #21

`PushTranscriptToKb()` and `PushSummaryToKb()` both do `var http = new HttpClient()`. This bypasses the DI-managed HTTP client pool and risks socket exhaustion on repeated use.

**Fix:** Add `@inject IHttpClientFactory HttpClientFactory` to the component and replace `new HttpClient()` with `HttpClientFactory.CreateClient()`:

```razor
@inject IHttpClientFactory HttpClientFactory
```
```csharp
private async Task PushTranscriptToKb()
{
    if (_meeting == null || _pushingTranscript) return;
    _pushingTranscript = true;
    try
    {
        var http = HttpClientFactory.CreateClient();  // ← change
        var resp = await http.PostAsync($"/api/meetings/{_meeting.Id}/push-transcript-to-kb", null);
        // ...
    }
```
Apply same change to `PushSummaryToKb`.

---

#### Issue 3: `MeetingComplete` in `FirmIntegrationController` doesn't swallow S3 exceptions → returns 500 instead of 200
**File:** `Controllers/FirmIntegrationController.cs`
**Checklist:** #30

`UploadToKbAsync` rethrows exceptions. `Task.WhenAll(tasks)` propagates. The outer `MeetingComplete` action has no try/catch. If S3 upload fails, the endpoint returns 500 to FIRM's fire-and-forget caller. This is non-fatal for FIRM (it catches the exception in the `Task.Run` wrapper), but it violates the "returns 200 OK even if auto-push fails" contract and produces misleading error logs.

**Fix:**
```csharp
if (tasks.Any())
{
    try
    {
        await Task.WhenAll(tasks);
        await StartPersonalIngestionAsync(user.Id);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "FirmIntegration: Auto-push failed for meeting {MeetingId} — returning 200 anyway", payload.MeetingId);
    }
}
```
`StartPersonalIngestionAsync` already has its own internal try/catch, so only the `Task.WhenAll` needs wrapping.

---

### 📝 NITPICK / ADVISORY

#### Advisory: FAIT `FirmIntegrationController` doesn't reuse `KbDocumentService`
**File:** `Controllers/FirmIntegrationController.cs`
**Checklist:** #29

The controller directly uses `IAmazonS3` and `IAmazonBedrockAgent` instead of delegating to `KbDocumentService`. The FIRM-side `FirmKbService` does the same. The FAIT controller does add metadata companion file upload (`{s3Key}.metadata.json` with `ownerId`) which is a meaningful addition. If `KbDocumentService` handles personal KB with metadata, consolidate; if not, leave as-is and document the deviation. Not a hard block.

---

## Regression / Security

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 34 | Existing FIRM endpoints unbroken? `ResolveOwnedMeeting` unchanged? | ✅ PASS | `ResolveOwnedMeeting` implementation is unchanged. Existing `DownloadTranscript`, `DownloadSummary`, `GetAudio` endpoints use it correctly. |
| 35 | `VpCallback` still works when FAIT notification fails? | ✅ PASS | FAIT notification is in `Task.Run(async () => { try { ... } catch { log } })`. Cannot break the `return Ok()` at the end of `VpCallback`. |
| 36 | No hardcoded secrets? | ✅ PASS | `_config["Firm:SharedSecret"]`, `_config["Firm:BotCallbackSecret"]` — both config-only. No hardcoded values. |
| 37 | FIRM csproj has `AWSSDK.BedrockAgent`? Correct package name? | ✅ PASS | `<PackageReference Include="AWSSDK.BedrockAgent" Version="3.*" />` — correct. NOT `BedrockAgentRuntime`. |

---

## Additional Observations

### resolve-user accuracy limitation (FYI — known TODO)
The `GET /api/firm/resolve-user` endpoint returns `OrderByDescending(u => u.CreatedAt).FirstOrDefault()` across all Entra users, not a reliable OID-to-user mapping. The code itself acknowledges this with inline comments ("TODO: add EntraOid column to AppUser"). This pre-exists this change and is adequately documented — no action required for this cycle.

### `FirmKbService` not registered as Scoped/Singleton consistently
`Program.cs` registers `FirmKbService` as `AddScoped<FirmKbService>()`. It uses `IDbContextFactory` and all AWS services — both are fine with Scoped. No issue.

### Summary format deviation from download endpoint
`DownloadSummary` uses plain text headings (`KEY DECISIONS:`, `ACTION ITEMS:`, `FOLLOW-UPS:`) while `FirmKbService.PushSummaryAsync` uses Markdown headings (`## Key Decisions`, etc.). This is actually better for Bedrock KB ingestion (Markdown parses more cleanly), but it's inconsistent between download and KB push. Not a defect, but worth noting.

---

## Fix Summary

| # | File | Change | Severity |
|---|------|--------|----------|
| 1 | `Controllers/MeetingsApiController.cs` | Refactor `PushTranscriptToKb` / `PushSummaryToKb` to call `ResolveOwnedMeeting` | Important |
| 2 | `Components/Pages/MeetingDetail.razor` | Replace `new HttpClient()` with `IHttpClientFactory` | Important |
| 3 | `Controllers/FirmIntegrationController.cs` | Wrap `Task.WhenAll` in try/catch so S3 failures return 200 | Important |
| A | `Controllers/FirmIntegrationController.cs` | Advisory: consider consolidating with `KbDocumentService` | Nitpick |

---

## Files Reviewed

**FIRM:**
- `src/FortressIntelligenceRM.Web/Models/FirmUser.cs` ✅
- `src/FortressIntelligenceRM.Web/Models/FirmMeeting.cs` ✅
- `src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs` ✅
- `src/FortressIntelligenceRM.Web/Data/DatabaseInitializationService.cs` ✅
- `src/FortressIntelligenceRM.Web/Services/FirmKbService.cs` ✅
- `src/FortressIntelligenceRM.Web/Controllers/MeetingsApiController.cs` ✅
- `src/FortressIntelligenceRM.Web/Components/Pages/MeetingDetail.razor` ✅
- `src/FortressIntelligenceRM.Web/Program.cs` ✅

**FAIT:**
- `src/FortressAI.Shared/Models/UserAssistantConfig.cs` ✅
- `src/FortressAI.Web/Data/AppDbContext.cs` ✅
- `src/FortressAI.Web/Services/DatabaseInitializationService.cs` ✅
- `src/FortressAI.Web/Services/AssistantConfigService.cs` ✅
- `src/FortressAI.Web/Components/Pages/Settings.razor` ✅
- `src/FortressAI.Web/Controllers/FirmIntegrationController.cs` ✅

---

*Hawkeye out. Three shots, three targets. Send it back to Tony.*
