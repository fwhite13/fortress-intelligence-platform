using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services.Discovery;

public interface IDiscoveryService
{
    Task<Guid> InitiateDiscoveryAsync(int submissionId, CancellationToken ct = default);
    Task<DiscoverySession?> GetSessionAsync(int submissionId, CancellationToken ct = default);
    Task<List<DiscoverySession>> GetAllSessionsAsync(int submissionId, CancellationToken ct = default);

    // Legacy single-round save — kept for backward compat
    Task SaveAnswersAsync(Guid sessionId, IEnumerable<(Guid QuestionId, string? Answer)> answers,
        string answeredByOid, CancellationToken ct = default);

    // Two-phase iterative discovery (ND-2 / ND-3)
    Task SaveRoundAnswersAsync(Guid sessionId, int phase, int round,
        IEnumerable<(Guid QuestionId, string? Answer)> answers,
        string upn, CancellationToken ct = default);

    Task AdvanceToPhase2Async(Guid sessionId, string upn, CancellationToken ct = default);

    Task SkipDiscoveryAsync(Guid sessionId, string skippedByOid, CancellationToken ct = default);
    Task SupersedeSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<string> BuildSpecContextAsync(int submissionId, CancellationToken ct = default);
}
