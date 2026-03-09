using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;
using FortressFormTools.Web.Models;
using FortressFormTools.Web.Services;
using Amazon.S3;
using Amazon.S3.Model;

namespace FortressFormTools.Web.Controllers;

[ApiController]
[Route("api/forms")]
public class FormsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ExtractionBackgroundService _extractionQueue;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FormsController> _logger;
    private readonly IConfiguration _config;
    private readonly IAmazonS3? _s3;

    public FormsController(
        IDbContextFactory<AppDbContext> contextFactory,
        ExtractionBackgroundService extractionQueue,
        IWebHostEnvironment env,
        ILogger<FormsController> logger,
        IConfiguration config,
        IAmazonS3? s3 = null)
    {
        _contextFactory = contextFactory;
        _extractionQueue = extractionQueue;
        _env = env;
        _logger = logger;
        _config = config;
        _s3 = s3;
    }

    /// <summary>GET /api/forms — paginated list with optional filters.</summary>
    [HttpGet]
    public async Task<IActionResult> GetForms(
        [FromQuery] string? carrier = null,
        [FromQuery] string? status = null,
        [FromQuery] string? formType = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var query = _db.FormLibraries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(carrier))
            query = query.Where(f => f.CarrierName.Contains(carrier));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(f => f.Status == status);
        if (!string.IsNullOrWhiteSpace(formType))
            query = query.Where(f => f.FormType == formType);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(f => f.CarrierName.Contains(search) || f.FormName.Contains(search));

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FormListItem
            {
                Id = f.Id,
                CarrierName = f.CarrierName,
                FormName = f.FormName,
                FormType = f.FormType,
                PageCount = f.PageCount,
                FieldCount = f.Fields.Count,
                Status = f.Status,
                ErrorMessage = f.ErrorMessage,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>POST /api/forms/upload — accepts multiple PDFs.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> UploadPdfs(
        [FromForm] List<IFormFile> files,
        [FromForm] string? carrierName = null,
        [FromForm] string? formType = null,
        [FromForm] int? projectId = null)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        if (files == null || files.Count == 0)
            return BadRequest(new { error = "No files provided" });

        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var results = new List<FormUploadResponse>();

        foreach (var file in files)
        {
            if (file.Length == 0 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new FormUploadResponse
                {
                    Id = 0,
                    FileName = file.FileName,
                    Status = "Error"
                });
                continue;
            }

            // Save PDF to S3 if configured, otherwise local disk
            var fileGuid = Guid.NewGuid().ToString("N");
            var s3BucketName = _config["S3:BucketName"];
            string pdfBlobPath;

            if (!string.IsNullOrEmpty(s3BucketName) && _s3 != null)
            {
                var s3Key = $"formiq/uploads/{fileGuid}.pdf";
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = s3BucketName,
                    Key = s3Key,
                    InputStream = ms,
                    ContentType = "application/pdf"
                });
                pdfBlobPath = s3Key;
                _logger.LogInformation("Uploaded PDF to S3: {Key}", s3Key);
            }
            else
            {
                var localPath = Path.Combine(uploadsDir, $"{fileGuid}.pdf");
                using (var stream = new FileStream(localPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                pdfBlobPath = localPath;
            }

            // Create FormLibrary record
            var form = new FormLibrary
            {
                CarrierName = carrierName ?? "Unknown",
                FormName = Path.GetFileNameWithoutExtension(file.FileName),
                FormType = formType ?? "Carrier",
                ProjectId = projectId,
                PdfBlobPath = pdfBlobPath,
                Status = "Queued",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.FormLibraries.Add(form);
            await _db.SaveChangesAsync();

            // Queue extraction
            _extractionQueue.Enqueue(form.Id);

            results.Add(new FormUploadResponse
            {
                Id = form.Id,
                FileName = file.FileName,
                Status = "Queued"
            });

            _logger.LogInformation("Uploaded form {Id}: {FileName}", form.Id, file.FileName);
        }

        return Ok(results);
    }

    /// <summary>GET /api/forms/{id} — form detail with fields.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetForm(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries
            .AsNoTracking()
            .Include(f => f.Fields.OrderBy(ff => ff.SortOrder ?? ff.Id))
            .FirstOrDefaultAsync(f => f.Id == id);

        if (form == null)
            return NotFound();

        var dto = new FormDetailDto
        {
            Id = form.Id,
            CarrierName = form.CarrierName,
            FormName = form.FormName,
            FormType = form.FormType,
            Version = form.Version,
            VerticalHint = form.VerticalHint,
            PageCount = form.PageCount,
            Status = form.Status,
            ErrorMessage = form.ErrorMessage,
            CreatedAt = form.CreatedAt,
            UpdatedAt = form.UpdatedAt,
            Fields = form.Fields.Select(ff => new FormFieldDto
            {
                Id = ff.Id,
                FieldLabel = ff.FieldLabel,
                FieldType = ff.FieldType,
                IsRequired = ff.IsRequired,
                SectionName = ff.SectionName,
                PageNumber = ff.PageNumber,
                AiConfidence = ff.AiConfidence,
                DictionaryFieldId = ff.DictionaryFieldId,
                ValidationRules = ff.ValidationRules,
                SortOrder = ff.SortOrder
            }).ToList()
        };

        return Ok(dto);
    }

    /// <summary>PUT /api/forms/{id}/fields — batch update extracted fields.</summary>
    [HttpPut("{id:int}/fields")]
    public async Task<IActionResult> UpdateFields(int id, [FromBody] List<FormFieldDto> fieldUpdates)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (form == null) return NotFound();

        foreach (var update in fieldUpdates)
        {
            var field = form.Fields.FirstOrDefault(f => f.Id == update.Id);
            if (field == null) continue;

            // Track corrections for training data
            if (field.FieldLabel != update.FieldLabel)
            {
                _db.FieldCorrections.Add(new FieldCorrection
                {
                    FormFieldId = field.Id,
                    FieldName = "FieldLabel",
                    OldValue = field.FieldLabel,
                    NewValue = update.FieldLabel
                });
            }

            field.FieldLabel = update.FieldLabel;
            field.FieldType = update.FieldType;
            field.IsRequired = update.IsRequired;
            field.SectionName = update.SectionName;
            field.DictionaryFieldId = update.DictionaryFieldId;
            field.SortOrder = update.SortOrder;
            field.UpdatedAt = DateTime.UtcNow;
        }

        form.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Fields updated", count = fieldUpdates.Count });
    }

    /// <summary>GET /api/forms/{id}/pdf — serve the uploaded PDF file (local or S3).</summary>
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries.FindAsync(id);
        if (form == null) return NotFound();

        var path = form.PdfBlobPath;

        // S3 path: starts with "s3://" or looks like an S3 key (no backslash, no drive letter)
        var s3Bucket = _config["S3:BucketName"];
        if (!string.IsNullOrEmpty(s3Bucket) && _s3 != null && IsS3Key(path))
        {
            var key = path.StartsWith($"s3://{s3Bucket}/") ? path.Substring($"s3://{s3Bucket}/".Length) : path;
            try
            {
                var s3Response = await _s3.GetObjectAsync(s3Bucket, key);
                return File(s3Response.ResponseStream, "application/pdf");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve PDF from S3: {Key}", key);
                return NotFound("PDF file not found in S3");
            }
        }

        // Local file fallback
        if (!System.IO.File.Exists(path)) return NotFound("PDF file not found");
        return PhysicalFile(Path.GetFullPath(path), "application/pdf");
    }

    private static bool IsS3Key(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.StartsWith("s3://")) return true;
        // Not a local path: no backslash, no drive letter, contains forward slash
        return !path.StartsWith("/") && !path.Contains('\\') && !System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:") && path.Contains('/');
    }

    /// <summary>DELETE /api/forms/{id} — delete a form and its fields.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteForm(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries
            .Include(f => f.Fields)
            .ThenInclude(ff => ff.Corrections)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (form == null) return NotFound();

        // Delete PDF (local file or S3 object)
        if (!string.IsNullOrEmpty(form.PdfBlobPath))
        {
            var s3Bucket = _config["S3:BucketName"];
            if (!string.IsNullOrEmpty(s3Bucket) && _s3 != null && IsS3Key(form.PdfBlobPath))
            {
                try
                {
                    var key = form.PdfBlobPath.StartsWith($"s3://{s3Bucket}/") ? form.PdfBlobPath.Substring($"s3://{s3Bucket}/".Length) : form.PdfBlobPath;
                    await _s3.DeleteObjectAsync(s3Bucket, key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete S3 object {Path}", form.PdfBlobPath);
                }
            }
            else if (!form.PdfBlobPath.StartsWith("s3://"))
            {
                try
                {
                    if (System.IO.File.Exists(form.PdfBlobPath))
                        System.IO.File.Delete(form.PdfBlobPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete PDF file {Path}", form.PdfBlobPath);
                }
            }
        }

        _db.FormLibraries.Remove(form); // cascade deletes FormFields + FieldCorrections
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted form {Id}: {FormName}", id, form.FormName);
        return NoContent();
    }

    /// <summary>POST /api/forms/{id}/resubmit — retry a failed extraction.</summary>
    [HttpPost("{id:int}/resubmit")]
    public async Task<IActionResult> ResubmitForm(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (form == null) return NotFound();

        if (form.Status == "Processing")
            return BadRequest(new { error = "Form is already processing" });

        // Clear existing fields and reset status
        if (form.Fields.Any())
        {
            _db.FormFields.RemoveRange(form.Fields);
        }

        form.Status = "Processing";
        form.ErrorMessage = null;
        form.FortressRequestId = null;
        form.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Re-enqueue for extraction
        _extractionQueue.Enqueue(form.Id);

        _logger.LogInformation("Resubmitted form {Id} for extraction", id);
        return Accepted(new { message = "Form resubmitted for extraction", id });
    }

    /// <summary>POST /api/forms/{id}/approve — mark form as Approved.</summary>
    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> ApproveForm(int id)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries.FindAsync(id);
        if (form == null) return NotFound();

        form.Status = "Approved";
        form.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Form approved", id });
    }
}
