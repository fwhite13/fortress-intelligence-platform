using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Generated SurveyJS JSON output.
/// </summary>
public class GeneratedSchema
{
    [Key]
    public int Id { get; set; }

    public int QuestionSetId { get; set; }

    public int? ToneTemplateId { get; set; }

    [Required]
    public string SchemaJson { get; set; } = string.Empty;

    /// <summary>Generation settings used (JSON).</summary>
    public string? SettingsJson { get; set; }

    public int Version { get; set; } = 1;

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Draft";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    // Navigation
    [ForeignKey(nameof(QuestionSetId))]
    public QuestionSet? QuestionSet { get; set; }

    [ForeignKey(nameof(ToneTemplateId))]
    public ToneTemplate? ToneTemplate { get; set; }
}
