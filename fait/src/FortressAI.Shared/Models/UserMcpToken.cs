using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Shared.Models;

public class UserMcpToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ServerId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public string? ExternalUserId { get; set; }
    public string? ExternalEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public AppUser? User { get; set; }
    public McpServer? Server { get; set; }
    [NotMapped]
    public bool IsExpired => TokenExpiresAt.HasValue && TokenExpiresAt.Value < DateTime.UtcNow.AddMinutes(5);
}
