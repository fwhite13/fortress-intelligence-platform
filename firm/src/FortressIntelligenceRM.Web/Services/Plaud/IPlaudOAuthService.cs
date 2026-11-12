namespace FortressIntelligenceRM.Web.Services.Plaud;

/// <summary>
/// WI #9133 — Firm:Plaud configuration. Gating is environment config only (spec §9.2): the
/// DB-backed firm_system_config is shared by dev and prod and must not hold the flag.
/// </summary>
public class PlaudOptions
{
    public const string Section = "Firm:Plaud";

    /// <summary>When false, every Plaud endpoint returns 404 and the Settings tab is hidden.</summary>
    public bool Enabled { get; set; }

    /// <summary>Plaud's remote MCP endpoint; also the RFC 8707 resource and the base for AS discovery.</summary>
    public string McpUrl { get; set; } = "https://mcp.plaud.ai/mcp";

    /// <summary>This environment's callback, e.g. https://firm.dev.fortressam.ai/oauth/plaud/callback.
    /// Deliberately has no default: a missing value must not send prod users to dev.</summary>
    public string RedirectUri { get; set; } = "";

    /// <summary>Optional override of the dynamically registered client id (spec §9.2).</summary>
    public string? ClientId { get; set; }
}

/// <summary>What the Settings tab and the Meetings page show.</summary>
public record PlaudAccountStatus(
    bool Connected,
    string? Email,
    bool NeedsReconnect,
    bool OtherEnvironment);

public interface IPlaudOAuthService
{
    /// <summary>Firm:Plaud:Enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Discovers the authorization server, registers this environment's client on first use
    /// (firm_oauth_clients) and returns the authorize URL with an S256 code_challenge, resource and state.
    /// </summary>
    Task<string> BuildAuthorizationUrlAsync(string state, string codeVerifier, CancellationToken ct = default);

    /// <summary>
    /// Exchanges the code with the PKCE verifier (public client: no secret), looks up the Plaud
    /// identity (best effort) and stores the encrypted tokens. Returns the "Connected as" label.
    /// </summary>
    Task<string?> HandleCallbackAsync(Guid userId, string code, string codeVerifier, CancellationToken ct = default);

    /// <summary>
    /// A usable access token for this user, or null (not connected, reconnect needed, other
    /// environment, or Plaud unreachable). Refreshes under SELECT … FOR UPDATE when within 5 minutes
    /// of expiry, or when <paramref name="forceRefresh"/> is set after a 401.
    /// </summary>
    Task<string?> GetAccessTokenAsync(Guid userId, bool forceRefresh = false, CancellationToken ct = default);

    Task<PlaudAccountStatus> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Deletes the account row. Imported meetings stay. Plaud has no revocation endpoint.</summary>
    Task DisconnectAsync(Guid userId, CancellationToken ct = default);
}
