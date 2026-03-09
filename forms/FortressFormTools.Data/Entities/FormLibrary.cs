using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressFormTools.Data.Entities;

/// <summary>
/// Uploaded PDF form — one record per PDF file.
/// </summary>
public class FormLibrary
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string CarrierName { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string FormName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string FormType { get; set; } = "Carrier"; // ACORD, Supplemental, State, Carrier

    [MaxLength(50)]
    public string? Version { get; set; }

    [MaxLength(100)]
    public string? VerticalHint { get; set; } // e.g., Builders, Museums, Churches

    [Required, MaxLength(1000)]
    public string PdfBlobPath { get; set; } = string.Empty;

    public int? PageCount { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "Queued"; // Queued, Processing, Draft, Reviewed, Approved, Error

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>Fortress API project request ID for polling.</summary>
    [MaxLength(200)]
    public string? FortressRequestId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    [MaxLength(50)]
    public string? DocumentType { get; set; } // application, supplement, pilot_form, driver_schedule, vehicle_schedule, other

    public DateTime? ApprovedAt { get; set; }

    // Navigation
    public ICollection<FormField> Fields { get; set; } = new List<FormField>();
    public ICollection<QuestionSetForm> QuestionSetForms { get; set; } = new List<QuestionSetForm>();
    public int? ProjectId { get; set; }
    public FormProject? Project { get; set; }
}
