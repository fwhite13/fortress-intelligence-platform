using FortressNexus.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Controllers;

[ApiController]
[Authorize]
[Route("nexus/{id:int}/artifacts")]
public class NexusArtifactsController : ControllerBase
{
    private readonly NexusDbContext _db;

    public NexusArtifactsController(NexusDbContext db)
    {
        _db = db;
    }

    [HttpGet("external-dependencies")]
    public async Task<IActionResult> GetExternalDependencies(int id)
    {
        var submission = await _db.Submissions
            .FirstOrDefaultAsync(s => s.Id == id);

        if (submission is null)
            return NotFound($"Submission {id} not found.");

        if (!submission.ActiveSpecDocumentId.HasValue)
            return NotFound("No active spec document.");

        var artifactSet = await _db.ArtifactSets
            .Where(a => a.SpecDocumentId == submission.ActiveSpecDocumentId.Value)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        if (artifactSet is null)
            return NotFound("No artifact set found.");

        var externalWis = await _db.WorkItemRecords
            .Where(w => w.ArtifactSetId == artifactSet.Id && w.IsExternalDependency)
            .ToListAsync();

        return Ok(externalWis);
    }
}
