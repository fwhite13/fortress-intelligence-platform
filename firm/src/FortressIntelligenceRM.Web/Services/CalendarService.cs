using System.Text.Json;
using System.Text.RegularExpressions;

namespace FortressIntelligenceRM.Web.Services;

public class CalendarService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FipTokenService _fipTokenService;
    private readonly ILogger<CalendarService> _logger;
    private readonly BrandingConfig _branding;

    public CalendarService(
        IHttpClientFactory httpClientFactory,
        FipTokenService fipTokenService,
        ILogger<CalendarService> logger,
        BrandingConfig branding)
    {
        _httpClientFactory = httpClientFactory;
        _fipTokenService = fipTokenService;
        _logger = logger;
        _branding = branding;
    }

    public async Task<List<CalendarMeetingDto>> GetUpcomingCalendarMeetingsAsync(string entraOid, string userEmail, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("[CalendarService] GetUpcomingCalendarMeetingsAsync — entry. OID={Oid} Email={Email}", entraOid, userEmail);

            var token = await _fipTokenService.GetValidAccessTokenAsync(entraOid);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[CalendarService] No delegated Graph token in fip_dev for OID={Oid}", entraOid);
                return new List<CalendarMeetingDto>();
            }
            _logger.LogInformation("[CalendarService] Delegated Graph token acquired (length={Len}). OID={Oid}", token.Length, entraOid);

            var startDateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDateTime = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var select = "id,subject,start,end,isOnlineMeeting,onlineMeetingProvider,onlineMeeting,organizer,body,location";
            // Use /me/calendarview with delegated token — not /users/{entraOid}/calendarview
            var url = $"https://graph.microsoft.com/v1.0/me/calendarview" +
                      $"?startDateTime={Uri.EscapeDataString(startDateTime)}" +
                      $"&endDateTime={Uri.EscapeDataString(endDateTime)}" +
                      $"&$select={select}" +
                      $"&$top=50";

            _logger.LogInformation("[CalendarService] Calling Graph calendarview (delegated). OID={Oid} URL={Url}", entraOid, url);
            var client = _httpClientFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
            var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("[CalendarService] Graph calendarview returned {Status} for OID {Oid}. Body={Body}", resp.StatusCode, entraOid, errorBody);
                return new List<CalendarMeetingDto>();
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var events = JsonSerializer.Deserialize<List<CalendarViewEvent>>(
                doc.RootElement.GetProperty("value").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<CalendarViewEvent>();

            _logger.LogInformation("[CalendarService] Got {Count} calendar events for OID {Oid}", events.Count, entraOid);
            _logger.LogInformation("[CalendarService] Filtering events for OID {Oid}: total={Total}, online={Online}, teamsForBusiness={Teams}",
                entraOid,
                events.Count,
                events.Count(e => e.IsOnlineMeeting),
                events.Count(e => e.IsOnlineMeeting && e.OnlineMeetingProvider == "teamsForBusiness"));

            var result = new List<CalendarMeetingDto>();
            foreach (var ev in events)
            {
                if (!ev.IsOnlineMeeting) continue;

                // Accept teamsForBusiness, googleMeet, and unknown (potential Zoom/Meet)
                var provider = ev.OnlineMeetingProvider ?? "";
                var isKnownProvider = provider == "teamsForBusiness" || provider == "googleMeet";
                var joinUrl = ExtractPlatformJoinUrl(ev) ?? "";

                // Skip if provider is unrecognised and we could not extract a known join URL
                if (!isKnownProvider && string.IsNullOrEmpty(joinUrl)) continue;
                if (string.IsNullOrEmpty(joinUrl)) continue;

                var platform = DerivePlatform(joinUrl);
                if (platform == "unknown") continue;

                var tenantId = ExtractTenantId(joinUrl);
                var organizerEmail = ev.Organizer?.EmailAddress?.Address?.Trim().ToLower() ?? "";
                var currentUserEmail = userEmail.Trim().ToLower();

                // Mode A is only applicable to Teams meetings in the home tenant
                var isFortressTenant = platform == "teams" && _branding.IsHomeTenant(tenantId);
                var isOrganizer = !string.IsNullOrEmpty(organizerEmail) && !string.IsNullOrEmpty(currentUserEmail) && organizerEmail == currentUserEmail;
                var mode = (isFortressTenant && isOrganizer) ? "A" : "B";

                result.Add(new CalendarMeetingDto
                {
                    CalendarEventId = ev.Id ?? "",
                    Subject = ev.Subject ?? "(No Subject)",
                    StartDateTime = ev.Start?.DateTime ?? "",
                    EndDateTime = ev.End?.DateTime ?? "",
                    JoinUrl = joinUrl,
                    Platform = platform,           // "teams" | "zoom" | "meet"
                    Mode = mode,
                    OrganizerEmail = organizerEmail,
                    TenantId = tenantId ?? "",
                    ModeText = mode == "A"
                        ? $"Mode A — {_branding.ModuleName} captures transcript directly"
                        : $"Mode B — {_branding.NotetakerName} will join to record"
                });
            }

            // Fix 2: Intentionally scanning ALL events (no isOnlineMeeting guard) — Zoom/Meet events from
            // external organizers may not have isOnlineMeeting=true set by Graph. The Prefer text body
            // (Fix 1 above) and tight URL regex minimize false positives.
            _logger.LogInformation("[CalendarService] Entering Zoom/Meet second pass for OID {Oid}. TotalEvents={Total}", entraOid, events.Count);

            var existingIds = new HashSet<string>(result.Select(r => r.CalendarEventId));
            var zoomMeetFound = 0;
            var zoomMeetSkippedDedup = 0;

            foreach (var ev in events)
            {
                var eventId = ev.Id ?? "";

                var match = ExtractZoomOrMeetJoinUrl(ev);
                if (match == null) continue;

                if (existingIds.Contains(eventId))
                {
                    zoomMeetSkippedDedup++;
                    _logger.LogInformation("[CalendarService] Skipping Zoom/Meet event {EventId} — already present from Teams pass. OID={Oid}", eventId, entraOid);
                    continue;
                }

                var (platform, joinUrl) = match.Value;
                var organizerEmail = ev.Organizer?.EmailAddress?.Address?.Trim().ToLower() ?? "";

                result.Add(new CalendarMeetingDto
                {
                    CalendarEventId = eventId,
                    Subject = ev.Subject ?? "(No Subject)",
                    StartDateTime = ev.Start?.DateTime ?? "",
                    EndDateTime = ev.End?.DateTime ?? "",
                    JoinUrl = joinUrl,
                    Platform = platform,           // "zoom" | "meet"
                    Mode = "B",                     // vpbot only — no Mode A logic for non-Teams
                    OrganizerEmail = organizerEmail,
                    TenantId = "",
                    ModeText = $"Mode B — {_branding.NotetakerName} will join to record"
                });

                existingIds.Add(eventId);
                zoomMeetFound++;
            }

            _logger.LogInformation("[CalendarService] Zoom/Meet second pass complete for OID {Oid}. Found={Found} SkippedDedup={Skipped}", entraOid, zoomMeetFound, zoomMeetSkippedDedup);

            _logger.LogInformation("[CalendarService] Returning {Count} meetings (Teams + Zoom/Meet) for OID {Oid}", result.Count, entraOid);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalendarService] Failed to get calendar meetings for OID {Oid}", entraOid);
            return new List<CalendarMeetingDto>();
        }
    }

    private static string? ExtractPlatformJoinUrl(CalendarViewEvent ev)
    {
        // Teams: join URL is in onlineMeeting.joinUrl
        if (ev.OnlineMeetingProvider == "teamsForBusiness")
            return ev.OnlineMeeting?.JoinUrl;

        // Zoom and Google Meet: join URL is carried in location.displayName
        var locationText = ev.Location?.DisplayName ?? "";

        // Nitpick fix: zoom.us/j/ (personal meeting ID) or zoom.us/wc/ (webinar) — /w/ variant not in spec
        // Regex anchor fixed: https://(?:[a-z0-9-]+\.)*zoom\.us/ prevents matching evilzoom.us
        var zoomMatch = Regex.Match(locationText, @"https://(?:[a-z0-9-]+\.)*zoom\.us/(?:j|wc)/\S+");
        if (zoomMatch.Success) return zoomMatch.Value.TrimEnd('.');

        // meet.google.com/
        var meetMatch = Regex.Match(locationText, @"https://meet\.google\.com/[a-z0-9\-]+");
        if (meetMatch.Success) return meetMatch.Value.TrimEnd('.');

        return null;
    }

    private static (string Platform, string JoinUrl)? ExtractZoomOrMeetJoinUrl(CalendarViewEvent ev)
    {
        var haystack = (ev.Body?.Content ?? "") + " " + (ev.Location?.DisplayName ?? "");

        var zoomMatch = Regex.Match(haystack, @"https://(?:[a-z0-9-]+\.)*zoom\.us/(?:j|wc)/[^\s""'<>]+", RegexOptions.IgnoreCase);
        if (zoomMatch.Success) return ("zoom", zoomMatch.Value.TrimEnd('.'));

        var meetMatch = Regex.Match(haystack, @"https://meet\.google\.com/[^\s""'<>]+", RegexOptions.IgnoreCase);
        if (meetMatch.Success) return ("meet", meetMatch.Value.TrimEnd('.'));

        return null;
    }

    private static string DerivePlatform(string? joinUrl)
    {
        if (string.IsNullOrEmpty(joinUrl)) return "unknown";
        if (joinUrl.Contains("teams.microsoft.com") || joinUrl.Contains("teams.live.com")) return "teams";
        if (joinUrl.Contains("zoom.us")) return "zoom";
        if (joinUrl.Contains("meet.google.com")) return "meet";
        return "unknown";
    }

    private static string? ExtractTenantId(string joinUrl)
    {
        if (string.IsNullOrEmpty(joinUrl)) return null;
        var contextMatch = Regex.Match(joinUrl, @"[Tt]id["":]([a-fA-F0-9\-]{36})");
        if (contextMatch.Success) return contextMatch.Groups[1].Value;
        var tenantMatch = Regex.Match(joinUrl, @"[Tt]enant[Ii]d=([a-fA-F0-9\-]{36})");
        if (tenantMatch.Success) return tenantMatch.Groups[1].Value;
        return null;
    }

    public static string? ExtractTenantIdFromUrl(string? url) => url == null ? null : ExtractTenantId(url);

    /// <summary>Kept for backward compat — prefer BrandingConfig.IsHomeTenant() for new code.</summary>
    public bool IsHomeTenant(string? tenantId) => _branding.IsHomeTenant(tenantId);

    [Obsolete("Use instance method IsHomeTenant or BrandingConfig.IsHomeTenant")]
    public static bool IsFortressTenant(string? tenantId) =>
        !string.IsNullOrEmpty(tenantId) && tenantId.StartsWith("7152ea12", StringComparison.OrdinalIgnoreCase);
}

public class CalendarMeetingDto
{
    public string CalendarEventId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string StartDateTime { get; set; } = "";
    public string EndDateTime { get; set; } = "";
    public string JoinUrl { get; set; } = "";
    public string Platform { get; set; } = "teams";
    public string Mode { get; set; } = "B";
    public string OrganizerEmail { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ModeText { get; set; } = "";
}

// DTOs for Graph calendarview response
internal class CalendarViewEvent
{
    public string? Id { get; set; }
    public string? Subject { get; set; }
    public bool IsOnlineMeeting { get; set; }
    public string? OnlineMeetingProvider { get; set; }
    public CalendarViewDatetime? Start { get; set; }
    public CalendarViewDatetime? End { get; set; }
    public CalendarViewOrganizer? Organizer { get; set; }
    public CalendarViewOnlineMeeting? OnlineMeeting { get; set; }
    public CalendarViewBody? Body { get; set; }
    public CalendarViewLocation? Location { get; set; }
}

internal class CalendarViewBody
{
    public string? Content { get; set; }
    public string? ContentType { get; set; }
}

internal class CalendarViewLocation
{
    public string? DisplayName { get; set; }
    public string? UniqueId { get; set; }
}

internal class CalendarViewDatetime
{
    public string? DateTime { get; set; }
    public string? TimeZone { get; set; }
}

internal class CalendarViewOrganizer
{
    public CalendarViewEmailAddress? EmailAddress { get; set; }
}

internal class CalendarViewEmailAddress
{
    public string? Name { get; set; }
    public string? Address { get; set; }
}

internal class CalendarViewOnlineMeeting
{
    public string? JoinUrl { get; set; }
}
