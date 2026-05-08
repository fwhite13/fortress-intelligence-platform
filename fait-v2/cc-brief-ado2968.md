# CC Opus Brief: ADO#2968 — Replace fip-mcp with direct integrations

## Working Directory
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

## Overview
Replace all fip-mcp HTTP dependencies in fait-v2 with direct API implementations copied from FAIT v1.
The fip-mcp server (mcp.fortressam.ai) returns 401 Unauthorized on every call, causing startup failures.
This is a P1 blocking fix — implement all direct service integrations and clean up fip-mcp references.

---

## File: `FortressAI.V2.Web.csproj`

Add to the `<ItemGroup>` with AWS packages:
```xml
<PackageReference Include="AWSSDK.BedrockAgent" Version="3.7.*" />
<PackageReference Include="AWSSDK.BedrockRuntime" Version="3.7.*" />
<PackageReference Include="AWSSDK.BedrockAgentRuntime" Version="3.7.*" />
```

---

## Task 1: Replace ForgeKbService with direct Bedrock implementation

### File: `Services/ForgeKbService.cs` — REPLACE ENTIRELY with:

```csharp
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;
using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;

namespace FortressAI.V2.Web.Services;

/// <summary>
/// Direct AWS Bedrock Knowledge Base implementation.
/// Replaces the old fip-mcp HTTP client approach (which required Entra auth that isn't wired yet).
/// Reads KB IDs from config: KnowledgeBase:CorpKbId, KnowledgeBase:PersonalKbId, KnowledgeBase:TeamKbId.
/// </summary>
public class ForgeKbService : IForgeKbService
{
    private readonly IAmazonBedrockAgentRuntime _bedrockRuntime;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<ForgeKbService> _logger;

    private readonly string _corpKbId;
    private readonly string _personalKbId;
    private readonly string _teamKbId;
    private readonly string _s3Bucket;

    public ForgeKbService(
        IAmazonBedrockAgentRuntime bedrockRuntime,
        IAmazonBedrockAgent bedrockAgent,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<ForgeKbService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _bedrockAgent = bedrockAgent;
        _s3 = s3;
        _config = config;
        _logger = logger;

        _corpKbId = config["KnowledgeBase:CorpKbId"] ?? "";
        _personalKbId = config["KnowledgeBase:PersonalKbId"] ?? "";
        _teamKbId = config["KnowledgeBase:TeamKbId"] ?? "";
        _s3Bucket = config["AWS:S3Bucket"] ?? "fortress-tools";
    }

    public Task<IReadOnlyList<KbInfo>> ListKbsAsync(string entraOid, CancellationToken ct = default)
    {
        // Return configured KBs directly from config — no HTTP call needed
        var kbs = new List<KbInfo>();

        if (!string.IsNullOrEmpty(_corpKbId))
            kbs.Add(new KbInfo(_corpKbId, "corp", "Fortress Corporate Knowledge Base", false));

        if (!string.IsNullOrEmpty(_personalKbId))
            kbs.Add(new KbInfo(_personalKbId, "personal", "Personal Knowledge Base", true));

        if (!string.IsNullOrEmpty(_teamKbId))
            kbs.Add(new KbInfo(_teamKbId, "team", "Team Knowledge Base", false));

        return Task.FromResult<IReadOnlyList<KbInfo>>(kbs);
    }

    public async Task<IReadOnlyList<KbSearchResult>> SearchKbAsync(
        string kbId, string query, int topK = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kbId))
            return Array.Empty<KbSearchResult>();

        try
        {
            var response = await _bedrockRuntime.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = kbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = Math.Min(topK, 10)
                    }
                }
            }, ct);

            _logger.LogInformation("KB search: kbId={KbId} results={Count} query='{Query}'",
                kbId, response.RetrievalResults.Count,
                query.Length > 50 ? query[..50] + "..." : query);

            return response.RetrievalResults
                .Where(r => r.Score > 0.3)
                .Select(r => new KbSearchResult(
                    r.Content.Text,
                    r.Location?.S3Location?.Uri ?? string.Empty,
                    r.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KB search failed for kbId={KbId}", kbId);
            return Array.Empty<KbSearchResult>();
        }
    }

    public async Task<string> AddToKbAsync(
        string kbId, string content, Dictionary<string, string> metadata, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kbId))
            return string.Empty;

        try
        {
            // Upload content to S3 first
            var s3Key = $"kb-uploads/{kbId}/{Guid.NewGuid()}.txt";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _s3Bucket,
                Key = s3Key,
                InputStream = stream,
                ContentType = "text/plain"
            }, ct);

            _logger.LogInformation("Uploaded KB content to S3: {Key}", s3Key);

            // Trigger Bedrock KB sync (StartIngestionJob)
            // The data source ID must exist; if not configured, log and return the S3 key as the job ID
            var dataSourceId = _config[$"KnowledgeBase:DataSourceId:{kbId}"]
                            ?? _config["KnowledgeBase:DefaultDataSourceId"] ?? "";

            if (!string.IsNullOrEmpty(dataSourceId))
            {
                var ingestionResponse = await _bedrockAgent.StartIngestionJobAsync(
                    new StartIngestionJobRequest
                    {
                        KnowledgeBaseId = kbId,
                        DataSourceId = dataSourceId,
                    }, ct);

                var jobId = ingestionResponse.IngestionJob.IngestionJobId;
                _logger.LogInformation("Started KB ingestion job {JobId} for kbId={KbId}", jobId, kbId);
                return jobId;
            }

            _logger.LogWarning("No DataSourceId configured for kbId={KbId}; S3 upload complete but ingestion not triggered", kbId);
            return s3Key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add content to KB {KbId}", kbId);
            return string.Empty;
        }
    }

    public async Task<KbMetadata> GetKbMetadataAsync(string kbId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kbId))
            return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);

        try
        {
            var response = await _bedrockAgent.GetKnowledgeBaseAsync(
                new GetKnowledgeBaseRequest { KnowledgeBaseId = kbId }, ct);

            var kb = response.KnowledgeBase;
            var kbType = kbId == _corpKbId ? "corp" :
                         kbId == _personalKbId ? "personal" :
                         kbId == _teamKbId ? "team" : "unknown";

            return new KbMetadata(
                kbId,
                kbType,
                0, // Document count not available from this API
                kb.UpdatedAt ?? DateTime.UtcNow,
                kb.KnowledgeBaseId ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get KB metadata for kbId={KbId}", kbId);
            return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);
        }
    }
}
```

---

## Task 2: Create BraveSearchService

### File: `Services/BraveSearchService.cs` — CREATE NEW:

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FortressAI.V2.Web.Services;

public interface IBraveSearchService
{
    Task<List<BraveSearchResult>> SearchAsync(string query, int count = 5, CancellationToken ct = default);
    string FormatResults(List<BraveSearchResult> results);
    bool IsConfigured { get; }
}

public class BraveSearchResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class BraveWebResults
{
    [JsonPropertyName("results")]
    public List<BraveSearchResult> Results { get; set; } = new();
}

public class BraveSearchResponse
{
    [JsonPropertyName("web")]
    public BraveWebResults? Web { get; set; }
}

/// <summary>
/// Direct Brave Search API client.
/// Replaces fip-mcp's web-search tool group with a direct API key call.
/// Config key: BraveSearch:ApiKey
/// </summary>
public class BraveSearchService : IBraveSearchService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<BraveSearchService> _logger;

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public BraveSearchService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<BraveSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = config["BraveSearch:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<BraveSearchResult>> SearchAsync(string query, int count = 5, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Brave Search API key not configured — returning empty results");
            return new List<BraveSearchResult>();
        }

        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={Math.Min(count, 10)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Subscription-Token", _apiKey);

        try
        {
            var http = _httpClientFactory.CreateClient("BraveSearchClient");
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BraveSearchResponse>(cancellationToken: ct);
            return result?.Web?.Results ?? new List<BraveSearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brave Search failed for query '{Query}'", query);
            return new List<BraveSearchResult>();
        }
    }

    public string FormatResults(List<BraveSearchResult> results)
    {
        if (!results.Any()) return "No results found.";

        var sb = new System.Text.StringBuilder();
        int num = 1;
        foreach (var r in results.Take(5))
        {
            sb.AppendLine($"{num}. {r.Title}");
            sb.AppendLine($"   URL: {r.Url}");
            if (!string.IsNullOrEmpty(r.Description))
                sb.AppendLine($"   {r.Description}");
            sb.AppendLine();
            num++;
        }
        return sb.ToString().TrimEnd();
    }
}
```

---

## Task 3: Create DevOpsConnectionService

### File: `Data/Models/UserDevOpsConnection.cs` — CREATE NEW:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

/// <summary>
/// Stores a user's Azure DevOps organization URL and encrypted PAT.
/// Each user has at most one DevOps connection.
/// Table: user_devops_connections (fait_v2_dev)
/// </summary>
[Table("user_devops_connections")]
public class UserDevOpsConnection
{
    [Key]
    [Column("user_id")]
    [MaxLength(36)]
    public string UserId { get; set; } = string.Empty;

    [Column("org_url")]
    [MaxLength(500)]
    [Required]
    public string OrgUrl { get; set; } = string.Empty;

    /// <summary>DataProtection-encrypted PAT. Protector purpose: "DevOpsPat"</summary>
    [Column("pat_encrypted")]
    [Required]
    public string PatEncrypted { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User? User { get; set; }
}
```

### File: `Services/DevOpsConnectionService.cs` — CREATE NEW:

```csharp
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IDevOpsConnectionService
{
    Task<bool> IsConnectedAsync(string userId);
    Task<string?> GetOrgUrlAsync(string userId);
    Task<string?> GetDecryptedPatAsync(string userId);
    Task SaveAsync(string userId, string orgUrl, string pat);
    Task DisconnectAsync(string userId);
    Task<(bool Success, string Message)> TestConnectionAsync(string orgUrl, string pat);
}

/// <summary>
/// Manages per-user Azure DevOps PAT connections.
/// PATs are encrypted at rest using ASP.NET Core Data Protection (purpose: "DevOpsPat").
/// Replaces fip-mcp's ADO tool group with a direct PAT-based approach.
/// </summary>
public class DevOpsConnectionService : IDevOpsConnectionService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DevOpsConnectionService> _logger;

    public DevOpsConnectionService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<DevOpsConnectionService> logger)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector("DevOpsPat");
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> IsConnectedAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserDevOpsConnections.AnyAsync(c => c.UserId == userId);
    }

    public async Task<string?> GetOrgUrlAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.UserDevOpsConnections.FindAsync(userId);
        return row?.OrgUrl;
    }

    public async Task<string?> GetDecryptedPatAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.UserDevOpsConnections.FindAsync(userId);
        if (row is null) return null;
        try { return _protector.Unprotect(row.PatEncrypted); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt DevOps PAT for user {UserId}", userId);
            return null;
        }
    }

    public async Task SaveAsync(string userId, string orgUrl, string pat)
    {
        var encryptedPat = _protector.Protect(pat);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserDevOpsConnections.FindAsync(userId);
        if (existing is null)
        {
            db.UserDevOpsConnections.Add(new UserDevOpsConnection
            {
                UserId = userId,
                OrgUrl = orgUrl.TrimEnd('/'),
                PatEncrypted = encryptedPat,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.OrgUrl = orgUrl.TrimEnd('/');
            existing.PatEncrypted = encryptedPat;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        _logger.LogInformation("Saved DevOps connection for user {UserId} → {OrgUrl}", userId, orgUrl);
    }

    public async Task DisconnectAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.UserDevOpsConnections.FindAsync(userId);
        if (row is null) return;
        db.UserDevOpsConnections.Remove(row);
        await db.SaveChangesAsync();
        _logger.LogInformation("Removed DevOps connection for user {UserId}", userId);
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(string orgUrl, string pat)
    {
        var normalizedOrg = orgUrl.TrimEnd('/');
        var url = $"{normalizedOrg}/_apis/projects?api-version=7.1";
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var http = _httpClientFactory.CreateClient("DevOpsTestClient");
            using var response = await http.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                int count = 0;
                try
                {
                    var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
                    if (doc.RootElement.TryGetProperty("count", out var countEl))
                        count = countEl.GetInt32();
                }
                catch { /* non-fatal */ }
                return (true, $"Connected — {count} project{(count == 1 ? "" : "s")} found");
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return (false, "Invalid PAT or insufficient permissions");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, "Organization URL not found — check the URL");

            return (false, $"Unexpected response: {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "DevOps connectivity test failed for {Url}", url);
            return (false, "Could not reach Azure DevOps — check the organization URL");
        }
        catch (TaskCanceledException)
        {
            return (false, "Connection timed out");
        }
    }
}
```

---

## Task 4: Create MicrosoftTokenService

### File: `Services/MicrosoftTokenService.cs` — CREATE NEW:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data;

namespace FortressAI.V2.Web.Services;

public interface IMicrosoftTokenService
{
    bool IsConfigured { get; }
    string GetAuthorizationUrl(string redirectUri, string state);
    Task<string?> GetValidAccessTokenAsync(string entraOid);
    Task<(bool Connected, string? Email, DateTime? ExpiresAt)> GetConnectionStatusAsync(string entraOid);
    Task DisconnectAsync(string entraOid);
}

/// <summary>
/// Per-user Microsoft 365 OAuth token service.
/// Reads delegated Entra tokens from fip_dev.user_microsoft_tokens (written at FIP portal login).
/// Replaces fip-mcp's ms365 tool group with direct token access.
/// Config keys: Azure:ClientId, Azure:TenantId, Azure:ClientSecret
/// </summary>
public class MicrosoftTokenService : IMicrosoftTokenService
{
    private readonly IDbContextFactory<FipPortalDbContext> _fipPortalDbFactory;
    private readonly ILogger<MicrosoftTokenService> _logger;
    private readonly HttpClient _httpClient;

    public bool IsConfigured { get; }
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;

    private static readonly string[] Scopes = new[]
    {
        "https://graph.microsoft.com/Mail.Read",
        "https://graph.microsoft.com/Calendars.Read",
        "https://graph.microsoft.com/User.Read",
        "https://graph.microsoft.com/Tasks.Read",
        "offline_access"
    };

    public MicrosoftTokenService(
        IDbContextFactory<FipPortalDbContext> fipPortalDbFactory,
        ILogger<MicrosoftTokenService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _fipPortalDbFactory = fipPortalDbFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("MicrosoftGraphClient");

        _clientId = configuration["Azure:ClientId"] ?? "";
        _tenantId = (configuration["Azure:TenantId"] ?? "").Trim().TrimEnd('/');
        _clientSecret = configuration["Azure:ClientSecret"] ?? "";
        IsConfigured = !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_tenantId) && !string.IsNullOrEmpty(_clientSecret);

        if (!IsConfigured)
            _logger.LogWarning("Azure:ClientId/TenantId/ClientSecret not configured — Microsoft 365 features disabled");
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var scopeString = string.Join(" ", Scopes);
        return $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/authorize" +
               $"?client_id={Uri.EscapeDataString(_clientId)}" +
               $"&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={Uri.EscapeDataString(scopeString)}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&prompt=select_account";
    }

    public async Task<string?> GetValidAccessTokenAsync(string entraOid)
    {
        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token == null)
        {
            _logger.LogWarning("No Microsoft token found for entraOid={EntraOid}", entraOid);
            return null;
        }

        // If token is still valid (with 5 min buffer), return it
        if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return token.AccessToken;

        // Refresh the token
        if (!IsConfigured || string.IsNullOrEmpty(token.RefreshToken))
        {
            _logger.LogWarning("Cannot refresh token for {EntraOid} — missing config or refresh token", entraOid);
            return null;
        }

        try
        {
            _logger.LogInformation("Refreshing Microsoft token for entraOid={EntraOid}", entraOid);
            var tokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = string.Join(" ", Scopes)
            });

            var response = await _httpClient.PostAsync(tokenEndpoint, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token refresh failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);
            token.AccessToken = tokenResponse.GetProperty("access_token").GetString()!;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.GetProperty("expires_in").GetInt32());
            if (tokenResponse.TryGetProperty("refresh_token", out var newRefresh))
                token.RefreshToken = newRefresh.GetString()!;
            token.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _logger.LogInformation("Token refreshed for entraOid={EntraOid}, new expiry: {Expiry}", entraOid, token.ExpiresAt);
            return token.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for entraOid={EntraOid}", entraOid);
            return null;
        }
    }

    public async Task<(bool Connected, string? Email, DateTime? ExpiresAt)> GetConnectionStatusAsync(string entraOid)
    {
        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token == null)
            return (false, null, null);
        return (true, token.MicrosoftEmail, token.ExpiresAt);
    }

    public async Task DisconnectAsync(string entraOid)
    {
        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token != null)
        {
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            _logger.LogInformation("Microsoft 365 disconnected for entraOid={EntraOid}", entraOid);
        }
    }
}
```

---

## Task 5: Add UserDevOpsConnections to FaitV2DbContext

### File: `Data/FaitV2DbContext.cs`

Add this DbSet after the existing ones:
```csharp
public DbSet<UserDevOpsConnection> UserDevOpsConnections => Set<UserDevOpsConnection>();
```

Add this entity configuration inside `OnModelCreating`, after the existing configurations:
```csharp
// user_devops_connections
modelBuilder.Entity<UserDevOpsConnection>(entity =>
{
    entity.ToTable("user_devops_connections");
    entity.HasKey(e => e.UserId);
    entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36);
    entity.Property(e => e.OrgUrl).HasColumnName("org_url").HasMaxLength(500).IsRequired();
    entity.Property(e => e.PatEncrypted).HasColumnName("pat_encrypted").HasColumnType("TEXT").IsRequired();
    entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
    entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

    entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_devops_connections_user_id");

    entity.HasOne(e => e.User)
          .WithMany()
          .HasForeignKey(e => e.UserId)
          .HasConstraintName("fk_user_devops_connections_user")
          .OnDelete(DeleteBehavior.Cascade);
});
```

---

## Task 6: Update Program.cs

### In `Program.cs`:

#### A. Add Bedrock AWS service registrations (after `builder.Services.AddAWSService<IAmazonS3>()` line):
```csharp
// AWS Bedrock (KB direct access)
builder.Services.AddAWSService<Amazon.BedrockAgentRuntime.IAmazonBedrockAgentRuntime>();
builder.Services.AddAWSService<Amazon.BedrockAgent.IAmazonBedrockAgent>();
```

#### B. Add HTTP clients (after the existing `builder.Services.AddHttpClient("HarnessClient")` line):
```csharp
builder.Services.AddHttpClient("BraveSearchClient");
builder.Services.AddHttpClient("DevOpsTestClient");
builder.Services.AddHttpClient("MicrosoftGraphClient");
```

#### C. REPLACE the fip-mcp block:
FIND AND REMOVE these lines:
```csharp
// FORGE KB / fip-mcp integration
builder.Services.AddHttpClient("FipMcpClient");
builder.Services.AddScoped<IFipTokenProvider, FipTokenProvider>();
builder.Services.AddScoped<IForgeKbService, ForgeKbService>();
```

REPLACE WITH:
```csharp
// FORGE KB — direct Bedrock integration (replaces fip-mcp)
builder.Services.AddScoped<IForgeKbService, ForgeKbService>();

// Brave Search — direct API (replaces fip-mcp web-search)
builder.Services.AddScoped<IBraveSearchService, BraveSearchService>();

// ADO — direct PAT-based connection (replaces fip-mcp ado)
builder.Services.AddScoped<IDevOpsConnectionService, DevOpsConnectionService>();

// MS365 — direct Graph API via delegated tokens (replaces fip-mcp ms365)
builder.Services.AddScoped<IMicrosoftTokenService, MicrosoftTokenService>();
```

#### D. REMOVE the FipPortalDbContext registration ONLY IF it's no longer needed.
Check: `FipPortalDbContext` is still used by `MicrosoftTokenService` (reads `user_microsoft_tokens`).
So KEEP the FipPortalDbContext registration — do NOT remove it.

---

## Task 7: Create EF Migration for user_devops_connections

After updating FaitV2DbContext, create a new migration:
```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web
dotnet ef migrations add AddUserDevOpsConnections
```

---

## Task 8: Run dotnet build and verify 0 errors

```bash
cd /home/fredw/projects/fip/fait-v2
dotnet build
```

Fix any compiler errors before completing.

---

## Key Notes

### IAM Gap to note for Rhodey:
The `fait-v2-task-role` needs these Bedrock IAM policies added:
- `bedrock:Retrieve` on the KB ARNs
- `bedrock:GetKnowledgeBase` on the KB ARNs
- `bedrock:StartIngestionJob` on the KB ARNs (for writes)
If these aren't added, ForgeKbService will throw AccessDenied at runtime, but the app will not crash (all errors are caught and logged).

### What changes:
1. `ForgeKbService` — no longer calls fip-mcp HTTP; uses Bedrock directly
2. `BraveSearchService` — new service, direct Brave API
3. `DevOpsConnectionService` — new service, direct ADO PAT auth
4. `MicrosoftTokenService` — new service, reads delegated tokens from FipPortalDbContext
5. `Program.cs` — removes FipMcpClient, FipTokenProvider; adds direct service registrations + Bedrock AWS services
6. `FipTokenProvider` and `IFipTokenProvider` — NO LONGER NEEDED (can be left in place for now if removing causes issues; they're harmless dead code)
7. `FipPortalDbContext` — KEEP (still needed by MicrosoftTokenService)

### CSS Variables rule (always applies):
No hardcoded colors, fonts, or spacing in any .razor files. All values via CSS variables.

---

## What NOT to do:
- Do NOT remove FipPortalDbContext — MicrosoftTokenService still reads from it
- Do NOT remove McpServer/McpUserToken tables — they're fine to stay (ConnectorService uses them)
- Do NOT modify ConnectorService — it's already clean (uses DB flags, no fip-mcp HTTP)
- Do NOT modify ContextEnvelopeService — it's already clean (catches exceptions from ListKbsAsync)
