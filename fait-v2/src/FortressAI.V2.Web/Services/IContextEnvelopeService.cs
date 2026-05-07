namespace FortressAI.V2.Web.Services;

public interface IContextEnvelopeService
{
    /// <summary>
    /// Builds the full system CLAUDE.md content (Layer 1) — static, versioned.
    /// </summary>
    string GetSystemClaudeMd();

    /// <summary>
    /// Builds the per-user payload (Layer 2) for injection into CC context.
    /// Includes user identity, KB access, MCP tokens, memory summary.
    /// </summary>
    Task<CCContextEnvelope> BuildEnvelopeAsync(
        string userId,
        string userDisplayName,
        string taskInstructions,
        CancellationToken ct = default);
}
