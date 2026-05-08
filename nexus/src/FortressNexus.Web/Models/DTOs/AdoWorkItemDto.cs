using System.Text.Json;
using System.Text.Json.Serialization;
using FortressNexus.Web.Services;

namespace FortressNexus.Web.Models.DTOs;

public class AdoWorkItemDto
{
    [JsonPropertyName("type")]
    public string WorkItemType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public int? StoryPoints { get; set; }
    public string? ParentTitle { get; set; }
    public List<string> Tags { get; set; } = new();

    // Classification fields (set by ArtifactGenerationService post-parse)
    // wiTemplate comes from Bedrock as lowercase/hyphenated strings e.g. "standard", "test-case"
    [JsonConverter(typeof(WiTemplateTypeConverter))]
    public WiTemplateType WiTemplate { get; set; } = WiTemplateType.Standard;
    public bool IsExternalDependency { get; set; }
    public string? ExternalOwner { get; set; }
    public List<string>? TestedByTitles { get; set; }
    public List<string>? PredecessorTitles { get; set; }
}

/// <summary>
/// Tolerant converter: handles "standard", "infrastructure", "migration", "test-case", "testcase", "TestCase", etc.
/// </summary>
public class WiTemplateTypeConverter : JsonConverter<WiTemplateType>
{
    public override WiTemplateType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString()?.Replace("-", "").Replace("_", "").ToLowerInvariant();
        return raw switch
        {
            "infrastructure" => WiTemplateType.Infrastructure,
            "migration"      => WiTemplateType.Migration,
            "testcase"       => WiTemplateType.TestCase,
            _                => WiTemplateType.Standard
        };
    }

    public override void Write(Utf8JsonWriter writer, WiTemplateType value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString().ToLowerInvariant());
}

public record AdoProcessTemplate(string TypeId, string Name, string Description);
