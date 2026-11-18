using System.Text.Json;
using FortressIntelligenceRM.Web.Models;
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

    /// <summary>Graph calendarview page size (<c>$top</c>).</summary>
    private const int CalendarViewPageSize = 50;

    /// <summary>WI #9678: upper bound on pages followed by the paged poll (20 x 50 = 1000 events in
    /// 7 days). A calendar that still has a nextLink after this is reported as not Complete.</summary>
    private const int CalendarViewMaxPages = 20;

    private const string GraphBaseUrl = "https://graph.microsoft.com/";

    /// <summary>
    /// The user's online meetings for now..now+7 days, as a plain list. Unchanged behaviour: one
    /// Graph page ($top=50, no paging), and an EMPTY list on a missing token, a non-2xx response or
    /// any exception — so an empty list does NOT mean "no events". Callers that need to tell those
    /// apart (WI #9678 calendar reconcile) use <see cref="GetUpcomingCalendarMeetingsPollAsync"/>.
    /// </summary>
    public async Task<List<CalendarMeetingDto>> GetUpcomingCalendarMeetingsAsync(string entraOid, string userEmail, CancellationToken ct = default)
        => (await FetchUpcomingCalendarMeetingsAsync(entraOid, userEmail, followPaging: false, ct)).Meetings;

    /// <summary>
    /// WI #9678: the same calendar read, but it follows <c>@odata.nextLink</c> until the 7-day
    /// window is exhausted and says whether the result can be trusted. <see cref="CalendarPollResult.Complete"/>
    /// is true only when the token was valid, every Graph page returned 2xx and parsed, and no
    /// nextLink was left unread. Anything else (missing token, non-2xx, exception, a truncated
    /// result) gives Complete=false, and callers must not treat a missing event as removed.
    /// </summary>
    public Task<CalendarPollResult> GetUpcomingCalendarMeetingsPollAsync(string entraOid, string userEmail, CancellationToken ct = default)
        => FetchUpcomingCalendarMeetingsAsync(entraOid, userEmail, followPaging: true, ct);

    private async Task<CalendarPollResult> FetchUpcomingCalendarMeetingsAsync(string entraOid, string userEmail, bool followPaging, CancellationToken ct)
    {
        var windowStart = DateTime.UtcNow;
        var windowEnd = windowStart.AddDays(7);
        CalendarPollResult Incomplete() => new()
        {
            Meetings = new List<CalendarMeetingDto>(),
            Complete = false,
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd
        };

        try
        {
            _logger.LogInformation("[CalendarService] GetUpcomingCalendarMeetingsAsync — entry. OID={Oid} Paged={Paged}", entraOid, followPaging);

            var token = await _fipTokenService.GetValidAccessTokenAsync(entraOid);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("[CalendarService] No delegated Graph token in fip_dev for OID={Oid}", entraOid);
                return Incomplete();
            }
            _logger.LogInformation("[CalendarService] Delegated Graph token acquired (length={Len}). OID={Oid}", token.Length, entraOid);

            var startDateTime = windowStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDateTime = windowEnd.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var select = "id,iCalUId,subject,start,end,isOnlineMeeting,onlineMeetingProvider,onlineMeeting,organizer,body,location";
            // Use /me/calendarview with delegated token — not /users/{entraOid}/calendarview
            var url = $"{GraphBaseUrl}v1.0/me/calendarview" +
                      $"?startDateTime={Uri.EscapeDataString(startDateTime)}" +
                      $"&endDateTime={Uri.EscapeDataString(endDateTime)}" +
                      $"&$select={select}" +
                      $"&$top={CalendarViewPageSize}";

            _logger.LogInformation("[CalendarService] Calling Graph calendarview (delegated). OID={Oid} Window={Start}..{End}", entraOid, startDateTime, endDateTime);
            var client = _httpClientFactory.CreateClient();
            var events = new List<CalendarViewEvent>();
            var complete = false;
            var pages = 0;
            string? nextUrl = url;
            while (nextUrl != null)
            {
                pages++;
                using var req = new HttpRequestMessage(HttpMethod.Get, nextUrl);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                req.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
                using var resp = await client.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var errorBody = await resp.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("[CalendarService] Graph calendarview returned {Status} for OID {Oid} (page {Page}). Body={Body}", resp.StatusCode, entraOid, pages, errorBody);
                    return Incomplete();
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var pageEvents = JsonSerializer.Deserialize<List<CalendarViewEvent>>(
                    doc.RootElement.GetProperty("value").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<CalendarViewEvent>();
                events.AddRange(pageEvents);

                nextUrl = doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkEl)
                          && nextLinkEl.ValueKind == JsonValueKind.String
                    ? nextLinkEl.GetString()
                    : null;
                if (string.IsNullOrEmpty(nextUrl))
                {
                    nextUrl = null;
                    complete = true; // the whole window has been read
                    break;
                }

                if (!followPaging)
                {
                    // Legacy single-page read: whatever is past the first page is silently dropped.
                    break;
                }
                if (pages >= CalendarViewMaxPages
                    || !nextUrl.StartsWith(GraphBaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    // Never send the bearer token anywhere but Graph, and never page forever.
                    _logger.LogWarning("[CalendarService] Graph calendarview paging stopped after {Pages} page(s) for OID {Oid} — result is truncated", pages, entraOid);
                    break;
                }
            }

            _logger.LogInformation("[CalendarService] Got {Count} calendar events for OID {Oid} ({Pages} page(s), complete={Complete})", events.Count, entraOid, pages, complete);
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
                    ICalUId = ev.ICalUId ?? "",
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
                    ICalUId = ev.ICalUId ?? "",
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
            return new CalendarPollResult
            {
                Meetings = result,
                Complete = complete,
                WindowStartUtc = windowStart,
                WindowEndUtc = windowEnd
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalendarService] Failed to get calendar meetings for OID {Oid}", entraOid);
            return Incomplete();
        }
    }

    /// <summary>
    /// WI #9678: the one "gone from the calendar" rule, shared by the Meetings page chip
    /// ("Removed from calendar") and CalendarAutoSyncService's server-side cleanup so the two can
    /// never disagree. A row is gone only when it has a CalendarEventId and is found by NONE of the
    /// three reconciliation anchors in the user's current calendar results: Graph event id,
    /// iCalUId (GraphMeetingId), or normalized join URL. Callers add their own status / start-time
    /// conditions, and must only pass anchors from a calendar read they trust.
    /// </summary>
    public static bool IsGoneFromCalendar(FirmMeeting meeting, CalendarAnchors current)
    {
        if (string.IsNullOrEmpty(meeting.CalendarEventId)) return false;
        if (current.EventIds.Contains(meeting.CalendarEventId)) return false;
        if (!string.IsNullOrEmpty(meeting.GraphMeetingId) && current.ICalUIds.Contains(meeting.GraphMeetingId)) return false;
        if (current.NormalizedUrls.Contains(NormalizeMeetingUrl(meeting.MeetingUrl))) return false;
        return true;
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

    /// <summary>
    /// Normalizes a meeting join URL for dedup/reconciliation comparison: lowercase, strip query
    /// string, strip trailing slash. Shared by CalendarAutoSyncService (DB dedup) and
    /// Meetings.razor (UI dedup + "Removed from calendar" detection) — previously duplicated
    /// locally in Meetings.razor only (ADO#6833 / Issue 5b).
    /// </summary>
    public static string NormalizeMeetingUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        return url.ToLowerInvariant().Split('?')[0].TrimEnd('/');
    }

    /// <summary>
    /// WI #8575: cleans a user-typed/pasted meeting link before it is saved or handed to the bot.
    /// Strips any leading non-URL text (e.g. "Join: https://...") by taking everything from the
    /// first "http", drops anything after the first whitespace, and requires a well-formed absolute
    /// http(s) URL. Returns null when the input isn't a recognizable meeting link.
    /// </summary>
    public static string? ExtractMeetingUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim();
        var idx = s.IndexOf("http", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        s = s[idx..];
        var ws = s.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        if (ws >= 0) s = s[..ws];

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
            return null;
        return s;
    }

    /// <summary>Kept for backward compat — prefer BrandingConfig.IsHomeTenant() for new code.</summary>
    public bool IsHomeTenant(string? tenantId) => _branding.IsHomeTenant(tenantId);

    [Obsolete("Use instance method IsHomeTenant or BrandingConfig.IsHomeTenant")]
    public static bool IsFortressTenant(string? tenantId) =>
        !string.IsNullOrEmpty(tenantId) && tenantId.StartsWith("7152ea12", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// WI #9678: a calendar read that says whether it can be trusted. <see cref="Complete"/> is false
/// on a missing token, a non-2xx Graph response, an exception, or a truncated (unpaged) result —
/// in which case <see cref="Meetings"/> is empty or partial and a missing event proves nothing.
/// </summary>
public sealed class CalendarPollResult
{
    public List<CalendarMeetingDto> Meetings { get; init; } = new();
    public bool Complete { get; init; }
    /// <summary>The calendarview window that was asked for (UTC).</summary>
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
}

/// <summary>
/// WI #9678: the three reconciliation anchors present in a user's current calendar results, for
/// <see cref="CalendarService.IsGoneFromCalendar"/>. Empty values are never added, so a row with a
/// blank anchor never "matches" on it.
/// </summary>
public sealed class CalendarAnchors
{
    public HashSet<string> EventIds { get; } = new();
    public HashSet<string> ICalUIds { get; } = new();
    public HashSet<string> NormalizedUrls { get; } = new();

    public static CalendarAnchors From(IEnumerable<(string? CalendarEventId, string? ICalUId, string? JoinUrl)> events)
    {
        var anchors = new CalendarAnchors();
        foreach (var (id, ical, url) in events)
        {
            if (!string.IsNullOrEmpty(id)) anchors.EventIds.Add(id);
            if (!string.IsNullOrEmpty(ical)) anchors.ICalUIds.Add(ical);
            if (!string.IsNullOrEmpty(url)) anchors.NormalizedUrls.Add(CalendarService.NormalizeMeetingUrl(url));
        }
        return anchors;
    }

    public static CalendarAnchors From(IEnumerable<CalendarMeetingDto> meetings) =>
        From(meetings.Select(m => ((string?)m.CalendarEventId, (string?)m.ICalUId, (string?)m.JoinUrl)));
}

public class CalendarMeetingDto
{
    public string CalendarEventId { get; set; } = "";
    /// <summary>Graph's iCalUId — the most stable identifier for a calendar event/occurrence
    /// (does not drift across polls the way the Graph <c>id</c> can). Used as the primary
    /// reconciliation anchor (Issue 5b).</summary>
    public string ICalUId { get; set; } = "";
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
    public string? ICalUId { get; set; }
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
