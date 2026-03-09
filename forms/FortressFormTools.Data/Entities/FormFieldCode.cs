using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Unified field code identified during cross-reference analysis.
/// One per unique field in the project's cross-referenced question set.
/// </summary>
[Table("FormFieldCodes")]
public class FormFieldCode
{
    [Key]
    public int Id { get; set; }

    public int ProjectId { get; set; }
    public FormProject? Project { get; set; }

    [Required, MaxLength(100)]
    public string FieldCode { get; set; } = string.Empty; // UPPER_SNAKE_CASE for vertical, lowercase for shared

    [Required, MaxLength(300)]
    public string FieldLabel { get; set; } = string.Empty;

    [MaxLength(50)]
    public string FieldType { get; set; } = "text"; // text, number, currency, date, yes_no, checkbox, select, textarea

    public bool IsSensitive { get; set; } = false; // maps to Encrypt

    public bool IsShared { get; set; } = false; // appears in multiple carriers

    [MaxLength(100)]
    public string? PanelId { get; set; } // dynamic panel this belongs to (e.g., "aircraft", "pilot")

    public string? CarrierSources { get; set; } // JSON array: ["carrier1", "carrier2"]

    public bool IsRequired { get; set; } = false; // required across all carriers

    public int SortOrder { get; set; } = 0;

    [MaxLength(200)]
    public string? SectionName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
