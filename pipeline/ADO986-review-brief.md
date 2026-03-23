# ADO #986 — Team Notes Page — Code Review Brief

## Task
Review commit `21d6a6f` implementing the Team Notes page for FAMOS. Check all 20 checklist items below against the actual diff provided.

## Diff Summary (7 files, 250 insertions)

### TeamNote.cs (new)
```csharp
namespace FamOs.Web.Data.Entities;

public class TeamNote
{
    public int      Id            { get; set; }
    public string   AuthorId      { get; set; } = "";
    public string   NoteText      { get; set; } = "";
    public Guid?    OpportunityId { get; set; }
    public string   TeamTag       { get; set; } = "TIG";
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
}
```

### FamOsDbContext.cs (additions)
```csharp
public DbSet<TeamNote>   TeamNotes  => Set<TeamNote>();

// In OnModelCreating:
m.Entity<TeamNote>(e =>
{
    e.ToTable("team_notes");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
    e.Property(x => x.AuthorId).HasColumnName("author_id").HasMaxLength(255).IsRequired();
    e.Property(x => x.NoteText).HasColumnName("note_text").HasColumnType("text").IsRequired();
    e.Property(x => x.OpportunityId).HasColumnName("opportunity_id");
    e.Property(x => x.TeamTag).HasColumnName("team_tag").HasMaxLength(20).HasDefaultValue("TIG");
    e.Property(x => x.CreatedAt).HasColumnName("created_at");
});
```

### Program.cs (additions)
```csharp
builder.Services.AddScoped<TeamNoteService>();

// In startup SQL block:
await db.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS team_notes (
        id             INT AUTO_INCREMENT PRIMARY KEY,
        author_id      VARCHAR(255) NOT NULL,
        note_text      TEXT NOT NULL,
        opportunity_id CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
        team_tag       VARCHAR(20) NOT NULL DEFAULT 'TIG',
        created_at     DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        INDEX idx_team_notes_opp (opportunity_id)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
""");
```

### TeamNoteService.cs (new)
```csharp
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
```

### Notes.razor (new, 150 lines)
Key excerpts:
```razor
@page "/notes"
@attribute [Authorize]
@inject TeamNoteService NoteService
@inject UserSessionService UserSession

@* Compose form *@
<MudSelect T="Guid?" @bind-Value="_selectedOppId" ...>
<MudTextField @bind-Value="_noteText" ... />
<MudButton OnClick="PostNoteAsync" Disabled="_posting">

@* Filter dropdown *@
<MudSelect T="Guid?" Value="_filterOppId" ValueChanged="OnFilterChanged" ...>

@* Notes list shows: avatar initials, team tag badge, timestamp, note text *@
<div class="famos-empty-state"> (for empty state)

@code {
    private async Task PostNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(_noteText)) return;
        try
        {
            _posting = true;
            await NoteService.PostNoteAsync(_userId, _noteText.Trim(), _selectedOppId);
            ...
        }
        catch (Exception ex) { }
        finally
        {
            _posting = false;
        }
    }

    private async Task OnFilterChanged(Guid? value)
    {
        _filterOppId = value;
        await LoadNotesAsync();
    }
}
```

### NavMenu.razor
```razor
<NavLink href="/notes" ...>
    <span class="famos-nav-icon">
        <MudIcon Icon="@Icons.Material.Outlined.StickyNote2" Size="Size.Small" />
    </span>
    Team Notes
</NavLink>
```
(Added AFTER Accounts block, BEFORE the disabled Reports item)

### famos.css
```css
.famos-note-team-tag { font-size:9px; font-weight:700; padding:1px 5px; border-radius:8px; margin-left:4px; vertical-align:middle; }
.famos-tag-tig  { background:#d1fae5; color:#065f46; }
.famos-tag-higg { background:#fef3c7; color:#92400e; }
```

---

## Review Checklist — Evaluate each item PASS or FAIL with reasoning

**Entity + DbContext**
1. `TeamNote` has all 6 fields: Id, AuthorId, NoteText, OpportunityId (nullable Guid), TeamTag, CreatedAt
2. DbContext has `DbSet<TeamNote> TeamNotes`
3. `OnModelCreating` config uses snake_case `HasColumnName()` for ALL properties
4. No EF migration files — raw SQL in startup (just confirm no migration files referenced)

**Program.cs**
5. `CREATE TABLE IF NOT EXISTS team_notes` SQL block present
6. `opportunity_id` column uses `CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL`
7. `AddScoped<TeamNoteService>()` registered

**TeamNoteService.cs**
8. `GetNotesAsync` — date arithmetic done AFTER `.ToListAsync()` (not inside EF `.Select()`) — NOTE: there is no date arithmetic here at all, evaluate whether the concern applies
9. `GetAccountsForDropdownAsync` — returns only non-closed opps, ordered by name
10. `PostNoteAsync` — uses `IDbContextFactory` pattern (constructor injection of `IDbContextFactory<FamOsDbContext>`)

**Notes.razor**
11. `@page "/notes"` + `@attribute [Authorize]` present
12. Compose form: MudTextField for note text, account dropdown, Post button
13. Filter dropdown uses `Value` + `ValueChanged` pattern (NOT `@bind-Value` + `oninput`)
14. Notes list: avatar initials, team tag badge, timestamp, note text
15. Empty state uses `.famos-empty-state` CSS class
16. `PostNoteAsync` handler has try/catch + `finally { _posting = false; }`
17. No `@onclick="() => AsyncMethod()"` without async/await — check the `OnClick="PostNoteAsync"` usage specifically

**NavMenu + CSS**
18. Team Notes nav item added between Accounts and Reports
19. `.famos-note-team-tag`, `.famos-tag-tig`, `.famos-tag-higg` CSS classes present
20. No duplicate `.famos-empty-state` definition (check if famos.css already had this class before this commit)

## Additional Review Concerns
- Is `TeamTag` hardcoded to "TIG" in `PostNoteAsync`? Does that make sense, or should it derive from the author's team?
- Catch block in `PostNoteAsync` swallows exceptions silently — is this intentional/acceptable?
- `_userId` is set once on init; could it be stale in a long-lived session?
- The `GetInitials` method parses email addresses — is it robust enough?

## Output
Produce a structured review report with:
- Pass/fail for each of the 20 checklist items
- Any issues found categorized as: Critical / Important / Nitpick
- Overall verdict: PASS or NEEDS-CHANGES
