using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

namespace FortressAI.Web.Services;

public class KnowledgeBaseService
{
    private readonly IAmazonBedrockAgentRuntime _client;
    private readonly string _corpKbId;
    private readonly string _personalKbId;
    private readonly string _teamKbId;
    private readonly string _projectKbId;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(IAmazonBedrockAgentRuntime client, IConfiguration config, ILogger<KnowledgeBaseService> logger)
    {
        _client = client;
        _corpKbId    = config["KnowledgeBase:CorpKbId"] ?? "";
        _personalKbId = config["KnowledgeBase:PersonalKbId"] ?? "";
        _teamKbId    = config["KnowledgeBase:TeamKbId"] ?? "";
        _projectKbId = config["KnowledgeBase:ProjectKbId"] ?? "";
        _logger = logger;
    }

    /// <summary>Retrieve from Corp KB. No filter — entire KB is Corp (structural isolation).</summary>
    public async Task<List<KbChunk>> RetrieveCorpAsync(string query)
    {
        if (string.IsNullOrEmpty(_corpKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _corpKbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration { NumberOfResults = 3 }
                }
            });

            _logger.LogInformation("Corp KB retrieval: raw={RawCount} results, query='{Query}'",
                response.RetrievalResults.Count, query.Length > 50 ? query[..50] + "..." : query);

            return response.RetrievalResults
                .Where(r => r.Score > 0.3)
                .Select(r => new KbChunk
                {
                    Content = r.Content.Text,
                    Source = r.Location?.S3Location?.Uri ?? "Corp KB",
                    Score = r.Score,
                    KbType = "Fortress"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Corp KB retrieval failed — continuing without KB context");
            return new();
        }
    }

    /// <summary>Retrieve from Personal KB using metadata filter on ownerId. Returns empty if not configured.</summary>
    public async Task<List<KbChunk>> RetrievePersonalAsync(string query, Guid userId)
    {
        if (string.IsNullOrEmpty(_personalKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _personalKbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = 5,
                        // Personal KB: filter on ownerId only.
                        // No compound AndAll filter needed — personal KB structurally contains ONLY personal docs.
                        // No kbType != "project" exclusion needed — that was the old shared-KB hack.
                        Filter = new RetrievalFilter
                        {
                            Equals = new FilterAttribute
                            {
                                Key = "ownerId",
                                Value = new Amazon.Runtime.Documents.Document(userId.ToString())
                            }
                        }
                    }
                }
            });

            _logger.LogInformation("Personal KB retrieval for user {UserId}: raw={RawCount} results, query='{Query}'",
                userId, response.RetrievalResults.Count, query.Length > 50 ? query[..50] + "..." : query);
            foreach (var r in response.RetrievalResults)
                _logger.LogDebug("  KB result score={Score:F3} source={Source}",
                    r.Score, r.Location?.S3Location?.Uri ?? "unknown");

            return response.RetrievalResults
                .Where(r => r.Score > 0.3)
                .Select(r => new KbChunk
                {
                    Content = r.Content.Text,
                    Source = r.Location?.S3Location?.Uri ?? "Personal KB",
                    Score = r.Score,
                    KbType = "Personal"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Personal KB retrieval failed — continuing without personal KB context");
            return new();
        }
    }

    /// <summary>Retrieve from Team KB using metadata filter on teamId. Returns empty if not configured.</summary>
    public async Task<List<KbChunk>> RetrieveTeamAsync(string query, int teamId)
    {
        if (string.IsNullOrEmpty(_teamKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _teamKbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = 5,
                        Filter = new RetrievalFilter
                        {
                            Equals = new FilterAttribute
                            {
                                Key = "teamId",
                                Value = new Amazon.Runtime.Documents.Document(teamId.ToString())
                            }
                        }
                    }
                }
            });

            _logger.LogInformation("Team KB retrieval for team {TeamId}: raw={RawCount} results, query='{Query}'",
                teamId, response.RetrievalResults.Count, query.Length > 50 ? query[..50] + "..." : query);
            foreach (var r in response.RetrievalResults)
                _logger.LogDebug("  Team KB result score={Score:F3} source={Source}",
                    r.Score, r.Location?.S3Location?.Uri ?? "unknown");

            return response.RetrievalResults
                .Where(r => r.Score > 0.3)
                .Select(r => new KbChunk
                {
                    Content = r.Content.Text,
                    Source = r.Location?.S3Location?.Uri ?? "Team KB",
                    Score = r.Score,
                    KbType = "Team"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Team KB retrieval failed for team {TeamId} — continuing without team KB context", teamId);
            return new();
        }
    }

    /// <summary>Retrieve from Project KB using metadata filter on projectId. Returns empty if not configured.</summary>
    public async Task<List<KbChunk>> RetrieveProjectAsync(string query, Guid projectId)
    {
        if (string.IsNullOrEmpty(_projectKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _projectKbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = 8,
                        // Project KB: filter on projectId only.
                        // No kbType filter needed — Project KB structurally contains ONLY project docs.
                        Filter = new RetrievalFilter
                        {
                            Equals = new FilterAttribute
                            {
                                Key = "projectId",
                                Value = new Amazon.Runtime.Documents.Document(projectId.ToString())
                            }
                        }
                    }
                }
            });

            _logger.LogInformation("Project KB retrieval for project {ProjectId}: raw={RawCount} results, query='{Query}'",
                projectId, response.RetrievalResults.Count, query.Length > 50 ? query[..50] + "..." : query);

            foreach (var chunk in response.RetrievalResults.Take(3))
            {
                _logger.LogDebug("[KB-RETRIEVE-PROJECT] Score={Score:F3} Source={Source} Meta={Meta}",
                    chunk.Score,
                    chunk.Location?.S3Location?.Uri ?? "unknown",
                    string.Join(",", chunk.Metadata?.Select(m => $"{m.Key}={m.Value}") ?? Array.Empty<string>()));
            }

            return response.RetrievalResults
                .Where(r => r.Score > 0.3)
                .Select(r => new KbChunk
                {
                    Content = r.Content.Text,
                    Source = r.Location?.S3Location?.Uri ?? "Project KB",
                    Score = r.Score,
                    KbType = "Project"
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Project KB retrieval failed for project {ProjectId}", projectId);
            return new();
        }
    }

    /// <summary>
    /// Multi-query parallel retrieval scoped to Corp KB only.
    /// Fans out all queries to Corp KB simultaneously, then merges + deduplicates results.
    /// Called by ChatView.Layer1. Falls back to empty list if queries list is empty or null.
    /// </summary>
    public async Task<List<KbChunk>> RetrieveCorpMultiQueryAsync(
        IEnumerable<string> queries,
        double minScore = 0.35)
    {
        var queryList = queries?.Where(q => !string.IsNullOrWhiteSpace(q)).ToList() ?? new();
        if (!queryList.Any() || string.IsNullOrEmpty(_corpKbId)) return new();

        // Fan out all queries in parallel to Corp KB
        var tasks = queryList.Select(q => RetrieveCorpAsync(q));
        var resultSets = await Task.WhenAll(tasks);

        // Merge all chunks from all queries
        var allChunks = resultSets.SelectMany(r => r).ToList();

        // Deduplicate by content hash (same chunk returned by multiple queries)
        var seen = new HashSet<string>();
        var deduped = allChunks
            .Where(chunk => seen.Add(ComputeContentHash(chunk.Content)))
            .ToList();

        _logger.LogInformation(
            "[KbCorpMultiQuery] queries={QCount} raw={Raw} deduped={Dedup} surviving={Surv}",
            queryList.Count, allChunks.Count, deduped.Count,
            deduped.Count(c => c.Score >= minScore));

        return deduped.Where(c => c.Score >= minScore)
            .OrderByDescending(c => c.Score).Take(6).ToList();
    }

    private static string ComputeContentHash(string content)
    {
        // Normalize whitespace before hashing to catch near-duplicates
        var normalized = System.Text.RegularExpressions.Regex.Replace(content.Trim(), @"\s+", " ");
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16]; // First 16 hex chars is plenty
    }

    public string FormatKbContext(List<KbChunk> chunks)
    {
        if (!chunks.Any()) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n## Knowledge Base Context");
        sb.AppendLine("The following information was retrieved from the Fortress knowledge base. Use it to inform your response where relevant.");
        sb.AppendLine();

        foreach (var chunk in chunks)
        {
            var sourceName = System.IO.Path.GetFileNameWithoutExtension(chunk.Source.Split('/').Last());
            sb.AppendLine($"### [{chunk.KbType} KB: {sourceName}]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public class KbChunk
{
    public string Content { get; set; } = "";
    public string Source { get; set; } = "";
    public double Score { get; set; }
    public string KbType { get; set; } = "";
}
