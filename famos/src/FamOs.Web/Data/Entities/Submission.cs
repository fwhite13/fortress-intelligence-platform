namespace FamOs.Web.Data.Entities;

/// <summary>
/// Tracks a single carrier submission for an opportunity.
/// One opportunity → many submissions (one per carrier).
/// </summary>
public class Submission
{
    public Guid     Id                { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId     { get; set; }

    // Carrier
    public string   CarrierName       { get; set; } = "";

    /// <summary>
    /// Comma-separated coverage type codes.
    /// E.g. "GL,AUTO,WC" — parsed at render time.
    /// Values: GL, AUTO, WC, UMBRELLA, IM (Inland Marine), OTHER
    /// </summary>
    public string?  CoverageTypes     { get; set; }

    // Status
    public SubmissionStatus Status    { get; set; } = SubmissionStatus.Pending;
    public DateTime? SubmittedAt      { get; set; }
    public DateTime? RespondedAt      { get; set; }

    // Quote result from scraper
    /// <summary>Raw JSON returned by the Fortress quote scraper API. Nullable until scrape completes.</summary>
    public string?  QuoteResultJson   { get; set; }

    /// <summary>Fortress API project request ID — set immediately when POST /requests returns. Null until upload starts.</summary>
    public string? FortressRequestId { get; set; }

    /// <summary>Error message if Status=Error.</summary>
    public string? ScraperError { get; set; }

    public string?  Notes             { get; set; }

    // Audit
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt         { get; set; } = DateTime.UtcNow;

    // Navigation
    public Opportunity Opportunity    { get; set; } = default!;
    public List<Quote> Quotes         { get; set; } = new();
}

public enum SubmissionStatus
{
    Pending        = 0,
    Sent           = 1,
    QuoteReceived  = 2,
    Declined       = 3,
    Bound          = 4,
    Uploading      = 5,   // PDF selected, upload in progress
    Processing     = 6,   // projectRequestId obtained, polling Fortress API
    Error          = 7    // failed at any stage after submission was created
}
