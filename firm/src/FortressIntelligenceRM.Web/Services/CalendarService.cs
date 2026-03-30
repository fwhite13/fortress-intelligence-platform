using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Data;

namespace FortressIntelligenceRM.Web.Services;

public class CalendarService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly FipTokenService _fipTokenService;
    private readonly ILogger<CalendarService> _logger;
    private const string FortressTenantPrefix = "7152ea12";

    public CalendarService(
        IHttpClientFactory httpClientFactory,
        FipTokenService fipTokenService,
        ILogger<CalendarService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _fipTokenService = fipTokenService;
        _logger = logger;
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
            var select = "id,subject,start,end,isOnlineMeeting,onlineMeetingProvider,onlineMeeting,organizer";
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
                if (!ev.IsOnlineMeeting || ev.OnlineMeetingProvider != "teamsForBusiness")
                    continue;

                var joinUrl = ev.OnlineMeeting?.JoinUrl ?? "";
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

            _logger.LogInformation("[CalendarService] Returning {Count} Teams meetings for OID {Oid}", result.Count, entraOid);
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

    public static string? ExtractTenantIdFromUrl(string? url) => url == null ? null : ExtractTenantId(url);

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
