using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services.Exporters;

public class MarkdownExporter : ISpecExporter
{
    public Task<(byte[] Content, string MimeType, string Filename)> ExportAsync(SpecDocument doc)
    {
        var text = doc.EditedContent ?? doc.Content;
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var name = $"spec-{doc.SubmissionId}-v{doc.Version}.md";
        return Task.FromResult((bytes, "text/markdown", name));
    }
}
