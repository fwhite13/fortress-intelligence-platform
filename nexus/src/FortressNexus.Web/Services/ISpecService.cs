using FortressNexus.Web.Models.Entities;

namespace FortressNexus.Web.Services;

public interface ISpecService
{
    Task SaveDraftAsync(int specDocumentId, string editedContent, string userUpn);
    Task<SpecDocument> ApproveAsync(int specDocumentId, string approverOid);
}
