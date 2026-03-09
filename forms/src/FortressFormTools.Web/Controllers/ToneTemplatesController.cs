using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;

namespace FortressFormTools.Web.Controllers;

[ApiController]
[Route("api/tone-templates")]
public class ToneTemplatesController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public ToneTemplatesController(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>GET /api/tone-templates — list all</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var items = await _db.ToneTemplates
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Description, t.IsSystem })
            .ToListAsync();

        return Ok(items);
    }
}
