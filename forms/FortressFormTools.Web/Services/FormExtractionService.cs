using System.Threading.Channels;
using System.Text.Json;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Amazon.S3;

namespace FortressFormTools.Web.Services;

/// <summary>
/// Orchestrates the extraction flow per uploaded form:
/// 1. Save PDF locally
/// 2. Upload to Fortress API (get link → upload to S3 → submit)
/// 3. Poll for results
/// 4. Map results to FormField entities
/// </summary>
public class FormExtractionService
{
    private readonly IFortressProjectsClient _fortressClient;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<FormExtractionService> _logger;
    private readonly IConfiguration _config;
    private readonly IAmazonS3? _s3;

    public FormExtractionService(
        IFortressProjectsClient fortressClient,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<FormExtractionService> logger,
        IConfiguration config,
        IAmazonS3? s3 = null)
    {
        _fortressClient = fortressClient;
        _contextFactory = contextFactory;
        _logger = logger;
        _config = config;
        _s3 = s3;
    }

    /// <summary>
    /// Run the full extraction pipeline for a single form.
    /// Called by the background service.
    /// </summary>
    public async Task ExtractAsync(int formLibraryId, CancellationToken ct = default)
    {
        await using var _db = await _contextFactory.CreateDbContextAsync();
        var form = await _db.FormLibraries.FindAsync(new object[] { formLibraryId }, ct);
        if (form == null)
        {
            _logger.LogWarning("FormLibrary {Id} not found", formLibraryId);
            return;
        }

        try
        {
            // ── Update status ──
            form.Status = "Processing";
            form.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // ── Read PDF bytes (S3 or local) ──
            var pdfPath = form.PdfBlobPath;
            var s3BucketName = _config["S3:BucketName"];
            bool isS3Key = !string.IsNullOrEmpty(s3BucketName) && _s3 != null
                && !pdfPath.StartsWith("/") && !pdfPath.Contains(":");

            byte[] pdfBytes;
            if (isS3Key)
            {
                _logger.LogInformation("Reading PDF from S3: {Key}", pdfPath);
                var response = await _s3!.GetObjectAsync(s3BucketName!, pdfPath, ct);
                using var ms = new MemoryStream();
                await response.ResponseStream.CopyToAsync(ms, ct);
                pdfBytes = ms.ToArray();
            }
            else
            {
                if (!File.Exists(pdfPath))
                {
                    throw new FileNotFoundException($"PDF not found: {pdfPath}");
                }
                pdfBytes = await File.ReadAllBytesAsync(pdfPath, ct);
            }

            // ── Get page count with PdfPig ──
            try
            {
                using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(pdfBytes);
                form.PageCount = pdfDoc.NumberOfPages;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read page count for form {Id}", formLibraryId);
            }

            // ── Upload to Fortress API ──
            var refId = $"form-{formLibraryId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var fileName = isS3Key ? pdfPath.Split('/').Last() : Path.GetFileName(pdfPath);

            var uploadLinks = await _fortressClient.GetUploadLinksAsync(refId, new List<string> { fileName });
            if (uploadLinks.Count == 0)
                throw new InvalidOperationException("No upload links returned from Fortress API");

            var link = uploadLinks[0];
            await _fortressClient.UploadFileAsync(link.UploadUrl, pdfBytes, "application/pdf");

            // ── Submit for processing ──
            var requestId = await _fortressClient.SubmitRequestAsync(refId, new List<string> { link.FileKey });
            form.FortressRequestId = requestId;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Submitted form {Id} to Fortress API, requestId={RequestId}", formLibraryId, requestId);

            // ── Poll for results ──
            var terminalStatuses = new HashSet<string> { "Completed", "Failed" };
            ProjectRequestResult? result = null;
            var maxAttempts = 60; // 5 minutes at 5s intervals

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                result = await _fortressClient.GetRequestStatusAsync(requestId);
                _logger.LogDebug("Form {Id} status: {Status} (attempt {Attempt})", formLibraryId, result.Status, attempt + 1);

                if (terminalStatuses.Contains(result.Status))
                    break;
            }

            if (result?.Status == "Completed")
            {
                // ── Parse and store extracted fields ──
                var fields = ParseExtractedFields(result, formLibraryId);
                if (fields.Count > 0)
                {
                    _db.FormFields.AddRange(fields);
                    _logger.LogInformation("Extracted {Count} fields for form {Id}", fields.Count, formLibraryId);
                }
                else
                {
                    _logger.LogWarning("Extraction completed but no fields parsed for form {Id}", formLibraryId);
                }

                form.Status = "Draft";
                form.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                form.Status = "Error";
                form.ErrorMessage = $"Extraction {result?.Status ?? "timed out"}. RequestId: {requestId}";
                form.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extraction failed for form {Id}", formLibraryId);

            form.Status = "Error";
            form.ErrorMessage = ex.Message;
            form.UpdatedAt = DateTime.UtcNow;

            try { await _db.SaveChangesAsync(ct); }
            catch { /* don't lose the original exception */ }
        }
    }

    /// <summary>
    /// Parse the Fortress API results into FormField entities.
    /// The API returns a JSON structure with extracted fields, sections, and confidence scores.
    /// </summary>
    private List<FormField> ParseExtractedFields(ProjectRequestResult result, int formLibraryId)
    {
        var fields = new List<FormField>();
        int sortOrder = 1;

        try
        {
            // result.Results could be a JsonElement, object, or string
            JsonElement root;

            if (result.Results is JsonElement je)
            {
                root = je;
            }
            else if (!string.IsNullOrWhiteSpace(result.RawJson))
            {
                using var doc = JsonDocument.Parse(result.RawJson);
                // Try to find results inside the response
                if (doc.RootElement.TryGetProperty("results", out var resultsElement))
                    root = resultsElement.Clone();
                else if (doc.RootElement.TryGetProperty("response", out var responseElement) 
                         && responseElement.TryGetProperty("results", out var nestedResults))
                    root = nestedResults.Clone();
                else
                    root = doc.RootElement.Clone();
            }
            else
            {
                _logger.LogWarning("No results data to parse for form {Id}", formLibraryId);
                return fields;
            }

            // Handle array of results (one per document)
            var items = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : new List<JsonElement> { root };

            foreach (var item in items)
            {
                // Try common structures: fields array, data object with sections, etc.
                if (item.TryGetProperty("fields", out var fieldsArray) && fieldsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in fieldsArray.EnumerateArray())
                    {
                        var field = MapJsonToFormField(f, formLibraryId, sortOrder++);
                        if (field != null) fields.Add(field);
                    }
                }
                else if (item.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
                {
                    foreach (var section in sections.EnumerateArray())
                    {
                        var sectionName = section.TryGetProperty("name", out var sn) ? sn.GetString() 
                            : section.TryGetProperty("sectionName", out var sn2) ? sn2.GetString() : null;

                        if (section.TryGetProperty("fields", out var sFields) && sFields.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var f in sFields.EnumerateArray())
                            {
                                var field = MapJsonToFormField(f, formLibraryId, sortOrder++, sectionName);
                                if (field != null) fields.Add(field);
                            }
                        }
                    }
                }
                else if (item.TryGetProperty("data", out var data))
                {
                    // Flat key-value extraction
                    if (data.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in data.EnumerateObject())
                        {
                            fields.Add(new FormField
                            {
                                FormLibraryId = formLibraryId,
                                FieldLabel = prop.Name,
                                FieldType = InferFieldType(prop.Value),
                                AiConfidence = 0.8m, // default when not provided
                                SortOrder = sortOrder++,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
                // Try iterating all properties as potential field groups
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in item.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            var field = MapJsonToFormField(prop.Value, formLibraryId, sortOrder++);
                            if (field != null)
                            {
                                if (string.IsNullOrEmpty(field.FieldLabel))
                                    field.FieldLabel = prop.Name;
                                fields.Add(field);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse extracted fields for form {Id}", formLibraryId);
        }

        return fields;
    }

    private FormField? MapJsonToFormField(JsonElement element, int formLibraryId, int sortOrder, string? defaultSection = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var label = element.TryGetProperty("label", out var l) ? l.GetString()
            : element.TryGetProperty("fieldLabel", out var fl) ? fl.GetString()
            : element.TryGetProperty("name", out var n) ? n.GetString()
            : element.TryGetProperty("fieldName", out var fn) ? fn.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(label))
            return null;

        var fieldType = element.TryGetProperty("type", out var t) ? MapFieldType(t.GetString())
            : element.TryGetProperty("fieldType", out var ft) ? MapFieldType(ft.GetString())
            : "text";

        decimal? confidence = null;
        if (element.TryGetProperty("confidence", out var c))
        {
            if (c.TryGetDecimal(out var conf))
                confidence = conf > 1 ? conf / 100m : conf; // normalize to 0-1
        }
        else if (element.TryGetProperty("aiConfidence", out var ac) && ac.TryGetDecimal(out var aconf))
        {
            confidence = aconf > 1 ? aconf / 100m : aconf;
        }

        var section = element.TryGetProperty("section", out var s) ? s.GetString()
            : element.TryGetProperty("sectionName", out var sn) ? sn.GetString()
            : element.TryGetProperty("group", out var g) ? g.GetString()
            : defaultSection;

        var required = element.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
        if (!required && element.TryGetProperty("isRequired", out var ir) && ir.ValueKind == JsonValueKind.True)
            required = true;

        int? pageNumber = null;
        if (element.TryGetProperty("page", out var p) && p.TryGetInt32(out var pg))
            pageNumber = pg;
        else if (element.TryGetProperty("pageNumber", out var pn) && pn.TryGetInt32(out var pgn))
            pageNumber = pgn;

        return new FormField
        {
            FormLibraryId = formLibraryId,
            FieldLabel = label,
            FieldType = fieldType,
            IsRequired = required,
            SectionName = section,
            PageNumber = pageNumber,
            AiConfidence = confidence,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string MapFieldType(string? apiType)
    {
        if (string.IsNullOrWhiteSpace(apiType)) return "text";
        return apiType.ToLowerInvariant() switch
        {
            "string" or "text" or "alphanumeric" => "text",
            "number" or "numeric" or "integer" or "int" or "float" or "decimal" => "number",
            "date" or "datetime" => "date",
            "boolean" or "checkbox" or "check" or "yes/no" or "yesno" => "checkbox",
            "select" or "dropdown" or "enum" or "list" => "dropdown",
            "radio" or "option" => "radio",
            "textarea" or "multiline" or "memo" or "long_text" => "textarea",
            "currency" or "money" or "dollar" => "number",
            "phone" or "telephone" => "text",
            "email" => "text",
            "address" => "text",
            "signature" => "signature",
            _ => "text"
        };
    }

    private static string InferFieldType(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "checkbox",
            _ => "text"
        };
    }
}

/// <summary>
/// Background hosted service that processes extraction jobs from a queue.
/// </summary>
public class ExtractionBackgroundService : BackgroundService
{
    private readonly Channel<int> _queue = Channel.CreateUnbounded<int>();
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExtractionBackgroundService> _logger;

    public ExtractionBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ExtractionBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>Enqueue a form for background extraction.</summary>
    public void Enqueue(int formLibraryId)
    {
        _queue.Writer.TryWrite(formLibraryId);
        _logger.LogInformation("Queued form {Id} for extraction", formLibraryId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Extraction background service started");

        await foreach (var formId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var extractionService = scope.ServiceProvider.GetRequiredService<FormExtractionService>();
                await extractionService.ExtractAsync(formId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background extraction failed for form {Id}", formId);
            }
        }
    }
}
