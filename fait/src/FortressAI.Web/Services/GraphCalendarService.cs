using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class GraphCalendarService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly MicrosoftTokenService _tokenService;
    private readonly ILogger<GraphCalendarService> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _useStubAuth;

    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const int MaxRetries = 3;

    public GraphCalendarService(
        IDbContextFactory<AppDbContext> dbFactory,
        MicrosoftTokenService tokenService,
        ILogger<GraphCalendarService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _tokenService = tokenService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _useStubAuth = configuration.GetValue<bool>("UseStubAuth", false);
    }

    /// <summary>
    /// Fetches the user's calendar events for a date range from MS Graph, caches them, and returns the list.
    /// In stub mode returns realistic mock data.
    /// </summary>
    public async Task<List<CalendarEvent>> GetUserCalendarEventsAsync(Guid userId, DateTime startDate, DateTime endDate)
    {
        if (_useStubAuth)
        {
            _logger.LogInformation("Stub auth: returning mock calendar events for user {UserId}", userId);
            return GetMockCalendarEvents(userId, startDate, endDate);
        }

        var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
        if (accessToken == null)
        {
            _logger.LogWarning("No valid Microsoft token for user {UserId}; returning cached events", userId);
            return await GetCachedEventsAsync(userId, startDate, endDate);
        }

        try
        {
            var events = await FetchEventsFromGraphAsync(accessToken, userId, startDate, endDate);
            await UpsertEventCacheAsync(userId, events, startDate, endDate);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch calendar events from Graph API for user {UserId}; returning cached", userId);
            return await GetCachedEventsAsync(userId, startDate, endDate);
        }
    }

    private async Task<List<CalendarEvent>> FetchEventsFromGraphAsync(string accessToken, Guid userId, DateTime startDate, DateTime endDate)
    {
        var allEvents = new List<CalendarEvent>();

        var startIso = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endIso = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var selectFields = "id,subject,start,end,location,attendees,onlineMeetingUrl,categories";

        var url = $"{GraphBaseUrl}/me/calendar/events" +
                  $"?startDateTime={Uri.EscapeDataString(startIso)}" +
                  $"&endDateTime={Uri.EscapeDataString(endIso)}" +
                  $"&$select={selectFields}" +
                  $"&$orderby=start/dateTime" +
                  $"&$top=50";

        while (!string.IsNullOrEmpty(url))
        {
            var response = await SendGraphRequestWithRetryAsync(url, accessToken);
            if (response == null) break;

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);

            if (json.TryGetProperty("value", out var eventsArray))
            {
                foreach (var evt in eventsArray.EnumerateArray())
                {
                    var calEvent = ParseCalendarEvent(evt, userId);
                    if (calEvent != null)
                        allEvents.Add(calEvent);
                }
            }

            // Handle pagination
            url = json.TryGetProperty("@odata.nextLink", out var nextLink)
                ? nextLink.GetString()
                : null;
        }

        _logger.LogInformation("Fetched {Count} calendar events from Graph API for user {UserId} ({Start} to {End})",
            allEvents.Count, userId, startIso, endIso);
        return allEvents;
    }

    private CalendarEvent? ParseCalendarEvent(JsonElement evt, Guid userId)
    {
        try
        {
            // Parse start/end times
            DateTime startTime = DateTime.UtcNow, endTime = DateTime.UtcNow;

            if (evt.TryGetProperty("start", out var startProp))
            {
                var dtStr = startProp.TryGetProperty("dateTime", out var dt) ? dt.GetString() : null;
                if (dtStr != null && DateTime.TryParse(dtStr, out var parsed))
                    startTime = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            if (evt.TryGetProperty("end", out var endProp))
            {
                var dtStr = endProp.TryGetProperty("dateTime", out var dt) ? dt.GetString() : null;
                if (dtStr != null && DateTime.TryParse(dtStr, out var parsed))
                    endTime = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }

            // Parse location
            string? location = null;
            if (evt.TryGetProperty("location", out var locProp) && locProp.ValueKind != JsonValueKind.Null)
            {
                location = locProp.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            }

            // Parse attendees
            string? attendeesJson = null;
            if (evt.TryGetProperty("attendees", out var attProp) && attProp.ValueKind == JsonValueKind.Array)
            {
                var attendees = new List<object>();
                foreach (var att in attProp.EnumerateArray())
                {
                    var email = att.TryGetProperty("emailAddress", out var ea)
                        ? new
                        {
                            name = ea.TryGetProperty("name", out var n) ? n.GetString() : null,
                            address = ea.TryGetProperty("address", out var a) ? a.GetString() : null
                        }
                        : null;

                    if (email != null)
                        attendees.Add(email);
                }
                if (attendees.Count > 0)
                    attendeesJson = JsonSerializer.Serialize(attendees);
            }

            // Parse online meeting URL
            var meetingUrl = evt.TryGetProperty("onlineMeetingUrl", out var mu) && mu.ValueKind != JsonValueKind.Null
                ? mu.GetString()
                : null;

            // Parse category (first one)
            string? category = null;
            if (evt.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
            {
                var first = cats.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String)
                    category = first.GetString();
            }

            return new CalendarEvent
            {
                UserId = userId,
                EventId = evt.GetProperty("id").GetString() ?? "",
                Subject = evt.TryGetProperty("subject", out var subj) ? subj.GetString() ?? "(no subject)" : "(no subject)",
                StartTime = startTime,
                EndTime = endTime,
                Location = location,
                OnlineMeetingUrl = meetingUrl,
                AttendeesJson = attendeesJson,
                Category = category,
                LastFetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse calendar event");
            return null;
        }
    }

    private async Task<HttpResponseMessage?> SendGraphRequestWithRetryAsync(string url, string accessToken)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return response;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Graph API rate limited. Retrying in {Seconds}s (attempt {Attempt}/{Max})",
                    retryAfter.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(retryAfter);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Graph API returned 401 — token may be invalid");
                return null;
            }

            _logger.LogWarning("Graph API returned {StatusCode} for calendar events", response.StatusCode);
            return null;
        }

        _logger.LogError("Graph API calendar request failed after {MaxRetries} retries", MaxRetries);
        return null;
    }

    private async Task UpsertEventCacheAsync(Guid userId, List<CalendarEvent> events, DateTime startDate, DateTime endDate)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Remove old cached events in the requested date range for this user
        var existing = await db.CalendarCache
            .Where(e => e.UserId == userId && e.StartTime >= startDate && e.StartTime <= endDate)
            .ToListAsync();
        db.CalendarCache.RemoveRange(existing);

        // Insert fresh data
        db.CalendarCache.AddRange(events);
        await db.SaveChangesAsync();

        _logger.LogInformation("Cached {Count} calendar events for user {UserId}", events.Count, userId);
    }

    private async Task<List<CalendarEvent>> GetCachedEventsAsync(Guid userId, DateTime startDate, DateTime endDate)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.CalendarCache
            .Where(e => e.UserId == userId && e.StartTime >= startDate && e.StartTime <= endDate)
            .OrderBy(e => e.StartTime)
            .ToListAsync();
    }

    private static List<CalendarEvent> GetMockCalendarEvents(Guid userId, DateTime startDate, DateTime endDate)
    {
        var events = new List<CalendarEvent>();
        var baseDate = startDate.Date;
        var now = DateTime.UtcNow;

        // Generate events for each day in the range (up to 7 days)
        for (int day = 0; day < Math.Min((endDate - startDate).Days + 1, 7); day++)
        {
            var date = baseDate.AddDays(day);
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                continue;

            events.AddRange(new[]
            {
                new CalendarEvent
                {
                    UserId = userId,
                    EventId = $"mock-event-{day}-001",
                    Subject = "Morning Standup",
                    StartTime = date.AddHours(13).AddMinutes(30),  // 9:30 AM ET = 13:30 UTC
                    EndTime = date.AddHours(13).AddMinutes(45),
                    Location = "Microsoft Teams",
                    OnlineMeetingUrl = "https://teams.microsoft.com/l/meetup-join/mock-standup",
                    AttendeesJson = JsonSerializer.Serialize(new[]
                    {
                        new { name = "Sarah Chen", address = "sarah.chen@fortressam.ai" },
                        new { name = "John Miller", address = "john.miller@fortressam.ai" }
                    }),
                    Category = "Meeting",
                    LastFetchedAt = now
                },
                new CalendarEvent
                {
                    UserId = userId,
                    EventId = $"mock-event-{day}-002",
                    Subject = "FAIT v2 Architecture Review",
                    StartTime = date.AddHours(15),  // 11:00 AM ET
                    EndTime = date.AddHours(16),
                    Location = "Conference Room B",
                    AttendeesJson = JsonSerializer.Serialize(new[]
                    {
                        new { name = "Dev Team", address = "devteam@fortressam.ai" },
                        new { name = "Patrick Brennan", address = "patrick.brennan@fortressam.ai" }
                    }),
                    Category = "Project",
                    LastFetchedAt = now
                },
                new CalendarEvent
                {
                    UserId = userId,
                    EventId = $"mock-event-{day}-003",
                    Subject = "Client Meeting: Higginbotham Policy Renewal",
                    StartTime = date.AddHours(17),  // 1:00 PM ET
                    EndTime = date.AddHours(18),
                    Location = "Microsoft Teams",
                    OnlineMeetingUrl = "https://teams.microsoft.com/l/meetup-join/mock-client",
                    AttendeesJson = JsonSerializer.Serialize(new[]
                    {
                        new { name = "Emily Ross", address = "emily.ross@higginbotham.net" },
                        new { name = "Mark Taylor", address = "mark.taylor@higginbotham.net" }
                    }),
                    Category = "Client",
                    LastFetchedAt = now
                },
                new CalendarEvent
                {
                    UserId = userId,
                    EventId = $"mock-event-{day}-004",
                    Subject = "1:1 with Sarah — IT Security Update",
                    StartTime = date.AddHours(19).AddMinutes(30),  // 3:30 PM ET
                    EndTime = date.AddHours(20),
                    Location = "Fred's Office",
                    AttendeesJson = JsonSerializer.Serialize(new[]
                    {
                        new { name = "Sarah Chen", address = "sarah.chen@fortressam.ai" }
                    }),
                    Category = "Meeting",
                    LastFetchedAt = now
                }
            });
        }

        // Pre-meeting brief QA test event — always starts 25 min from now, visible in stub mode
        events.Add(new CalendarEvent
        {
            UserId = userId,
            EventId = "mock-upcoming-q2-budget",
            Subject = "Q2 Budget Review",
            StartTime = DateTime.UtcNow.AddMinutes(25),
            EndTime = DateTime.UtcNow.AddMinutes(85),
            Location = "Conference Room A",
            AttendeesJson = JsonSerializer.Serialize(new[]
            {
                new { name = "John Miller", address = "j.miller@higginbotham.com" },
                new { name = "Sarah Chen", address = "s.chen@fortressam.ai" }
            }),
            // Category field repurposed to carry agenda text for pre-meeting briefs
            Category = "Review Q2 budget projections and approve variance requests",
            LastFetchedAt = now
        });

        // Post-meeting prompt QA test event — ended ~15 min ago, visible in stub mode
        events.Add(new CalendarEvent
        {
            UserId = userId,
            EventId = "mock-ended-product-strategy-review",
            Subject = "Product Strategy Review",
            StartTime = DateTime.UtcNow.AddMinutes(-75),
            EndTime = DateTime.UtcNow.AddMinutes(-15),
            Location = "Conference Room B",
            AttendeesJson = JsonSerializer.Serialize(new[]
            {
                new { name = "Sarah Chen", address = "s.chen@fortressam.ai" },
                new { name = "John Miller", address = "j.miller@higginbotham.com" }
            }),
            Category = "Review Q3 product roadmap priorities",
            LastFetchedAt = now
        });

        return events;
    }
}
