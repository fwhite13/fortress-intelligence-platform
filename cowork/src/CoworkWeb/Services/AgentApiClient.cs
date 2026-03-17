using System.Net.Http.Headers;

namespace CoworkWeb.Services;

/// <summary>
/// Proxies requests from Blazor to the CoworkAgent Node.js API.
/// Injects a short-lived internal JWT per request to carry user identity.
/// </summary>
public sealed class AgentApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InternalTokenService _tokens;
    private readonly CoworkSessionService _session;

    public AgentApiClient(
        IHttpClientFactory httpClientFactory,
        InternalTokenService tokens,
        CoworkSessionService session)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _session = session;
    }

    public async Task<string> StartTaskAsync(string prompt, IEnumerable<(string Name, Stream Data, string ContentType)> files, CancellationToken ct = default)
    {
        var client = CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(prompt), "prompt");

        foreach (var (name, data, contentType) in files)
        {
            var fileContent = new StreamContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "files", name);
        }

        var resp = await client.PostAsync("/tasks", form, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<StartTaskResponse>(cancellationToken: ct);
        return body?.TaskId ?? throw new InvalidOperationException("Agent API did not return taskId");
    }

    public async Task<Stream> OpenStreamAsync(string taskId, CancellationToken ct = default)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/tasks/{taskId}/stream");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    // ── Approval ──────────────────────────────────────────────────────────────

    /// <summary>Send an approve or reject decision for a pending tool call.</summary>
    public async Task SendApprovalAsync(string taskId, string approvalId, bool approve, CancellationToken ct = default)
    {
        var client = CreateClient();
        var action = approve ? "approve" : "reject";
        var resp   = await client.PostAsJsonAsync($"/tasks/{taskId}/{action}", new { approvalId }, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Task history ──────────────────────────────────────────────────────────

    /// <summary>Get task history for the current user (most recent first, up to 20).</summary>
    public async Task<List<TaskSummary>> GetTaskHistoryAsync(CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp   = await client.GetAsync("/tasks", ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<TaskListResponse>(ct: ct);
        return body?.Tasks ?? new List<TaskSummary>();
    }

    /// <summary>Get metadata for a single task (returns null if not found or not owned by user).</summary>
    public async Task<TaskSummary?> GetTaskMetaAsync(string taskId, CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp   = await client.GetAsync($"/tasks/{taskId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TaskSummary>(ct: ct);
    }

    /// <summary>Cancel a running task (sends reject for any pending approval + signals cancellation).</summary>
    public async Task CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        var client = CreateClient();
        // Best-effort — ignore errors (task may already be done)
        try { await client.PostAsJsonAsync($"/tasks/{taskId}/cancel", new { }, ct); }
        catch { /* Non-fatal */ }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("cowork-agent");
        var token = _tokens.Issue(_session.UserId, _session.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private record StartTaskResponse(string TaskId);
}

public record OutputFileSummary(string Name, string Type, string DownloadUrl);

public record TaskSummary(
    string TaskId,
    string Status,
    string Prompt,
    string CreatedAt,
    string? CompletedAt,
    List<OutputFileSummary> OutputFiles);

file record TaskListResponse(List<TaskSummary> Tasks);
