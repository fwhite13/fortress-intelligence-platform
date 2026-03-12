namespace FortressAI.Shared.Models;

/// <summary>
/// Stores a user's Azure DevOps organization URL and encrypted PAT.
/// Each user has at most one DevOps connection (primary key = UserId).
/// </summary>
public class UserDevOpsConnection
{
    public Guid UserId { get; set; }

    /// <summary>
    /// Azure DevOps organization URL, e.g. "https://dev.azure.com/myorg"
    /// </summary>
    public string OrgUrl { get; set; } = string.Empty;

    /// <summary>
    /// DataProtection-encrypted PAT. Protector purpose: "DevOpsPat"
    /// </summary>
    public string PatEncrypted { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public AppUser? User { get; set; }
}
