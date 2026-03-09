using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// A single extracted field from a PDF form page.
/// </summary>
public class FormField
{
    [Key]
    public int Id { get; set; }

    public int FormLibraryId { get; set; }

    [Required, MaxLength(500)]
    public string FieldLabel { get; set; } = string.Empty;

    public int? DictionaryFieldId { get; set; }

    [Required, MaxLength(50)]
    public string FieldType { get; set; } = "text"; // text, number, date, checkbox, dropdown, radio, matrix, signature, address

    public bool IsRequired { get; set; }

    [MaxLength(200)]
    public string? SectionName { get; set; }

    public int? PageNumber { get; set; }

    /// <summary>Bounding box JSON for PDF highlighting.</summary>
    public string? PositionJson { get; set; }

    /// <summary>JSON: min/max, regex, dependencies, options for dropdowns.</summary>
    public string? ValidationRules { get; set; }

    /// <summary>AI confidence 0.00–1.00.</summary>
    [Column(TypeName = "REAL")]
    public decimal? AiConfidence { get; set; }

    public int? SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(FormLibraryId))]
    public FormLibrary? FormLibrary { get; set; }

    [ForeignKey(nameof(DictionaryFieldId))]
    public DictionaryField? DictionaryField { get; set; }

    public ICollection<FieldCorrection> Corrections { get; set; } = new List<FieldCorrection>();
}
