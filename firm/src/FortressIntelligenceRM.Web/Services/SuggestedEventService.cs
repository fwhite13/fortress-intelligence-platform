using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #8294 — turns a transcriber-detected scheduling suggestion into a real Outlook/Teams event on the
/// organizer's calendar via Graph POST /me/events (delegated token from FipTokenService, which needs
/// Calendars.ReadWrite), and records Create / Not-an-event decisions on the meeting row.
/// </summary>
public class SuggestedEventService
{
    public const string DefaultTimeZone = "Central Standard Time";
    private const string GraphEventsUrl = "https://graph.microsoft.com/v1.0/me/events";

    private readonly MeetingService _meetingService;
    private readonly FipTokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SuggestedEventService> _logger;

    public SuggestedEventService(MeetingService meetingService, FipTokenService tokenService, IHttpClientFactory httpClientFactory, ILogger<SuggestedEventService> logger)
    {
        _meetingService = meetingService;
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Builds the editable draft for a suggestion — the defaults shown in the Edit form and used by one-click Create.</summary>
    public static SuggestedEventDraft BuildDraft(SuggestedEvent suggestion, FirmMeeting meeting)
    {
        var tz = string.IsNullOrWhiteSpace(suggestion.Timezone) ? DefaultTimeZone : suggestion.Timezone!;
        DateTime? start = DateTime.TryParse(suggestion.ProposedDatetime, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : null;
        var meetingTitle = string.IsNullOrWhiteSpace(meeting.Title) ? "Meeting" : meeting.Title!;
        return new SuggestedEventDraft
        {
            Subject = string.IsNullOrWhiteSpace(suggestion.Subject) ? $"{meetingTitle} Follow-up" : suggestion.Subject!,
            Start = start,
            DurationMinutes = suggestion.ProposedDurationMinutes is > 0 ? suggestion.ProposedDurationMinutes.Value : 30,
            TimeZone = tz,
            Attendees = ResolveAttendees(suggestion.Attendees, meeting.Participants)
        };
    }

    /// <summary>
    /// Maps attendee names to emails using the meeting's participant list. Graph requires an address per
    /// attendee, so entries already containing "@" pass through; unresolvable names are kept as-is so the
    /// organizer can fix them in the Edit form (they're skipped at create time).
    /// </summary>
    public static List<string> ResolveAttendees(IEnumerable<string> attendees, IEnumerable<FirmMeetingParticipant> participants)
    {
        var withEmail = participants.Where(p => !string.IsNullOrWhiteSpace(p.Email)).ToList();
        var resolved = new List<string>();
        foreach (var raw in attendees)
        {
            var a = raw.Trim();
            if (a.Length == 0) continue;
            if (a.Contains('@')) { resolved.Add(a); continue; }

            var match = withEmail.FirstOrDefault(p => string.Equals(p.DisplayName, a, StringComparison.OrdinalIgnoreCase))
                ?? withEmail.FirstOrDefault(p => p.DisplayName.StartsWith(a + " ", StringComparison.OrdinalIgnoreCase));
            resolved.Add(match?.Email ?? a);
        }
        return resolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Creates the calendar event and marks the suggestion created. Returns (true, null) on success,
    /// (false, error) on validation/Graph failure. Caller must have verified the user is the organizer.
    /// </summary>
    public async Task<(bool success, string? error)> CreateEventAsync(FirmMeeting meeting, FirmUser user, int index, SuggestedEventDraft draft)
    {
        if (meeting.CreatedBy != user.Id) return (false, "Only the meeting organizer can create suggested events.");
        if (draft.Start == null) return (false, "A start date and time is required.");
        if (string.IsNullOrWhiteSpace(draft.Subject)) return (false, "A subject is required.");

        var suggestions = await _meetingService.GetSuggestedEventsAsync(meeting.Id, user.Id);
        if (index < 0 || index >= suggestions.Count) return (false, "Suggestion not found.");
        var suggestion = suggestions[index];
        if (suggestion.Status != SuggestedEvent.StatusPending) return (false, "This suggestion was already handled.");

        var token = await _tokenService.GetValidAccessTokenAsync(user.EntraOid);
        if (string.IsNullOrEmpty(token))
            return (false, "Your Microsoft calendar connection has expired. Sign in to the FIP portal again and retry.");

        var addresses = draft.Attendees.Select(a => a.Trim()).Where(a => a.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var tz = string.IsNullOrWhiteSpace(draft.TimeZone) ? DefaultTimeZone : draft.TimeZone;
        var start = DateTime.SpecifyKind(draft.Start.Value, DateTimeKind.Unspecified);
        var end = start.AddMinutes(draft.DurationMinutes > 0 ? draft.DurationMinutes : 30);
        var meetingTitle = WebUtility.HtmlEncode(meeting.Title ?? $"Meeting {meeting.Id}");
        var quote = WebUtility.HtmlEncode(suggestion.SourceQuote ?? "");

        var body = new
        {
            subject = draft.Subject.Trim(),
            start = new { dateTime = start.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timeZone = tz },
            end = new { dateTime = end.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), timeZone = tz },
            attendees = addresses.Select(a => new { emailAddress = new { address = a }, type = "required" }),
            body = new { contentType = "HTML", content = $"Scheduled from FIRM meeting: {meetingTitle}<br>Source: \"{quote}\"" },
            isOnlineMeeting = true,
            onlineMeetingProvider = "teamsForBusiness"
        };

        var http = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphEventsUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("FIRM: Graph create event failed for meeting {MeetingId} suggestion {Index}: {Status} {Body}",
                meeting.Id, index, response.StatusCode, responseBody);
            return (false, response.StatusCode == HttpStatusCode.Forbidden
                ? "Calendar permission denied — Calendars.ReadWrite consent is required."
                : $"Calendar event could not be created ({(int)response.StatusCode}).");
        }

        string? eventId = null;
        try { eventId = JsonDocument.Parse(responseBody).RootElement.GetProperty("id").GetString(); }
        catch { /* id is informational only */ }

        await _meetingService.UpdateSuggestedEventStatusAsync(meeting.Id, user.Id, index, SuggestedEvent.StatusCreated, eventId);
        _logger.LogInformation("FIRM: Created calendar event {EventId} from meeting {MeetingId} suggestion {Index} ({Count} attendee(s))",
            eventId, meeting.Id, index, addresses.Count);
        return (true, null);
    }

    public Task<bool> DismissAsync(long meetingId, Guid userId, int index) =>
        _meetingService.UpdateSuggestedEventStatusAsync(meetingId, userId, index, SuggestedEvent.StatusDismissed);
}

public class SuggestedEventDraft
{
    public string Subject { get; set; } = "";
    public DateTime? Start { get; set; }
    public int DurationMinutes { get; set; } = 30;
    public string TimeZone { get; set; } = SuggestedEventService.DefaultTimeZone;
    public List<string> Attendees { get; set; } = new();
}
