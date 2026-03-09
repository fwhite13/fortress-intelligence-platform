using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// A unified question set built from cross-referencing multiple carrier forms.
/// </summary>
public class QuestionSet
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Vertical { get; set; }

    public string? Description { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Draft";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    // Navigation
    public ICollection<QuestionSetForm> QuestionSetForms { get; set; } = new List<QuestionSetForm>();
    public ICollection<QuestionSetField> Fields { get; set; } = new List<QuestionSetField>();
    public ICollection<GeneratedSchema> GeneratedSchemas { get; set; } = new List<GeneratedSchema>();
    public int? ProjectId { get; set; }
    public FormProject? Project { get; set; }
}
