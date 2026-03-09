using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;
using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

/// <summary>
/// Fetches upcoming calendar events and generates AI-powered pre-meeting context briefs.
/// Used by the Dashboard to surface briefing cards for meetings starting within the next 60 minutes.
/// </summary>
public class PreMeetingBriefService
{
    private readonly GraphCalendarService _calendarSvc;
    private readonly AssistantConfigService _configSvc;
    private readonly IConfiguration _config;
    private readonly ILogger<PreMeetingBriefService> _logger;

    private static readonly string[] FilteredKeywords =
        ["standup", "stand-up", "stand up", "sync", "daily", "scrum"];

    public PreMeetingBriefService(
        GraphCalendarService calendarSvc,
        AssistantConfigService configSvc,
        IConfiguration config,
        ILogger<PreMeetingBriefService> logger)
    {
        _calendarSvc = calendarSvc;
        _configSvc = configSvc;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns calendar events starting between now+1min and now+WindowMinutes (default 60).
    /// Filters out standups, all-day events, and solo events.
    /// </summary>
    public async Task<List<CalendarEvent>> GetUpcomingMeetingsAsync(Guid userId)
    {
        var windowMinutes = _config.GetValue<int>("PreMeetingBrief:WindowMinutes", 60);
        var now = DateTime.UtcNow;

        // Fetch a window slightly larger than needed to ensure we capture all events
        var events = await _calendarSvc.GetUserCalendarEventsAsync(
            userId,
            now.AddMinutes(-5),
            now.AddMinutes(windowMinutes + 5));

        return events
            .Where(e => !IsFilteredOut(e)
                && e.StartTime >= now.AddMinutes(15)
                && e.StartTime <= now.AddMinutes(windowMinutes))
            .OrderBy(e => e.StartTime)
            .ToList();
    }

    /// <summary>
    /// Returns true if the event should be excluded from pre-meeting briefs.
    /// Excludes: standups/syncs/scrums, all-day events, solo events (no other attendees).
    /// </summary>
    public bool IsFilteredOut(CalendarEvent evt)
    {
        // Filter by title keywords (case-insensitive)
        var subject = evt.Subject?.ToLowerInvariant() ?? string.Empty;
        if (FilteredKeywords.Any(k => subject.Contains(k)))
            return true;

        // Filter all-day events: starts at midnight UTC, duration >= 23 hours
        var duration = evt.EndTime - evt.StartTime;
        if (evt.StartTime.TimeOfDay == TimeSpan.Zero && duration.TotalHours >= 23)
            return true;

        // Filter solo events: no attendees listed
        if (string.IsNullOrWhiteSpace(evt.AttendeesJson))
            return true;

        try
        {
            var attendees = JsonSerializer.Deserialize<List<JsonElement>>(evt.AttendeesJson);
            if (attendees == null || attendees.Count == 0)
                return true;
        }
        catch
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Generates meeting context using Bedrock (Claude Haiku) or returns a formatted stub on failure.
    /// </summary>
    public async Task<string> GenerateMeetingContextAsync(CalendarEvent meeting, string assistantName)
    {
        var minutesUntil = Math.Max(0, (int)(meeting.StartTime - DateTime.UtcNow).TotalMinutes);
        var durationMin = (int)(meeting.EndTime - meeting.StartTime).TotalMinutes;
        var attendeeNames = GetAttendeeNames(meeting);
        // Category field repurposed to store agenda text in stub/mock data
        var agenda = !string.IsNullOrWhiteSpace(meeting.Category) ? meeting.Category : "No agenda provided";

        var model = _config.GetValue<string>("PreMeetingBrief:BedrockModel", "anthropic.claude-3-haiku-20240307-v1:0")!;
        var regionStr = _config.GetValue<string>("AWS:Region", "us-east-1")!;

        try
        {
            var region = Amazon.RegionEndpoint.GetBySystemName(regionStr);
            using var client = new AmazonBedrockRuntimeClient(region);

            var prompt = $"You are {assistantName}. In 2-3 sentences, give me key context for this upcoming meeting: " +
                         $"{meeting.Subject}, attendees: {string.Join(", ", attendeeNames)}, agenda: {agenda}.";

            var requestBody = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 200,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            });

            var request = new InvokeModelRequest
            {
                ModelId = model,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody))
            };

            var response = await client.InvokeModelAsync(request);

            using var reader = new StreamReader(response.Body);
            var json = await reader.ReadToEndAsync();
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var aiText = doc.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

            _logger.LogInformation("Generated Bedrock context for meeting '{Subject}'", meeting.Subject);
            return BuildContext(meeting.Subject, minutesUntil, durationMin, attendeeNames, agenda, aiText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bedrock context generation failed for '{Subject}'; using stub", meeting.Subject);
            return BuildStubContext(meeting.Subject, minutesUntil, durationMin, attendeeNames, agenda);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static List<string> GetAttendeeNames(CalendarEvent meeting)
    {
        if (string.IsNullOrWhiteSpace(meeting.AttendeesJson))
            return [];

        try
        {
            var items = JsonSerializer.Deserialize<List<JsonElement>>(meeting.AttendeesJson) ?? [];
            return items
                .Select(a =>
                {
                    if (a.TryGetProperty("name", out var n)
                        && n.ValueKind != JsonValueKind.Null
                        && !string.IsNullOrWhiteSpace(n.GetString()))
                        return n.GetString()!;
                    return a.TryGetProperty("address", out var addr)
                        ? addr.GetString() ?? string.Empty
                        : string.Empty;
                })
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string BuildStubContext(
        string subject, int minutesUntil, int durationMin,
        List<string> attendees, string agenda)
    {
        var names = attendees.Count > 0
            ? string.Join(", ", attendees)
            : "no attendees listed";

        return $"## {subject}\n" +
               $"**In {minutesUntil} minutes** | {durationMin} min | {attendees.Count} attendees\n\n" +
               $"### Attendees\n{names}\n\n" +
               $"### Agenda\n{agenda}\n\n" +
               $"### Context\n" +
               $"This meeting covers {subject}. {attendees.Count} attendees are joining. " +
               $"Review any recent correspondence with attendees before joining.";
    }

    private static string BuildContext(
        string subject, int minutesUntil, int durationMin,
        List<string> attendees, string agenda, string aiContext)
    {
        var names = attendees.Count > 0
            ? string.Join(", ", attendees)
            : "no attendees listed";

        return $"## {subject}\n" +
               $"**In {minutesUntil} minutes** | {durationMin} min | {attendees.Count} attendees\n\n" +
               $"### Attendees\n{names}\n\n" +
               $"### Agenda\n{agenda}\n\n" +
               $"### Context\n{aiContext}";
    }
}
