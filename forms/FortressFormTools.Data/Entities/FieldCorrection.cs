using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Tracks user corrections to extracted fields — training data for prompt improvement.
/// </summary>
public class FieldCorrection
{
    [Key]
    public int Id { get; set; }

    public int FormFieldId { get; set; }

    [Required, MaxLength(50)]
    public string FieldName { get; set; } = string.Empty; // which property was corrected

    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    [MaxLength(100)]
    public string? CorrectedBy { get; set; }

    public DateTime CorrectedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(FormFieldId))]
    public FormField? FormField { get; set; }
}
