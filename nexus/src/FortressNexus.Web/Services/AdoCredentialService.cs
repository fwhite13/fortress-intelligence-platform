using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Services;

public class AdoCredentialService : IAdoCredentialService
{
    private readonly NexusDbContext _db;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AdoCredentialService> _logger;

    public AdoCredentialService(
        NexusDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<AdoCredentialService> logger)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("NexusAdoPat");
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> HasCredentialAsync(string userUpn)
    {
        return await FindCredentialAsync(userUpn) is not null;
    }

    public async Task SaveCredentialAsync(string userUpn, string rawPat)
    {
        var encrypted = _protector.Protect(rawPat);
        var hint = rawPat.Length >= 4 ? rawPat[^4..] : rawPat;
        var now = DateTime.UtcNow;

        var existing = await _db.UserAdoCredentials
            .FirstOrDefaultAsync(c => c.UserUpn == userUpn);

        if (existing is not null)
        {
            existing.EncryptedPat = encrypted;
            existing.PatHint = hint;
            existing.UpdatedAt = now;
        }
        else
        {
            _db.UserAdoCredentials.Add(new UserAdoCredential
            {
                UserUpn = userUpn,
                EncryptedPat = encrypted,
                PatHint = hint,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("[AdoCredentialService] Saved PAT for {UserUpn}", userUpn);
    }

    public async Task<string?> GetDecryptedPatAsync(string userUpn)
    {
        var cred = await FindCredentialAsync(userUpn);
        if (cred is null) return null;

        try
        {
            return _protector.Unprotect(cred.EncryptedPat);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdoCredentialService] Failed to decrypt PAT for {UserUpn}", userUpn);
            return null;
        }
    }

    public async Task<string?> GetPatHintAsync(string userUpn)
    {
        var cred = await FindCredentialAsync(userUpn);
        return cred?.PatHint;
    }

    /// <summary>
    /// Finds a credential by UPN, falling back to username-only match across domains.
    /// Handles cases where the same user has credentials stored under an alternate UPN
    /// (e.g. fwhite@fortressinsurance.com vs fwhite@fortressaffinitygroup.com).
    /// </summary>
    private async Task<UserAdoCredential?> FindCredentialAsync(string userUpn)
    {
        // Exact match first
        var cred = await _db.UserAdoCredentials
            .FirstOrDefaultAsync(c => c.UserUpn == userUpn);
        if (cred is not null) return cred;

        // Fallback: match on username portion only (before @)
        var atIndex = userUpn.IndexOf('@');
        if (atIndex <= 0) return null;
        var username = userUpn[..atIndex].ToLowerInvariant();

        return await _db.UserAdoCredentials
            .FirstOrDefaultAsync(c => c.UserUpn.ToLower().StartsWith(username + "@"));
    }

    public async Task DeleteCredentialAsync(string userUpn)
    {
        var cred = await _db.UserAdoCredentials
            .FirstOrDefaultAsync(c => c.UserUpn == userUpn);
        if (cred is not null)
        {
            _db.UserAdoCredentials.Remove(cred);
            await _db.SaveChangesAsync();
            _logger.LogInformation("[AdoCredentialService] Deleted PAT for {UserUpn}", userUpn);
        }
    }

    public async Task<List<string>> GetProjectsAsync(string userUpn)
    {
        var pat = await GetDecryptedPatAsync(userUpn);
        if (pat is null)
            throw new InvalidOperationException($"No ADO credential found for {userUpn}");

        var org = _config["Nexus:Ado:Organization"] ?? "FortressAffinityGroup";
        var url = $"https://dev.azure.com/{org}/_apis/projects?api-version=7.1";

        using var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = BuildBasicAuth(pat);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var projects = new List<string>();
        if (doc.RootElement.TryGetProperty("value", out var valueArr))
        {
            foreach (var item in valueArr.EnumerateArray())
            {
                if (item.TryGetProperty("name", out var name))
                    projects.Add(name.GetString() ?? "");
            }
        }
        return projects;
    }

    public async Task<bool> ValidatePatAsync(string rawPat)
    {
        var org = _config["Nexus:Ado:Organization"] ?? "FortressAffinityGroup";
        var url = $"https://dev.azure.com/{org}/_apis/projects?api-version=7.1";

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = BuildBasicAuth(rawPat);
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdoCredentialService] PAT validation request failed");
            return false;
        }
    }

    private static AuthenticationHeaderValue BuildBasicAuth(string pat)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
