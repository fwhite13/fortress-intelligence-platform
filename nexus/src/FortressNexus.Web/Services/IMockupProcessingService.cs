using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public interface IMockupProcessingService
{
    Task<string> ExtractTextAsync(UploadedFile file);
}
