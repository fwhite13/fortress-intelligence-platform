namespace FortressAI.Web.Services;

/// <summary>
/// Result of a KB retrieval pass. Carries the injected context string + metadata
/// needed by the UI indicator.
/// </summary>
public class KbRetrievalResult
{
    /// <summary>Was KB retrieval attempted at all (i.e., is KB enabled for this chat)?</summary>
    public bool WasSearched { get; set; }

    /// <summary>Formatted context string to inject into system prompt. Empty = nothing to inject.</summary>
    public string FormattedContext { get; set; } = "";

    /// <summary>Chunks that survived dedup + threshold filtering and were injected.</summary>
    public List<KbChunk> InjectedChunks { get; set; } = new();

    /// <summary>How many chunks were retrieved but filtered out by score threshold.</summary>
    public int FilteredOutCount { get; set; }

    /// <summary>Queries that were actually sent to Bedrock Retrieve.</summary>
    public List<string> QueriesUsed { get; set; } = new();

    /// <summary>Per-KB source stats for the UI expand panel.</summary>
    public List<KbSourceStat> SourceStats { get; set; } = new();

    public bool HasResults => InjectedChunks.Any();

    public int UniqueSourceCount => InjectedChunks
        .Select(c => c.Source?.Split('/').Last() ?? "")
        .Where(s => !string.IsNullOrEmpty(s))
        .Distinct()
        .Count();
}

public class KbSourceStat
{
    /// <summary>Display name: "Fortress Corp KB", "Personal KB", "Team KB: Marketing", "Project KB"</summary>
    public string KbName { get; set; } = "";

    public int ResultCount { get; set; }

    /// <summary>Distinct source document names returned from this KB.</summary>
    public List<string> SourceDocuments { get; set; } = new();
}
