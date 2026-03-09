using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

namespace FortressAI.Web.Services;

public class KnowledgeBaseService
{
    private readonly IAmazonBedrockAgentRuntime _client;
    private readonly string _fortressKbId;
    private readonly string _personalTeamKbId;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(IAmazonBedrockAgentRuntime client, IConfiguration config, ILogger<KnowledgeBaseService> logger)
    {
        _client = client;
        _fortressKbId = config["KnowledgeBase:FortressKbId"] ?? "";
        _personalTeamKbId = config["KnowledgeBase:PersonalTeamKbId"] ?? "";
        _logger = logger;
    }

    public async Task<List<KbChunk>> RetrieveAsync(string query, bool useFortressKb, bool usePersonalKb, Guid? userId = null)
    {
        if (!useFortressKb && !usePersonalKb) return new();
        if (string.IsNullOrEmpty(_fortressKbId) && !usePersonalKb) return new();  // Not configured — silent no-op

        var results = new List<KbChunk>();

        if (useFortressKb && !string.IsNullOrEmpty(_fortressKbId))
        {
            try
            {
                var response = await _client.RetrieveAsync(new RetrieveRequest
                {
                    KnowledgeBaseId = _fortressKbId,
                    RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                    RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                    {
                        VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration { NumberOfResults = 3 }
                    }
                });

                results.AddRange(response.RetrievalResults
                    .Where(r => r.Score > 0.3)  // Only include reasonably relevant results
                    .Select(r => new KbChunk
                    {
                        Content = r.Content.Text,
                        Source = r.Location?.S3Location?.Uri ?? "Fortress KB",
                        Score = r.Score,
                        KbType = "Fortress"
                    }));
                _logger.LogInformation("Fortress KB retrieval: raw={RawCount} results added, query='{Query}'",
                    response.RetrievalResults.Count, query.Length > 50 ? query[..50] + "..." : query);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KB retrieval failed — continuing without KB context");
                // Fail silently — don't break chat
            }
        }

        if (usePersonalKb && userId.HasValue && !string.IsNullOrEmpty(_personalTeamKbId))
        {
            try
            {
                var personalChunks = await RetrievePersonalAsync(query, userId.Value);
                results.AddRange(personalChunks.Select(c => { c.KbType = "Personal"; return c; }));
                _logger.LogInformation("[KB] Personal KB retrieval: {Count} results for user {UserId}", personalChunks.Count, userId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Personal KB retrieval failed in RetrieveAsync for user {UserId}", userId.Value);
            }
        }

        return results.OrderByDescending(r => r.Score).Take(3).ToList();
    }

    /// <summary>Retrieve from personal KB using metadata filter on ownerId. Returns empty if not configured.</summary>
    public async Task<List<KbChunk>> RetrievePersonalAsync(string query, Guid userId)
    {
        if (string.IsNullOrEmpty(_personalTeamKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _personalTeamKbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = 5,
                        // Personal KB filter: match ownerId AND exclude project docs.
                        // Project docs also have ownerId set to their creator — without the NOT clause,
                        // personal KB retrieval would silently return the user's own project documents.
                        Filter = new RetrievalFilter
                        {
                            AndAll = new List<RetrievalFilter>
                            {
                                new() { Equals = new FilterAttribute
                                    { Key = "ownerId", Value = new Amazon.Runtime.Documents.Document(userId.ToString()) } },
                                new() { NotEquals = new FilterAttribute
                                    { Key = "kbType", Value = new Amazon.Runtime.Documents.Document("project") } }
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

    /// <summary>Retrieve from team KB using metadata filter on teamId. Returns empty if not configured.</summary>
    public async Task<List<KbChunk>> RetrieveTeamAsync(string query, int teamId)
    {
        if (string.IsNullOrEmpty(_personalTeamKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _personalTeamKbId,
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

    /// <summary>Retrieve from project KB using kbType=project + projectId filter.</summary>
    public async Task<List<KbChunk>> RetrieveProjectAsync(string query, Guid projectId)
    {
        if (string.IsNullOrEmpty(_personalTeamKbId)) return new();

        try
        {
            var response = await _client.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = _personalTeamKbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = 8,
                        Filter = new RetrievalFilter
                        {
                            AndAll = new List<RetrievalFilter>
                            {
                                new() { Equals = new FilterAttribute { Key = "kbType", Value = new Amazon.Runtime.Documents.Document("project") } },
                                new() { Equals = new FilterAttribute { Key = "projectId", Value = new Amazon.Runtime.Documents.Document(projectId.ToString()) } }
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
    /// Multi-query parallel retrieval. Fans out all queries to Bedrock Retrieve
    /// simultaneously, then merges + deduplicates results.
    /// Falls back to empty list if queries list is empty or null.
    /// </summary>
    public async Task<List<KbChunk>> RetrieveMultiQueryAsync(
        IEnumerable<string> queries,
        bool useFortressKb,
        bool usePersonalKb,
        Guid? userId = null,
        double minScore = 0.35)
    {
        var queryList = queries?.Where(q => !string.IsNullOrWhiteSpace(q)).ToList() ?? new();
        if (!queryList.Any()) return new();
        if (!useFortressKb && !usePersonalKb) return new();

        Console.WriteLine($"[KbSvc.RetrieveMultiQueryAsync] useFortressKb={useFortressKb} usePersonalKb={usePersonalKb} userId={userId} queries={queryList.Count}");

        // Fan out all queries in parallel
        var tasks = queryList.Select(q => RetrieveAsync(q, useFortressKb, usePersonalKb, userId));
        var resultSets = await Task.WhenAll(tasks);

        // Merge all chunks from all queries
        var allChunks = resultSets.SelectMany(r => r).ToList();

        // Deduplicate by content hash (same chunk returned by multiple queries)
        var seen = new HashSet<string>();
        var deduped = allChunks
            .Where(chunk => seen.Add(ComputeContentHash(chunk.Content)))
            .ToList();

        // Apply score threshold
        var surviving = deduped.Where(c => c.Score >= minScore).ToList();

        _logger.LogInformation(
            "[KbMultiQuery] queries={QCount} raw={Raw} deduped={Dedup} surviving={Surv} filtered={Filt}",
            queryList.Count, allChunks.Count, deduped.Count, surviving.Count,
            deduped.Count - surviving.Count);

        return surviving.OrderByDescending(c => c.Score).Take(6).ToList();
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
