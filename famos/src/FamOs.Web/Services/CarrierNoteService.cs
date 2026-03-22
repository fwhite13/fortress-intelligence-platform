using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class CarrierNoteService : ICarrierNoteService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;

    public CarrierNoteService(IDbContextFactory<FamOsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>Returns notes keyed by QuoteId for all quotes on the given account.</summary>
    public async Task<Dictionary<Guid, string>> GetNotesForAccountAsync(Guid accountId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var notes = await db.CarrierNotes
            .Where(n => n.AccountId == accountId && n.TenantId == tenantId)
            .ToListAsync();

        // Latest note wins if multiple exist per quote
        return notes
            .GroupBy(n => n.QuoteId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.UpdatedAt).First().NoteText);
    }

    public async Task SaveNoteAsync(Guid accountId, Guid quoteId, Guid userId, int tenantId, string noteText)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.CarrierNotes
            .FirstOrDefaultAsync(n => n.AccountId == accountId && n.QuoteId == quoteId && n.TenantId == tenantId);

        if (existing != null)
        {
            existing.NoteText          = noteText;
            existing.UpdatedByUserId   = userId;
            existing.UpdatedAt         = DateTime.UtcNow;
        }
        else
        {
            db.CarrierNotes.Add(new CarrierNote
            {
                AccountId         = accountId,
                QuoteId           = quoteId,
                TenantId          = tenantId,
                NoteText          = noteText,
                CreatedByUserId   = userId,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteNoteAsync(Guid noteId, Guid userId, int tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var note = await db.CarrierNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.TenantId == tenantId)
            ?? throw new KeyNotFoundException($"CarrierNote {noteId} not found");

        db.CarrierNotes.Remove(note);
        await db.SaveChangesAsync();
    }
}
