namespace FortressAI.V2.Web.Services;

public interface IForgeKbService
{
    /// <summary>List KBs accessible to the current user.</summary>
    Task<IReadOnlyList<KbInfo>> ListKbsAsync(string entraOid, CancellationToken ct = default);

    /// <summary>Search a KB for content relevant to the query.</summary>
    Task<IReadOnlyList<KbSearchResult>> SearchKbAsync(string kbId, string query, int topK = 5, CancellationToken ct = default);

    /// <summary>Add content to a KB. Returns job ID for polling.</summary>
    Task<string> AddToKbAsync(string kbId, string content, Dictionary<string, string> metadata, CancellationToken ct = default);

    /// <summary>Upload raw file bytes to S3 and start KB ingestion. Returns job ID.</summary>
    Task<string> UploadFileAsync(string kbId, Stream fileStream, string filename, string contentType, CancellationToken ct = default);

    /// <summary>Get metadata for a KB.</summary>
    Task<KbMetadata> GetKbMetadataAsync(string kbId, CancellationToken ct = default);
}

public record KbInfo(string KbId, string KbType, string Description, bool Writable);
public record KbSearchResult(string Content, object Metadata, double RelevanceScore);
public record KbMetadata(string KbId, string KbType, int DocumentCount, DateTime LastUpdated, string DataSourceId);
