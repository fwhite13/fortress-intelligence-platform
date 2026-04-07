namespace FortressNexus.Web.Services.Discovery;

public interface IKnowledgeBaseService
{
    Task<IEnumerable<KbPassage>> RetrieveAsync(string query, int maxResults = 5,
        CancellationToken ct = default);
}

public record KbPassage(string Content, string SourceUri, double Score);
