using System.ComponentModel.DataAnnotations;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Standardized field code — the shared data dictionary.
/// </summary>
public class DictionaryField
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string FieldCode { get; set; } = string.Empty; // e.g., business_name, years_in_business

    [Required, MaxLength(300)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Category { get; set; } // General, Property, Liability, Auto, WC

    [MaxLength(50)]
    public string? FieldType { get; set; } // default type

    public string? Description { get; set; }

    /// <summary>JSON array of alternate labels.</summary>
    public string? Synonyms { get; set; }

    /// <summary>Default validation rules JSON.</summary>
    public string? ValidationTemplate { get; set; }

    public bool IsStandard { get; set; } = true;

    /// <summary>Whether this field contains PII/PHI that requires special handling.</summary>
    public bool IsSensitive { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<FormField> FormFields { get; set; } = new List<FormField>();
}
