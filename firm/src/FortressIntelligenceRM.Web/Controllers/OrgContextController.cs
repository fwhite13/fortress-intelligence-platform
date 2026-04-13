using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
[Route("api/org-context")]
[Authorize]
public class OrgContextController : ControllerBase
{
    private readonly IOrgContextService _orgContextService;
    private readonly IConfiguration _config;
    private readonly ILogger<OrgContextController> _logger;

    public OrgContextController(IOrgContextService orgContextService, IConfiguration config, ILogger<OrgContextController> logger)
    {
        _orgContextService = orgContextService;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId)) return BadRequest(new { error = "Tenant ID not available" });

        var ctx = await _orgContextService.GetContextAsync(tenantId);
        return Ok(new
        {
            wikiContent = ctx?.WikiContent ?? "",
            updatedAt = ctx?.UpdatedAt,
            updatedBy = ctx?.UpdatedBy
        });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] OrgContextRequest request)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = GetTenantId();
        if (string.IsNullOrEmpty(tenantId)) return BadRequest(new { error = "Tenant ID not available" });

        var updatedBy = User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("oid")?.Value
            ?? "unknown";

        await _orgContextService.UpsertContextAsync(tenantId, request.WikiContent ?? "", updatedBy);
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

    private bool IsAdmin()
    {
        var userOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var adminOid = _config["Firm:AdminEntraOid"];
        if (!string.IsNullOrEmpty(adminOid) && !string.IsNullOrEmpty(userOid))
            return string.Equals(adminOid, userOid, StringComparison.OrdinalIgnoreCase);
        // Fallback: check roles claim
        return User.IsInRole("admin") || User.IsInRole("Admin") || User.HasClaim("roles", "admin");
    }
}

public class OrgContextRequest
{
    public string? WikiContent { get; set; }
}
