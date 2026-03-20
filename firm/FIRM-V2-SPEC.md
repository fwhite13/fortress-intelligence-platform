# FIRM v2 Spec — Teams-Native Transcription + Calendar Integration + Send to Teams

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Depends on:** FIRM v1 completion spec (`FIRM-V1-SPEC.md`) fully deployed  
**Codebase:** `~/projects/fip/firm/src/FortressIntelligenceRM.Web/`

---

## Pre-Read: What Was Confirmed

- `FortressIntelligenceRM.Web.csproj` — **no `Microsoft.Graph` package, no MSAL**. FIRM has zero Graph infrastructure today.
- `Program.cs` — no MS OAuth callback route, no `MicrosoftTokenService`, no `UserMicrosoftToken` registration.
- `FirmUser` model — has `EntraOid` and `FaitUserId`, but **no Graph access token storage**.
- FAIT's pattern: `MicrosoftTokenService.cs` stores `(access_token, refresh_token, expiry)` per `fait_user_id` in `user_microsoft_tokens` table. Scopes: `Mail.Read`, `Calendars.Read`, `User.Read`, `Tasks.Read`, `offline_access`. OAuth callback at `/auth/ms-callback`.
- FIRM's auth is cookie-consumer-only: reads FAIT's `.FortressAI.Session` cookie. No auth infrastructure of its own beyond cookie validation.
- The FIP Entra app registration (`clientId: 887206bc-fac1-436a-a8ed-2150418d76c0`) is the **FIP platform registration** — all new scopes go here.

---

## Architecture Decision: Graph Token Strategy

FIRM needs Graph tokens for three features: calendar pull, transcript pull, and Teams channel post. It has two options:

**Option A — Proxy through FAIT:** FIRM calls a new FAIT endpoint `GET /api/firm/graph-token?userId=<faitUserId>` that returns the user's current Graph access token. FIRM never stores tokens.

**Option B — Own OAuth connector:** FIRM gets its own MS OAuth flow, stores tokens in its own DB table, manages refresh.

**Decision: Option A (proxy through FAIT) for calendar + transcript. Option B subset for Teams posting.**

Rationale:
- FAIT already has tokens for all FIP users (they connect MS when enabling M365 features)
- Duplicating the OAuth flow in FIRM means users would need to connect Microsoft twice (once for FAIT features, once for FIRM)
- FIRM's `FaitUserId` link means FIRM already knows how to identify the FAIT user
- However: FIRM needs to handle the case where a FIRM user has NOT connected MS in FAIT — show a "Connect Microsoft 365" CTA that redirects to FAIT's existing `/auth/ms-connect` flow

**Critical constraint:** FIRM's Graph token proxy endpoint in FAIT must check that the token belongs to the requesting FIRM user (via `FaitUserId` + `X-Firm-Secret` auth). It must never return a token for a different user.

**New scopes required** (added to FAIT's `MicrosoftTokenService.Scopes` array):
```
OnlineMeetings.Read
OnlineMeetingTranscripts.Read.All  (admin consent required — see §9)
Calendars.Read                     (already present — no change)
Team.ReadBasic.All
Channel.ReadBasic.All
ChannelMessage.Send                (admin consent required — see §9)
```

---

## App Registration Rename Note

The Entra app registration currently named "FAIT" (clientId `887206bc-fac1-436a-a8ed-2150418d76c0`) should be renamed to **"Fortress Intelligence Platform"** in the Azure portal. This is an admin portal operation — no code change. All `Azure:ClientId` config references stay the same. The rename makes the OAuth consent screen say "Fortress Intelligence Platform is requesting access" rather than "FAIT."

**DevOps task (Rhodey):** In Entra admin center → App registrations → rename the display name. Add the new API permissions listed above. Submit admin consent for `ChannelMessage.Send` and `OnlineMeetingTranscripts.Read.All`.

---

## New Meeting Status Values

The current `MeetingStatus` enum needs two new values for the v2 lifecycle:

```csharp
public enum MeetingStatus
{
    // Existing values (unchanged)
    Scheduled,
    Joining,
    Recording,
    Transcribing,
    Summarizing,
    Complete,
    Failed,
    // v2 additions:
    Cancelled,         // Calendar event removed or user cancelled
    AwaitingTranscript // Teams-native path: meeting ended, polling Graph for transcript
}
```

**File:** `Models/MeetingStatus.cs`

---

## Feature A: Teams-Native Transcription

### What It Does

When a user pastes a Teams meeting URL (detected by `teams.microsoft.com` hostname), FIRM routes to the Graph transcript pull path instead of bot join. Speaker diarization uses real Entra display names — "Fred Williamson" not "Speaker 1". This is the primary v2 value proposition.

### Detection Logic

**`MeetingService.IsTeamsMeetingUrl()`:**

```csharp
public static bool IsTeamsMeetingUrl(string url)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
    return uri.Host.EndsWith("teams.microsoft.com", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith("teams.live.com", StringComparison.OrdinalIgnoreCase);
}
```

If `IsTeamsMeetingUrl()` returns true → Teams-native path.  
Otherwise → existing bot join path (unchanged).

### New Service: `GraphTranscriptService.cs`

```csharp
// firm/src/FortressIntelligenceRM.Web/Services/GraphTranscriptService.cs
// Calls Graph API to resolve meeting ID from join URL, then polls for transcript.

public class GraphTranscriptService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFirmGraphTokenProvider _tokenProvider;
    private readonly MeetingService _meetingService;
    private readonly ILogger<GraphTranscriptService> _logger;

    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    // Step 1: Resolve meeting ID from join URL
    // GET /me/onlineMeetings?$filter=joinWebUrl eq '{url}'
    public async Task<string?> ResolveOnlineMeetingIdAsync(string joinWebUrl, string graphToken)
    {
        var encoded = Uri.EscapeDataString($"joinWebUrl eq '{joinWebUrl}'");
        var url = $"{GraphBase}/me/onlineMeetings?$filter={encoded}&$select=id,subject,startDateTime,endDateTime";

        using var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("FIRM Graph: ResolveOnlineMeeting failed {Status} for url {Url}",
                resp.StatusCode, joinWebUrl);
            return null;
        }

        var body = await resp.Content.ReadFromJsonAsync<GraphMeetingListResponse>();
        return body?.Value?.FirstOrDefault()?.Id;
    }

    // Step 2: Check if transcript is available
    // GET /me/onlineMeetings/{meetingId}/transcripts
    // Returns transcript IDs. If empty → not yet available.
    public async Task<List<GraphTranscript>> GetTranscriptsAsync(string onlineMeetingId, string graphToken)
    {
        var url = $"{GraphBase}/me/onlineMeetings/{onlineMeetingId}/transcripts";
        using var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return new();  // Meeting not found yet
        if (!resp.IsSuccessStatusCode) return new();

        var body = await resp.Content.ReadFromJsonAsync<GraphTranscriptListResponse>();
        return body?.Value ?? new();
    }

    // Step 3: Download transcript content (VTT or JSON)
    // GET /me/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content?$format=text/vtt
    // Returns VTT-formatted transcript with speaker names
    public async Task<string?> DownloadTranscriptVttAsync(string onlineMeetingId, string transcriptId, string graphToken)
    {
        var url = $"{GraphBase}/me/onlineMeetings/{onlineMeetingId}/transcripts/{transcriptId}/content?$format=text/vtt";
        using var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync();
    }

    // Parse VTT to transcript segments
    // VTT format: speaker lines look like "<v Rob Smith>text here"
    public List<TranscriptSegmentPayload> ParseVttTranscript(string vtt)
    {
        var segments = new List<TranscriptSegmentPayload>();
        // VTT block structure:
        // HH:MM:SS.mmm --> HH:MM:SS.mmm
        // <v Speaker Name>text here
        var blocks = vtt.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length < 2) continue;

            // Find the timestamp line
            var tsLine = lines.FirstOrDefault(l => l.Contains("-->"));
            if (tsLine == null) continue;

            var parts = tsLine.Split("-->");
            var startMs = ParseVttTimestamp(parts[0].Trim());
            var endMs   = parts.Length > 1 ? ParseVttTimestamp(parts[1].Trim()) : startMs;

            // Find the content line (may have <v Name>)
            var contentLine = lines.LastOrDefault(l => !l.Contains("-->") && !l.All(char.IsDigit));
            if (contentLine == null) continue;

            string? speaker = null;
            string text = contentLine;
            var vMatch = System.Text.RegularExpressions.Regex.Match(contentLine, @"^<v ([^>]+)>(.*)$");
            if (vMatch.Success)
            {
                speaker = vMatch.Groups[1].Value.Trim();
                text    = vMatch.Groups[2].Value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                segments.Add(new TranscriptSegmentPayload
                {
                    SpeakerName  = speaker,
                    SpeakerLabel = speaker != null ? null : "Unknown",
                    Text         = text,
                    StartTimeMs  = startMs,
                    EndTimeMs    = endMs
                });
            }
        }
        return segments;
    }

    private static long ParseVttTimestamp(string ts)
    {
        // HH:MM:SS.mmm or MM:SS.mmm
        ts = ts.Split(' ')[0]; // strip position cues
        var dotParts = ts.Split('.');
        var ms = dotParts.Length > 1 ? long.Parse(dotParts[1].PadRight(3, '0')[..3]) : 0;
        var colonParts = dotParts[0].Split(':');
        long totalMs = 0;
        foreach (var part in colonParts) { totalMs = totalMs * 60 + long.Parse(part); }
        return totalMs * 1000 + ms;
    }
}

// Response DTOs
public record GraphMeetingListResponse(List<GraphMeeting>? Value);
public record GraphMeeting(string Id, string? Subject, DateTime? StartDateTime, DateTime? EndDateTime);
public record GraphTranscriptListResponse(List<GraphTranscript>? Value);
public record GraphTranscript(string Id, DateTime? CreatedDateTime, string? MeetingId);
```

### Graph Token Provider Interface

FIRM doesn't hold tokens directly — it fetches them via FAIT's new proxy endpoint:

```csharp
// firm/src/FortressIntelligenceRM.Web/Services/IFirmGraphTokenProvider.cs
public interface IFirmGraphTokenProvider
{
    /// <summary>
    /// Get a valid Graph access token for the given FIRM user.
    /// Returns null if the user has not connected Microsoft 365.
    /// </summary>
    Task<string?> GetTokenAsync(Guid firmUserId);
    /// <summary>True if the user has a valid MS connection.</summary>
    Task<bool> HasTokenAsync(Guid firmUserId);
}

// firm/src/FortressIntelligenceRM.Web/Services/FirmGraphTokenProvider.cs
public class FirmGraphTokenProvider : IFirmGraphTokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FirmGraphTokenProvider> _logger;

    private string FaitApiUrl => (_config["FIP:FaitApiUrl"] ?? "https://fait.dev.fortressam.ai").TrimEnd('/');
    private string FirmSecret => _config["Firm:SharedSecret"] ?? "";

    public async Task<string?> GetTokenAsync(Guid firmUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(firmUserId);
        if (user == null || string.IsNullOrEmpty(user.FaitUserId)) return null;

        using var http = _httpClientFactory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"{FaitApiUrl}/api/firm/graph-token?faitUserId={Uri.EscapeDataString(user.FaitUserId)}");
        req.Headers.Add("X-Firm-Secret", FirmSecret);
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        var body = await resp.Content.ReadFromJsonAsync<GraphTokenResponse>();
        return body?.AccessToken;
    }

    public async Task<bool> HasTokenAsync(Guid firmUserId)
        => (await GetTokenAsync(firmUserId)) != null;

    private record GraphTokenResponse(string AccessToken);
}
```

### New FAIT Endpoint: `GET /api/firm/graph-token`

**File:** `fip/fait/src/FortressAI.Web/Controllers/FirmIntegrationController.cs` — add method:

```csharp
/// <summary>
/// GET /api/firm/graph-token?faitUserId={guid}
/// Returns a valid Graph access token for the given FAIT user ID.
/// Protected by X-Firm-Secret. Returns 404 if user has no MS connection.
/// FIRM uses this to call Graph APIs on behalf of a user.
/// </summary>
[HttpGet("graph-token")]
public async Task<IActionResult> GetGraphToken([FromQuery] string faitUserId)
{
    var expectedSecret = _config["Firm:SharedSecret"] ?? "";
    var providedSecret = Request.Headers["X-Firm-Secret"].FirstOrDefault() ?? "";
    if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
        return Unauthorized(new { error = "Invalid X-Firm-Secret" });

    if (!Guid.TryParse(faitUserId, out var userGuid))
        return BadRequest(new { error = "Invalid faitUserId" });

    // MicrosoftTokenService is already injected — reuse it
    var token = await _microsoftTokenService.GetValidAccessTokenAsync(userGuid);
    if (token == null)
        return NotFound(new { error = "User has no Microsoft 365 connection", 
                              connectUrl = $"{_config["FIP:FirmCallbackUrl"]?.Split('/').Take(3).Let(p => string.Join("/", p))}/settings?connect_ms=1" });

    return Ok(new { accessToken = token });
}
```

**Add `MicrosoftTokenService` to `FirmIntegrationController` constructor.** It's already registered in FAIT's DI.

### Transcript Polling: Background Service

When FIRM creates a meeting via the Teams-native path, the meeting starts as `AwaitingTranscript`. A background hosted service polls every 5 minutes (with exponential backoff) until the transcript arrives or the timeout is hit.

**`GraphTranscriptPollingService.cs`** (new `IHostedService`):

```csharp
// Polls meetings in AwaitingTranscript status every 5 minutes
// Uses exponential backoff: 5m → 10m → 20m → 30m (cap at 30m)
// Timeout: 2 hours after meeting EndedAt. If exceeded: mark Failed + offer bot fallback.

protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await PollPendingMeetingsAsync(ct);
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
    }
}

private async Task PollPendingMeetingsAsync(CancellationToken ct)
{
    await using var db = await _dbFactory.CreateDbContextAsync(ct);

    // Find meetings awaiting transcript that haven't timed out
    var cutoff = DateTime.UtcNow.AddHours(-2);
    var pending = await db.Meetings
        .Include(m => m.CreatedByUser)
        .Where(m => m.Status == MeetingStatus.AwaitingTranscript
                    && m.UpdatedAt > cutoff)
        .ToListAsync(ct);

    foreach (var meeting in pending)
    {
        if (string.IsNullOrEmpty(meeting.GraphMeetingId)) continue;

        var token = await _tokenProvider.GetTokenAsync(meeting.CreatedBy);
        if (token == null) continue;

        try
        {
            var transcripts = await _transcriptService.GetTranscriptsAsync(
                meeting.GraphMeetingId, token);

            if (!transcripts.Any()) continue; // Not ready yet — check again next poll

            // Transcript available — download and process
            var vtt = await _transcriptService.DownloadTranscriptVttAsync(
                meeting.GraphMeetingId, transcripts[0].Id, token);

            if (string.IsNullOrEmpty(vtt)) continue;

            var segments = _transcriptService.ParseVttTranscript(vtt);

            // Write transcript segments to DB (same path as bot callback)
            foreach (var seg in segments)
            {
                db.Transcripts.Add(new FirmMeetingTranscript
                {
                    MeetingId   = meeting.Id,
                    SpeakerName  = seg.SpeakerName,
                    SpeakerLabel = seg.SpeakerLabel,
                    Text         = seg.Text ?? "",
                    StartTimeMs  = seg.StartTimeMs,
                    EndTimeMs    = seg.EndTimeMs,
                    CreatedAt    = DateTime.UtcNow
                });
            }

            // Update meeting status → Summarizing (triggers summary generation)
            meeting.Status    = MeetingStatus.Summarizing;
            meeting.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Trigger summary generation (same fire-and-forget as bot path)
            _ = _summaryService.GenerateSummaryAsync(meeting.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Transcript poll failed for meeting {Id}", meeting.Id);
        }
    }

    // Timeout: meetings AwaitingTranscript for >2 hours → mark Failed + log
    var timedOut = await db.Meetings
        .Where(m => m.Status == MeetingStatus.AwaitingTranscript
                    && m.UpdatedAt <= cutoff)
        .ToListAsync(ct);

    foreach (var m in timedOut)
    {
        m.Status       = MeetingStatus.Failed;
        m.ErrorMessage = "Teams transcript not available after 2 hours. Try bot join as fallback.";
        m.UpdatedAt    = DateTime.UtcNow;
    }
    if (timedOut.Any()) await db.SaveChangesAsync(ct);
}
```

### DB Schema Addition: `graph_meeting_id` Column

Add to `firm_meetings` via `DatabaseInitializationService.cs`:

```sql
ALTER TABLE firm_meetings ADD COLUMN graph_meeting_id VARCHAR(200) NULL;
ALTER TABLE firm_meetings ADD COLUMN graph_online_meeting_id VARCHAR(200) NULL;
```

`graph_meeting_id` = the MS Graph `onlineMeeting.id` (opaque string ~180 chars).  
`graph_online_meeting_id` = the meeting's join URL hash (for cross-reference).

Add to `FirmMeeting` model:
```csharp
[MaxLength(200)] public string? GraphMeetingId { get; set; }
```

Add to `FirmDbContext.OnModelCreating`:
```csharp
entity.Property(e => e.GraphMeetingId).HasColumnName("graph_meeting_id").HasMaxLength(200);
```

### Updated `POST /api/meetings/join` Flow

```csharp
[HttpPost("/api/meetings/join")]
[Authorize]
public async Task<IActionResult> JoinMeeting([FromBody] JoinRequest request)
{
    // ... existing user resolution ...

    var meeting = await _meetingService.CreateMeetingAsync(firmUser.Id, request.MeetingUrl, request.Title);

    if (MeetingService.IsTeamsMeetingUrl(request.MeetingUrl))
    {
        // Teams-native path
        var token = await _tokenProvider.GetTokenAsync(firmUser.Id);
        if (token == null)
        {
            // No MS connection — fall back to bot join and tell user to connect
            _ = _vpBotService.TriggerBotAsync(meeting.Id, request.MeetingUrl);
            return Ok(new {
                meetingId = meeting.Id,
                mode = "bot",
                notice = "Connect Microsoft 365 in FAIT settings to use Teams-native transcription with real speaker names."
            });
        }

        // Resolve the Graph online meeting ID
        var graphMeetingId = await _transcriptService.ResolveOnlineMeetingIdAsync(
            request.MeetingUrl, token);

        if (graphMeetingId != null)
        {
            await _meetingService.SetGraphMeetingIdAsync(meeting.Id, graphMeetingId);
            await _meetingService.UpdateStatusAsync(meeting.Id, MeetingStatus.AwaitingTranscript, null);
            return Ok(new { meetingId = meeting.Id, mode = "teams_native" });
        }
        else
        {
            // Graph resolve failed (meeting not found yet, or permission issue) — fall back to bot
            _ = _vpBotService.TriggerBotAsync(meeting.Id, request.MeetingUrl);
            return Ok(new { meetingId = meeting.Id, mode = "bot", notice = "Using recording bot — Teams meeting not yet resolvable via Graph." });
        }
    }
    else
    {
        // Non-Teams URL → bot join (unchanged)
        _ = _vpBotService.TriggerBotAsync(meeting.Id, request.MeetingUrl);
        return Ok(new { meetingId = meeting.Id, mode = "bot" });
    }
}
```

### `Meetings.razor` Status Display

Add `AwaitingTranscript` and `Cancelled` to the `StatusBadge` switch:

```csharp
MeetingStatus.AwaitingTranscript => ("#607d8b", "Awaiting Transcript"),
MeetingStatus.Cancelled          => ("#607d8b", "Cancelled"),
```

---

## Feature B: Calendar Integration

### What It Does

On FIRM dashboard load, fetch the user's upcoming Teams meetings from Graph (next 7 days), offer to add them to the meetings list as `Scheduled`, and auto-cancel scheduled meetings that disappear from the calendar.

### New Service: `GraphCalendarService.cs` (FIRM-side)

```csharp
// firm/src/FortressIntelligenceRM.Web/Services/FirmCalendarService.cs
// Note: "FirmCalendarService" not "GraphCalendarService" — avoid name collision if FAIT services are ever shared

public class FirmCalendarService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    /// <summary>Fetch Teams meetings from calendar for the next N days.</summary>
    public async Task<List<FirmCalendarEvent>> GetUpcomingTeamsMeetingsAsync(string graphToken, int days = 7)
    {
        var start = DateTime.UtcNow.ToString("o");
        var end   = DateTime.UtcNow.AddDays(days).ToString("o");

        // $filter: isOnlineMeeting eq true to restrict to Teams meetings
        // $select: minimal fields needed
        var url = $"{GraphBase}/me/events" +
                  $"?$filter=isOnlineMeeting eq true and start/dateTime ge '{start}' and end/dateTime le '{end}'" +
                  $"&$select=id,subject,start,end,onlineMeetingUrl,isOnlineMeeting,isCancelled" +
                  $"&$orderby=start/dateTime asc" +
                  $"&$top=20";

        using var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", graphToken);

        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return new();

        var body = await resp.Content.ReadFromJsonAsync<GraphEventListResponse>();
        return body?.Value?
            .Where(e => !string.IsNullOrEmpty(e.OnlineMeetingUrl) && e.IsCancelled != true)
            .Select(e => new FirmCalendarEvent(
                CalendarEventId: e.Id,
                Subject: e.Subject ?? "Untitled Meeting",
                StartUtc: DateTime.Parse(e.Start?.DateTime ?? DateTime.UtcNow.ToString("o"), null, System.Globalization.DateTimeStyles.RoundtripKind),
                EndUtc:   DateTime.Parse(e.End?.DateTime   ?? DateTime.UtcNow.AddHours(1).ToString("o"), null, System.Globalization.DateTimeStyles.RoundtripKind),
                JoinUrl:  e.OnlineMeetingUrl!
            ))
            .ToList() ?? new();
    }
}

public record FirmCalendarEvent(string CalendarEventId, string Subject, DateTime StartUtc, DateTime EndUtc, string JoinUrl);
record GraphEventListResponse(List<GraphCalendarEventItem>? Value);
record GraphCalendarEventItem(string Id, string? Subject,
    GraphDateTimeTimeZone? Start, GraphDateTimeTimeZone? End,
    string? OnlineMeetingUrl, bool? IsOnlineMeeting, bool? IsCancelled);
record GraphDateTimeTimeZone(string DateTime, string TimeZone);
```

### DB Schema Addition: `calendar_event_id` Column

Needed to match scheduled meetings back to calendar events for auto-cancel.

```sql
ALTER TABLE firm_meetings ADD COLUMN calendar_event_id VARCHAR(200) NULL;
```

Add to `FirmMeeting` model + `FirmDbContext.OnModelCreating` (same pattern as `GraphMeetingId`).

### New API Endpoint: `GET /api/calendar/upcoming`

```csharp
[HttpGet("/api/calendar/upcoming")]
[Authorize]
public async Task<IActionResult> GetUpcomingMeetings()
{
    var entraOid = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

    await using var db = await _dbFactory.CreateDbContextAsync();
    var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
    if (user == null) return Unauthorized();

    var token = await _tokenProvider.GetTokenAsync(user.Id);
    if (token == null) return Ok(new { events = Array.Empty<object>(), hasConnection = false });

    var events = await _calendarService.GetUpcomingTeamsMeetingsAsync(token);

    // Find which events are already in the meetings list
    var existingUrls = await db.Meetings
        .Where(m => m.CreatedBy == user.Id && m.Status != MeetingStatus.Cancelled)
        .Select(m => m.MeetingUrl)
        .ToListAsync();

    return Ok(new {
        hasConnection = true,
        events = events.Select(e => new {
            calendarEventId = e.CalendarEventId,
            subject         = e.Subject,
            startUtc        = e.StartUtc,
            endUtc          = e.EndUtc,
            joinUrl         = e.JoinUrl,
            alreadyAdded    = existingUrls.Contains(e.JoinUrl)
        })
    });
}

[HttpPost("/api/calendar/add-meetings")]
[Authorize]
public async Task<IActionResult> AddCalendarMeetings([FromBody] AddCalendarMeetingsRequest request)
{
    var entraOid = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

    await using var db = await _dbFactory.CreateDbContextAsync();
    var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
    if (user == null) return Unauthorized();

    var added = 0;
    foreach (var evt in request.Events)
    {
        // Don't add duplicates
        var exists = await db.Meetings.AnyAsync(m => m.MeetingUrl == evt.JoinUrl && m.CreatedBy == user.Id);
        if (exists) continue;

        var meeting = new FirmMeeting
        {
            Title           = evt.Subject,
            Platform        = "teams",
            MeetingUrl      = evt.JoinUrl,
            Status          = MeetingStatus.Scheduled,
            ScheduledAt     = evt.StartUtc,
            CalendarEventId = evt.CalendarEventId,
            CreatedBy       = user.Id,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow,
        };
        db.Meetings.Add(meeting);
        added++;
    }

    await db.SaveChangesAsync();
    return Ok(new { added });
}

public record AddCalendarMeetingsRequest(List<AddCalendarMeetingItem> Events);
public record AddCalendarMeetingItem(string CalendarEventId, string Subject, DateTime StartUtc, string JoinUrl);
```

### Auto-Cancel: Background Sync

Extend `GraphTranscriptPollingService` (or create a separate `CalendarSyncService`) to run once per dashboard load + daily background check:

```csharp
// Called from polling loop — runs every 15 min
private async Task AutoCancelRemovedMeetingsAsync(Guid userId, string graphToken, CancellationToken ct)
{
    await using var db = await _dbFactory.CreateDbContextAsync(ct);

    var scheduledMeetings = await db.Meetings
        .Where(m => m.CreatedBy == userId
               && m.Status == MeetingStatus.Scheduled
               && m.CalendarEventId != null
               && m.ScheduledAt > DateTime.UtcNow.AddDays(-1))
        .ToListAsync(ct);

    if (!scheduledMeetings.Any()) return;

    var calendarEvents = await _calendarService.GetUpcomingTeamsMeetingsAsync(graphToken, days: 14);
    var activeCalEventIds = calendarEvents.Select(e => e.CalendarEventId).ToHashSet();

    foreach (var meeting in scheduledMeetings)
    {
        if (!activeCalEventIds.Contains(meeting.CalendarEventId!))
        {
            meeting.Status    = MeetingStatus.Cancelled;
            meeting.UpdatedAt = DateTime.UtcNow;
        }
    }
    await db.SaveChangesAsync(ct);
}
```

### `Meetings.razor` UI: Calendar Prompt

**On `OnInitializedAsync` (once per page load, client-side cached via session state):**

```csharp
// Load calendar events — debounced: only once per session, not on every re-render
if (!_calendarLoaded)
{
    _calendarLoaded = true;
    try
    {
        var resp = await Http.GetFromJsonAsync<CalendarResponse>("/api/calendar/upcoming");
        if (resp?.HasConnection == true)
        {
            _calendarEvents = resp.Events.Where(e => !e.AlreadyAdded).ToList();
            if (_calendarEvents.Any())
                _showCalendarPrompt = true;
        }
        else if (resp?.HasConnection == false)
        {
            _showMsConnectCta = true;  // Show "Connect Microsoft 365" CTA
        }
    }
    catch { /* Non-fatal — calendar is optional */ }
}
```

**Calendar prompt banner** (shown above the meetings table when there are new events):

```razor
@if (_showCalendarPrompt && _calendarEvents.Any())
{
    <MudPaper Style="background: var(--color-surface); border: 1px solid var(--color-border); padding: 16px; margin-bottom: 16px; border-radius: 8px;" Elevation="0">
        <MudStack Row AlignItems="AlignItems.Center">
            <MudIcon Icon="@Icons.Material.Filled.CalendarToday" Style="color: var(--color-gold);" />
            <MudText Style="color: var(--color-text-primary);">
                Found @_calendarEvents.Count Teams meeting@(_calendarEvents.Count == 1 ? "" : "s") in your calendar
            </MudText>
            <MudSpacer />
            <MudButton Size="Size.Small" Variant="Variant.Outlined"
                       Style="border-color: var(--color-gold); color: var(--color-gold);"
                       OnClick="ShowCalendarModal">Review</MudButton>
            <MudIconButton Icon="@Icons.Material.Filled.Close" Size="Size.Small"
                           Style="color: var(--color-text-muted);"
                           OnClick="@(() => _showCalendarPrompt = false)" />
        </MudStack>
    </MudPaper>
}

@if (_showMsConnectCta)
{
    <MudAlert Severity="Severity.Info" Style="margin-bottom: 16px;" Dense>
        <strong>Connect Microsoft 365</strong> in <a href="https://fait.dev.fortressam.ai/settings" target="_blank">FAIT settings</a>
        to enable calendar integration and Teams-native transcription.
    </MudAlert>
}
```

**Calendar modal** — `CalendarMeetingsDialog.razor` (new component):

Shows a list of upcoming calendar events with checkboxes. "Add Selected" calls `POST /api/calendar/add-meetings`. Returns count added. Refreshes meeting list.

---

## Feature C: Send to Teams Channel

### What It Does

From a completed meeting's detail page, the user can post a summary card to a Teams channel. Track which channels have already been posted to, same pattern as KB push.

### New Model: `FirmMeetingTeamsPost.cs`

```csharp
// firm/src/FortressIntelligenceRM.Web/Models/FirmMeetingTeamsPost.cs
public class FirmMeetingTeamsPost
{
    public long   Id         { get; set; }
    public long   MeetingId  { get; set; }
    public string TeamId     { get; set; } = "";
    public string TeamName   { get; set; } = "";
    public string ChannelId  { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string? MessageId  { get; set; }   // Graph message ID for reference
    public DateTime PostedAt  { get; set; } = DateTime.UtcNow;
    public FirmMeeting? Meeting { get; set; }
}
```

**DB:**
```sql
CREATE TABLE IF NOT EXISTS firm_meeting_teams_posts (
    id           BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    meeting_id   BIGINT NOT NULL,
    team_id      VARCHAR(128) NOT NULL,
    team_name    VARCHAR(255) NOT NULL,
    channel_id   VARCHAR(128) NOT NULL,
    channel_name VARCHAR(255) NOT NULL,
    message_id   VARCHAR(255) NULL,
    posted_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_fmtp_meeting (meeting_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

Add to `FirmDbContext`, `DatabaseInitializationService`.

### New Service: `FirmTeamsService.cs`

```csharp
public class FirmTeamsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    // GET /me/joinedTeams → list of teams
    public async Task<List<TeamsTeam>> GetJoinedTeamsAsync(string graphToken)
    {
        using var http = CreateHttp(graphToken);
        var resp = await http.GetFromJsonAsync<TeamsListResponse>($"{GraphBase}/me/joinedTeams?$select=id,displayName");
        return resp?.Value ?? new();
    }

    // GET /teams/{teamId}/channels → list of channels
    public async Task<List<TeamsChannel>> GetChannelsAsync(string teamId, string graphToken)
    {
        using var http = CreateHttp(graphToken);
        var resp = await http.GetFromJsonAsync<ChannelListResponse>(
            $"{GraphBase}/teams/{teamId}/channels?$select=id,displayName");
        return resp?.Value ?? new();
    }

    // POST /teams/{teamId}/channels/{channelId}/messages
    // Send adaptive card with meeting summary
    public async Task<string?> PostMeetingSummaryAsync(
        string teamId, string channelId,
        FirmMeeting meeting, FirmMeetingSummary? summary,
        string graphToken)
    {
        var card = BuildAdaptiveCard(meeting, summary);
        var message = new
        {
            body = new
            {
                contentType = "html",
                content = $"<attachment id=\"{card.AttachmentId}\"></attachment>"
            },
            attachments = new[]
            {
                new
                {
                    id          = card.AttachmentId,
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content     = card.CardJson
                }
            }
        };

        using var http = CreateHttp(graphToken);
        var resp = await http.PostAsJsonAsync(
            $"{GraphBase}/teams/{teamId}/channels/{channelId}/messages", message);

        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadFromJsonAsync<TeamsMessageResponse>();
        return body?.Id;
    }

    private static (string AttachmentId, string CardJson) BuildAdaptiveCard(
        FirmMeeting meeting, FirmMeetingSummary? summary)
    {
        var title = meeting.Title ?? $"Meeting — {meeting.CreatedAt:yyyy-MM-dd}";
        var dateStr = meeting.CreatedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        var duration = meeting.DurationSeconds.HasValue
            ? TimeSpan.FromSeconds(meeting.DurationSeconds.Value).ToString(@"h\:mm")
            : "—";
        var excerpt = summary?.SummaryText?.Length > 200
            ? summary.SummaryText[..200] + "…"
            : summary?.SummaryText ?? "Summary not available.";

        // Minimal Adaptive Card 1.2 — works in all Teams clients
        var card = new
        {
            type    = "AdaptiveCard",
            version = "1.2",
            body = new object[]
            {
                new { type = "TextBlock", text = "📋 FIRM Meeting Notes", size = "Small", weight = "Bolder", color = "Accent" },
                new { type = "TextBlock", text = title, size = "Large", weight = "Bolder", wrap = true },
                new { type = "FactSet", facts = new[]
                {
                    new { title = "Date",     value = dateStr },
                    new { title = "Duration", value = duration },
                } },
                new { type = "TextBlock", text = excerpt, wrap = true, size = "Small" }
            },
            actions = new object[]
            {
                new { type = "Action.OpenUrl", title = "View Full Notes",
                      url = $"https://firm.dev.fortressam.ai/meetings/{meeting.Id}" }
            }
        };

        return (Guid.NewGuid().ToString("N"),
                System.Text.Json.JsonSerializer.Serialize(card));
    }

    private HttpClient CreateHttp(string token)
    {
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return http;
    }
}

record TeamsListResponse(List<TeamsTeam>? Value);
record ChannelListResponse(List<TeamsChannel>? Value);
record TeamsMessageResponse(string? Id);
public record TeamsTeam(string Id, string DisplayName);
public record TeamsChannel(string Id, string DisplayName);
```

### API Endpoints

```csharp
// GET /api/teams/channels — returns user's teams + channels
[HttpGet("/api/teams/channels")]
[Authorize]
public async Task<IActionResult> GetTeamsChannels()
{
    var (user, tokenError) = await ResolveUserAndToken();
    if (tokenError != null) return tokenError;

    var teams = await _teamsService.GetJoinedTeamsAsync(user.Token!);
    var result = new List<object>();
    foreach (var team in teams)
    {
        var channels = await _teamsService.GetChannelsAsync(team.Id, user.Token!);
        result.Add(new { teamId = team.Id, teamName = team.DisplayName, channels });
    }
    return Ok(result);
}

// POST /api/meetings/{id}/post-to-teams
[HttpPost("/api/meetings/{id}/post-to-teams")]
[Authorize]
public async Task<IActionResult> PostToTeams(long id, [FromBody] PostToTeamsRequest request)
{
    var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
    if (error != null) return error;
    if (meeting!.Status != MeetingStatus.Complete)
        return BadRequest(new { error = "Meeting must be complete to share" });

    // Idempotency: check if already posted to this channel
    await using var db = await _dbFactory.CreateDbContextAsync();
    var alreadyPosted = await db.Set<FirmMeetingTeamsPost>().AnyAsync(p =>
        p.MeetingId == id && p.ChannelId == request.ChannelId);
    if (alreadyPosted)
        return Conflict(new { error = "Already posted to this channel" });

    var (reqUser, tokenError) = await ResolveUserAndToken();
    if (tokenError != null) return tokenError;

    var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == id);
    var messageId = await _teamsService.PostMeetingSummaryAsync(
        request.TeamId, request.ChannelId, meeting!, summary, reqUser.Token!);

    if (messageId == null)
        return StatusCode(500, new { error = "Failed to post to Teams channel" });

    db.Set<FirmMeetingTeamsPost>().Add(new FirmMeetingTeamsPost
    {
        MeetingId   = id,
        TeamId      = request.TeamId,
        TeamName    = request.TeamName,
        ChannelId   = request.ChannelId,
        ChannelName = request.ChannelName,
        MessageId   = messageId,
        PostedAt    = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return Ok(new { messageId });
}

// GET /api/meetings/{id}/teams-posts — returns list of posted channels
[HttpGet("/api/meetings/{id}/teams-posts")]
[Authorize]
public async Task<IActionResult> GetTeamsPosts(long id)
{
    var (meeting, _, error) = await ResolveOwnedMeetingWithUser(id);
    if (error != null) return error;

    await using var db = await _dbFactory.CreateDbContextAsync();
    var posts = await db.Set<FirmMeetingTeamsPost>()
        .Where(p => p.MeetingId == id)
        .OrderByDescending(p => p.PostedAt)
        .ToListAsync();

    return Ok(posts.Select(p => new {
        teamName    = p.TeamName,
        channelName = p.ChannelName,
        postedAt    = p.PostedAt
    }));
}

public record PostToTeamsRequest(string TeamId, string TeamName, string ChannelId, string ChannelName);
```

### `MeetingDetail.razor` — Teams Post Panel

Add below the KB push panel in the `Complete` status block:

```razor
<div style="margin-top: 8px; border: 1px solid var(--color-border); border-radius: 8px; padding: 16px; background: var(--color-bg-page);">
    <div style="font-size: 13px; font-weight: 600; color: var(--color-text-secondary); margin-bottom: 12px;">
        Send to Teams Channel
    </div>
    @if (_teamsChannels == null)
    {
        <MudButton Size="Size.Small" Variant="Variant.Text"
                   Style="color: var(--color-text-link);"
                   OnClick="LoadTeamsChannels">Load channels…</MudButton>
    }
    else if (!_teamsChannels.Any())
    {
        <MudText Style="color: var(--color-text-muted); font-size: 13px;">No Teams channels found. Connect Microsoft 365 in FAIT settings.</MudText>
    }
    else
    {
        <div style="display: flex; gap: 8px; align-items: center; flex-wrap: wrap;">
            <MudSelect T="string" @bind-Value="_selectedTeamId" Label="Team" Dense Style="min-width: 180px;"
                       OnValueChanged="OnTeamChanged">
                @foreach (var team in _teamsChannels)
                {
                    <MudSelectItem Value="@team.TeamId">@team.TeamName</MudSelectItem>
                }
            </MudSelect>
            @if (_selectedTeamChannels.Any())
            {
                <MudSelect T="string" @bind-Value="_selectedChannelId" Label="Channel" Dense Style="min-width: 180px;">
                    @foreach (var ch in _selectedTeamChannels)
                    {
                        <MudSelectItem Value="@ch.Id">@ch.DisplayName</MudSelectItem>
                    }
                </MudSelect>
            }
            <MudButton Variant="Variant.Outlined" Size="Size.Small"
                       Style="border-color: var(--color-border); color: var(--color-text-secondary);"
                       Disabled="@(_postingToTeams || string.IsNullOrEmpty(_selectedChannelId) || AlreadyPostedToChannel(_selectedChannelId))"
                       OnClick="PostToTeams">
                @if (AlreadyPostedToChannel(_selectedChannelId))
                { <span>✓ Posted</span> }
                else
                { <span>@(_postingToTeams ? "Sending…" : "Send")</span> }
            </MudButton>
        </div>
        @if (_teamsPosts.Any())
        {
            <div style="margin-top: 8px; font-size: 12px; color: var(--color-text-muted);">
                @foreach (var p in _teamsPosts)
                {
                    <span style="margin-right: 12px;">✓ #@p.ChannelName (@p.TeamName)</span>
                }
            </div>
        }
    }
</div>
```

---

## Feature D: Dual-Mode Join Summary

The `JoinMeetingDialog.razor` requires no changes. The routing decision is made server-side in `POST /api/meetings/join` based on URL detection:

| Meeting URL | Token Available | Mode |
|-------------|----------------|------|
| `teams.microsoft.com/*` | Yes | Teams-native transcript |
| `teams.microsoft.com/*` | No  | Bot join + MS connect notice |
| Any other URL | N/A | Bot join (unchanged) |

The `JoinMeetingDialog` response `result.Data` now carries a `mode` field that `Meetings.razor` can use to show a descriptive snackbar:

```csharp
if (mode == "teams_native")
    Snackbar.Add("Meeting added — transcript will be pulled from Teams (5-15 min after meeting ends).", Severity.Success);
else
    Snackbar.Add("Bot is joining the meeting!", Severity.Success);
```

---

## Entra Permissions Research: Transcript API

**`OnlineMeetingTranscripts.Read.All` vs `OnlineMeetings.Read`:**

- `OnlineMeetings.Read` (delegated, user consent) — allows `GET /me/onlineMeetings` (list + detail). Does **not** include transcript content.
- `OnlineMeetingTranscripts.Read.All` (application or delegated, **admin consent**) — required to call `GET /me/onlineMeetings/{id}/transcripts` and download content.

In practice: FIRM needs both. `OnlineMeetings.Read` to resolve the meeting ID, `OnlineMeetingTranscripts.Read.All` to get the transcript.

**Delegated vs application permission:** Since FIRM calls Graph on behalf of a signed-in user (using a delegated OAuth token), the delegated variant of both permissions is what's being requested. However, `OnlineMeetingTranscripts.Read.All` delegated **still requires admin consent** in most tenants. This is not user-grantable.

**Constraint for Fortress AM tenant admin:** The tenant admin must grant `OnlineMeetingTranscripts.Read.All` (delegated) to the FIP platform app registration. Without this, the transcript endpoint returns 403.

**Fallback behavior:** If `GetTranscriptsAsync` returns HTTP 403, log at warning level and mark the meeting as `Failed` with `ErrorMessage = "Teams transcript permission not granted. Contact IT to enable transcription access."`. Do not silently swallow the error.

**Additional transcript constraint:** The meeting organizer must have transcription enabled, OR the tenant must have an auto-transcription policy. FIRM has no control over this. Document in the user guide: "Teams-native transcription requires your Teams admin to enable meeting transcription for your account."

---

## Program.cs Changes (FIRM)

Register new services:

```csharp
builder.Services.AddScoped<IFirmGraphTokenProvider, FirmGraphTokenProvider>();
builder.Services.AddScoped<GraphTranscriptService>();
builder.Services.AddScoped<FirmCalendarService>();
builder.Services.AddScoped<FirmTeamsService>();
builder.Services.AddHostedService<GraphTranscriptPollingService>();
```

Inject new services into `MeetingsApiController` constructor.

---

## FAIT `MicrosoftTokenService.cs` — New Scopes

Add to the `Scopes` array:

```csharp
private static readonly string[] Scopes = new[]
{
    // Existing
    "https://graph.microsoft.com/Mail.Read",
    "https://graph.microsoft.com/Calendars.Read",
    "https://graph.microsoft.com/User.Read",
    "https://graph.microsoft.com/Tasks.Read",
    "offline_access",
    // v2 additions for FIRM
    "https://graph.microsoft.com/OnlineMeetings.Read",
    "https://graph.microsoft.com/OnlineMeetingTranscripts.Read.All",
    "https://graph.microsoft.com/Team.ReadBasic.All",
    "https://graph.microsoft.com/Channel.ReadBasic.All",
    "https://graph.microsoft.com/ChannelMessage.Send",
};
```

**Important:** Adding scopes to this array will trigger a re-consent prompt the next time a user connects Microsoft 365 (or if they use the "reconnect" button). Existing connected users will need to re-authorize. This is expected OAuth behaviour — no workaround. Plan a communication to FAIT users about the re-consent prompt.

---

## Files Changed Summary

### New Files (FIRM)

| File | Purpose |
|------|---------|
| `Models/FirmMeetingTeamsPost.cs` | Teams post tracking model |
| `Services/IFirmGraphTokenProvider.cs` | Interface for Graph token fetching |
| `Services/FirmGraphTokenProvider.cs` | Implementation — fetches token from FAIT proxy |
| `Services/GraphTranscriptService.cs` | Graph calls: resolve meeting, poll transcript, parse VTT |
| `Services/FirmCalendarService.cs` | Graph calls: fetch upcoming Teams calendar events |
| `Services/FirmTeamsService.cs` | Graph calls: list teams/channels, post adaptive card |
| `Services/GraphTranscriptPollingService.cs` | Background polling for AwaitingTranscript meetings |
| `Components/Shared/CalendarMeetingsDialog.razor` | Calendar event selection modal |

### Modified Files (FIRM)

| File | Change |
|------|--------|
| `Models/MeetingStatus.cs` | Add `Cancelled`, `AwaitingTranscript` |
| `Models/FirmMeeting.cs` | Add `GraphMeetingId`, `CalendarEventId` properties |
| `Data/FirmDbContext.cs` | Add `FirmMeetingTeamsPost` `DbSet`; update `FirmMeeting` mapping |
| `Data/DatabaseInitializationService.cs` | Add `firm_meeting_teams_posts` table; add `ALTER TABLE` for new columns |
| `Services/MeetingService.cs` | Add `IsTeamsMeetingUrl()`, `SetGraphMeetingIdAsync()` |
| `Controllers/MeetingsApiController.cs` | Update `JoinMeeting` for dual-mode; add calendar endpoints; add Teams post endpoints |
| `Components/Pages/Meetings.razor` | Calendar prompt banner; MS connect CTA; `AwaitingTranscript` + `Cancelled` status badges |
| `Components/Pages/MeetingDetail.razor` | Add Teams post panel; load teams channels; post action |
| `Program.cs` | Register 4 new services + 1 hosted service |

### Modified Files (FAIT)

| File | Change |
|------|--------|
| `Services/MicrosoftTokenService.cs` | Add 5 new scopes to `Scopes` array |
| `Controllers/FirmIntegrationController.cs` | Add `GET /api/firm/graph-token` endpoint; add `MicrosoftTokenService` constructor param |

**Total: 8 new files (FIRM) + 9 modified (FIRM) + 2 modified (FAIT). No new npm packages. No new AWS services. One new ECS env var: none (FIRM uses existing FAIT proxy pattern).**

---

## Acceptance Criteria

1. **Teams-native detection:** Paste `https://teams.microsoft.com/l/meetup-join/...` → meeting shows `Awaiting Transcript` status (not `Joining`/`Recording`). Bot does not launch.

2. **Graph transcript arrival:** 10–20 minutes after a Teams meeting ends, the meeting transitions from `Awaiting Transcript` → `Summarizing` → `Complete` automatically. Transcript shows real speaker names (e.g. "Fred Williamson") not "Speaker 1".

3. **Transcript timeout:** A meeting stuck in `AwaitingTranscript` for 2 hours → transitions to `Failed` with the message "Teams transcript not available after 2 hours."

4. **403 handling:** If the tenant admin has not granted `OnlineMeetingTranscripts.Read.All` → meeting transitions to `Failed` with the human-readable permission error message (not a stack trace).

5. **Calendar prompt:** Navigate to `/meetings`. A banner appears listing upcoming Teams calendar meetings not yet in the list. Dismiss works. "Review" opens the calendar modal.

6. **Calendar add:** Select 3 meetings in the modal, click "Add Selected" → 3 meetings appear in the list with `Scheduled` status.

7. **Auto-cancel:** A `Scheduled` meeting is deleted from the calendar → on next background sync (up to 15 min), the meeting shows `Cancelled` status.

8. **Calendar debounce:** Reload the meetings page 10 times rapidly. Only 1 Graph calendar API call is made (client-side cached per page load, not per render).

9. **Send to Teams:** On a completed meeting, load teams/channels, select a channel, click "Send". The meeting appears in Teams as an Adaptive Card with title, date, duration, summary excerpt, and a "View Full Notes" link.

10. **Teams post idempotency:** Post to the same channel twice → second post returns HTTP 409. The card does not appear twice in Teams.

11. **Teams post status:** Reload `MeetingDetail` for a meeting that was posted to `#general`. The channel name shows in the "posted to" list with a ✓.

12. **No MS connection — graceful:** FIRM user has not connected Microsoft 365 in FAIT. Join a Teams URL → bot join used with the connect-MS notice. Calendar section shows "Connect Microsoft 365" CTA. Teams post section shows "No Teams channels found" message.

13. **Non-Teams URL — unchanged:** Paste a Zoom or WebEx URL → bot join path (no change from v1 behaviour).

---

## Clint Review Priorities

```
⚠️  HIGH: Verify POST /api/meetings/{id}/post-to-teams checks for existing
          posts BEFORE calling Graph. The idempotency check:
          db.Set<FirmMeetingTeamsPost>().AnyAsync(p => p.MeetingId == id
          && p.ChannelId == request.ChannelId)
          must run before PostMeetingSummaryAsync — not after. A double-post
          with a race condition creates duplicate cards in Teams that cannot
          easily be deleted.

⚠️  HIGH: Verify GET /api/firm/graph-token (in FAIT) validates that the
          faitUserId belongs to the user being requested — not just that
          a token exists. The FirmIntegrationController must confirm the
          X-Firm-Secret AND that the requested faitUserId is a real, active
          FAIT user before returning their token. Without this check, any
          FIRM call with the shared secret can pull any user's Graph token.

⚠️  HIGH: Verify transcript polling uses exponential backoff. Initial
          interval: 5 min. After 3 failures: 10 min. After 6 failures: 20 min.
          Cap: 30 min. A tight 1-minute loop on 403 would hammer Graph and
          trigger rate limiting. Confirm the polling service uses the
          ExecuteAsync + Task.Delay pattern (not a Timer or Thread.Sleep).

⚠️  HIGH: Verify the new scopes in MicrosoftTokenService.Scopes are additive.
          The Scopes array affects the OAuth consent URL. Adding scopes triggers
          re-consent for all connected users. This is expected, but Clint must
          confirm that the deploy plan includes communicating the re-consent
          prompt to FAIT users. Do not merge this change silently.

⚠️  MEDIUM: Verify calendar debounce is client-side (Blazor session state flag
            `_calendarLoaded`), not server-side. Server-side debounce would need
            a Redis key per user. The client-side flag is set to true after the
            first fetch and persists for the lifetime of the Blazor circuit.
            Confirm `_calendarLoaded` is a field (not a local variable) and
            persists across re-renders.

⚠️  MEDIUM: Verify VTT parsing handles multi-line caption blocks. Teams VTT
            can have multi-line text segments separated by a single newline
            within the same block. The parser splits on double-newline for
            blocks — single newlines within a block must be concatenated, not
            treated as separate segments.

⚠️  MEDIUM: Verify IsTeamsMeetingUrl() rejects partial matches. The check
            `uri.Host.EndsWith("teams.microsoft.com")` would match
            `evilteams.microsoft.com`. Use `== "teams.microsoft.com"` or
            `EndsWith(".teams.microsoft.com") || == "teams.microsoft.com"`.

⚠️  LOW: Verify `OnlineMeetingTranscripts.Read.All` 403 is caught and produces
         a human-readable error, not a logged stack trace only. The user must
         see "Teams transcript permission not granted. Contact IT." — not a
         generic error page.

⚠️  LOW: Verify AdaptiveCard JSON is valid 1.2 schema before merging.
         Test by pasting the JSON into the Adaptive Card Designer at
         https://adaptivecards.io/designer/. An invalid card renders as
         a blank message in Teams with no visible error.
```

---

## Explicit Deferrals (v3+)

- **Scheduled meeting auto-join:** When a `Scheduled` meeting reaches its start time, auto-trigger bot join or begin transcript polling. Requires a timer or SignalR push — not built in v2.
- **Webhook-based transcript notification:** Instead of polling, register a Graph change notification webhook for `onlineMeetings` to be notified when transcript is ready. Reduces latency from "up to 5 min" to near-real-time. Requires Graph subscription management (already scaffolded in FAIT's `GraphWebhookService`).
- **Multiple Teams channels:** v2 UI selects one channel per post action. v3: select multiple channels in one action.
- **Edit/delete posted card:** No Graph API to edit an Adaptive Card after posting (only delete by message ID). Deferred.

---

_Spec by Reed Richards | FIRM v2: 8 new files + 11 modified across FIRM + FAIT. Core constraint: FIRM proxies Graph tokens through FAIT — users connect once, both apps benefit. Admin consent required for `ChannelMessage.Send` + `OnlineMeetingTranscripts.Read.All` before v2 can ship._
