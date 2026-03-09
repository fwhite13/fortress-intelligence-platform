using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;
using FortressFormTools.Web.Services;

namespace FortressFormTools.Web.Controllers;

[ApiController]
[Route("api/generator")]
public class GeneratorController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly GeneratorService _generator;

    public GeneratorController(IDbContextFactory<AppDbContext> contextFactory, GeneratorService generator)
    {
        _contextFactory = contextFactory;
        _generator = generator;
    }

    /// <summary>POST /api/generator/{questionSetId} — generate SurveyJS JSON</summary>
    [HttpPost("{questionSetId:int}")]
    public async Task<IActionResult> Generate(int questionSetId, [FromBody] GenerateRequest request)
    {
        try
        {
            var schema = await _generator.GenerateSurveyJsonAsync(
                questionSetId, request.ToneTemplateId, request.Settings);
            return Ok(new
            {
                schema.Id,
                schema.QuestionSetId,
                schema.ToneTemplateId,
                schema.SchemaJson,
                schema.Version,
                schema.Status,
                schema.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Generation failed", detail = ex.Message });
        }
    }

    /// <summary>GET /api/generator/{questionSetId}/schemas — list schemas</summary>
    [HttpGet("{questionSetId:int}/schemas")]
    public async Task<IActionResult> ListSchemas(int questionSetId)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var schemas = await _db.GeneratedSchemas
            .AsNoTracking()
            .Where(s => s.QuestionSetId == questionSetId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                s.QuestionSetId,
                s.ToneTemplateId,
                s.Version,
                s.Status,
                s.CreatedAt,
                JsonLength = s.SchemaJson.Length
            })
            .ToListAsync();

        return Ok(schemas);
    }

    /// <summary>GET /api/generator/schemas/{schemaId} — get specific schema</summary>
    [HttpGet("schemas/{schemaId:int}")]
    public async Task<IActionResult> GetSchema(int schemaId)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var schema = await _db.GeneratedSchemas
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schemaId);

        if (schema == null) return NotFound();

        return Ok(new
        {
            schema.Id,
            schema.QuestionSetId,
            schema.ToneTemplateId,
            schema.SchemaJson,
            schema.SettingsJson,
            schema.Version,
            schema.Status,
            schema.CreatedAt
        });
    }

    /// <summary>GET /api/generator/tone-templates — list available tones</summary>
    [HttpGet("tone-templates")]
    public async Task<IActionResult> GetToneTemplates()
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var templates = await _db.ToneTemplates
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.Name, t.Description, t.IsSystem })
            .ToListAsync();

        return Ok(templates);
    }
}
