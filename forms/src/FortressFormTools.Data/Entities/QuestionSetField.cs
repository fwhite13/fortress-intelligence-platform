using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Individual question in a question set.
/// </summary>
public class QuestionSetField
{
    [Key]
    public int Id { get; set; }

    public int QuestionSetId { get; set; }

    public int? DictionaryFieldId { get; set; }

    [Required, MaxLength(1000)]
    public string QuestionText { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string FieldType { get; set; } = "text";

    [MaxLength(200)]
    public string? SectionName { get; set; }

    public bool IsRequired { get; set; }

    public int? SortOrder { get; set; }

    /// <summary>Show/hide rules JSON.</summary>
    public string? ConditionalLogicJson { get; set; }

    /// <summary>How many source forms had this field.</summary>
    public int? SourceFormCount { get; set; }

    public string? ValidationRules { get; set; }

    // Navigation
    [ForeignKey(nameof(QuestionSetId))]
    public QuestionSet? QuestionSet { get; set; }

    [ForeignKey(nameof(DictionaryFieldId))]
    public DictionaryField? DictionaryField { get; set; }
}
