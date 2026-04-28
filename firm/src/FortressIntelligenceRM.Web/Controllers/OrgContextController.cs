using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
[Route("api/org-context")]
[Authorize]
public class OrgContextController : ControllerBase
{
    private readonly IOrgContextService _orgContextService;
    private readonly IConfiguration _config;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<OrgContextController> _logger;

    public OrgContextController(IOrgContextService orgContextService, IConfiguration config, IDbContextFactory<FirmDbContext> dbFactory, ILogger<OrgContextController> logger)
    {
        _orgContextService = orgContextService;
        _config = config;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId)) return BadRequest(new { error = "Tenant ID not available" });

        var entries = await _orgContextService.GetContextAsync(tenantId);
        var updatedAt = await _orgContextService.GetUpdatedAtAsync(tenantId);
        var updatedBy = await _orgContextService.GetUpdatedByAsync(tenantId);

        return Ok(new
        {
            entries,
            wikiContent = System.Text.Json.JsonSerializer.Serialize(entries,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            updatedAt,
            updatedBy
        });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] OrgContextRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();

        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId)) return BadRequest(new { error = "Tenant ID not available" });

        var updatedBy = User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("oid")?.Value
            ?? "unknown";

        // Try to parse as JSON entries first; if not, wrap plain text as a single entry
        List<OrgContextEntry> entries;
        var content = request.WikiContent ?? "";
        if (content.TrimStart().StartsWith('['))
        {
            try
            {
                entries = System.Text.Json.JsonSerializer.Deserialize<List<OrgContextEntry>>(
                    content,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                    ?? [];
            }
            catch
            {
                entries = [new OrgContextEntry(Term: "Content", Description: content)];
            }
        }
        else if (!string.IsNullOrWhiteSpace(content))
        {
            entries = [new OrgContextEntry(Term: "Content", Description: content)];
        }
        else
        {
            entries = [];
        }

        await _orgContextService.UpsertContextAsync(tenantId, entries, updatedBy);
        _logger.LogInformation("[OrgContext] Upserted wiki for tenant {TenantId} by {User}", tenantId, updatedBy);
        return Ok(new { success = true });
    }

    private string? GetTenantId()
    {
        // Try claim first (works for delegated auth), fall back to config (single-tenant deployments)
        return User.FindFirst("tid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? _config["Firm:GraphTenantId"];
    }

    private async Task<bool> IsAdminAsync()
    {
        var userOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        // Primary: check DB is_admin flag
        if (!string.IsNullOrEmpty(userOid))
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var firmUser = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == userOid);
            if (firmUser?.IsAdmin == true)
                return true;
        }

        // Fallback: Firm:AdminEntraOid config (bootstrap)
        var adminOid = _config["Firm:AdminEntraOid"];
        if (!string.IsNullOrEmpty(adminOid) && !string.IsNullOrEmpty(userOid))
        {
            var adminOids = adminOid.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (adminOids.Any(oid => string.Equals(oid, userOid, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return User.IsInRole("admin") || User.IsInRole("Admin") || User.HasClaim("roles", "admin");
    }
}

public class OrgContextRequest
{
    public string? WikiContent { get; set; }
}
