namespace FamOs.Web.Data.Entities;

public class TeamNote
{
    public int      Id            { get; set; }
    public string   AuthorId      { get; set; } = "";   // userId (email)
    public string   NoteText      { get; set; } = "";
    public Guid?    OpportunityId { get; set; }          // nullable — note may be unlinked
    public string   TeamTag       { get; set; } = "TIG"; // "TIG" or "Higg" — default TIG
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
}
