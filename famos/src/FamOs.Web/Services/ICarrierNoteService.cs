namespace FamOs.Web.Services;

public interface ICarrierNoteService
{
    Task<Dictionary<Guid, string>> GetNotesForAccountAsync(Guid accountId, int tenantId);
    Task SaveNoteAsync(Guid accountId, Guid quoteId, Guid userId, int tenantId, string noteText);
    Task DeleteNoteAsync(Guid noteId, Guid userId, int tenantId);
}
