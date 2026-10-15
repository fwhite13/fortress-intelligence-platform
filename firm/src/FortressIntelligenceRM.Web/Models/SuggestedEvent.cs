using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// WI #8294 — one scheduling suggestion detected by the transcriber's summarization pass.
/// Persisted as an element of the firm_meetings.suggested_events JSON array (snake_case to match
/// the transcriber output). Items are never removed from the array — Status tracks the organizer's
/// decision so list indexes stay stable as identifiers.
/// </summary>
public class SuggestedEvent
{
    public const string StatusPending = "pending";
    public const string StatusCreated = "created";
    public const string StatusDismissed = "dismissed";

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }
    [JsonPropertyName("source_quote")]
    public string? SourceQuote { get; set; }
    [JsonPropertyName("attendees")]
    public List<string> Attendees { get; set; } = new();
    [JsonPropertyName("attendees_inferred")]
    public bool AttendeesInferred { get; set; }
    [JsonPropertyName("proposed_datetime")]
    public string? ProposedDatetime { get; set; }
    [JsonPropertyName("proposed_duration_minutes")]
    public int? ProposedDurationMinutes { get; set; }
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
    [JsonPropertyName("status")]
    public string Status { get; set; } = StatusPending;
    [JsonPropertyName("created_event_id")]
    public string? CreatedEventId { get; set; }

    public static List<SuggestedEvent> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<SuggestedEvent>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string SerializeList(List<SuggestedEvent> events) => JsonSerializer.Serialize(events);
}
