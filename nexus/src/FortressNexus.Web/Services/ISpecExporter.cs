using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public interface ISpecExporter
{
    Task<(byte[] Content, string MimeType, string Filename)> ExportAsync(SpecDocument doc);
}
