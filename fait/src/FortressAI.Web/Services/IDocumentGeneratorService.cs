namespace FortressAI.Web.Services;

public record DocumentSection(string Heading, string Content);

public interface IDocumentGeneratorService
{
    Task<byte[]> GenerateAsync(string type, string title, List<DocumentSection> sections,
        CancellationToken ct = default);
}
