using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

[Table("form_projects")]
public class FormProject
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Vertical { get; set; } = "general";

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "draft";

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FormLibrary> Documents { get; set; } = new List<FormLibrary>();
    public ICollection<QuestionSet> QuestionSets { get; set; } = new List<QuestionSet>();
}
