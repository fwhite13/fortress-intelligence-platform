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

    // WI #9134 — import caps (spec §4.6, §8.2). All enforced before get_file is called.

    /// <summary>New imports (get_file + download + ingest) per user per sync run.</summary>
    public int MaxImportsPerRun { get; set; } = 3;

    /// <summary>The user's Plaud meetings in Transcribing/Summarizing; at most 1 during an explicit backfill.</summary>
    public int MaxInFlightPerUser { get; set; } = 2;

    /// <summary>Plaud meetings in Transcribing/Summarizing across all users of this database.</summary>
    public int MaxInFlightGlobal { get; set; } = 2;

    /// <summary>Longer recordings (list_files duration) become too_long without a download (D16: 240).</summary>
    public int MaxDurationMinutes { get; set; } = 240;

    /// <summary>Download byte cap, enforced while streaming (500 MB).</summary>
    public long MaxAudioBytes { get; set; } = 500L * 1024 * 1024;

    /// <summary>list_files pages per run (page size 20).</summary>
    public int MaxListPages { get; set; } = 5;

    /// <summary>Base for failed-row backoff: next_attempt_at = now + base × 2^attempts.</summary>
    public int RetryBaseMinutes { get; set; } = 15;
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
