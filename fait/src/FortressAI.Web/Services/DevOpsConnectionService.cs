using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

/// <summary>
/// Manages per-user Azure DevOps PAT connections via the dedicated
/// user_devops_connections table.  PATs are encrypted at rest using
/// ASP.NET Core Data Protection (purpose: "DevOpsPat").
/// </summary>
public class DevOpsConnectionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DevOpsConnectionService> _logger;

    public DevOpsConnectionService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<DevOpsConnectionService> logger)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector("DevOpsPat");
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Read
    // -------------------------------------------------------------------------

    public async Task<bool> IsConnectedAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserDevOpsConnections.AnyAsync(c => c.UserId == userId);
    }

    /// <summary>
    /// Returns the stored org URL for display, or null if not connected.
    /// Does NOT decrypt the PAT.
    /// </summary>
    public async Task<string?> GetOrgUrlAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.UserDevOpsConnections.FindAsync(userId);
        return row?.OrgUrl;
    }

    /// <summary>
    /// Returns the decrypted PAT, or null if not connected / decryption fails.
    /// </summary>
    public async Task<string?> GetDecryptedPatAsync(Guid userId)
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

    // -------------------------------------------------------------------------
    // Write
    // -------------------------------------------------------------------------

    public async Task SaveAsync(Guid userId, string orgUrl, string pat)
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

    public async Task DisconnectAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var row = await db.UserDevOpsConnections.FindAsync(userId);
        if (row is null) return;
        db.UserDevOpsConnections.Remove(row);
        await db.SaveChangesAsync();
        _logger.LogInformation("Removed DevOps connection for user {UserId}", userId);
    }

    // -------------------------------------------------------------------------
    // Connectivity test
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates the supplied org URL and PAT by calling
    /// GET {orgUrl}/_apis/projects?api-version=7.1
    ///
    /// Azure DevOps PAT auth header convention:
    ///   Authorization: Basic {base64(":{PAT}")}
    ///   (empty username, PAT as password)
    ///
    /// Returns (success, message) where message is a human-readable result.
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(string orgUrl, string pat)
    {
        var normalizedOrg = orgUrl.TrimEnd('/');
        var url = $"{normalizedOrg}/_apis/projects?api-version=7.1";

        // Build Basic auth header: base64(":{PAT}")
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var http = _httpClientFactory.CreateClient("devops-test");
            using var response = await http.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                // Parse project count from response
                int count = 0;
                try
                {
                    var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
                    if (doc.RootElement.TryGetProperty("count", out var countEl))
                        count = countEl.GetInt32();
                }
                catch { /* count stays 0 — non-fatal */ }

                return (true, $"Connected — {count} project{(count == 1 ? "" : "s")} found");
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
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
            return (false, "Connection timed out — check the organization URL");
        }
    }
}
