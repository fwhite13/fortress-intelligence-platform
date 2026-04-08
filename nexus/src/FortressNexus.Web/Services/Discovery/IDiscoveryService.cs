using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services.Discovery;

public interface IDiscoveryService
{
    Task<Guid> InitiateDiscoveryAsync(int submissionId, CancellationToken ct = default);
    Task<DiscoverySession?> GetSessionAsync(int submissionId, CancellationToken ct = default);
    Task<List<DiscoverySession>> GetAllSessionsAsync(int submissionId, CancellationToken ct = default);
    Task SaveAnswersAsync(Guid sessionId, IEnumerable<(Guid QuestionId, string? Answer)> answers,
        string answeredByOid, CancellationToken ct = default);
    Task SkipDiscoveryAsync(Guid sessionId, string skippedByOid, CancellationToken ct = default);
    Task SupersedeSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<string> BuildSpecContextAsync(int submissionId, CancellationToken ct = default);
}
