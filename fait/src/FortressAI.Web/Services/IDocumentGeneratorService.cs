namespace FortressAI.Web.Services;

public record DocumentSection(string Heading, string Content);

public record DocumentGenerationRequest(
    string Type,        // "word" (v1 only)
    string Title,
    List<DocumentSection> Sections
);

public interface IDocumentGeneratorService
{
    Task<byte[]> GenerateAsync(DocumentGenerationRequest request, CancellationToken ct = default);
}
