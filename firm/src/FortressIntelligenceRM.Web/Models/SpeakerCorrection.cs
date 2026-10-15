using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// WI #8335 — one user speaker-attribution correction. Persisted as an element of the
/// firm_meetings.speaker_corrections JSON array (snake_case to match transcribe.py, which applies the
/// same overlay before a summarize-only pass). The array is applied in order, so later entries win.
/// </summary>
public class SpeakerCorrection
{
    public const string TypeAssign = "assign";
    public const string TypeOverride = "override";
    public const string TypeMerge = "merge";
    public const int MaxNameLength = 255;   // firm_meeting_transcripts.speaker_name
    public const int MaxCorrections = 500;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    /// <summary>assign: the SPEAKER_XX label whose segments all get ResolvedName.</summary>
    [JsonPropertyName("speaker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Speaker { get; set; }
    /// <summary>override: index into the transcript in StartTimeMs order (the order MeetingDetail renders).</summary>
    [JsonPropertyName("segment_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SegmentIndex { get; set; }
    /// <summary>merge: diarization labels that are really one person.</summary>
    [JsonPropertyName("speakers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Speakers { get; set; }
    [JsonPropertyName("resolved_name")]
    public string ResolvedName { get; set; } = "";
    /// <summary>True once a retranscribe replaced the transcript this correction was made against —
    /// kept (corrections are never deleted) but no longer applied.</summary>
    [JsonPropertyName("archived")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Archived { get; set; }

    public static List<SpeakerCorrection> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<SpeakerCorrection>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string SerializeList(List<SpeakerCorrection> corrections) => JsonSerializer.Serialize(corrections);

    /// <summary>Returns an error message, or null if the correction is well-formed.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ResolvedName)) return "resolved_name is required";
        if (ResolvedName.Trim().Length > MaxNameLength) return $"resolved_name must be {MaxNameLength} characters or fewer";
        return Type switch
        {
            TypeAssign when string.IsNullOrWhiteSpace(Speaker) => "assign requires speaker",
            TypeOverride when SegmentIndex is null or < 0 => "override requires a non-negative segment_index",
            TypeMerge when Speakers == null || Speakers.Count(s => !string.IsNullOrWhiteSpace(s)) < 2 => "merge requires at least two speakers",
            TypeAssign or TypeOverride or TypeMerge => null,
            _ => $"unknown correction type '{Type}'"
        };
    }

    /// <summary>
    /// Effective speaker name per segment after applying the active (non-archived) corrections in
    /// order. <paramref name="segments"/> must be in StartTimeMs order. A label matches a segment's
    /// SpeakerLabel or SpeakerName — mirrors apply_speaker_corrections in transcribe.py.
    /// </summary>
    public static string?[] Apply(IReadOnlyList<FirmMeetingTranscript> segments, IEnumerable<SpeakerCorrection> corrections)
    {
        var names = segments.Select(s => s.SpeakerName ?? s.SpeakerLabel).ToArray();
        foreach (var c in corrections)
        {
            if (c.Archived || string.IsNullOrWhiteSpace(c.ResolvedName)) continue;
            var name = c.ResolvedName.Trim();
            if (c.Type == TypeOverride)
            {
                if (c.SegmentIndex is int i && i >= 0 && i < names.Length) names[i] = name;
                continue;
            }
            var labels = c.Type switch
            {
                TypeAssign => c.Speaker != null ? new HashSet<string> { c.Speaker } : new HashSet<string>(),
                TypeMerge => new HashSet<string>(c.Speakers ?? new()),
                _ => new HashSet<string>()
            };
            for (var i = 0; i < segments.Count; i++)
            {
                if ((segments[i].SpeakerLabel != null && labels.Contains(segments[i].SpeakerLabel!))
                    || (segments[i].SpeakerName != null && labels.Contains(segments[i].SpeakerName!)))
                    names[i] = name;
            }
        }
        return names;
    }
}
