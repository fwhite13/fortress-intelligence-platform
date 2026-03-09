using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;
using FortressFormTools.Web.Services;

namespace FortressFormTools.Web.Controllers;

[ApiController]
[Route("api/question-sets")]
public class QuestionSetsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly CrossReferenceService _crossRef;

    public QuestionSetsController(IDbContextFactory<AppDbContext> contextFactory, CrossReferenceService crossRef)
    {
        _contextFactory = contextFactory;
        _crossRef = crossRef;
    }

    /// <summary>GET /api/question-sets — list all with counts</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var items = await _db.QuestionSets
            .AsNoTracking()
            .Select(qs => new
            {
                qs.Id,
                qs.Name,
                qs.Description,
                qs.Vertical,
                qs.Status,
                qs.CreatedAt,
                qs.UpdatedAt,
                qs.CreatedBy,
                FormCount = qs.QuestionSetForms.Count,
                QuestionCount = qs.Fields.Count
            })
            .OrderByDescending(qs => qs.CreatedAt)
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>GET /api/question-sets/{id} — detail with related forms + questions</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var qs = await _db.QuestionSets
            .AsNoTracking()
            .Include(q => q.QuestionSetForms)
                .ThenInclude(qsf => qsf.FormLibrary)
            .Include(q => q.Fields)
                .ThenInclude(f => f.DictionaryField)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (qs == null) return NotFound();

        return Ok(new
        {
            qs.Id,
            qs.Name,
            qs.Description,
            qs.Vertical,
            qs.Status,
            qs.CreatedAt,
            qs.UpdatedAt,
            qs.CreatedBy,
            Forms = qs.QuestionSetForms.Select(qsf => new
            {
                qsf.FormLibraryId,
                FormName = qsf.FormLibrary?.FormName,
                CarrierName = qsf.FormLibrary?.CarrierName
            }),
            Questions = qs.Fields.OrderBy(f => f.SortOrder).Select(f => new
            {
                f.Id,
                f.QuestionText,
                f.FieldType,
                f.SectionName,
                f.IsRequired,
                f.SortOrder,
                f.SourceFormCount,
                DictionaryFieldCode = f.DictionaryField?.FieldCode
            })
        });
    }

    /// <summary>POST /api/question-sets — create new</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQuestionSetRequest body)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var qs = new QuestionSet
        {
            Name = body.Name,
            Description = body.Description,
            Vertical = body.Vertical,
            Status = body.Status ?? "Draft",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = body.CreatedBy
        };

        _db.QuestionSets.Add(qs);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = qs.Id }, new
        {
            qs.Id,
            qs.Name,
            qs.Description,
            qs.Vertical,
            qs.Status,
            qs.CreatedAt,
            FormCount = 0,
            QuestionCount = 0
        });
    }

    /// <summary>DELETE /api/question-sets/{id}</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var qs = await _db.QuestionSets.FindAsync(id);
        if (qs == null) return NotFound();

        _db.QuestionSets.Remove(qs);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>POST /api/question-sets/{id}/analyze — cross-reference forms</summary>
    [HttpPost("{id:int}/analyze")]
    public async Task<IActionResult> Analyze(int id, [FromBody] AnalyzeRequest request)
    {
        try
        {
            var result = await _crossRef.AnalyzeFormsAsync(id, request.FormIds);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Analysis failed", detail = ex.Message });
        }
    }

    /// <summary>POST /api/question-sets/{id}/fields/bulk — save selected fields</summary>
    [HttpPost("{id:int}/fields/bulk")]
    public async Task<IActionResult> BulkAddFields(int id, [FromBody] BulkFieldsRequest request)
    {
        try
        {
            var saved = await _crossRef.SaveBulkFieldsAsync(id, request.Fields);
            return Ok(new { count = saved.Count, message = $"Added {saved.Count} fields to question set" });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Save failed", detail = ex.Message });
        }
    }
}

public class CreateQuestionSetRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Vertical { get; set; }
    public string? Status { get; set; }
    public string? CreatedBy { get; set; }
    public int? ToneTemplateId { get; set; }
}
