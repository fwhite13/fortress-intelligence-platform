using System.ComponentModel.DataAnnotations;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Tone/voice template for SurveyJS generation.
/// </summary>
public class ToneTemplate
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Text injected into the generation prompt.</summary>
    [Required]
    public string PromptFragment { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
