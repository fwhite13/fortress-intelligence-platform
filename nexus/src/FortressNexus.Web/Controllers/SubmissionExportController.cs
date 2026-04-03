using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FortressNexus.Web.Data;
using FortressNexus.Web.Services;
using FortressNexus.Web.Services.Exporters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Controllers;

[ApiController]
[Authorize]
[Route("nexus/{id:int}/export")]
public class SubmissionExportController : ControllerBase
{
    private readonly NexusDbContext _db;
    private readonly ISpecExporter _markdownExporter;
    private readonly PdfExporter _pdfExporter;
    private readonly ILogger<SubmissionExportController> _logger;

    public SubmissionExportController(
        NexusDbContext db,
        ISpecExporter markdownExporter,
        PdfExporter pdfExporter,
        ILogger<SubmissionExportController> logger)
    {
        _db = db;
        _markdownExporter = markdownExporter;
        _pdfExporter = pdfExporter;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Export(int id, [FromQuery] string format)
    {
        _logger.LogInformation("[EXPORT] Export requested for submission {SubmissionId} format={Format}", id, format);

        var submission = await _db.Submissions
            .Include(s => s.SpecDocuments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (submission is null)
            return NotFound($"Submission {id} not found.");

        if (!submission.ActiveSpecDocumentId.HasValue)
            return NotFound("No active spec document for this submission.");

        var specDoc = submission.SpecDocuments
            .FirstOrDefault(d => d.Id == submission.ActiveSpecDocumentId.Value);

        if (specDoc is null)
            return NotFound("Active spec document not found.");

        var slug = SlugHelper.Slugify(submission.Title);
        var baseFilename = $"{slug}-spec-v{specDoc.Version}";

        switch (format?.ToLowerInvariant())
        {
            case "md":
            {
                var (content, mimeType, filename) = await _markdownExporter.ExportAsync(specDoc);
                var mdFilename = $"{baseFilename}.md";
                return File(content, mimeType, mdFilename);
            }

            case "docx":
            {
                var docxBytes = ConvertMarkdownToDocx(specDoc.EditedContent ?? specDoc.Content, specDoc.SubmissionId, specDoc.Version);
                return File(docxBytes,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    $"{baseFilename}.docx");
            }

            case "pdf":
            {
                var (content, mimeType, filename) = await _pdfExporter.ExportAsync(specDoc);
                return File(content, mimeType, $"{baseFilename}.pdf");
            }

            default:
                return BadRequest($"Unknown export format '{format}'. Supported: md, docx, pdf.");
        }
    }

    private static byte[] ConvertMarkdownToDocx(string markdownContent, int submissionId, int version)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            foreach (var line in markdownContent.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');

                if (trimmed.StartsWith("### "))
                {
                    body.AppendChild(CreateHeadingParagraph(trimmed[4..], "Heading3"));
                }
                else if (trimmed.StartsWith("## "))
                {
                    body.AppendChild(CreateHeadingParagraph(trimmed[3..], "Heading2"));
                }
                else if (trimmed.StartsWith("# "))
                {
                    body.AppendChild(CreateHeadingParagraph(trimmed[2..], "Heading1"));
                }
                else
                {
                    body.AppendChild(CreateNormalParagraph(trimmed));
                }
            }

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static Paragraph CreateHeadingParagraph(string text, string styleId)
    {
        var para = new Paragraph();
        var props = new ParagraphProperties(new ParagraphStyleId { Val = styleId });
        para.AppendChild(props);
        var run = new Run(new Text(text));
        para.AppendChild(run);
        return para;
    }

    private static Paragraph CreateNormalParagraph(string text)
    {
        var para = new Paragraph();
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        para.AppendChild(run);
        return para;
    }
}
