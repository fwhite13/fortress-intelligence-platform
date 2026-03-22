using FamOs.Web.Data.Entities;

namespace FamOs.Web.Components.Pages.QuoteCompare;

public class RequirementGroup
{
    public string Name { get; set; } = "";
    public List<Requirement> Requirements { get; set; } = new();
}

public record QuoteSelectionArgs(string LineSlug, Guid QuoteId, string Package);

public record CarrierNoteArgs(Guid QuoteId, string NoteText);
