using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FamOs.Web.Services;

public class TeamNoteService(IDbContextFactory<FamOsDbContext> dbFactory)
{
    public async Task<List<TeamNote>> GetNotesAsync(Guid? opportunityId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.TeamNotes.AsQueryable();
        if (opportunityId.HasValue)
            query = query.Where(n => n.OpportunityId == opportunityId);
        return await query.OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync();
    }

    public async Task<List<Opportunity>> GetAccountsForDropdownAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Where(o => !o.IsClosed)
            .OrderBy(o => o.Name)
            .Select(o => new Opportunity { Id = o.Id, Name = o.Name })
            .ToListAsync();
    }

    public async Task PostNoteAsync(string authorId, string text, Guid? opportunityId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.TeamNotes.Add(new TeamNote
        {
            AuthorId      = authorId,
            NoteText      = text,
            OpportunityId = opportunityId,
            TeamTag       = "TIG",
            CreatedAt     = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
