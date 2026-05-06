using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Controllers;

public record PatchTitleRequest(string Title);
public record PatchDescriptionRequest(string Description);
public record PatchAcRequest(string AcceptanceCriteria);
public record PatchParentRequest(string ParentTitle);
public record CreateWiRequest(
    int ArtifactSetId,
    string WorkItemType,
    string Title,
    string? ParentTitle
);

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

        // Ownership check: caller must own the submission OR be NexusAdmin
        var currentUpn = User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
            && !User.IsInRole(NexusRoles.Admin))
        {
            return Forbid();
        }

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

    // PATCH wi/{wiId}/title — update title + cascade ParentTitle references in same ArtifactSet
    [HttpPatch("wi/{wiId:int}/title")]
    public async Task<IActionResult> PatchTitle(int id, int wiId, [FromBody] PatchTitleRequest req)
    {
        if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
            return Forbid();

        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("Title cannot be empty.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
            if (wi is null) return NotFound();

            // Ownership check
            if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
                return Forbid();

            var oldTitle = wi.Title;
            wi.Title = req.Title;

            // Cascade: update all ParentTitle references in the same ArtifactSet
            if (oldTitle != req.Title)
            {
                var dependents = await _db.WorkItemRecords
                    .Where(w => w.ArtifactSetId == wi.ArtifactSetId && w.ParentTitle == oldTitle)
                    .ToListAsync();
                foreach (var dep in dependents)
                    dep.ParentTitle = req.Title;
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok(wi);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // PATCH wi/{wiId}/description
    [HttpPatch("wi/{wiId:int}/description")]
    public async Task<IActionResult> PatchDescription(int id, int wiId, [FromBody] PatchDescriptionRequest req)
    {
        if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
            return Forbid();

        var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
        if (wi is null) return NotFound();

        if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
            return Forbid();

        wi.Description = req.Description;
        await _db.SaveChangesAsync();
        return Ok(wi);
    }

    // PATCH wi/{wiId}/ac
    [HttpPatch("wi/{wiId:int}/ac")]
    public async Task<IActionResult> PatchAc(int id, int wiId, [FromBody] PatchAcRequest req)
    {
        if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
            return Forbid();

        var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
        if (wi is null) return NotFound();

        if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
            return Forbid();

        wi.AcceptanceCriteria = req.AcceptanceCriteria;
        await _db.SaveChangesAsync();
        return Ok(wi);
    }

    // PATCH wi/{wiId}/parent — reparent WI
    [HttpPatch("wi/{wiId:int}/parent")]
    public async Task<IActionResult> PatchParent(int id, int wiId, [FromBody] PatchParentRequest req)
    {
        if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
            return Forbid();

        var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
        if (wi is null) return NotFound();

        if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
            return Forbid();

        // Validate target parent exists in same ArtifactSet
        var targetParent = await _db.WorkItemRecords
            .FirstOrDefaultAsync(w => w.ArtifactSetId == wi.ArtifactSetId && w.Title == req.ParentTitle);
        if (targetParent is null)
            return BadRequest($"Parent WI with title '{req.ParentTitle}' not found in artifact set.");

        wi.ParentTitle = req.ParentTitle;
        await _db.SaveChangesAsync();
        return Ok(wi);
    }

    // POST wi — create new WI
    [HttpPost("wi")]
    public async Task<IActionResult> CreateWi(int id, [FromBody] CreateWiRequest req)
    {
        if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
            return Forbid();

        if (!await VerifySubmissionAccessByArtifactSetAsync(id, req.ArtifactSetId))
            return Forbid();

        var wi = new WorkItemRecord
        {
            ArtifactSetId = req.ArtifactSetId,
            WorkItemType = req.WorkItemType,
            Title = req.Title,
            ParentTitle = req.ParentTitle,
            Status = "Created",
            AdoWorkItemId = 0,
            AdoWorkItemUrl = "",
            WiTemplate = WiTemplateType.Standard
        };

        _db.WorkItemRecords.Add(wi);
        await _db.SaveChangesAsync();
        return Ok(wi);
    }

    // DELETE wi/{wiId} — cascade delete all descendants
    [HttpDelete("wi/{wiId:int}")]
    public async Task<IActionResult> DeleteWi(int id, int wiId)
    {
        if (!User.IsInRole(NexusRoles.Admin) && !User.IsInRole(NexusRoles.Reviewer))
            return Forbid();

        var wi = await _db.WorkItemRecords.FirstOrDefaultAsync(w => w.Id == wiId);
        if (wi is null) return NotFound();

        if (!await VerifySubmissionAccessAsync(id, wi.ArtifactSetId))
            return Forbid();

        // Load all WIs in the same ArtifactSet to walk the tree
        var allWis = await _db.WorkItemRecords
            .Where(w => w.ArtifactSetId == wi.ArtifactSetId)
            .ToListAsync();

        // Collect WI + all descendants recursively by ParentTitle
        var toDelete = new List<WorkItemRecord>();
        CollectDescendants(wi, allWis, toDelete);
        toDelete.Add(wi);

        _db.WorkItemRecords.RemoveRange(toDelete);
        await _db.SaveChangesAsync();

        return Ok(new { deleted = toDelete.Count });
    }

    // Helper: collect all descendants recursively by ParentTitle
    private static void CollectDescendants(
        WorkItemRecord parent,
        List<WorkItemRecord> all,
        List<WorkItemRecord> result)
    {
        var children = all.Where(w => w.ParentTitle == parent.Title && w.Id != parent.Id).ToList();
        foreach (var child in children)
        {
            result.Add(child);
            CollectDescendants(child, all, result);
        }
    }

    // Helper: verify submission ownership and that ArtifactSet belongs to submission
    private async Task<bool> VerifySubmissionAccessAsync(int submissionId, int artifactSetId)
    {
        var submission = await _db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId);
        if (submission is null) return false;

        var currentUpn = User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
            && !User.IsInRole(NexusRoles.Admin)
            && !User.IsInRole(NexusRoles.Reviewer))
            return false;

        var artifactSet = await _db.ArtifactSets.FirstOrDefaultAsync(a => a.Id == artifactSetId);
        if (artifactSet is null) return false;

        var specDoc = await _db.SpecDocuments.FirstOrDefaultAsync(sd => sd.Id == artifactSet.SpecDocumentId);
        if (specDoc is null) return false;

        return specDoc.SubmissionId == submissionId;
    }

    private async Task<bool> VerifySubmissionAccessByArtifactSetAsync(int submissionId, int artifactSetId)
    {
        return await VerifySubmissionAccessAsync(submissionId, artifactSetId);
    }
}
