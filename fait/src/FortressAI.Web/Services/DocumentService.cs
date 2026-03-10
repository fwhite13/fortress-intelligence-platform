using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using System.Text;

namespace FortressAI.Web.Services;

public class DocumentService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<DocumentService> _logger;
    private readonly KbDocumentService _kbDocumentService;

    public DocumentService(IDbContextFactory<AppDbContext> contextFactory, ILogger<DocumentService> logger, KbDocumentService kbDocumentService)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _kbDocumentService = kbDocumentService;
    }

    public async Task<ProjectDocument?> UploadDocumentAsync(Guid projectId, Guid userId, string filename, string contentType, Stream fileStream, long fileSize)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        // Verify project ownership
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId);
        if (project == null) return null;

        // Buffer the stream so we can use it for both text extraction and S3 upload
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer);
        buffer.Position = 0;

        // Extract text content
        string? textContent = null;
        try
        {
            textContent = await ExtractTextAsync(filename, contentType, buffer);
            _logger.LogInformation("[UPLOAD] Extracted content from {Filename}: {ContentLength} chars, IsNull={IsNull}",
                filename, textContent?.Length ?? 0, textContent == null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UPLOAD] Failed to extract text from {Filename}", filename);
        }

        var doc = new ProjectDocument
        {
            ProjectId = projectId,
            Filename = filename,
            ContentType = contentType,
            Content = textContent,
            FileSize = fileSize,
            UploadedAt = DateTime.UtcNow,
            IngestionStatus = "none"
        };

        db.ProjectDocuments.Add(doc);
        project.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        _logger.LogInformation("[UPLOAD] Saved document {Filename} (Id={DocId}) to project {ProjectId}. Content saved: {HasContent}",
            filename, doc.Id, projectId, !string.IsNullOrEmpty(doc.Content));

        if (IsImageFile(filename))
        {
            doc.UploadWarning = "Images are stored but cannot be directly viewed by the AI in project context. For image analysis, attach images directly in a chat message instead.";
            _logger.LogInformation("[UPLOAD] Image file {Filename} — set upload warning for UI", filename);
        }

        // Upload to S3 for Bedrock RAG (non-fatal — doc is still usable inline if this fails)
        try
        {
            buffer.Position = 0;
            var s3Key = await _kbDocumentService.UploadProjectDocumentAsync(buffer, filename, contentType, projectId, userId);
            doc.S3Key = s3Key;
            doc.IngestionStatus = "pending";
            await db.SaveChangesAsync();

            // Trigger Bedrock ingestion sync (fire-and-forget style — conflict handled internally)
            await _kbDocumentService.StartProjectIngestionAsync();

            _logger.LogInformation("[UPLOAD] Project doc {Filename} uploaded to S3 key {S3Key}, ingestion triggered", filename, s3Key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UPLOAD] S3/RAG upload failed for {Filename} — document saved inline only", filename);
            // Non-fatal: doc is still accessible via inline content injection
        }

        return doc;
    }

    /// <summary>
    /// Migrates a pre-RAG document (has Content but no S3Key) to S3 for Bedrock ingestion.
    /// Uploads the content as a text file, sets S3Key + IngestionStatus = "pending", saves to DB.
    /// Returns the S3Key on success, null on failure.
    /// </summary>
    public async Task<string?> MigrateDocumentToS3Async(ProjectDocument doc, Guid userId)
    {
        if (string.IsNullOrEmpty(doc.Content) || !string.IsNullOrEmpty(doc.S3Key))
        {
            _logger.LogInformation("[MIGRATE] Skipping doc {DocId} — already has S3Key or no Content", doc.Id);
            return doc.S3Key;
        }

        await using var db = await _contextFactory.CreateDbContextAsync();

        // Reload doc from DB to get current state and verify ownership
        var dbDoc = await db.ProjectDocuments
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == doc.Id && d.Project!.UserId == userId);

        if (dbDoc == null)
        {
            _logger.LogWarning("[MIGRATE] Doc {DocId} not found or access denied", doc.Id);
            return null;
        }

        if (!string.IsNullOrEmpty(dbDoc.S3Key))
        {
            _logger.LogInformation("[MIGRATE] Doc {DocId} already migrated (S3Key={S3Key})", doc.Id, dbDoc.S3Key);
            // Sync back to in-memory doc
            doc.S3Key = dbDoc.S3Key;
            return dbDoc.S3Key;
        }

        if (string.IsNullOrEmpty(dbDoc.Content))
        {
            _logger.LogWarning("[MIGRATE] Doc {DocId} has no Content to migrate", doc.Id);
            return null;
        }

        try
        {
            // Use a text-friendly filename for S3 — append .txt for non-text extensions
            var filename = dbDoc.Filename;
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            var uploadFilename = (ext == ".txt" || ext == ".md" || ext == ".csv" || ext == ".json" || ext == ".xml" || ext == ".yaml" || ext == ".yml")
                ? filename
                : filename + ".txt";

            var contentBytes = System.Text.Encoding.UTF8.GetBytes(dbDoc.Content);
            using var stream = new MemoryStream(contentBytes);

            var s3Key = await _kbDocumentService.UploadProjectDocumentAsync(
                stream, uploadFilename, "text/plain", dbDoc.ProjectId, userId);

            dbDoc.S3Key = s3Key;
            dbDoc.IngestionStatus = "pending";
            await db.SaveChangesAsync();

            // Sync back to the in-memory doc so callers see the new key
            doc.S3Key = s3Key;

            _logger.LogInformation("[MIGRATE] Migrated doc {DocId} ({Filename}) to S3 key {S3Key}", doc.Id, filename, s3Key);
            return s3Key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MIGRATE] Failed to migrate doc {DocId} ({Filename}) to S3", doc.Id, dbDoc.Filename);
            return null;
        }
    }

    /// <summary>
    /// Migrates a pre-RAG document by ID to S3 (avoids capturing EF-tracked entities across scope boundaries).
    /// Uploads Content as text/plain to S3, sets S3Key + IngestionStatus = "pending".
    /// NOTE: StartIngestionAsync should be called once after all migrations complete — not inside this method.
    /// </summary>
    public async Task MigrateDocumentToS3ByIdAsync(Guid docId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var doc = await db.ProjectDocuments
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == docId && d.Project!.UserId == userId);

        if (doc == null || !string.IsNullOrEmpty(doc.S3Key)) return;

        if (string.IsNullOrEmpty(doc.Content))
        {
            _logger.LogWarning("[MIGRATE] Doc {DocId} has no Content to migrate", docId);
            return;
        }

        var filename = doc.Filename;
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        var uploadFilename = (ext == ".txt" || ext == ".md" || ext == ".csv" || ext == ".json" || ext == ".xml" || ext == ".yaml" || ext == ".yml")
            ? filename
            : filename + ".txt";

        try
        {
            var contentBytes = System.Text.Encoding.UTF8.GetBytes(doc.Content);
            using var stream = new MemoryStream(contentBytes);
            var s3Key = await _kbDocumentService.UploadProjectDocumentAsync(
                stream, uploadFilename, "text/plain", doc.ProjectId, userId);

            doc.S3Key = s3Key;
            doc.IngestionStatus = "pending";
            await db.SaveChangesAsync();
            _logger.LogInformation("[MIGRATE] Doc {DocId} migrated to S3 key {S3Key}", docId, s3Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MIGRATE] Failed to migrate doc {DocId} ({Filename}) to S3 by ID", docId, filename);
        }
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId, Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var doc = await db.ProjectDocuments
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == documentId && d.Project!.UserId == userId);

        if (doc == null) return false;

        // Clean up S3 + metadata if doc was uploaded to RAG
        if (!string.IsNullOrEmpty(doc.S3Key))
        {
            try
            {
                await _kbDocumentService.DeleteDocumentAsync(doc.S3Key);
                // Re-trigger ingestion to rebuild the KB index without this doc
                await _kbDocumentService.StartProjectIngestionAsync();
                _logger.LogInformation("[DELETE] Removed S3 doc {S3Key} and triggered re-ingestion", doc.S3Key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DELETE] S3 cleanup failed for {S3Key} — removing from DB anyway", doc.S3Key);
            }
        }

        db.ProjectDocuments.Remove(doc);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ProjectDocument>> GetProjectDocumentsAsync(Guid projectId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ProjectDocuments
            .Where(d => d.ProjectId == projectId)
            .OrderBy(d => d.UploadedAt)
            .ToListAsync();
    }

    // All supported file extensions
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        ".pdf", ".docx", ".xlsx", ".pptx", ".rtf",
        // Text & Data
        ".txt", ".md", ".csv", ".json", ".xml", ".yaml", ".yml", ".log", ".sql",
        // Code
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".html", ".css", ".java",
        ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".swift", ".kt",
        ".sh", ".bash", ".ps1", ".r", ".scala", ".vue", ".svelte",
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".tiff", ".tif"
    };

    public static string AcceptFilter => string.Join(",", SupportedExtensions);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".tiff", ".tif"
    };

    public static bool IsImageExtension(string ext) => ImageExtensions.Contains(ext);

    public static bool IsImageFile(string filename) => IsImageExtension(Path.GetExtension(filename).ToLowerInvariant());

    public static string GetImageMediaType(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        ".tiff" or ".tif" => "image/tiff",
        _ => "application/octet-stream"
    };

    public static bool IsSupported(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return SupportedExtensions.Contains(ext);
    }

    private async Task<string?> ExtractTextAsync(string filename, string contentType, Stream stream)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        _logger.LogInformation("[UPLOAD] ExtractText: filename={Filename}, contentType={ContentType}, ext={Ext}, streamLength={StreamLength}",
            filename, contentType, ext, stream.CanSeek ? stream.Length : -1);

        if (contentType == "application/pdf" || ext == ".pdf")
        {
            _logger.LogInformation("[UPLOAD] Using PDF base64 encoder for {Filename}", filename);
            return await ConvertPdfToBase64Async(stream);
        }

        if (ext == ".docx")
        {
            _logger.LogInformation("[UPLOAD] Using DOCX extractor for {Filename}", filename);
            return await ExtractDocxTextAsync(stream);
        }

        if (ext == ".xlsx")
        {
            _logger.LogInformation("[UPLOAD] Using XLSX extractor for {Filename}", filename);
            return await ExtractXlsxTextAsync(stream);
        }

        if (ext == ".pptx")
        {
            _logger.LogInformation("[UPLOAD] Using PPTX extractor for {Filename}", filename);
            return await ExtractPptxTextAsync(stream);
        }

        // Plain text / code files
        if (contentType.StartsWith("text/") || SupportedExtensions.Contains(ext))
        {
            _logger.LogInformation("[UPLOAD] Using text reader for {Filename}", filename);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var content = await reader.ReadToEndAsync();
            _logger.LogInformation("[UPLOAD] Text reader got {Length} chars from {Filename}", content.Length, filename);
            return content;
        }

        // Check if it's an image
        if (IsImageExtension(ext))
        {
            _logger.LogInformation("[UPLOAD] Using base64 encoder for image {Filename}", filename);
            return await ConvertImageToBase64(stream, ext);
        }

        _logger.LogWarning("[UPLOAD] No extractor matched for {Filename} (ext={Ext}, contentType={ContentType})", filename, ext, contentType);
        return null;
    }

    private async Task<string> ConvertImageToBase64(Stream stream, string ext)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var mediaType = GetImageMediaType(ext);
        _logger.LogInformation("[UPLOAD] Image converted to base64: {Bytes} bytes, mediaType={MediaType}", ms.Length, mediaType);
        // Store as data URI so we can extract media type later
        return $"data:{mediaType};base64,{base64}";
    }

    private async Task<string?> ExtractDocxTextAsync(Stream stream)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return null;

            var sb = new StringBuilder();

            foreach (var element in body.ChildElements)
            {
                if (element is Paragraph para)
                {
                    var text = ProcessParagraph(para);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                        sb.AppendLine();
                    }
                }
                else if (element is DocumentFormat.OpenXml.Wordprocessing.Table table)
                {
                    sb.AppendLine(ProcessTable(table));
                    sb.AppendLine();
                }
            }

            var result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DOCX extraction failed");
            return null;
        }
    }

    private static string ProcessParagraph(Paragraph para)
    {
        var sb = new StringBuilder();

        // Check for heading style
        var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        var headingPrefix = "";
        if (!string.IsNullOrEmpty(styleId))
        {
            var lower = styleId.ToLowerInvariant();
            if (lower == "heading1" || lower == "title") headingPrefix = "# ";
            else if (lower == "heading2" || lower == "subtitle") headingPrefix = "## ";
            else if (lower == "heading3") headingPrefix = "### ";
            else if (lower == "heading4") headingPrefix = "#### ";
        }

        // Check for list formatting
        var isListItem = para.ParagraphProperties?.NumberingProperties != null;

        // Process runs
        foreach (var run in para.Elements<DocumentFormat.OpenXml.Wordprocessing.Run>())
        {
            var text = run.InnerText;
            if (string.IsNullOrEmpty(text)) continue;

            var rProps = run.RunProperties;
            var isBold = rProps?.Bold != null && (rProps.Bold.Val == null || rProps.Bold.Val.Value);
            var isItalic = rProps?.Italic != null && (rProps.Italic.Val == null || rProps.Italic.Val.Value);

            if (isBold && isItalic) text = $"***{text}***";
            else if (isBold) text = $"**{text}**";
            else if (isItalic) text = $"*{text}*";

            sb.Append(text);
        }

        var result = sb.ToString().Trim();
        if (string.IsNullOrEmpty(result)) return "";

        if (!string.IsNullOrEmpty(headingPrefix))
            return headingPrefix + result;
        if (isListItem)
            return "- " + result;

        return result;
    }

    private static string ProcessTable(DocumentFormat.OpenXml.Wordprocessing.Table table)
    {
        var rows = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().ToList();
        if (rows.Count == 0) return "";

        var sb = new StringBuilder();
        var isFirst = true;

        foreach (var row in rows)
        {
            var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().ToList();
            var values = cells.Select(c => (c.InnerText?.Trim() ?? "").Replace("|", "\\|")).ToList();
            sb.AppendLine("| " + string.Join(" | ", values) + " |");

            if (isFirst)
            {
                sb.AppendLine("| " + string.Join(" | ", values.Select(_ => "---")) + " |");
                isFirst = false;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<string?> ExtractXlsxTextAsync(Stream stream)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var doc = SpreadsheetDocument.Open(ms, false);
            var sb = new StringBuilder();
            var sheets = doc.WorkbookPart?.Workbook?.Sheets?.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>() ?? Enumerable.Empty<DocumentFormat.OpenXml.Spreadsheet.Sheet>();
            var sharedStrings = doc.WorkbookPart?.SharedStringTablePart?.SharedStringTable;

            foreach (var sheet in sheets)
            {
                sb.AppendLine($"## Sheet: {sheet.Name}");
                sb.AppendLine();
                var wsPart = (WorksheetPart?)doc.WorkbookPart?.GetPartById(sheet.Id!);
                if (wsPart == null) continue;

                var rows = wsPart.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().ToList();
                if (rows.Count == 0) continue;

                // Collect all row data first
                var allRowData = new List<List<string>>();
                int maxCols = 0;

                foreach (var row in rows)
                {
                    var cells = row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>().ToList();
                    var values = new List<string>();

                    foreach (var cell in cells)
                    {
                        var val = cell.InnerText;
                        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString && sharedStrings != null)
                        {
                            if (int.TryParse(val, out var idx))
                                val = sharedStrings.ElementAt(idx).InnerText;
                        }
                        values.Add(val?.Trim() ?? "");
                    }
                    if (values.Count > maxCols) maxCols = values.Count;
                    allRowData.Add(values);
                }

                if (maxCols == 0) continue;

                // Pad rows to max columns
                foreach (var rowData in allRowData)
                {
                    while (rowData.Count < maxCols) rowData.Add("");
                }

                // Find non-empty columns
                var nonEmptyCols = new List<int>();
                for (int c = 0; c < maxCols; c++)
                {
                    if (allRowData.Any(r => !string.IsNullOrWhiteSpace(r[c])))
                        nonEmptyCols.Add(c);
                }

                if (nonEmptyCols.Count == 0) continue;

                // Check if first row has content (use as headers) or use column letters
                var firstRow = allRowData[0];
                var hasHeaders = nonEmptyCols.Any(c => !string.IsNullOrWhiteSpace(firstRow[c]));

                // Header row
                if (hasHeaders)
                {
                    sb.AppendLine("| " + string.Join(" | ", nonEmptyCols.Select(c => firstRow[c].Replace("|", "\\|"))) + " |");
                }
                else
                {
                    sb.AppendLine("| " + string.Join(" | ", nonEmptyCols.Select(c => GetColumnLetter(c))) + " |");
                }

                // Separator
                sb.AppendLine("| " + string.Join(" | ", nonEmptyCols.Select(_ => "---")) + " |");

                // Data rows (skip first if it was headers)
                int startRow = hasHeaders ? 1 : 0;
                int maxRows = 100;
                int dataRowCount = allRowData.Count - startRow;
                int rowsToShow = Math.Min(dataRowCount, maxRows);

                for (int r = startRow; r < startRow + rowsToShow; r++)
                {
                    var rowData = allRowData[r];
                    sb.AppendLine("| " + string.Join(" | ", nonEmptyCols.Select(c => rowData[c].Replace("|", "\\|"))) + " |");
                }

                if (dataRowCount > maxRows)
                {
                    sb.AppendLine($"... ({dataRowCount - maxRows} more rows truncated)");
                }

                sb.AppendLine();
            }
            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "XLSX extraction failed");
            return null;
        }
    }

    private static string GetColumnLetter(int colIndex)
    {
        var result = "";
        while (colIndex >= 0)
        {
            result = (char)('A' + colIndex % 26) + result;
            colIndex = colIndex / 26 - 1;
        }
        return result;
    }

    private async Task<string?> ExtractPptxTextAsync(Stream stream)
    {
        try
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            using var doc = PresentationDocument.Open(ms, false);
            var sb = new StringBuilder();
            var slideParts = doc.PresentationPart?.SlideParts ?? Enumerable.Empty<SlidePart>();
            int slideNum = 1;
            foreach (var slidePart in slideParts)
            {
                // Try to extract title from title placeholder
                string? slideTitle = null;
                var shapes = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Shape>();
                var bodyTexts = new List<string>();
                bool hasImage = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.Picture>().Any();
                bool hasChart = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Presentation.GraphicFrame>().Any();

                foreach (var shape in shapes)
                {
                    var ph = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
                        .GetFirstChild<DocumentFormat.OpenXml.Presentation.PlaceholderShape>();

                    var isTitle = false;
                    if (ph != null)
                    {
                        var phType = ph.Type?.Value;
                        var phIdx = ph.Index?.Value;
                        isTitle = phType == DocumentFormat.OpenXml.Presentation.PlaceholderValues.Title
                               || phType == DocumentFormat.OpenXml.Presentation.PlaceholderValues.CenteredTitle
                               || (phType == null && phIdx == 0);
                    }

                    var shapeText = shape.TextBody?.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t));

                    if (shapeText == null || !shapeText.Any()) continue;

                    var combinedText = string.Join(" ", shapeText).Trim();
                    if (string.IsNullOrWhiteSpace(combinedText)) continue;

                    if (isTitle && slideTitle == null)
                    {
                        slideTitle = combinedText;
                    }
                    else
                    {
                        // Check if paragraphs have list markers
                        var paragraphs = shape.TextBody?.Elements<DocumentFormat.OpenXml.Drawing.Paragraph>();
                        if (paragraphs != null)
                        {
                            foreach (var para in paragraphs)
                            {
                                var paraText = string.Join("", para.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                                    .Select(t => t.Text)).Trim();
                                if (string.IsNullOrWhiteSpace(paraText)) continue;

                                var hasBullet = para.ParagraphProperties?.GetFirstChild<DocumentFormat.OpenXml.Drawing.BulletFont>() != null
                                    || para.ParagraphProperties?.GetFirstChild<DocumentFormat.OpenXml.Drawing.CharacterBullet>() != null
                                    || para.ParagraphProperties?.GetFirstChild<DocumentFormat.OpenXml.Drawing.AutoNumberedBullet>() != null
                                    || (para.ParagraphProperties?.Level?.Value ?? 0) > 0;

                                bodyTexts.Add(hasBullet ? $"- {paraText}" : paraText);
                            }
                        }
                    }
                }

                // Build slide output
                if (!string.IsNullOrWhiteSpace(slideTitle))
                    sb.AppendLine($"## Slide {slideNum}: {slideTitle}");
                else
                    sb.AppendLine($"## Slide {slideNum}");

                sb.AppendLine();

                foreach (var text in bodyTexts)
                    sb.AppendLine(text);

                if (hasImage) sb.AppendLine("[Image]");
                if (hasChart) sb.AppendLine("[Chart]");

                sb.AppendLine();
                slideNum++;
            }
            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PPTX extraction failed");
            return null;
        }
    }

    private async Task<string> ConvertPdfToBase64Async(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());
        _logger.LogInformation("[UPLOAD] PDF converted to base64: {Bytes} bytes", ms.Length);
        // Store as data URI — BedrockService will detect this and route to document blocks
        return $"data:application/pdf;base64,{base64}";
    }
}
