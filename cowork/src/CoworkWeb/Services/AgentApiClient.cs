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
        var body = await resp.Content.ReadFromJsonAsync<TaskListResponse>(cancellationToken: ct);
        return body?.Tasks ?? new List<TaskSummary>();
    }

    /// <summary>Get metadata for a single task (returns null if not found or not owned by user).</summary>
    public async Task<TaskSummary?> GetTaskMetaAsync(string taskId, CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp   = await client.GetAsync($"/tasks/{taskId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<TaskSummary>(cancellationToken: ct);
    }

    /// <summary>Cancel a running task (sends reject for any pending approval + signals cancellation).</summary>
    public async Task CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        var client = CreateClient();
        try { await client.DeleteAsync($"/tasks/{taskId}", ct); }
        catch { /* Non-fatal */ }
    }

    public async Task<string?> GetInstructionsAsync(CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp = await client.GetAsync("/users/me/instructions", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadFromJsonAsync<InstructionsResponse>(cancellationToken: ct);
        return body?.Text;
    }

    public async Task SaveInstructionsAsync(string text, CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp = await client.PutAsJsonAsync("/users/me/instructions", new { text }, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Agent metadata ────────────────────────────────────────────────────────

    /// <summary>Get metadata for a single agent by ID (returns null if not found).</summary>
    public async Task<AgentMeta?> GetAgentMetaAsync(string agentId, CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp   = await client.GetAsync($"/agents/{agentId}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgentMeta>(cancellationToken: ct);
    }

    // ── Design Agent ──────────────────────────────────────────────────────────

    /// <summary>Start a new design screen generation task. Returns (taskId, screenId).</summary>
    public async Task<(string TaskId, string ScreenId)> StartDesignScreenAsync(
        string projectId, string prompt, string deviceTarget,
        int variantCount, bool convertToBlazor, string orgId,
        IEnumerable<(string Name, Stream Data, string ContentType)> refs,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(prompt),                                     "prompt");
        form.Add(new StringContent(deviceTarget),                               "deviceTarget");
        form.Add(new StringContent(variantCount.ToString()),                    "variantCount");
        form.Add(new StringContent(convertToBlazor ? "true" : "false"),         "convertToBlazor");
        form.Add(new StringContent(orgId),                                      "orgId");

        foreach (var (name, data, contentType) in refs)
        {
            var fc = new StreamContent(data);
            fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(fc, "refs", name);
        }

        var resp = await client.PostAsync(
            $"/agents/design/projects/{projectId}/screens", form, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<DesignScreenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("No response from design API");
        return (body.TaskId, body.ScreenId);
    }

    /// <summary>Submit an edit to an existing screen. Returns the new taskId.</summary>
    public async Task<string> EditDesignScreenAsync(
        string projectId, string screenId, string prompt,
        string priorHtml, string orgId, string deviceTarget,
        CancellationToken ct = default)
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync(
            $"/agents/design/projects/{projectId}/screens/{screenId}/edit",
            new { prompt, priorHtml, orgId, deviceTarget }, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<DesignScreenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("No response from design edit API");
        return body.TaskId;
    }

    /// <summary>Open SSE stream for a design task (carries internal JWT same as OpenStreamAsync).</summary>
    public async Task<Stream> OpenDesignStreamAsync(string taskId, CancellationToken ct = default)
    {
        var client  = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/agents/design/tasks/{taskId}/stream");
        request.Headers.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        var resp = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    private record DesignScreenResponse(string TaskId, string ScreenId);

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("cowork-agent");
        var token = _tokens.Issue(_session.UserId, _session.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private record StartTaskResponse(string TaskId);
    private record InstructionsResponse(string Text, string? UpdatedAt);
}

public record AgentMeta(string Id, string Name, string Description, string Icon, string Color);

public record OutputFileSummary(string Name, string Type, string DownloadUrl);

public record TaskSummary(
    string TaskId,
    string Status,
    string Prompt,
    string CreatedAt,
    string? CompletedAt,
    List<OutputFileSummary> OutputFiles);

file record TaskListResponse(List<TaskSummary> Tasks);
