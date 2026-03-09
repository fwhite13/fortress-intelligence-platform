using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;

namespace FortressFormTools.Web.Controllers;

[ApiController]
[Route("api/dictionary")]
public class DictionaryController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public DictionaryController(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>GET /api/dictionary — list all, filterable by ?q= and ?category=</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? q = null,
        [FromQuery] string? category = null)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var query = _db.DictionaryFields.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(d => d.FieldCode.Contains(q) || d.DisplayName.Contains(q));

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(d => d.Category == category);

        var items = await query.OrderBy(d => d.DisplayName).ToListAsync();
        return Ok(items);
    }

    /// <summary>GET /api/dictionary/{id} — single by id</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var item = await _db.DictionaryFields.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    /// <summary>POST /api/dictionary — create a new DictionaryField</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DictionaryFieldRequest body)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var field = new DictionaryField
        {
            FieldCode = body.FieldCode,
            DisplayName = body.DisplayName,
            Category = body.Category,
            FieldType = body.FieldType,
            Description = body.Description,
            Synonyms = body.Synonyms,
            IsSensitive = body.IsSensitive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.DictionaryFields.Add(field);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = field.Id }, field);
    }

    /// <summary>PUT /api/dictionary/{id} — update existing DictionaryField</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] DictionaryFieldRequest body)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var field = await _db.DictionaryFields.FindAsync(id);
        if (field == null) return NotFound();

        field.FieldCode = body.FieldCode;
        field.DisplayName = body.DisplayName;
        field.Category = body.Category;
        field.FieldType = body.FieldType;
        field.Description = body.Description;
        field.Synonyms = body.Synonyms;
        field.IsSensitive = body.IsSensitive;
        field.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(field);
    }

    /// <summary>DELETE /api/dictionary/{id} — delete a DictionaryField</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var field = await _db.DictionaryFields.FindAsync(id);
        if (field == null) return NotFound();

        _db.DictionaryFields.Remove(field);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public class DictionaryFieldRequest
{
    public string FieldCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? FieldType { get; set; }
    public string? Description { get; set; }
    public string? Synonyms { get; set; }
    public bool IsSensitive { get; set; }
}
