namespace FortressAI.Web.Services;

public class ForgeQueryService
{
    private readonly KnowledgeBaseService _kbService;
    private readonly ILogger<ForgeQueryService> _logger;

    public ForgeQueryService(KnowledgeBaseService kbService, ILogger<ForgeQueryService> logger)
    {
        _kbService = kbService;
        _logger = logger;
    }

    /// <summary>
    /// Get semantic KB context for a user query via Bedrock Retrieve.
    /// Searches personal KB and optionally multiple team KBs.
    /// Returns empty string if nothing is configured or no relevant results found.
    /// </summary>
    public async Task<string> GetKbContextAsync(Guid userId, string query, bool personalKbEnabled = true, List<int>? teamKbIds = null, Guid? projectId = null)
    {
        var chunks = new List<KbChunk>();

        // Personal KB — Bedrock semantic search
        if (personalKbEnabled)
        {
        try
        {
            var personalChunks = await _kbService.RetrievePersonalAsync(query, userId);
            chunks.AddRange(personalChunks.Select(c => { c.KbType = "Personal"; return c; }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Personal KB retrieval failed for user {UserId}", userId);
        }
        }

        // Team KBs — query each selected team
        if (teamKbIds?.Any() == true)
        {
            foreach (var teamId in teamKbIds)
            {
                try
                {
                    var teamChunks = await _kbService.RetrieveTeamAsync(query, teamId);
                    chunks.AddRange(teamChunks.Select(c => { c.KbType = $"Team:{teamId}"; return c; }));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Team KB retrieval failed for team {TeamId}", teamId);
                }
            }
        }

        // Project KB — if conversation is in a project with RAG mode
        if (projectId.HasValue)
        {
            try
            {
                var projectChunks = await _kbService.RetrieveProjectAsync(query, projectId.Value);
                chunks.AddRange(projectChunks.Select(c => { c.KbType = "Project"; return c; }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Project KB retrieval failed for project {ProjectId}", projectId.Value);
            }
        }

        if (!chunks.Any()) return string.Empty;

        return _kbService.FormatKbContext(chunks);
    }

    /// <summary>
    /// Multi-query variant of GetKbContextAsync. Fans out all queries in parallel
    /// across personal/team/project KBs, then merges + deduplicates.
    /// Returns context string + chunk list for UI metadata.
    /// Falls back to empty result on any failure — never throws.
    /// </summary>
    public async Task<(string Context, List<KbChunk> Chunks, int FilteredCount)> GetKbContextMultiQueryAsync(
        Guid userId,
        IEnumerable<string> queries,
        bool personalKbEnabled = true,
        List<int>? teamKbIds = null,
        Guid? projectId = null,
        double minScore = 0.35)
    {
        var queryList = queries?.Where(q => !string.IsNullOrWhiteSpace(q)).ToList() ?? new();
        if (!queryList.Any()) return (string.Empty, new(), 0);

        Console.WriteLine($"[ForgeQuery.GetKbContextMultiQueryAsync] personalKbEnabled={personalKbEnabled} teamKbIds={teamKbIds?.Count ?? 0} projectId={projectId} queries={queryList.Count}");
        // Log once (outside loop) what KB types will be searched
        if (personalKbEnabled) Console.WriteLine("[ForgeQuery] Will search: Personal KB");
        if (teamKbIds?.Any() == true) Console.WriteLine($"[ForgeQuery] Will search: {teamKbIds.Count} Team KB(s)");
        if (projectId.HasValue) Console.WriteLine($"[ForgeQuery] Will search: Project KB ({projectId})");

        var allChunks = new List<KbChunk>();

        // Fan out all queries × all KB types in parallel
        var tasks = new List<Task<List<KbChunk>>>();

        foreach (var query in queryList)
        {
            // Personal KB
            if (personalKbEnabled)
            {
                tasks.Add(_kbService.RetrievePersonalAsync(query, userId)
                    .ContinueWith(t => t.IsCompletedSuccessfully
                        ? t.Result.Select(c => { c.KbType = "Personal"; return c; }).ToList()
                        : new List<KbChunk>()));
            }

            // Team KBs
            if (teamKbIds?.Any() == true)
            {
                foreach (var teamId in teamKbIds)
                {
                    var capturedTeamId = teamId; // closure capture
                    tasks.Add(_kbService.RetrieveTeamAsync(query, capturedTeamId)
                        .ContinueWith(t => t.IsCompletedSuccessfully
                            ? t.Result.Select(c => { c.KbType = $"Team:{capturedTeamId}"; return c; }).ToList()
                            : new List<KbChunk>()));
                }
            }

            // Project KB
            if (projectId.HasValue)
            {
                var capturedProjectId = projectId.Value;
                tasks.Add(_kbService.RetrieveProjectAsync(query, capturedProjectId)
                    .ContinueWith(t => t.IsCompletedSuccessfully
                        ? t.Result.Select(c => { c.KbType = "Project"; return c; }).ToList()
                        : new List<KbChunk>()));
            }
        }

        var resultSets = await Task.WhenAll(tasks);
        allChunks.AddRange(resultSets.SelectMany(r => r));

        // Deduplicate by content hash
        var seen = new HashSet<string>();
        var deduped = allChunks
            .Where(chunk => seen.Add(ComputeContentHash(chunk.Content)))
            .ToList();

        // Apply score threshold
        var preFilter = deduped.Count;
        var surviving = deduped.Where(c => c.Score >= minScore).OrderByDescending(c => c.Score).Take(8).ToList();
        var filteredCount = preFilter - surviving.Count;

        _logger.LogInformation(
            "[ForgeMultiQuery] queries={QCount} raw={Raw} deduped={Dedup} surviving={Surv} filtered={Filt}",
            queryList.Count, allChunks.Count, deduped.Count, surviving.Count, filteredCount);

        var context = surviving.Any() ? _kbService.FormatKbContext(surviving) : string.Empty;
        return (context, surviving, filteredCount);
    }

    private static string ComputeContentHash(string content)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\s+", " ");
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16];
    }
}
