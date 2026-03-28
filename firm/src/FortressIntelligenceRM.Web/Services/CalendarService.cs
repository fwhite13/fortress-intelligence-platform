using System.Text.Json;
using System.Text.RegularExpressions;

namespace FortressIntelligenceRM.Web.Services;

public class CalendarService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<CalendarService> _logger;
    private const string FortressTenantPrefix = "7152ea12";

    public CalendarService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<CalendarService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<List<CalendarMeetingDto>> GetUpcomingCalendarMeetingsAsync(string entraOid, string userEmail, CancellationToken ct = default)
    {
        var faitUrl = _config["FIP:FaitApiUrl"]?.TrimEnd('/') ?? "";
        var secret = _config["Firm:SharedSecret"] ?? "";
        if (string.IsNullOrEmpty(faitUrl)) return new List<CalendarMeetingDto>();

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"{faitUrl}/api/firm/calendarview?entraOid={Uri.EscapeDataString(entraOid)}&days=7";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(secret)) req.Headers.Add("X-Firm-Secret", secret);
            var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[CalendarService] FAIT calendarview returned {Status} for OID {Oid}", resp.StatusCode, entraOid);
                return new List<CalendarMeetingDto>();
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            var events = JsonSerializer.Deserialize<List<CalendarViewEvent>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<CalendarViewEvent>();

            _logger.LogInformation("[CalendarService] Got {Count} calendar events for OID {Oid}", events.Count, entraOid);

            var result = new List<CalendarMeetingDto>();
            foreach (var ev in events)
            {
                if (!ev.IsOnlineMeeting || ev.OnlineMeetingProvider != "teamsForBusiness")
                    continue;

                var joinUrl = ev.OnlineMeeting?.JoinUrl ?? ev.JoinUrl ?? "";
                var tenantId = ExtractTenantId(joinUrl);
                var organizerEmail = ev.Organizer?.EmailAddress?.Address?.ToLower() ?? "";
                var currentUserEmail = userEmail.ToLower();

                var isFortressTenant = !string.IsNullOrEmpty(tenantId) && tenantId.StartsWith(FortressTenantPrefix, StringComparison.OrdinalIgnoreCase);
                var isOrganizer = !string.IsNullOrEmpty(organizerEmail) && organizerEmail == currentUserEmail;

                var mode = (isFortressTenant && isOrganizer) ? "A" : "B";

                result.Add(new CalendarMeetingDto
                {
                    CalendarEventId = ev.Id ?? "",
                    Subject = ev.Subject ?? "(No Subject)",
                    StartDateTime = ev.Start?.DateTime ?? "",
                    EndDateTime = ev.End?.DateTime ?? "",
                    JoinUrl = joinUrl,
                    Platform = "teams",
                    Mode = mode,
                    OrganizerEmail = organizerEmail,
                    TenantId = tenantId ?? "",
                    ModeText = mode == "A"
                        ? "Mode A — FIRM captures transcript directly"
                        : "Mode B — Fortress Notetaker will join to record"
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalendarService] Failed to get calendar meetings for OID {Oid}", entraOid);
            return new List<CalendarMeetingDto>();
        }
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

    public static string? ExtractTenantIdFromUrl(string? url) => url == null ? null : ExtractTenantIdStatic(url);

    private static string? ExtractTenantIdStatic(string joinUrl)
    {
        if (string.IsNullOrEmpty(joinUrl)) return null;
        var contextMatch = Regex.Match(joinUrl, @"[Tt]id["":]([a-fA-F0-9\-]{36})");
        if (contextMatch.Success) return contextMatch.Groups[1].Value;
        var tenantMatch = Regex.Match(joinUrl, @"[Tt]enant[Ii]d=([a-fA-F0-9\-]{36})");
        if (tenantMatch.Success) return tenantMatch.Groups[1].Value;
        return null;
    }

    public static bool IsFortressTenant(string? tenantId) =>
        !string.IsNullOrEmpty(tenantId) && tenantId.StartsWith(FortressTenantPrefix, StringComparison.OrdinalIgnoreCase);
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

// DTOs for FAIT calendarview response
internal class CalendarViewEvent
{
    public string? Id { get; set; }
    public string? Subject { get; set; }
    public bool IsOnlineMeeting { get; set; }
    public string? OnlineMeetingProvider { get; set; }
    public string? JoinUrl { get; set; }
    public CalendarViewDatetime? Start { get; set; }
    public CalendarViewDatetime? End { get; set; }
    public CalendarViewOrganizer? Organizer { get; set; }
    public CalendarViewOnlineMeeting? OnlineMeeting { get; set; }
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
