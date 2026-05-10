using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FortressAI.Web.Services;

public class StubDocumentGeneratorService : IDocumentGeneratorService
{
    public Task<byte[]> GenerateAsync(string type, string title, List<DocumentSection> sections,
        CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    new Paragraph(new Run(new Text($"{title} — Document generation coming soon"))),
                    new SectionProperties()
                )
            );
            mainPart.Document.Save();
        }
        return Task.FromResult(ms.ToArray());
    }
}
