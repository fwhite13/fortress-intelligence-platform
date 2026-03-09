namespace FortressAI.Shared.Models;

public class McpOAuthConfig
{
    public string ClientId { get; set; } = string.Empty;
    // ClientSecret REMOVED — stored separately in McpServer.OAuthClientSecret (encrypted via DataProtection)
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public string RedirectUri { get; set; } = string.Empty;
}
