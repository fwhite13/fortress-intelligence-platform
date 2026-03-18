# CC Brief: WI844 — FIRM v1: Fix 5 Blocking Gaps

You are implementing fixes to the FIRM (FortressIntelligenceRM) web application.
Working directory: `/home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web/`
DO NOT touch any files outside `fip/firm/`.

---

## CRITICAL RULES — Non-Negotiable

### Rule 1: ResolveFaitUserIdAsync guard fires ONLY when FaitUserId IS NULL
In `GetOrCreateUserAsync`, AFTER fetching/creating the user, check:
```csharp
if (!string.IsNullOrEmpty(user.FaitUserId)) return user; // already resolved — skip
```
Only call `ResolveFaitUserIdAsync` if FaitUserId is null/empty.
Wrap in try/catch, log warning on failure, NEVER throw to caller.

### Rule 2: PushDocumentAsync checks firm_meeting_kb_pushes BEFORE S3 upload
```csharp
// FIRST — check for existing push record
var existing = await db.FirmMeetingKbPushes
    .FirstOrDefaultAsync(p => p.MeetingId == meetingId && p.DocType == docType && p.KbScope == scope);
if (existing != null) continue; // skip — already pushed

// THEN — do S3 upload + Bedrock ingestion
```

### Rule 3: GetAudio() returns Redirect(url) not Ok(new { url })
```csharp
return Redirect(url);  // NOT: return Ok(new { url });
```

### Rule 4: Old endpoints keep FULL body — just mark [Obsolete]
```csharp
[Obsolete("Use /push-to-kb instead")]
[HttpPost("{id}/push-transcript-to-kb")]
public async Task<IActionResult> PushTranscriptToKb(long id) { /* KEEP FULL BODY — do NOT gut */ }

[Obsolete("Use /push-to-kb instead")]
[HttpPost("{id}/push-summary-to-kb")]
public async Task<IActionResult> PushSummaryToKb(long id) { /* KEEP FULL BODY — do NOT gut */ }
```
Do NOT return 410. Keep existing behavior exactly.

### Rule 5: firm_meeting_kb_pushes MUST be in DatabaseInitializationService.extraTables
Add this entry to the `extraTables` array:
```csharp
("firm_meeting_kb_pushes", @"CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    meeting_id BIGINT NOT NULL,
    doc_type VARCHAR(20) NOT NULL,
    kb_scope VARCHAR(50) NOT NULL,
    kb_id VARCHAR(100) NULL,
    pushed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uq_push (meeting_id, doc_type, kb_scope),
    INDEX idx_meeting (meeting_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"),
```

### Rule 6: HttpClient base address fix in MeetingDetail.razor
Replace `@inject IHttpClientFactory HttpClientFactory` with `@inject HttpClient Http`.
Replace all `HttpClientFactory.CreateClient()` usages with direct `Http` usage.

---

## Task 1: Services/MeetingService.cs — FaitUserId Resolution

The current `GetOrCreateUserAsync` does NOT call FAIT's resolve-user endpoint. Fix it.

**Current constructor:**
```csharp
public MeetingService(IDbContextFactory<FirmDbContext> dbFactory, ILogger<MeetingService> logger)
```

**New constructor (add IHttpClientFactory and IConfiguration):**
```csharp
private readonly IHttpClientFactory _httpClientFactory;
private readonly IConfiguration _config;

public MeetingService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, ILogger<MeetingService> logger, IHttpClientFactory httpClientFactory)
{
    _dbFactory = dbFactory;
    _config = config;
    _logger = logger;
    _httpClientFactory = httpClientFactory;
}
```

**Updated GetOrCreateUserAsync** — add FaitUserId resolution AFTER saving user:
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

    // Populate FaitUserId if not already set — best-effort, never throws
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

private record ResolveFaitUserResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("userId")] string UserId);
```

The `using System.Net.Http.Json;` namespace may be needed — add if not present. `IHttpClientFactory` is already registered in `Program.cs` — no new registrations needed.

---

## Task 2: Controllers/MeetingsApiController.cs — Audio Redirect + New Endpoints

### 2A: Fix GetAudio — return Redirect instead of Ok
Find the existing `GetAudio` method:
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
    return Ok(new { url });  // <-- CHANGE THIS LINE
}
```

Change the last line to:
```csharp
    return Redirect(url);
```

### 2B: Add [Obsolete] to old push endpoints (keep full body)
```csharp
[Obsolete("Use /push-to-kb instead")]
[HttpPost("/api/meetings/{id}/push-transcript-to-kb")]
[Authorize]
public async Task<IActionResult> PushTranscriptToKb(long id)
{
    // KEEP ALL EXISTING BODY UNCHANGED
}

[Obsolete("Use /push-to-kb instead")]
[HttpPost("/api/meetings/{id}/push-summary-to-kb")]
[Authorize]
public async Task<IActionResult> PushSummaryToKb(long id)
{
    // KEEP ALL EXISTING BODY UNCHANGED
}
```

### 2C: Add new POST /push-to-kb endpoint
Add AFTER the existing push endpoints:

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

---

## Task 3: Components/Pages/MeetingDetail.razor — HttpClient Base Address Fix + Multi-KB UI

### 3A: Replace IHttpClientFactory injection
Remove:
```razor
@inject IHttpClientFactory HttpClientFactory
```
Add:
```razor
@inject HttpClient Http
```

### 3B: Add KB state fields to @code block
In the `@code` section, add these fields alongside the existing `_pushingTranscript` and `_pushingSummary`:
```csharp
// KB push status (loaded from /kb-status on init)
private HashSet<string> _transcriptPushedTo = new();
private HashSet<string> _summaryPushedTo = new();

// KB selection state
private bool _transcriptSelectPersonal = true;
private bool _transcriptSelectTeam = false;
private bool _summarySelectPersonal = true;
private bool _summarySelectTeam = false;
```

### 3C: Load KB status in OnInitializedAsync
After loading `_meeting`, add:
```csharp
// Load KB push status
if (_meeting != null && _meeting.Status == MeetingStatus.Complete)
{
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
}
```

Add record in @code:
```csharp
private record KbStatusResponse(List<string>? Transcript, List<string>? Summary);
```

### 3D: Replace the two old KB push buttons with multi-KB panel
Find the section in the markup that renders the `Add Transcript to KB` and `Add Summary to KB` MudButtons (the two buttons with `OnClick="PushTranscriptToKb"` and `OnClick="PushSummaryToKb"`).

Replace those two MudButton elements with this KB push panel:

```razor
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

### 3E: Replace old PushTranscriptToKb() and PushSummaryToKb() methods
Remove the old methods and add these:

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

## Task 4: New Model + DbContext + DatabaseInitializationService

### 4A: Create Models/FirmMeetingKbPush.cs (NEW FILE)
```csharp
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
    public string? KbId { get; set; }
    public DateTime PushedAt { get; set; } = DateTime.UtcNow;
    public FirmMeeting? Meeting { get; set; }
}
```

### 4B: Data/FirmDbContext.cs — add DbSet and OnModelCreating mapping
Add DbSet AFTER existing Summaries DbSet:
```csharp
public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();
```

Add in OnModelCreating (after FirmMeetingSummary entity config):
```csharp
modelBuilder.Entity<FirmMeetingKbPush>(entity =>
{
    entity.ToTable("firm_meeting_kb_pushes");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).ValueGeneratedOnAdd();
    entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
    entity.Property(e => e.DocType).HasColumnName("doc_type").HasMaxLength(20).IsRequired();
    entity.Property(e => e.KbScope).HasColumnName("kb_scope").HasMaxLength(50).IsRequired();
    entity.Property(e => e.KbId).HasColumnName("kb_id").HasMaxLength(100);
    entity.Property(e => e.PushedAt).HasColumnName("pushed_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
    entity.HasOne(e => e.Meeting)
        .WithMany()
        .HasForeignKey(e => e.MeetingId)
        .OnDelete(DeleteBehavior.Cascade)
        .HasConstraintName("fk_fmkp_meeting");
    entity.HasIndex(e => new { e.MeetingId, e.DocType, e.KbScope }).HasDatabaseName("idx_fmkp_lookup");
});
```

### 4C: Data/DatabaseInitializationService.cs — add firm_meeting_kb_pushes to extraTables
Add this tuple to the `extraTables` array AFTER the `firm_meeting_summaries` entry:
```csharp
("firm_meeting_kb_pushes", @"CREATE TABLE IF NOT EXISTS firm_meeting_kb_pushes (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    meeting_id BIGINT NOT NULL,
    doc_type VARCHAR(20) NOT NULL,
    kb_scope VARCHAR(50) NOT NULL,
    kb_id VARCHAR(100) NULL,
    pushed_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uq_push (meeting_id, doc_type, kb_scope),
    INDEX idx_meeting (meeting_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"),
```

DO NOT REMOVE existing `transcript_kb_pushed` and `summary_kb_pushed` columns or ALTER TABLE statements — they are still used by the auto-complete flow.

---

## Task 5: Services/FirmKbService.cs — PushDocumentAsync + GetPushedScopesAsync

Add these methods to `FirmKbService`. Keep the existing `PushTranscriptAsync` and `PushSummaryAsync` methods — do NOT remove them.

The `FirmDbContext` has a `FirmMeetingKbPushes` DbSet — use `db.FirmMeetingKbPushes` for dedup checks.

Add the namespace import `using FortressIntelligenceRM.Web.Models;` if not already present (it should be).

```csharp
/// <summary>
/// Push meeting content (transcript or summary) to one or more KB scopes.
/// Checks for existing push record BEFORE uploading to S3 — dedup is mandatory.
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
        // DEDUP CHECK FIRST — before any S3 upload
        var existing = await db.FirmMeetingKbPushes
            .FirstOrDefaultAsync(p => p.MeetingId == meetingId && p.DocType == docType && p.KbScope == scope);
        if (existing != null)
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
            s3Prefix = "kb-docs/team/firm";
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

            // For personal KB: write metadata.json for KB isolation
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

            // Trigger Bedrock ingestion
            await StartIngestionAsync(kbId, dsId);

            // Record the push in firm_meeting_kb_pushes
            db.FirmMeetingKbPushes.Add(new FirmMeetingKbPush
            {
                MeetingId = meetingId,
                DocType = docType,
                KbScope = scope,
                KbId = kbId,
                PushedAt = DateTime.UtcNow
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
    var pushed = await db.FirmMeetingKbPushes
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
    var sb = new StringBuilder();
    foreach (var seg in segments)
    {
        var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
        var ts = seg.StartTimeMs.HasValue
            ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
            : "00:00:00";
        sb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
    }
    return (sb.ToString(), "text/plain", "txt");
}

private async Task<(string content, string contentType, string extension)> BuildSummaryContentAsync(FirmDbContext db, long meetingId)
{
    var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == meetingId);
    if (summary == null) return ("", "", "");
    var sb = new StringBuilder();
    if (!string.IsNullOrEmpty(summary.SummaryText))
    {
        sb.AppendLine("## Overview");
        sb.AppendLine(summary.SummaryText);
        sb.AppendLine();
    }
    if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
    {
        var decisions = TryDeserializeList(summary.KeyDecisionsJson);
        if (decisions.Any())
        {
            sb.AppendLine("## Key Decisions");
            decisions.ForEach(d => sb.AppendLine($"- {d}"));
            sb.AppendLine();
        }
    }
    if (!string.IsNullOrEmpty(summary.ActionItemsJson))
    {
        sb.AppendLine("## Action Items");
        try
        {
            var items = JsonSerializer.Deserialize<List<ActionItemDto>>(summary.ActionItemsJson) ?? new();
            items.ForEach(i => sb.AppendLine($"- [{i.Owner ?? "?"}] {i.Description}"));
        }
        catch { }
        sb.AppendLine();
    }
    if (!string.IsNullOrEmpty(summary.FollowUpsJson))
    {
        var followUps = TryDeserializeList(summary.FollowUpsJson);
        if (followUps.Any())
        {
            sb.AppendLine("## Follow-ups");
            followUps.ForEach(f => sb.AppendLine($"- {f}"));
        }
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

Keep `StartPersonalIngestionAsync` method as-is — it's still used by `PushTranscriptAsync` and `PushSummaryAsync`.

---

## Files to Modify Summary

| File | Action |
|------|--------|
| `Services/MeetingService.cs` | Add IConfiguration + IHttpClientFactory to constructor; add FaitUserId null guard + ResolveFaitUserIdAsync |
| `Controllers/MeetingsApiController.cs` | Fix GetAudio to Redirect; add [Obsolete] to old push endpoints; add PushToKb + GetKbStatus endpoints |
| `Components/Pages/MeetingDetail.razor` | Replace IHttpClientFactory with Http; add KB state; update OnInitializedAsync; replace KB buttons with multi-KB panel; replace push methods |
| `Models/FirmMeetingKbPush.cs` | CREATE NEW FILE |
| `Data/FirmDbContext.cs` | Add DbSet<FirmMeetingKbPush> + OnModelCreating config |
| `Data/DatabaseInitializationService.cs` | Add firm_meeting_kb_pushes to extraTables array |
| `Services/FirmKbService.cs` | Add PushDocumentAsync, GetPushedScopesAsync, BuildTranscriptContentAsync, BuildSummaryContentAsync, StartIngestionAsync; keep existing methods |

## Build Verification
After all changes, run:
```bash
cd /home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web
dotnet build
```
Fix any compilation errors.
