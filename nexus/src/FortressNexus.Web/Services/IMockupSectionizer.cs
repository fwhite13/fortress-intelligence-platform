using FortressNexus.Web.Models;

namespace FortressNexus.Web.Services;

public interface IMockupSectionizer
{
    Task<List<MockupSection>> SectionizeAsync(string htmlContent, string submissionId);
}
