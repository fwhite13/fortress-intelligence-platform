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
