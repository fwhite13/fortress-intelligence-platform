using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace FortressAI.Shared.Models;

public class McpServer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public string TransportType { get; set; } = "http";
    public string? EndpointUrl { get; set; }
    public string AuthType { get; set; } = "none";
    public string? AuthConfigJson { get; set; }
    public string? ToolManifestJson { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequiresUserAuth { get; set; } = false;
    public string? SystemApiKey { get; set; }
    public string? OAuthClientSecret { get; set; }  // Encrypted via DataProtection (if OAuth2 server)
    public int RateLimitPerMinute { get; set; } = 30;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<UserMcpToken> UserTokens { get; set; } = new List<UserMcpToken>();
    [NotMapped]
    public McpOAuthConfig? AuthConfig =>
        AuthConfigJson is null ? null : JsonSerializer.Deserialize<McpOAuthConfig>(AuthConfigJson);
    [NotMapped]
    public List<McpToolDefinition> Tools =>
        ToolManifestJson is null ? new() : JsonSerializer.Deserialize<List<McpToolDefinition>>(ToolManifestJson) ?? new();
}
