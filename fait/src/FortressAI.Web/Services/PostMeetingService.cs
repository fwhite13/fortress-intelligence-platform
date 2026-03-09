using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Text.Json;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

/// <summary>
/// Detects calendar events that recently ended, saves post-meeting notes to DB,
/// and optionally generates a Bedrock-powered meeting summary.
/// </summary>
public class PostMeetingService
{
    private readonly GraphCalendarService _calendarSvc;
    private readonly AssistantConfigService _configSvc;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PostMeetingService> _logger;

    private static readonly string[] FilteredKeywords =
        ["standup", "stand-up", "stand up", "sync", "daily", "scrum"];

    /// <summary>How far back (in minutes) to look for recently-ended meetings.</summary>
    private const int LookbackMinutes = 30;

    public PostMeetingService(
        GraphCalendarService calendarSvc,
        AssistantConfigService configSvc,
        AppDbContext db,
        IConfiguration config,
        ILogger<PostMeetingService> logger)
    {
        _calendarSvc = calendarSvc;
        _configSvc = configSvc;
        _db = db;
        _config = config;
        _logger = logger;
    }

    // ── Ensure table exists ──────────────────────────────────────────────────

    /// <summary>
    /// Creates the post_meeting_notes table if it doesn't already exist.
    /// Follows the same IRelationalDatabaseCreator pattern used by other FAIT tables.
    /// </summary>
    public async Task EnsureTableCreatedAsync()
    {
        try
        {
            var creator = _db.Database.GetService<IRelationalDatabaseCreator>();
            await creator.CreateTablesAsync();
        }
        catch (Exception ex)
        {
            // Table likely already exists — not an error
            _logger.LogDebug("EnsureTableCreated: {Message}", ex.Message);
        }
    }

    // ── Query ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns calendar events that ended within the last 30 minutes, after applying filters.
    /// Events that already have a saved note are excluded.
    /// </summary>
    public async Task<List<CalendarEvent>> GetRecentlyEndedMeetingsAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-LookbackMinutes);

        // Fetch a wide window by StartTime; downstream filter narrows by EndTime
        var events = await _calendarSvc.GetUserCalendarEventsAsync(
            userId,
            now.AddHours(-8),   // look back 8 hours for start times
            now.AddMinutes(5));

        // Filter to events that ended in the lookback window
        var candidates = events
            .Where(e => e.EndTime >= windowStart && e.EndTime <= now)
            .Where(e => !IsFilteredOut(e))
            .OrderByDescending(e => e.EndTime)
            .ToList();

        if (!candidates.Any())
            return [];

        // Exclude events that already have a saved note
        var eventIds = candidates.Select(e => e.EventId).ToList();
        var notedEventIds = await _db.PostMeetingNotes
            .Where(n => n.UserId == userId && eventIds.Contains(n.EventId))
            .Select(n => n.EventId)
            .ToListAsync();

        return candidates
            .Where(e => !notedEventIds.Contains(e.EventId))
            .ToList();
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Persists a post-meeting note (without Bedrock summary) to the database.
    /// </summary>
    public async Task<PostMeetingNote> SavePostMeetingNoteAsync(
        Guid userId, string eventId, string eventSubject,
        DateTime meetingEndTime, string notes)
    {
        var note = new PostMeetingNote
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventId = eventId,
            EventSubject = eventSubject,
            MeetingEndTime = meetingEndTime,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };

        _db.PostMeetingNotes.Add(note);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Saved post-meeting note for event '{EventId}' user {UserId}", eventId, userId);
        return note;
    }

    /// <summary>
    /// Updates an existing note's Bedrock-generated summary.
    /// </summary>
    public async Task UpdateSummaryAsync(Guid noteId, string summary)
    {
        var note = await _db.PostMeetingNotes.FindAsync(noteId);
        if (note is null) return;

        note.Summary = summary;
        await _db.SaveChangesAsync();
    }

    // ── Bedrock summary ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a concise meeting summary using Bedrock (Claude Haiku).
    /// Returns a stub summary in stub mode (UseStubAuth=true) or on Bedrock failure.
    /// </summary>
    public async Task<string> GenerateMeetingSummaryAsync(CalendarEvent meeting, string notes)
    {
        var config = await _configSvc.GetOrCreateConfigAsync(meeting.UserId);
        var assistantName = config.AssistantName;
        var attendeeNames = GetAttendeeNames(meeting);
        var namesStr = attendeeNames.Count > 0
            ? string.Join(", ", attendeeNames)
            : "no attendees listed";

        var useStub = _config.GetValue<bool>("UseStubAuth", false);
        if (useStub)
        {
            _logger.LogInformation("Stub mode: returning mock summary for '{Subject}'", meeting.Subject);
            return BuildStubSummary(meeting.Subject, namesStr, notes);
        }

        var model = _config.GetValue<string>("PreMeetingBrief:BedrockModel", "anthropic.claude-3-haiku-20240307-v1:0")!;
        var regionStr = _config.GetValue<string>("AWS:Region", "us-east-1")!;

        try
        {
            var region = Amazon.RegionEndpoint.GetBySystemName(regionStr);
            using var client = new AmazonBedrockRuntimeClient(region);

            var prompt =
                $"You are {assistantName}. Based on this meeting ({meeting.Subject}, " +
                $"attendees: {namesStr}) and the following notes, write a concise 3-5 sentence " +
                $"meeting summary with key decisions and action items: {notes}";

            var requestBody = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 300,
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

            _logger.LogInformation("Generated Bedrock summary for meeting '{Subject}'", meeting.Subject);
            return aiText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bedrock summary failed for '{Subject}'; using stub", meeting.Subject);
            return BuildStubSummary(meeting.Subject, namesStr, notes);
        }
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the event should be excluded from post-meeting prompts.
    /// Excludes: standups/syncs/scrums, all-day events, solo events (no external attendees).
    /// </summary>
    public bool IsFilteredOut(CalendarEvent evt)
    {
        // Filter by title keywords (case-insensitive)
        var subject = evt.Subject?.ToLowerInvariant() ?? string.Empty;
        if (FilteredKeywords.Any(k => subject.Contains(k)))
            return true;

        // Filter all-day events
        var duration = evt.EndTime - evt.StartTime;
        if (evt.StartTime.TimeOfDay == TimeSpan.Zero && duration.TotalHours >= 23)
            return true;

        // Filter solo events
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

    private static string BuildStubSummary(string subject, string attendees, string notes)
    {
        return $"**Meeting Summary — {subject}**\n\n" +
               $"Attendees: {attendees}\n\n" +
               $"The team met to discuss {subject}. Key topics were covered as noted below. " +
               $"Action items were identified and assigned to relevant stakeholders. " +
               $"Follow-up tasks should be tracked in the project management system. " +
               $"Next steps will be confirmed via email.\n\n" +
               $"*Notes captured:* {notes.Trim()}";
    }
}
