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

        var body = await resp.Content.ReadFromJsonAsync<StartTaskResponse>(ct: ct);
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

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("cowork-agent");
        var token = _tokens.Issue(_session.UserId, _session.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private record StartTaskResponse(string TaskId);
}
