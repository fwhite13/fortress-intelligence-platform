namespace FamOs.Web.Domain;

/// <summary>
/// Predefined tasks auto-generated when an opportunity enters a lifecycle stage.
/// Templates are static — no DB lookup required. All tasks created with DueAt = null
/// (ER sets due dates). Title is the only required field.
/// </summary>
public static class StageTaskTemplates
{
    public static IReadOnlyList<string> ForStage(LifecycleStage stage) => stage switch
    {
        LifecycleStage.UnderwritingPrep => new[]
        {
            "Request 3-year loss runs from client",
            "Obtain signed trucking application (ACORD 193)",
            "Collect driver schedule (MVRs)",
            "Confirm effective date and coverage requirements",
        },

        LifecycleStage.Marketed => new[]
        {
            "Confirm submission receipt with each carrier",
            "Log carrier contact name and reference number",
        },

        LifecycleStage.QuotesReceived => new[]
        {
            "Review all quotes and compare premiums",
            "Select recommended carrier and coverage",
            "Prepare proposal document for client",
        },

        LifecycleStage.ClientDecision => new[]
        {
            "Send proposal to client",
            "Follow up with client on decision (5-day cadence)",
        },

        LifecycleStage.Binding => new[]
        {
            "Submit bind order to carrier",
            "Request binder confirmation",
            "Confirm policy effective date with client",
        },

        LifecycleStage.Bound => new[]
        {
            "Deliver binder/policy to client",
            "Issue certificates of insurance",
            "Update policy record in Epic/AMS",
            "Confirm premium payment arrangement",
        },

        _ => Array.Empty<string>()
    };
}
