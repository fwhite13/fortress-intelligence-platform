using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services.Exporters;

public class PdfExporter : ISpecExporter
{
    public Task<(byte[] Content, string MimeType, string Filename)> ExportAsync(SpecDocument doc)
    {
        var content = doc.EditedContent ?? doc.Content;
        var title = $"Specification v{doc.Version} — Submission {doc.SubmissionId}";

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var fontBold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        double pageWidth = 595;
        double leftMargin = 50;
        double rightMargin = 545;
        double y = 780;
        double lineHeight = 14;
        double titleFontSize = 16;
        double bodyFontSize = 10;
        double maxLineWidth = rightMargin - leftMargin;

        // Title
        page.AddText(title, titleFontSize, new UglyToad.PdfPig.Core.PdfPoint(leftMargin, y), fontBold);
        y -= (lineHeight * 2);

        // Meta
        var meta = $"Generated: {doc.GeneratedAt:yyyy-MM-dd}";
        page.AddText(meta, bodyFontSize, new UglyToad.PdfPig.Core.PdfPoint(leftMargin, y), font);
        y -= (lineHeight * 1.5);

        // Content lines
        var lines = content.Split('\n');
        foreach (var rawLine in lines)
        {
            if (y < 50)
            {
                page = builder.AddPage(PageSize.A4);
                y = 780;
            }

            var line = rawLine.TrimEnd('\r');

            // Strip markdown heading markers for display
            if (line.StartsWith("### ")) line = line[4..];
            else if (line.StartsWith("## ")) line = line[3..];
            else if (line.StartsWith("# ")) line = line[2..];

            // Truncate very long lines
            if (line.Length > 120) line = line[..120] + "...";

            if (string.IsNullOrWhiteSpace(line))
            {
                y -= lineHeight * 0.5;
                continue;
            }

            page.AddText(line, bodyFontSize, new UglyToad.PdfPig.Core.PdfPoint(leftMargin, y), font);
            y -= lineHeight;
        }

        var bytes = builder.Build();
        var filename = $"spec-{doc.SubmissionId}-v{doc.Version}.pdf";
        return Task.FromResult((bytes, "application/pdf", filename));
    }
}
