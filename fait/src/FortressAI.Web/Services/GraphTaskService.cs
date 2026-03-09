using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class GraphTaskService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly MicrosoftTokenService _tokenService;
    private readonly ILogger<GraphTaskService> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _useStubAuth;

    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const int MaxRetries = 3;

    public GraphTaskService(
        IDbContextFactory<AppDbContext> dbFactory,
        MicrosoftTokenService tokenService,
        ILogger<GraphTaskService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _dbFactory = dbFactory;
        _tokenService = tokenService;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _useStubAuth = configuration.GetValue<bool>("UseStubAuth", false);
    }

    /// <summary>
    /// Fetches the user's incomplete Planner tasks from MS Graph, caches them, and returns the list.
    /// In stub mode returns realistic mock data.
    /// </summary>
    public async Task<List<TaskItem>> GetUserTasksAsync(Guid userId)
    {
        if (_useStubAuth)
        {
            _logger.LogInformation("Stub auth: returning mock tasks for user {UserId}", userId);
            return GetMockTasks(userId);
        }

        var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
        if (accessToken == null)
        {
            _logger.LogWarning("No valid Microsoft token for user {UserId}; returning cached tasks", userId);
            return await GetCachedTasksAsync(userId);
        }

        try
        {
            var tasks = await FetchTasksFromGraphAsync(accessToken, userId);
            await UpsertTaskCacheAsync(userId, tasks);
            return tasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch tasks from Graph API for user {UserId}; returning cached", userId);
            return await GetCachedTasksAsync(userId);
        }
    }

    private async Task<List<TaskItem>> FetchTasksFromGraphAsync(string accessToken, Guid userId)
    {
        var allTasks = new List<TaskItem>();
        var planTitleCache = new Dictionary<string, string>();

        var url = $"{GraphBaseUrl}/me/planner/tasks?$filter=percentComplete ne 100&$orderby=dueDateTime/dateTime";

        while (!string.IsNullOrEmpty(url))
        {
            var response = await SendGraphRequestWithRetryAsync(url, accessToken);
            if (response == null) break;

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);

            if (json.TryGetProperty("value", out var tasksArray))
            {
                foreach (var task in tasksArray.EnumerateArray())
                {
                    var planId = task.TryGetProperty("planId", out var pid) ? pid.GetString() : null;
                    string? planTitle = null;

                    if (!string.IsNullOrEmpty(planId))
                    {
                        if (!planTitleCache.TryGetValue(planId, out planTitle))
                        {
                            planTitle = await FetchPlanTitleAsync(planId, accessToken);
                            if (planTitle != null) planTitleCache[planId] = planTitle;
                        }
                    }

                    var bucketId = task.TryGetProperty("bucketId", out var bid) ? bid.GetString() : null;
                    string? bucketName = null;
                    if (!string.IsNullOrEmpty(bucketId))
                    {
                        bucketName = await FetchBucketNameAsync(bucketId, accessToken);
                    }

                    DateTime? dueDate = null;
                    if (task.TryGetProperty("dueDateTime", out var dd) && dd.ValueKind != JsonValueKind.Null)
                    {
                        if (DateTime.TryParse(dd.GetString(), out var parsed))
                            dueDate = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    }

                    var priority = 5;
                    if (task.TryGetProperty("priority", out var pri) && pri.ValueKind == JsonValueKind.Number)
                        priority = pri.GetInt32();

                    allTasks.Add(new TaskItem
                    {
                        UserId = userId,
                        TaskId = task.GetProperty("id").GetString() ?? "",
                        Title = task.GetProperty("title").GetString() ?? "(untitled)",
                        DueDate = dueDate,
                        PercentComplete = task.TryGetProperty("percentComplete", out var pc) ? pc.GetInt32() : 0,
                        Priority = priority,
                        PlanTitle = planTitle,
                        BucketName = bucketName,
                        LastFetchedAt = DateTime.UtcNow
                    });
                }
            }

            // Handle pagination
            url = json.TryGetProperty("@odata.nextLink", out var nextLink)
                ? nextLink.GetString()
                : null;
        }

        _logger.LogInformation("Fetched {Count} incomplete tasks from Graph API for user {UserId}", allTasks.Count, userId);
        return allTasks;
    }

    private async Task<string?> FetchPlanTitleAsync(string planId, string accessToken)
    {
        try
        {
            var response = await SendGraphRequestWithRetryAsync($"{GraphBaseUrl}/planner/plans/{planId}?$select=title", accessToken);
            if (response == null) return null;

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            return json.TryGetProperty("title", out var title) ? title.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch plan title for {PlanId}", planId);
            return null;
        }
    }

    private async Task<string?> FetchBucketNameAsync(string bucketId, string accessToken)
    {
        try
        {
            var response = await SendGraphRequestWithRetryAsync($"{GraphBaseUrl}/planner/buckets/{bucketId}?$select=name", accessToken);
            if (response == null) return null;

            var body = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            return json.TryGetProperty("name", out var name) ? name.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch bucket name for {BucketId}", bucketId);
            return null;
        }
    }

    private async Task<HttpResponseMessage?> SendGraphRequestWithRetryAsync(string url, string accessToken)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return response;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Graph API rate limited. Retrying in {Seconds}s (attempt {Attempt}/{Max})",
                    retryAfter.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(retryAfter);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Graph API returned 401 — token may be invalid");
                return null;
            }

            _logger.LogWarning("Graph API returned {StatusCode} for {Url}", response.StatusCode, url);
            return null;
        }

        _logger.LogError("Graph API request failed after {MaxRetries} retries for {Url}", MaxRetries, url);
        return null;
    }

    private async Task UpsertTaskCacheAsync(Guid userId, List<TaskItem> tasks)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Remove old cached tasks for this user
        var existing = await db.TaskCache.Where(t => t.UserId == userId).ToListAsync();
        db.TaskCache.RemoveRange(existing);

        // Insert fresh data
        db.TaskCache.AddRange(tasks);
        await db.SaveChangesAsync();

        _logger.LogInformation("Cached {Count} tasks for user {UserId}", tasks.Count, userId);
    }

    private async Task<List<TaskItem>> GetCachedTasksAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.TaskCache
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.DueDate)
            .ToListAsync();
    }

    private static List<TaskItem> GetMockTasks(Guid userId)
    {
        var now = DateTime.UtcNow;
        return new List<TaskItem>
        {
            new()
            {
                UserId = userId,
                TaskId = "mock-task-001",
                Title = "Review Q4 budget proposal from Higginbotham",
                DueDate = now.AddDays(-1),  // Overdue
                PercentComplete = 0,
                Priority = 1,  // Urgent
                PlanTitle = "Fortress AM Operations",
                BucketName = "Finance",
                LastFetchedAt = now
            },
            new()
            {
                UserId = userId,
                TaskId = "mock-task-002",
                Title = "Prepare talking points for client renewal meeting",
                DueDate = now.Date,  // Due today
                PercentComplete = 50,
                Priority = 3,
                PlanTitle = "Fortress AM Operations",
                BucketName = "Client Relations",
                LastFetchedAt = now
            },
            new()
            {
                UserId = userId,
                TaskId = "mock-task-003",
                Title = "Complete FAIT v2 Phase 3 architecture review",
                DueDate = now.AddDays(2),  // Upcoming
                PercentComplete = 25,
                Priority = 3,
                PlanTitle = "IT Projects",
                BucketName = "Development",
                LastFetchedAt = now
            },
            new()
            {
                UserId = userId,
                TaskId = "mock-task-004",
                Title = "Renew VPN certificate before expiration",
                DueDate = now.AddDays(3),
                PercentComplete = 0,
                Priority = 5,
                PlanTitle = "IT Projects",
                BucketName = "Infrastructure",
                LastFetchedAt = now
            },
            new()
            {
                UserId = userId,
                TaskId = "mock-task-005",
                Title = "Schedule quarterly team offsite",
                DueDate = now.AddDays(7),
                PercentComplete = 0,
                Priority = 7,
                PlanTitle = "Fortress AM Operations",
                BucketName = "Team Management",
                LastFetchedAt = now
            },
            new()
            {
                UserId = userId,
                TaskId = "mock-task-006",
                Title = "Submit benefits enrollment selections",
                DueDate = now.Date,  // Due today — synced with Lambda GenerateMockTasks()
                PercentComplete = 0,
                Priority = 3,
                PlanTitle = "Fortress AM Operations",
                BucketName = "Admin",
                LastFetchedAt = now
            }
        };
    }
}
