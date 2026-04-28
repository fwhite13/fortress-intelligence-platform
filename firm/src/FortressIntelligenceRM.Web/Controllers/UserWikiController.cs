using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
[Route("api/user-wiki")]
[Authorize]
public class UserWikiController : ControllerBase
{
    private readonly IUserWikiService _userWikiService;
    private readonly IConfiguration _config;
    private readonly ILogger<UserWikiController> _logger;

    public UserWikiController(IUserWikiService userWikiService, IConfiguration config, ILogger<UserWikiController> logger)
    {
        _userWikiService = userWikiService;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var (entraOid, tenantId) = GetUserIdentity();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });
        if (string.IsNullOrEmpty(tenantId)) return BadRequest(new { error = "Tenant ID not available" });

        var entries = await _userWikiService.GetEntriesAsync(entraOid, tenantId);
        var updatedAt = await _userWikiService.GetUpdatedAtAsync(entraOid, tenantId);

        return Ok(new { entries, updatedAt });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UserWikiRequest request)
    {
        var (entraOid, tenantId) = GetUserIdentity();
        if (string.IsNullOrEmpty(entraOid)) return BadRequest(new { error = "User identity not available" });
        if (string.IsNullOrEmpty(tenantId)) return BadRequest(new { error = "Tenant ID not available" });

        var updatedBy = User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst(ClaimTypes.Email)?.Value
            ?? entraOid;

        await _userWikiService.UpsertEntriesAsync(entraOid, tenantId, request.Entries ?? [], updatedBy);
        _logger.LogInformation("[UserWiki] Upserted wiki for user {EntraOid} by {User}", entraOid, updatedBy);
        return Ok(new { success = true });
    }

    private (string? entraOid, string? tenantId) GetUserIdentity()
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var tenantId = User.FindFirst("tid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? _config["Firm:GraphTenantId"];
        return (entraOid, tenantId);
    }
}

public class UserWikiRequest
{
    public List<OrgContextEntry>? Entries { get; set; }
}
