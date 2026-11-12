namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// WI #9133 — one linked Plaud account per FIRM user (firm_plaud_accounts, spec §9.2).
/// Token columns hold DataProtection ciphertext (purpose "Firm.PlaudTokens.v1"); they are only ever
/// assigned from <see cref="Services.OAuth.TokenProtector.Protect"/>, never a raw token.
/// </summary>
public class FirmPlaudAccount
{
    public uint Id { get; set; }
    public Guid UserId { get; set; }
    public string? PlaudUserId { get; set; }
    public string? PlaudEmail { get; set; }
    public string AccessTokenEnc { get; set; } = "";
    public string RefreshTokenEnc { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    /// <summary>The DCR client that issued these tokens; refresh must use the same one.</summary>
    public string ClientId { get; set; } = "";
    /// <summary>Redirect host of the FIRM environment that issued the tokens (dev and prod share the DB).</summary>
    public string OAuthEnv { get; set; } = "";
    public bool NeedsReconnect { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
