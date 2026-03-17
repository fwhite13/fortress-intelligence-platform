# FIRM v1 Completion Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Codebase:** `~/projects/fip/firm/src/FortressIntelligenceRM.Web/`  
**Dev URL:** `https://firm.dev.fortressam.ai`

---

## Part 1: What's Actually Built

### ✅ Fully Functional

| Component | State | Notes |
|-----------|-------|-------|
| Data model (DB) | ✅ Done | `firm_users`, `firm_meetings`, `firm_meeting_participants`, `firm_meeting_transcripts`, `firm_meeting_summaries` — all tables, all columns including `transcript_kb_pushed`, `summary_kb_pushed`, `fait_user_id` |
| `DatabaseInitializationService` | ✅ Done | Idempotent `CREATE TABLE IF NOT EXISTS` + `ALTER TABLE` on startup. Runs clean on fresh DB. |
| `MeetingService` | ✅ Done | `GetOrCreateUserAsync`, `GetMeetingsAsync`, `GetMeetingAsync`, `CreateMeetingAsync`, `UpdateStatusAsync`, `UpdateBotTaskArnAsync` — all implemented |
| `VpBotService` | ✅ Done | Launches an ECS Fargate task (`firm-vpbot` container) with `MEETING_ID`, `MEETING_URL`, `FIRM_API_URL`, `BOT_CALLBACK_SECRET` env vars injected. Awaits RunTask and stores task ARN. |
| `S3Service` | ✅ Done | `GeneratePresignedUrlAsync`, `GetTranscriptTextAsync`, `GetSummaryTextAsync` |
| `FirmKbService` | ✅ Done | `PushTranscriptAsync` and `PushSummaryAsync` — assemble text from DB, upload to S3, trigger Bedrock ingestion for personal KB only. |
| `MeetingsApiController` | ✅ Done | `POST /api/meetings/join`, `POST /api/vp/callback`, `GET /api/meetings/{id}/transcript/download`, `GET /api/meetings/{id}/summary/download`, `GET /api/meetings/{id}/audio`, `POST /api/meetings/{id}/push-transcript-to-kb`, `POST /api/meetings/{id}/push-summary-to-kb` |
| `/api/vp/callback` state machine | ✅ Done | recording → Transcribing → Summarizing → Complete transitions; participant + transcript + summary DB writes; FAIT auto-add fire-and-forget |
| `Meetings.razor` | ✅ Done | Meeting list, pagination, status badges, 10-second polling for active meetings, `JoinMeetingDialog` integration |
| `MeetingDetail.razor` | ✅ Done | Summary tab (key decisions, action items, follow-ups), Transcript tab (speaker/time/text), download buttons for transcript + summary + audio, per-meeting "Add to KB" buttons |
| `JoinMeetingDialog.razor` | ✅ Done | Teams URL input + optional title, validates URL starts with `https://`, returns `(url, title)` tuple to parent |
| FipNavBar + FipShared RCL | ✅ Done | `MainLayout.razor` uses `<FipNavBar ActiveModule="FipModule.FIRM" .../>`. `FipTheme.cs` is light-only (`PaletteLight` only, comment confirms dark removed 2026-03-15). `firm.css` is clean light. |
| Auth (cookie consumer) | ✅ Done | FIRM reads the `.FortressAI.Session` cookie from FAIT. DataProtection key ring shared via `fred_dev.DataProtectionKeys` table. Unauthenticated → redirect to FAIT login. |
| FAIT integration (`FirmIntegrationController`) | ✅ Done | `POST /api/firm/meeting-complete` (auto-add to personal KB on completion), `GET /api/firm/resolve-user` — both implemented in `fait/src/FortressAI.Web/Controllers/FirmIntegrationController.cs`. |

### ❌ Broken / Missing (Gap Analysis)

| Gap | Severity | Impact |
|-----|----------|--------|
| **`FaitUserId` never populated** | **CRITICAL** | Manual KB push always fails. `PushTranscriptToKb` and `PushSummaryToKb` buttons return "FAIT user ID not linked" 100% of the time because `GetOrCreateUserAsync` never calls FAIT's `resolve-user` endpoint to populate `firm_users.fait_user_id`. The auto-complete flow (via `FirmIntegrationController`) uses an entraOid workaround that doesn't require `FaitUserId`, but the UI push buttons are completely broken. |
| **No Team KB support in UI or service** | **HIGH** | `FirmKbService` has `TeamKbId` and `TeamDsId` config properties but zero methods for team KB. No `PushTranscriptToTeamKbAsync`. The current UI only pushes to personal KB. Fred's v1 requirement: multi-KB with My KB + Team KB(s). |
| **No KB push state tracking per KB** | **HIGH** | `firm_meetings.transcript_kb_pushed` and `summary_kb_pushed` are single booleans. With multi-KB, these need to be per-KB. The schema doesn't support "transcript pushed to my KB but not team KB." |
| **Audio download returns S3 redirect, not file** | **MEDIUM** | `GET /api/meetings/{id}/audio` returns `{ url: "https://s3...presigned" }` JSON, not the audio bytes. `MeetingDetail.razor` calls `Navigation.NavigateTo($"/api/meetings/{_meeting.Id}/audio", forceLoad: true)` which loads a JSON page, not an audio file. |
| **`PushTranscriptToKb` / `PushSummaryToKb` use `HttpClientFactory.CreateClient()` without base address** | **MEDIUM** | In Blazor Server, `IHttpClientFactory.CreateClient()` returns a client with no base address. The `PostAsync($"/api/meetings/{id}/push-transcript-to-kb", null)` call fails because there's no host. It should use the registered named or typed client. |
| **`JoinMeetingDialog.razor` return type mismatch** | **MEDIUM** | `Meetings.razor` expects `result.Data is ValueTuple<string, string?>`. MudBlazor Dialog's `result.Data` is typed as `object`. The cast may work at runtime, but the dialog must close with `MudDialog.Close(DialogResult.Ok((MeetingUrl, Title)))` where the tuple is boxed correctly. This needs verification. |
| **No `EntraOid` in `FirmIntegrationController.ResolveUser`** | **MEDIUM** | `GET /api/firm/resolve-user` does a workaround: returns `"the first active Entra user"` — there's a TODO in the code admitting this only works for single-user deployment. For multi-user, it returns the wrong FAIT user. The real fix is the `FaitUserId` population flow (Gap 1). |
| **Dark theme WI#796 — `FipTheme.cs` and `firm.css` already fixed** | **DONE** | `FipTheme.cs` comment says `No PaletteDark — app is always light mode` (as of 2026-03-15). `firm.css` comment says `Dark theme removed 2026-03-15`. WI#796 Task 1 is already deployed. ✅ |
| **`MeetingDetail.razor` uses `HttpClientFactory` not `Http`** | **LOW** | The detail page injects `IHttpClientFactory` but the list page uses `@inject HttpClient Http`. One is an typed HttpClient, the other is a factory-created client without base address. Both might hit the same base-address-missing bug. |
| **No `kb_pushes` table** | **STRUCTURAL** | Without per-KB tracking, cannot show "Already in My KB ✓" vs "Already in Team KB ✓" as required by v1. Need a `firm_meeting_kb_pushes` table: `(meeting_id, doc_type, kb_id, pushed_at)`. |

---

## Part 2: Gap vs Fred's v1 Requirements

### Requirement 1: Bot join + full recording pipeline
**Status: ✅ Mostly done. One production-readiness gap.**

The pipeline exists end-to-end: UI join → `POST /api/meetings/join` → `VpBotService.TriggerBotAsync()` → ECS Fargate `firm-vpbot` task → bot calls back `POST /api/vp/callback` → status transitions → transcript segments + summary written to DB → FAIT auto-add fire-and-forget.

The only gap: **the VP bot container (`firm-vpbot`) is a separate project** not in this codebase. It must exist and be deployed separately. The FIRM app's side of this integration is complete.

### Requirement 2: Meetings list — download audio, add to FORGE KBs (My KB + Team KB, multi-select, KB status indicators)
**Status: ❌ Partially done. Three blocking gaps.**

- ✅ Meetings list with status, dates, download transcript/summary links
- ❌ Audio download broken (returns JSON, not file)
- ❌ KB push only goes to personal KB; no Team KB support
- ❌ KB status indicators are single booleans — no per-KB tracking
- ❌ Multi-select KB UI doesn't exist (Fred requires FAIT's KB toggle pattern)
- ❌ Manual KB push buttons broken due to `FaitUserId` never being populated

### Requirement 3: Teams meeting join (manual, not scheduled)
**Status: ✅ Done.**

`JoinMeetingDialog.razor` takes a Teams URL and optional title. `POST /api/meetings/join` validates the URL and triggers the bot. Status polling every 10 seconds. The UX is functional.

---

## Part 3: v1 Completion Spec

### Overview: 5 tasks

| # | Task | Files | Complexity |
|---|------|-------|------------|
| 1 | Fix `FaitUserId` population (login-time resolve) | `MeetingService.cs` | Small |
| 2 | Fix audio download (proxy bytes, not redirect) | `MeetingsApiController.cs`, `MeetingDetail.razor` | Small |
| 3 | Fix `HttpClient` base address in Blazor pages | `MeetingDetail.razor`, `Program.cs` | Small |
| 4 | Add `firm_meeting_kb_pushes` table + per-KB tracking | `FirmDbContext.cs`, `DatabaseInitializationService.cs`, new model, `FirmKbService.cs`, `MeetingsApiController.cs` | Medium |
| 5 | Multi-KB push UI (My KB + Team KB, status indicators) | `MeetingDetail.razor`, `FirmKbService.cs` | Medium |

---

### Task 1: Fix `FaitUserId` Population

**Root cause:** `GetOrCreateUserAsync` creates the user but never calls `GET /api/firm/resolve-user` to link the FAIT internal user ID.

**Fix:** After creating or loading the user, if `FaitUserId` is null, call FAIT's resolve endpoint to populate it.

**File:** `firm/src/FortressIntelligenceRM.Web/Services/MeetingService.cs`

Replace `GetOrCreateUserAsync` with:

```csharp
public async Task<FirmUser?> GetOrCreateUserAsync(string entraOid, string email, string displayName)
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
    if (user == null)
    {
        user = new FirmUser
        {
            Id = Guid.NewGuid(),
            EntraOid = entraOid,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Provisioned new user {Email}", email);
    }
    else
    {
        user.LastLoginAt = DateTime.UtcNow;
        user.DisplayName = displayName;
        user.Email = email;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // Populate FaitUserId if not already set
    if (string.IsNullOrEmpty(user.FaitUserId))
    {
        try
        {
            var faitId = await ResolveFaitUserIdAsync(entraOid);
            if (!string.IsNullOrEmpty(faitId))
            {
                user.FaitUserId = faitId;
                user.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                _logger.LogInformation("FIRM: Linked FaitUserId {FaitId} for user {Email}", faitId, email);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — user can still use FIRM; KB push will show error until resolved
            _logger.LogWarning(ex, "FIRM: Failed to resolve FAIT user ID for {Email} — KB push unavailable until next login", email);
        }
    }

    return user;
}

private async Task<string?> ResolveFaitUserIdAsync(string entraOid)
{
    var faitApiUrl = _config["FIP:FaitApiUrl"]?.TrimEnd('/') ?? "https://fait.dev.fortressam.ai";
    var sharedSecret = _config["Firm:SharedSecret"] ?? "";
    if (string.IsNullOrEmpty(sharedSecret))
    {
        _logger.LogWarning("FIRM: Firm:SharedSecret not configured — cannot resolve FAIT user ID");
        return null;
    }

    using var http = _httpClientFactory.CreateClient();
    var url = $"{faitApiUrl}/api/firm/resolve-user?entraOid={Uri.EscapeDataString(entraOid)}";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("X-Firm-Secret", sharedSecret);
    var response = await http.SendAsync(request);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("FIRM: resolve-user returned {Status} for entraOid {OID}", response.StatusCode, entraOid);
        return null;
    }

    var body = await response.Content.ReadFromJsonAsync<ResolveFaitUserResponse>();
    return body?.UserId;
}

private record ResolveFaitUserResponse(string UserId);
```

**Also add `IHttpClientFactory` to `MeetingService` constructor:**

```csharp
// Existing constructor — add parameter:
private readonly IHttpClientFactory _httpClientFactory;

public MeetingService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, ILogger<MeetingService> logger, IHttpClientFactory httpClientFactory)
{
    _dbFactory = dbFactory;
    _config = config;
    _logger = logger;
    _httpClientFactory = httpClientFactory;
}
```

`IHttpClientFactory` is already registered in `Program.cs` (`builder.Services.AddHttpClient()`). No new registrations needed.

**Constraints for CC:**
- This is `MeetingService.cs` only — do not touch `MeetingsApiController.cs` or `Program.cs`
- The `ResolveFaitUserIdAsync` call is best-effort — never throw to the caller
- The record `ResolveFaitUserResponse` should use `System.Text.Json.Serialization.JsonPropertyName("userId")` attribute since FAIT returns camelCase

---

### Task 2: Fix Audio Download

**Root cause:** `GET /api/meetings/{id}/audio` returns a JSON object `{ url: "..." }`. `MeetingDetail.razor` navigates to that URL (which loads a JSON page in the browser, not an audio file).

**Two-part fix:**

**Part A — `MeetingsApiController.cs`:** Change the audio endpoint to redirect to the pre-signed URL instead of returning JSON:

```csharp
[HttpGet("/api/meetings/{id}/audio")]
[Authorize]
public async Task<IActionResult> GetAudio(long id)
{
    var (meeting, error) = await ResolveOwnedMeeting(id);
    if (error != null) return error;
    if (string.IsNullOrEmpty(meeting!.AudioS3Key))
        return NotFound(new { error = "Audio not available" });

    var url = await _s3Service.GeneratePresignedUrlAsync(meeting.AudioS3Key, expiryHours: 1);
    return Redirect(url);  // Was: return Ok(new { url });
}
```

**Part B — `MeetingDetail.razor`:** The button already uses `Navigation.NavigateTo($"/api/meetings/{_meeting.Id}/audio", forceLoad: true)` — this works correctly with a redirect. No change needed in the Razor file.

**Constraints for CC:**
- Change only `MeetingsApiController.cs`, specifically `GetAudio()`
- A 302 Redirect to the S3 presigned URL is the correct HTTP pattern — the browser follows the redirect and downloads the audio file directly from S3
- Do not change `MeetingDetail.razor`

---

### Task 3: Fix `HttpClient` Base Address in `MeetingDetail.razor`

**Root cause:** `MeetingDetail.razor` injects `IHttpClientFactory` and calls `HttpClientFactory.CreateClient()` which returns a client with no base address. The relative URL `$"/api/meetings/{_meeting.Id}/push-transcript-to-kb"` fails.

**Fix — `MeetingDetail.razor`:** Replace `IHttpClientFactory` approach with a named `HttpClient` that has the base address pre-configured, or use the typed `HttpClient Http` already injected in `Meetings.razor`.

The simplest correct fix is to inject `HttpClient` directly (Blazor Server's default registered HttpClient has the base address set to the server's own URL):

Remove from `MeetingDetail.razor`:
```razor
@inject IHttpClientFactory HttpClientFactory
```

Add:
```razor
@inject HttpClient Http
```

Replace the two `HttpClientFactory.CreateClient()` calls in `PushTranscriptToKb()` and `PushSummaryToKb()`:

```csharp
// Was:
var http = HttpClientFactory.CreateClient();
var resp = await http.PostAsync($"/api/meetings/{_meeting.Id}/push-transcript-to-kb", null);

// Replace with:
var resp = await Http.PostAsync($"/api/meetings/{_meeting.Id}/push-transcript-to-kb", null);
```

Same change in `PushSummaryToKb()`.

**Constraints for CC:**
- Change only `MeetingDetail.razor` — remove `IHttpClientFactory` injection, add `HttpClient Http` injection, replace two `HttpClientFactory.CreateClient()` calls
- `Program.cs` already registers `builder.Services.AddHttpClient<>()` — no changes needed there

---

### Task 4: Multi-KB Tracking Schema

**What's needed:** Replace the two boolean columns `transcript_kb_pushed` / `summary_kb_pushed` on `firm_meetings` with a proper per-KB push log table.

**New model:** `FirmMeetingKbPush.cs`

```csharp
// firm/src/FortressIntelligenceRM.Web/Models/FirmMeetingKbPush.cs

namespace FortressIntelligenceRM.Web.Models;

/// <summary>Tracks which meeting documents have been pushed to which KB.</summary>
public class FirmMeetingKbPush
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    /// <summary>"transcript" or "summary"</summary>
    public string DocType { get; set; } = "";
    /// <summary>"personal" or "team"</summary>
    public string KbScope { get; set; } = "";
    public string KbId { get; set; } = "";
    public DateTime PushedAt { get; set; } = DateTime.UtcNow;
    public FirmMeeting? Meeting { get; set; }
}
```

**`FirmDbContext.cs`** — add `DbSet` and `OnModelCreating` mapping:

```csharp
public DbSet<FirmMeetingKbPush> KbPushes => Set<FirmMeetingKbPush>();
```

In `OnModelCreating`:

```csharp
modelBuilder.Entity<FirmMeetingKbPush>(entity =>
{
    entity.ToTable("firm_meeting_kb_pushes");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).ValueGeneratedOnAdd();
    entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
    entity.Property(e => e.DocType).HasColumnName("doc_type").HasMaxLength(20).IsRequired();
    entity.Property(e => e.KbScope).HasColumnName("kb_scope").HasMaxLength(20).IsRequired();
    entity.Property(e => e.KbId).HasColumnName("kb_id").HasMaxLength(50).IsRequired();
    entity.Property(e => e.PushedAt).HasColumnName("pushed_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
    entity.HasOne(e => e.Meeting)
        .WithMany()
        .HasForeignKey(e => e.MeetingId)
        .OnDelete(DeleteBehavior.Cascade)
        .HasConstraintName("fk_fmkp_meeting");
    entity.HasIndex(e => new { e.MeetingId, e.DocType, e.KbScope }).HasDatabaseName("idx_fmkp_lookup");
});
```

**`DatabaseInitializationService.cs`** — add the new table to `extraTables`:

```csharp
("firm_meeting_kb_pushes", @"CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    meeting_id BIGINT NOT NULL,
    doc_type VARCHAR(20) NOT NULL,
    kb_scope VARCHAR(20) NOT NULL,
    kb_id VARCHAR(50) NOT NULL,
    pushed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_fmkp_lookup (meeting_id, doc_type, kb_scope)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
```

**Why keep the old boolean columns?** Keep `transcript_kb_pushed` and `summary_kb_pushed` on `firm_meetings` — they remain for the auto-complete FAIT integration (which uses them for auto-add logic). Don't remove them. The new `firm_meeting_kb_pushes` table is additive for the multi-KB UI.

**Constraints for CC:**
- New file: `Models/FirmMeetingKbPush.cs`
- Modified files: `Data/FirmDbContext.cs`, `Data/DatabaseInitializationService.cs`
- Do NOT remove the existing `TranscriptKbPushed` and `SummaryKbPushed` boolean fields from `FirmMeeting.cs` or `FirmDbContext.cs` — they are still used by the auto-complete flow

---

### Task 5: Multi-KB Push Service + UI

This is the main v1 feature gap. It has two sub-tasks: the service layer and the UI.

#### Task 5A: `FirmKbService.cs` — Add Team KB + Push tracking

**Add to `FirmKbService.cs`:**

```csharp
/// <summary>
/// Push meeting content to one or more KBs.
/// kbScopes: list of "personal" and/or "team"
/// docType: "transcript" or "summary"
/// </summary>
public async Task PushDocumentAsync(long meetingId, Guid userId, string faitUserId, string docType, IEnumerable<string> kbScopes)
{
    var scopeList = kbScopes.Distinct().ToList();
    if (!scopeList.Any()) return;

    await using var db = await _dbFactory.CreateDbContextAsync();

    // Build the document content once
    string content;
    string contentType;
    string fileExtension;
    if (docType == "transcript")
    {
        (content, contentType, fileExtension) = await BuildTranscriptContentAsync(db, meetingId);
    }
    else if (docType == "summary")
    {
        (content, contentType, fileExtension) = await BuildSummaryContentAsync(db, meetingId);
    }
    else
    {
        throw new ArgumentException($"Unknown docType: {docType}");
    }

    if (string.IsNullOrWhiteSpace(content))
    {
        _logger.LogWarning("FirmKbService: No content for {DocType} of meeting {MeetingId}", docType, meetingId);
        return;
    }

    foreach (var scope in scopeList)
    {
        // Check if already pushed to this scope+docType
        var alreadyPushed = await db.KbPushes.AnyAsync(p =>
            p.MeetingId == meetingId && p.DocType == docType && p.KbScope == scope);
        if (alreadyPushed)
        {
            _logger.LogInformation("FirmKbService: {DocType} for meeting {MeetingId} already in {Scope} KB — skipping", docType, meetingId, scope);
            continue;
        }

        string kbId, dsId, s3Prefix;
        if (scope == "personal")
        {
            kbId = PersonalKbId;
            dsId = PersonalDsId;
            s3Prefix = $"kb-docs/personal/{faitUserId}";
        }
        else if (scope == "team")
        {
            kbId = TeamKbId;
            dsId = TeamDsId;
            // Team KB uses a shared prefix — documents are not user-scoped
            s3Prefix = $"kb-docs/team/firm";
        }
        else
        {
            _logger.LogWarning("FirmKbService: Unknown KB scope {Scope} — skipping", scope);
            continue;
        }

        var s3Key = $"{s3Prefix}/firm-{docType}-{meetingId}.{fileExtension}";

        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                ContentBody = content,
                ContentType = contentType
            });

            // For personal KB: write metadata.json for KB isolation (same pattern as FAIT)
            if (scope == "personal" && !string.IsNullOrEmpty(faitUserId))
            {
                var metadata = new { metadataAttributes = new Dictionary<string, object> { ["ownerId"] = faitUserId } };
                var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = BucketName,
                    Key = $"{s3Key}.metadata.json",
                    ContentBody = metadataJson,
                    ContentType = "application/json"
                });
            }

            // Start ingestion for this KB
            await StartIngestionAsync(kbId, dsId);

            // Record the push
            db.KbPushes.Add(new FirmMeetingKbPush
            {
                MeetingId = meetingId,
                DocType = docType,
                KbScope = scope,
                KbId = kbId,
                PushedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();

            _logger.LogInformation("FirmKbService: Pushed {DocType} for meeting {MeetingId} to {Scope} KB ({KbId})", docType, meetingId, scope, kbId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FirmKbService: Failed to push {DocType} to {Scope} KB for meeting {MeetingId}", docType, scope, meetingId);
            throw;
        }
    }
}

/// <summary>Returns which KB scopes a document has already been pushed to.</summary>
public async Task<HashSet<string>> GetPushedScopesAsync(long meetingId, string docType)
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    var pushed = await db.KbPushes
        .Where(p => p.MeetingId == meetingId && p.DocType == docType)
        .Select(p => p.KbScope)
        .ToListAsync();
    return pushed.ToHashSet();
}

private async Task<(string content, string contentType, string extension)> BuildTranscriptContentAsync(FirmDbContext db, long meetingId)
{
    var segments = await db.Transcripts
        .Where(t => t.MeetingId == meetingId)
        .OrderBy(t => t.StartTimeMs)
        .ToListAsync();
    if (!segments.Any()) return ("", "", "");
    var sb = new System.Text.StringBuilder();
    foreach (var seg in segments)
    {
        var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
        var ts = seg.StartTimeMs.HasValue ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss") : "00:00:00";
        sb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
    }
    return (sb.ToString(), "text/plain", "txt");
}

private async Task<(string content, string contentType, string extension)> BuildSummaryContentAsync(FirmDbContext db, long meetingId)
{
    var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == meetingId);
    if (summary == null) return ("", "", "");
    var sb = new System.Text.StringBuilder();
    if (!string.IsNullOrEmpty(summary.SummaryText)) { sb.AppendLine("## Overview"); sb.AppendLine(summary.SummaryText); sb.AppendLine(); }
    if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
    {
        var decisions = TryDeserializeList(summary.KeyDecisionsJson);
        if (decisions.Any()) { sb.AppendLine("## Key Decisions"); decisions.ForEach(d => sb.AppendLine($"- {d}")); sb.AppendLine(); }
    }
    if (!string.IsNullOrEmpty(summary.ActionItemsJson))
    {
        sb.AppendLine("## Action Items");
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<ActionItemDto>>(summary.ActionItemsJson) ?? new();
            items.ForEach(i => sb.AppendLine($"- [{i.Owner ?? "?"}] {i.Description}"));
        }
        catch { }
        sb.AppendLine();
    }
    if (!string.IsNullOrEmpty(summary.FollowUpsJson))
    {
        var followUps = TryDeserializeList(summary.FollowUpsJson);
        if (followUps.Any()) { sb.AppendLine("## Follow-ups"); followUps.ForEach(f => sb.AppendLine($"- {f}")); }
    }
    return (sb.ToString(), "text/markdown", "md");
}

private async Task StartIngestionAsync(string kbId, string dsId)
{
    try
    {
        await _bedrockAgent.StartIngestionJobAsync(new StartIngestionJobRequest
        {
            KnowledgeBaseId = kbId,
            DataSourceId = dsId
        });
    }
    catch (Amazon.BedrockAgent.Model.ConflictException)
    {
        _logger.LogInformation("FirmKbService: Ingestion already in progress for KB {KbId} — will sync on next run", kbId);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "FirmKbService: Failed to start ingestion for KB {KbId} (non-fatal)", kbId);
    }
}
```

**Keep the existing `PushTranscriptAsync` and `PushSummaryAsync` methods** — they are still called by the auto-complete flow. Do not remove them. The new `PushDocumentAsync` is additive.

#### Task 5B: Updated API Endpoint

**`MeetingsApiController.cs`** — replace `push-transcript-to-kb` and `push-summary-to-kb` with a single unified endpoint:

```csharp
[HttpPost("/api/meetings/{id}/push-to-kb")]
[Authorize]
public async Task<IActionResult> PushToKb(long id, [FromBody] PushToKbRequest request)
{
    var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
    if (error != null) return error;

    if (meeting!.Status != MeetingStatus.Complete)
        return BadRequest(new { error = "Meeting is not complete" });

    if (string.IsNullOrEmpty(user!.FaitUserId))
        return BadRequest(new { error = "FAIT user ID not linked. Please sign out and back in." });

    if (string.IsNullOrEmpty(request.DocType) || !new[] { "transcript", "summary" }.Contains(request.DocType))
        return BadRequest(new { error = "docType must be 'transcript' or 'summary'" });

    if (request.KbScopes == null || !request.KbScopes.Any())
        return BadRequest(new { error = "At least one KB scope required" });

    var validScopes = request.KbScopes.Where(s => new[] { "personal", "team" }.Contains(s)).ToList();
    if (!validScopes.Any())
        return BadRequest(new { error = "Valid scopes: 'personal', 'team'" });

    try
    {
        await _firmKbService.PushDocumentAsync(id, user.Id, user.FaitUserId, request.DocType, validScopes);
        return Ok(new { success = true });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "FIRM: Failed to push {DocType} to KB for meeting {Id}", request.DocType, id);
        return StatusCode(500, new { error = "Failed to push to KB" });
    }
}

[HttpGet("/api/meetings/{id}/kb-status")]
[Authorize]
public async Task<IActionResult> GetKbStatus(long id)
{
    var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
    if (error != null) return error;

    var transcriptScopes = await _firmKbService.GetPushedScopesAsync(id, "transcript");
    var summaryScopes    = await _firmKbService.GetPushedScopesAsync(id, "summary");

    return Ok(new {
        transcript = transcriptScopes,
        summary    = summaryScopes,
    });
}

public record PushToKbRequest(string DocType, List<string> KbScopes);
```

**Keep the old `push-transcript-to-kb` and `push-summary-to-kb` endpoints** for now — they might be wired to existing user bookmarks or tests. Mark them as deprecated with a comment but do not delete them.

#### Task 5C: Multi-KB UI in `MeetingDetail.razor`

Replace the current four buttons (Add Transcript to KB / In My KB / Add Summary to KB / In My KB) with a more capable UI that supports KB selection.

**State changes in `@code` block:**

```csharp
// Replace old state:
// private bool _pushingTranscript = false;
// private bool _pushingSummary = false;

// Add new state:
private HashSet<string> _transcriptPushedTo = new();  // "personal", "team"
private HashSet<string> _summaryPushedTo = new();

// KB selection state for each doc type
private bool _transcriptSelectPersonal = true;
private bool _transcriptSelectTeam = false;
private bool _summarySelectPersonal = true;
private bool _summarySelectTeam = false;

private bool _pushingTranscript = false;
private bool _pushingSummary = false;
```

**Load KB status on init** — add to `OnInitializedAsync()` after loading the meeting:

```csharp
// Load KB push status
try
{
    var kbStatusResp = await Http.GetFromJsonAsync<KbStatusResponse>($"/api/meetings/{Id}/kb-status");
    if (kbStatusResp != null)
    {
        _transcriptPushedTo = kbStatusResp.Transcript?.ToHashSet() ?? new();
        _summaryPushedTo    = kbStatusResp.Summary?.ToHashSet() ?? new();
    }
}
catch { /* Non-fatal */ }
```

Add record:
```csharp
private record KbStatusResponse(List<string>? Transcript, List<string>? Summary);
```

**Replacement UI for the KB push section** (replaces the two existing KB push buttons in `MeetingDetail.razor`):

```razor
@* Replace the two "Add * to KB" buttons with this KB push panel *@
<div style="margin-top: 8px; border: 1px solid var(--color-border); border-radius: 8px; padding: 16px; background: var(--color-bg-page);">
    <div style="font-size: 13px; font-weight: 600; color: var(--color-text-secondary); margin-bottom: 12px;">Add to FORGE Knowledge Base</div>

    @* Transcript row *@
    <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 8px; flex-wrap: wrap;">
        <span style="font-size: 13px; color: var(--color-text-primary); min-width: 80px;">Transcript</span>
        <div style="display: flex; gap: 8px; align-items: center;">
            <label style="display: flex; align-items: center; gap: 4px; font-size: 13px; color: var(--color-text-secondary); cursor: pointer;">
                <input type="checkbox" @bind="_transcriptSelectPersonal"
                       disabled="@(_transcriptPushedTo.Contains("personal") || _pushingTranscript)" />
                My KB
                @if (_transcriptPushedTo.Contains("personal"))
                {
                    <span style="color: #4caf50; font-size: 11px; margin-left: 2px;">✓</span>
                }
            </label>
            <label style="display: flex; align-items: center; gap: 4px; font-size: 13px; color: var(--color-text-secondary); cursor: pointer;">
                <input type="checkbox" @bind="_transcriptSelectTeam"
                       disabled="@(_transcriptPushedTo.Contains("team") || _pushingTranscript)" />
                Team KB
                @if (_transcriptPushedTo.Contains("team"))
                {
                    <span style="color: #4caf50; font-size: 11px; margin-left: 2px;">✓</span>
                }
            </label>
        </div>
        <MudButton Variant="Variant.Outlined" Size="Size.Small"
                   Style="border-color: var(--color-border); color: var(--color-text-secondary);"
                   Disabled="@(!TranscriptHasSelection() || _pushingTranscript)"
                   OnClick="PushTranscript">
            @(_pushingTranscript ? "Adding…" : "Add")
        </MudButton>
    </div>

    @* Summary row *@
    <div style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap;">
        <span style="font-size: 13px; color: var(--color-text-primary); min-width: 80px;">Summary</span>
        <div style="display: flex; gap: 8px; align-items: center;">
            <label style="display: flex; align-items: center; gap: 4px; font-size: 13px; color: var(--color-text-secondary); cursor: pointer;">
                <input type="checkbox" @bind="_summarySelectPersonal"
                       disabled="@(_summaryPushedTo.Contains("personal") || _pushingSummary)" />
                My KB
                @if (_summaryPushedTo.Contains("personal"))
                {
                    <span style="color: #4caf50; font-size: 11px; margin-left: 2px;">✓</span>
                }
            </label>
            <label style="display: flex; align-items: center; gap: 4px; font-size: 13px; color: var(--color-text-secondary); cursor: pointer;">
                <input type="checkbox" @bind="_summarySelectTeam"
                       disabled="@(_summaryPushedTo.Contains("team") || _pushingSummary)" />
                Team KB
                @if (_summaryPushedTo.Contains("team"))
                {
                    <span style="color: #4caf50; font-size: 11px; margin-left: 2px;">✓</span>
                }
            </label>
        </div>
        <MudButton Variant="Variant.Outlined" Size="Size.Small"
                   Style="border-color: var(--color-border); color: var(--color-text-secondary);"
                   Disabled="@(!SummaryHasSelection() || _pushingSummary)"
                   OnClick="PushSummary">
            @(_pushingSummary ? "Adding…" : "Add")
        </MudButton>
    </div>
</div>
```

**Helpers and push methods:**

```csharp
private bool TranscriptHasSelection() =>
    (_transcriptSelectPersonal && !_transcriptPushedTo.Contains("personal")) ||
    (_transcriptSelectTeam     && !_transcriptPushedTo.Contains("team"));

private bool SummaryHasSelection() =>
    (_summarySelectPersonal && !_summaryPushedTo.Contains("personal")) ||
    (_summarySelectTeam     && !_summaryPushedTo.Contains("team"));

private async Task PushTranscript()
{
    if (_pushingTranscript || _meeting == null) return;
    _pushingTranscript = true;
    try
    {
        var scopes = new List<string>();
        if (_transcriptSelectPersonal && !_transcriptPushedTo.Contains("personal")) scopes.Add("personal");
        if (_transcriptSelectTeam     && !_transcriptPushedTo.Contains("team"))     scopes.Add("team");
        if (!scopes.Any()) return;

        var resp = await Http.PostAsJsonAsync($"/api/meetings/{_meeting.Id}/push-to-kb",
            new { docType = "transcript", kbScopes = scopes });
        if (resp.IsSuccessStatusCode)
        {
            foreach (var s in scopes) _transcriptPushedTo.Add(s);
            Snackbar.Add($"Transcript added to {string.Join(" + ", scopes.Select(s => s == "personal" ? "My KB" : "Team KB"))}!", Severity.Success);
        }
        else
        {
            var body = await resp.Content.ReadAsStringAsync();
            Snackbar.Add($"Failed: {body}", Severity.Error);
        }
    }
    catch (Exception ex) { Snackbar.Add($"Error: {ex.Message}", Severity.Error); }
    finally { _pushingTranscript = false; StateHasChanged(); }
}

private async Task PushSummary()
{
    if (_pushingSummary || _meeting == null) return;
    _pushingSummary = true;
    try
    {
        var scopes = new List<string>();
        if (_summarySelectPersonal && !_summaryPushedTo.Contains("personal")) scopes.Add("personal");
        if (_summarySelectTeam     && !_summaryPushedTo.Contains("team"))     scopes.Add("team");
        if (!scopes.Any()) return;

        var resp = await Http.PostAsJsonAsync($"/api/meetings/{_meeting.Id}/push-to-kb",
            new { docType = "summary", kbScopes = scopes });
        if (resp.IsSuccessStatusCode)
        {
            foreach (var s in scopes) _summaryPushedTo.Add(s);
            Snackbar.Add($"Summary added to {string.Join(" + ", scopes.Select(s => s == "personal" ? "My KB" : "Team KB"))}!", Severity.Success);
        }
        else
        {
            var body = await resp.Content.ReadAsStringAsync();
            Snackbar.Add($"Failed: {body}", Severity.Error);
        }
    }
    catch (Exception ex) { Snackbar.Add($"Error: {ex.Message}", Severity.Error); }
    finally { _pushingSummary = false; StateHasChanged(); }
}
```

---

## Part 4: Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Models/FirmMeetingKbPush.cs` | KB push tracking model |

### Modified Files

| File | Task | What Changes |
|------|------|-------------|
| `Services/MeetingService.cs` | T1 | Add `ResolveFaitUserIdAsync`; update `GetOrCreateUserAsync` to populate `FaitUserId`; add `IHttpClientFactory` parameter |
| `Controllers/MeetingsApiController.cs` | T2, T5B | Fix `GetAudio()` to `Redirect(url)`; add `POST /api/meetings/{id}/push-to-kb`; add `GET /api/meetings/{id}/kb-status`; keep old push endpoints but mark deprecated |
| `Components/Pages/MeetingDetail.razor` | T3, T5C | Replace `IHttpClientFactory` with `HttpClient Http`; replace KB push buttons with multi-KB UI; add KB status load on init |
| `Services/FirmKbService.cs` | T5A | Add `PushDocumentAsync`, `GetPushedScopesAsync`, `BuildTranscriptContentAsync`, `BuildSummaryContentAsync`, `StartIngestionAsync`; keep existing methods |
| `Data/FirmDbContext.cs` | T4 | Add `DbSet<FirmMeetingKbPush> KbPushes`; add `OnModelCreating` mapping |
| `Data/DatabaseInitializationService.cs` | T4 | Add `firm_meeting_kb_pushes` CREATE TABLE to `extraTables` array |

**Total: 1 new file + 5 modified. No new packages. No new env vars beyond what's already in the spec.**

---

## Part 5: Environment Variables Required

All existing env vars are assumed configured. The following must be verified in the FIRM ECS task definition:

| Variable | Required By | Notes |
|----------|------------|-------|
| `Firm__SharedSecret` | T1 — `ResolveFaitUserIdAsync`; also `VpCallback` | Must match `Firm__SharedSecret` in FAIT ECS task def |
| `FIP__FaitApiUrl` | T1 — resolve-user call | e.g. `https://fait.dev.fortressam.ai` |
| `Firm__PersonalKbId` | T5A | Bedrock KB ID for personal KB |
| `Firm__PersonalKbDsId` | T5A | Bedrock DS ID for personal KB |
| `Firm__TeamKbId` | T5A | Bedrock KB ID for team KB |
| `Firm__TeamKbDsId` | T5A | Bedrock DS ID for team KB |
| `Firm__KbS3Bucket` | T5A | S3 bucket for KB docs |
| `Firm__VpBotTaskDefinition` | VpBotService | ECS task definition for bot |
| `Firm__EcsCluster` | VpBotService | ECS cluster ARN |
| `Firm__BotCallbackSecret` | VpCallback auth | Shared secret for bot → FIRM callback |

---

## Part 6: Acceptance Criteria

1. **FaitUserId linked on first login:** A new FIRM user logs in. CloudWatch shows `FIRM: Linked FaitUserId <guid> for user <email>`. `firm_users.fait_user_id` is populated in the DB.

2. **Manual KB push works:** On a completed meeting in `MeetingDetail`, check "My KB" for Transcript, click "Add". Response 200. `firm_meeting_kb_pushes` has a row with `doc_type='transcript'`, `kb_scope='personal'`. The checkbox shows a green ✓.

3. **Team KB push works:** Check "Team KB" for Summary, click "Add". Response 200. `firm_meeting_kb_pushes` has `doc_type='summary'`, `kb_scope='team'`.

4. **Already-pushed KBs disabled:** Reload `MeetingDetail` for the meeting from criterion 2. The "My KB" checkbox for Transcript is disabled and shows ✓. "Team KB" is still enabled (not yet pushed).

5. **Audio download works:** Click "Audio" button on a completed meeting with an audio file. Browser prompts a download (or plays audio). Not a JSON page.

6. **No "FAIT user ID not linked" error:** Before this fix, all KB push buttons showed this error. After: the error never appears for users who have logged in after the fix deploys.

7. **`FaitUserId` resolve failure is non-fatal:** Take down the FAIT API (or misconfigure `Firm:SharedSecret`). Log in to FIRM. No error shown to user — the page loads normally. CloudWatch logs a warning. KB push shows "FAIT user ID not linked" until the user logs out and back in (when the resolve will retry).

---

## Part 7: Clint Review Priorities

```
⚠️  HIGH: Verify ResolveFaitUserIdAsync is called ONLY when FaitUserId is null.
          It must not be called on every login for users who already have it set.
          The guard `if (string.IsNullOrEmpty(user.FaitUserId))` must be present.
          Without it, every page load makes an HTTP request to FAIT.

⚠️  HIGH: Verify the new PushDocumentAsync checks for already-pushed status
          BEFORE uploading to S3. The check is:
          `db.KbPushes.AnyAsync(p => p.MeetingId == meetingId && p.DocType == docType && p.KbScope == scope)`
          This prevents duplicate S3 uploads and double-triggering ingestion.

⚠️  HIGH: Verify GetAudio() returns Redirect(url) not Ok(new { url }).
          The old code returns JSON — the fix is a single-line change to
          `return Redirect(url)`. Confirm the S3 presigned URL has
          Content-Disposition=attachment set (or browser may play inline).
          If not, the S3 presigned URL needs a ResponseContentDisposition
          param added to GeneratePresignedUrlAsync.

⚠️  MEDIUM: Verify MeetingDetail.razor no longer uses IHttpClientFactory
            for the push calls. Confirm both PushTranscript() and PushSummary()
            use the injected `Http` (Blazor's default HttpClient with base address),
            not a factory-created client with no base address.

⚠️  MEDIUM: Verify the new push-to-kb endpoint preserves backward compat:
            the old push-transcript-to-kb and push-summary-to-kb endpoints must
            still return 200 (not 404) since they may be bookmarked or called
            by existing retry logic. Mark with [Obsolete] attribute and leave body
            intact.

⚠️  MEDIUM: Verify Firm:SharedSecret is set in BOTH FIRM and FAIT ECS task
            definitions and that the values match. The resolve-user call will
            silently fail (non-fatal) if they don't match — but KB push will
            be broken for all users. Confirm in the deploy checklist.

⚠️  LOW: Verify firm_meeting_kb_pushes table is created idempotently.
         The CREATE TABLE IF NOT EXISTS must be in DatabaseInitializationService
         extraTables. On a fresh DB and on an existing DB with the table already
         created, startup must not throw.
```

---

## Part 8: What This Spec Does NOT Include

**Scheduled meeting join:** Fred specified "manual join (not scheduled)" for v1. The `MeetingStatus.Scheduled` enum value and `ScheduledAt` DB column exist from earlier development. These are v2.

**Multiple team KBs:** The model supports multiple Team KBs (by design: `kb_id` is stored). But v1 UI has only one Team KB toggle. Multiple team KBs (per team/project) are v2.

**VP bot codebase:** The `firm-vpbot` ECS container is a separate project. This spec only covers the FIRM web app's interface with it (the `TriggerBotAsync` launch + the `/api/vp/callback` receiver). The bot itself must be deployed separately.

**Auto-add settings UI:** `FirmIntegrationController` checks `config.FirmAutoTranscript` and `config.FirmAutoSummary` on the FAIT `UserAssistantConfig`. There is no FIRM-side settings page to toggle these. That's v2 (or a FAIT settings page change).

**`FirmIntegrationController` multi-user fix:** The `resolve-user` endpoint in FAIT still has the single-user workaround (returns first active Entra user). The proper fix (add `EntraOid` column to FAIT's `AppUser`) is a FAIT change, not a FIRM change. The `FaitUserId` resolution in Task 1 works correctly because FIRM now calls the endpoint at login time — FAIT's response may be wrong in multi-user scenarios until FAIT's `AppUser` model is updated. This is a known limitation, acceptable for v1 (single-tenant, single Entra user).

---

_Audit + spec by Reed Richards | FIRM v1: 1 new file + 5 modified. Critical path: FaitUserId resolution → manual KB push works → multi-KB UI complete._
