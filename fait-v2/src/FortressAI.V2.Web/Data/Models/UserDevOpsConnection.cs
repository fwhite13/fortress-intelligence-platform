using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

/// <summary>
/// Stores a user's Azure DevOps organization URL and encrypted PAT.
/// Each user has at most one DevOps connection.
/// Table: user_devops_connections (fait_v2_dev)
/// </summary>
[Table("user_devops_connections")]
public class UserDevOpsConnection
{
    [Key]
    [Column("user_id")]
    [MaxLength(36)]
    public string UserId { get; set; } = string.Empty;

    [Column("org_url")]
    [MaxLength(500)]
    [Required]
    public string OrgUrl { get; set; } = string.Empty;

    /// <summary>DataProtection-encrypted PAT. Protector purpose: "DevOpsPat"</summary>
    [Column("pat_encrypted")]
    [Required]
    public string PatEncrypted { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}
