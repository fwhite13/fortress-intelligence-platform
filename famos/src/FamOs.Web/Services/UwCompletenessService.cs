using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class UwCompletenessResult
{
    public int   Score            { get; init; }
    public bool  CanRouteToMarket { get; init; }
    public List<string> UnmetItems { get; init; } = new();
    public List<string> MetItems   { get; init; } = new();
}

/// <summary>
/// Computes the underwriting completeness score for an opportunity.
/// Pure function — no DB calls. All data must be loaded (Contacts, Submissions, Quotes).
/// </summary>
public class UwCompletenessService
{
    private record CheckItem(string Description, Func<Opportunity, bool> IsMet, int Weight);

    private static readonly List<CheckItem> Items = new()
    {
        new("Intake questionnaire filled",
            o => !string.IsNullOrEmpty(o.IntakeResponsesJson), 20),

        new("At least one carrier submission created",
            o => o.Submissions.Any(), 15),

        new("All submissions have carrier name and coverage types",
            o => o.Submissions.Any()
                && o.Submissions.All(s =>
                    !string.IsNullOrEmpty(s.CarrierName)
                    && !string.IsNullOrEmpty(s.CoverageTypes)), 10),

        new("At least one quote received",
            o => o.Submissions.Any(s => s.Status == SubmissionStatus.QuoteReceived)
              || o.Quotes.Any(), 20),

        new("Primary contact assigned",
            o => o.Contacts.Any(c => c.ContactType == ContactType.Primary), 15),

        new("Target effective date set",
            o => o.EffectiveDateTarget.HasValue, 10),

        new("Estimated premium set",
            o => o.EstimatedPremium.HasValue, 10),
    };

    public UwCompletenessResult Evaluate(Opportunity opp)
    {
        var metItems   = Items.Where(i => i.IsMet(opp)).ToList();
        var unmetItems = Items.Where(i => !i.IsMet(opp)).ToList();
        var score      = metItems.Sum(i => i.Weight);

        return new UwCompletenessResult
        {
            Score            = Math.Min(score, 100),
            CanRouteToMarket = score >= 60,
            MetItems         = metItems.Select(i => i.Description).ToList(),
            UnmetItems       = unmetItems.Select(i => i.Description).ToList(),
        };
    }
}
