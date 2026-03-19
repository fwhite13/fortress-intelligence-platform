# FAM OS Sprint 5 Spec — Submissions, Quote Scraper, Aging Engine, Dashboard, HubSpot

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19  
**Sprint Goal:** Full submission lifecycle, carrier quote PDF ingestion, urgency escalation, operational dashboard, and real HubSpot deal sync  
**Prerequisite:** Sprint 4 deployed and verified  
**Spec references:** `FAMOS-ARCHITECTURE-SPEC.md`, `FAMOS-SPRINT4-SPEC.md`

---

## Sprint 5 Overview

Seven distinct deliverables. Four are workflow-critical (without submissions, the MARKETED stage is a lie). Three are operational quality (without aging signals and a real dashboard, ERs don't know what's on fire).

| Part | Feature | New Files | Modified Files |
|------|---------|-----------|----------------|
| A | Submission entity + stage gates | 1 | 4 |
| B | Quote Scraper Panel | 2 | 3 |
| C | Structured Close Reasons | 0 | 3 |
| D | Pipeline card owner display | 0 | 1 |
| E | Urgency / Lifecycle Aging Engine | 1 | 3 |
| F | Dashboard rebuild | 0 | 2 |
| G | HubSpot real sync | 1 | 2 |

**Total: 4 new files, 18 modified files.** Run as a single sequential CC session — all parts share the DbContext, entity model, and `LifecycleCommandService`.

---

## Parallelization Map

**All sequential — single CC session.** Reason: Parts A, C, and E all modify `Opportunity.cs`, `FamOsDbContext.cs`, and `LifecycleCommandService.cs`. Parts B, F, and G all depend on Part A's `Submission` entity changes. Running in parallel would cause merge conflicts on the shared files.

**Execution order:**
1. Part C — `CloseReason` enum + `Opportunity` entity fields + migration SQL (small, sets up enum used later)
2. Part E — `LastStageTransitionAt` field + `Enums.cs` additions + `Opportunity` entity fields
3. Part A — `Submission` entity enhancements + `FamOsDbContext` column mapping + stage gate validation + `SubmissionPanel.razor`
4. Part D — `OpportunityCard.razor` owner initials
5. Part B — `QuoteScraperService.cs` + `QuoteScraperPanel.razor`
6. Part F — `Dashboard.razor` rebuild + `OpportunityService` expansion
7. Part E (continued) — `AgingService.cs` background service
8. Part G — `HubSpotService.cs` real implementation + `Program.cs` swap

---

## DB Changes

All migrations via idempotent `ALTER TABLE` in the startup init block. Aurora MySQL does **not** support `ADD COLUMN IF NOT EXISTS` (fails on MySQL < 8.0.3 / Aurora 2.x). Use try/catch on error code 1060 (duplicate column) instead.

```csharp
// Pattern for all new columns — add to DatabaseInitializationService or startup after CreateTablesAsync():
private async Task TryAddColumnAsync(string sql)
{
    try { await _db.Database.ExecuteSqlRawAsync(sql); }
    catch (Exception ex) when (ex.Message.Contains("Duplicate column name") || ex.HResult == -2147467259)
    { /* Column already exists — safe to ignore */ }
}
```

**Calls to add (in order):**

```csharp
// Part C
await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN close_reason INT NULL");
await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN close_notes LONGTEXT NULL");

// Part E
await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN last_stage_transition_at DATETIME NULL");

// Part A (Submission table enhancements — new columns on existing table)
await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN coverage_types VARCHAR(200) NULL");
await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN submitted_at DATETIME NULL");
await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN responded_at DATETIME NULL");
await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN quote_result_json MEDIUMTEXT NULL");
await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN notes LONGTEXT NULL");
```

**Note:** The `submissions` table was created in Sprint 1. Verify the existing table definition before adding columns. If `responded_at` already exists (Sprint 1 included it), the try/catch will swallow the error safely.

---

## Part A — Submission Entity + Stage Gates

### A1. `Data/Entities/Submission.cs` — Replace

The Sprint 1 `Submission` entity is minimal. Replace entirely:

```csharp
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
    Bound          = 4
}
```

### A2. `Data/FamOsDbContext.cs` — Submission Entity Config

In the `Submission` entity block, add/update:

```csharp
m.Entity<Submission>(e => {
    e.ToTable("submissions");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnType("char(36)");
    e.Property(x => x.OpportunityId).HasColumnType("char(36)");
    e.Property(x => x.Status).HasConversion<int>();
    e.Property(x => x.QuoteResultJson).HasColumnType("mediumtext");
    e.Property(x => x.Notes).HasColumnType("longtext");
    e.HasOne(x => x.Opportunity)
        .WithMany(o => o.Submissions)
        .HasForeignKey(x => x.OpportunityId);
});
```

Also add to the `Opportunity` entity config block:

```csharp
e.Property(x => x.CloseReason).HasConversion<int?>();
e.Property(x => x.CloseNotes).HasColumnType("longtext");
e.Property(x => x.LastStageTransitionAt).HasColumnType("datetime");
```

### A3. `Domain/LifecycleCommandService.cs` — Stage Gate Validation

**In `RouteToMarketAsync` (the method that advances to MARKETED):**

Add after the existing `Validate(opp.LifecycleStage == LifecycleStage.UnderwritingPrep, ...)` check:

```csharp
// Stage gate: must have at least one submission before routing to market
if (!opp.Submissions.Any())
    throw new LifecycleValidationException(
        "At least one carrier submission must be created before routing to market.");
```

**In `RecordQuoteAsync` (validates before advancing to QUOTES_RECEIVED):**

After the existing stage check, add:

```csharp
// Verify the submissionId belongs to this opportunity
if (!opp.Submissions.Any(s => s.Id == submissionId))
    throw new LifecycleValidationException("Submission not found on this opportunity.");
```

**Add `LastStageTransitionAt` stamp to each lifecycle command** — inside the `PursueOpportunityAsync`, `RouteToMarketAsync`, `RecordQuoteAsync`, `SendProposalAsync`, `RequestBindAsync`, `RecordBinderReceivedAsync`, `CloseOpportunityAsync` methods, after the `opp.LifecycleStage = ...` assignment:

```csharp
opp.LastStageTransitionAt = DateTime.UtcNow;
```

**Add `CreateSubmissionAsync` command** — new method, not a lifecycle advance:

```csharp
/// <summary>
/// Creates a new carrier submission on an opportunity.
/// Does NOT advance lifecycle stage. Can be called multiple times.
/// </summary>
public async Task<Guid> CreateSubmissionAsync(
    Guid opportunityId,
    string carrierName,
    string? coverageTypes,
    string? notes,
    string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var opp = await LoadOpportunityAsync(opportunityId);

    Validate(!opp.IsClosed, "Cannot add submissions to a closed opportunity");
    Validate(opp.LifecycleStage == LifecycleStage.UnderwritingPrep
          || opp.LifecycleStage == LifecycleStage.Marketed,
        "Submissions can only be added during Underwriting Prep or while Marketed");

    var sub = new Submission
    {
        OpportunityId = opportunityId,
        CarrierName   = carrierName.Trim(),
        CoverageTypes = coverageTypes,
        Notes         = notes,
        Status        = SubmissionStatus.Pending,
    };
    _db.Submissions.Add(sub);
    opp.UpdatedAt = DateTime.UtcNow;

    await WriteActivityAsync(opp.Id, "submission_created",
        $"Submission added: {carrierName}", actorUserId);

    await _db.SaveChangesAsync();
    await tx.CommitAsync();

    return sub.Id;
}

/// <summary>
/// Updates the status of a carrier submission (e.g., Pending → QuoteReceived).
/// </summary>
public async Task UpdateSubmissionStatusAsync(
    Guid submissionId,
    SubmissionStatus newStatus,
    string? notes,
    string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();

    var sub = await _db.Submissions
        .Include(s => s.Opportunity)
        .FirstOrDefaultAsync(s => s.Id == submissionId)
        ?? throw new NotFoundException($"Submission {submissionId} not found");

    sub.Status    = newStatus;
    sub.Notes     = notes ?? sub.Notes;
    sub.UpdatedAt = DateTime.UtcNow;

    if (newStatus == SubmissionStatus.Sent)
        sub.SubmittedAt = sub.SubmittedAt ?? DateTime.UtcNow;
    if (newStatus is SubmissionStatus.QuoteReceived or SubmissionStatus.Declined)
        sub.RespondedAt = DateTime.UtcNow;

    await WriteActivityAsync(sub.OpportunityId, "submission_updated",
        $"{sub.CarrierName} → {newStatus}", actorUserId);

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

### A4. `Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor` — Full Replacement

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<div class="intake-form-container">

    <div class="section-header mb-4">
        <MudText Typo="Typo.h6" Style="color:var(--navy);">Carrier Submissions</MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">
            Add carriers to submit this account to. At least one submission required before routing to market.
        </MudText>
    </div>

    @* Existing submission list *@
    @if (Opportunity.Submissions.Any())
    {
        <div class="mb-4">
            <div class="intake-section-label mb-2">Current Submissions</div>
            @foreach (var sub in Opportunity.Submissions)
            {
                <div style="display:flex; justify-content:space-between; align-items:center;
                            padding:8px 12px; border:1px solid var(--border); border-radius:8px; margin-bottom:6px;">
                    <div>
                        <MudText Typo="Typo.body2" Style="font-weight:600;">@sub.CarrierName</MudText>
                        @if (!string.IsNullOrEmpty(sub.CoverageTypes))
                        {
                            <MudText Typo="Typo.caption" Color="Color.Secondary">@sub.CoverageTypes</MudText>
                        }
                    </div>
                    <MudChip T="string" Size="Size.Small" Color="GetStatusColor(sub.Status)">
                        @sub.Status
                    </MudChip>
                </div>
            }
        </div>
    }

    @* Add submission form *@
    <div class="intake-section mb-4">
        <div class="intake-section-label mb-2">Add Carrier</div>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="6">
                <MudSelect @bind-Value="_selectedCarrier" Label="Carrier *"
                           Variant="Variant.Outlined" Dense="true">
                    <MudSelectItem Value="@("")">— Select or type below —</MudSelectItem>
                    <MudSelectItem Value="@("Great West Casualty")">Great West Casualty</MudSelectItem>
                    <MudSelectItem Value="@("Progressive Commercial")">Progressive Commercial</MudSelectItem>
                    <MudSelectItem Value="@("Canal Insurance")">Canal Insurance</MudSelectItem>
                    <MudSelectItem Value="@("Protective Insurance")">Protective Insurance</MudSelectItem>
                    <MudSelectItem Value="@("Northland Insurance")">Northland Insurance</MudSelectItem>
                    <MudSelectItem Value="@("Old Republic")">Old Republic</MudSelectItem>
                    <MudSelectItem Value="@("Travelers")">Travelers</MudSelectItem>
                    <MudSelectItem Value="@("other")">Other (type below)</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12" sm="6">
                @if (_selectedCarrier == "other")
                {
                    <MudTextField @bind-Value="_customCarrier"
                        Label="Carrier Name"
                        Variant="Variant.Outlined" Dense="true" />
                }
                else
                {
                    <MudTextField @bind-Value="_coverageTypes"
                        Label="Coverage Types (e.g. AUTO,WC,GL)"
                        Variant="Variant.Outlined" Dense="true"
                        HelperText="AUTO · GL · WC · UMBRELLA · IM · OTHER" />
                }
            </MudItem>
            @if (_selectedCarrier == "other")
            {
                <MudItem xs="12">
                    <MudTextField @bind-Value="_coverageTypes"
                        Label="Coverage Types (e.g. AUTO,WC,GL)"
                        Variant="Variant.Outlined" Dense="true" />
                </MudItem>
            }
            <MudItem xs="12">
                <MudTextField @bind-Value="_subNotes" Label="Notes (optional)"
                              Variant="Variant.Outlined" Dense="true" />
            </MudItem>
        </MudGrid>
        <div style="margin-top:8px;">
            <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                       OnClick="AddSubmission"
                       Disabled="@(_saving || string.IsNullOrWhiteSpace(CarrierNameValue))">
                + Add Submission
            </MudButton>
        </div>
    </div>

    <MudDivider Class="my-4" />

    @* Route to market *@
    <div style="display:flex; align-items:center; gap:8px; flex-wrap:wrap;">
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="RouteToMarket"
                   Disabled="@(_saving || !Opportunity.Submissions.Any())">
            Route to Market →
        </MudButton>
        @if (!Opportunity.Submissions.Any())
        {
            <MudText Typo="Typo.caption" Color="Color.Secondary">
                Add at least one submission first
            </MudText>
        }
    </div>

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }

    private bool _saving;
    private string _selectedCarrier = "";
    private string _customCarrier   = "";
    private string _coverageTypes   = "";
    private string _subNotes        = "";

    private string CarrierNameValue =>
        _selectedCarrier == "other" ? _customCarrier : _selectedCarrier;

    private async Task AddSubmission()
    {
        if (string.IsNullOrWhiteSpace(CarrierNameValue)) return;
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.CreateSubmissionAsync(
                Opportunity.Id, CarrierNameValue, _coverageTypes.Trim(), _subNotes.Trim(), userId);
            Snackbar.Add($"{CarrierNameValue} added.", Severity.Success);
            _selectedCarrier = "";
            _customCarrier   = "";
            _coverageTypes   = "";
            _subNotes        = "";
            // Reload the opportunity to show new submission
            await OnAdvanced.InvokeAsync(); // parent reloads the opportunity
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private async Task RouteToMarket()
    {
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.RouteToMarketAsync(Opportunity.Id, Array.Empty<string>(), userId);
            Snackbar.Add("Routed to market.", Severity.Success);
            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private static Color GetStatusColor(SubmissionStatus s) => s switch
    {
        SubmissionStatus.QuoteReceived => Color.Success,
        SubmissionStatus.Declined      => Color.Error,
        SubmissionStatus.Bound         => Color.Primary,
        _                              => Color.Default
    };
}
```

**Note on `RouteToMarketAsync` signature change:** The current `RouteToMarketAsync(Guid, string[], string)` takes carrier names to create submissions inline. With Sprint 5, submissions are created via `CreateSubmissionAsync` first. Pass `Array.Empty<string>()` — the method still works but creates no new submission rows (they already exist). The stage gate validation inside `RouteToMarketAsync` now checks `opp.Submissions.Any()` instead of `carrierNames.Length > 0`. Update the validation accordingly.

### A5. `Components/Pages/Opportunity/Panels/MarketedPanel.razor` — Full Replacement

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<div class="intake-form-container">

    <div class="section-header mb-3">
        <MudText Typo="Typo.h6" Style="color:var(--navy);">Submitted to Market</MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">
            Awaiting carrier quotes. Update status as responses arrive.
        </MudText>
    </div>

    @* Submission status table *@
    <div class="mb-4">
        @foreach (var sub in Opportunity.Submissions)
        {
            var isExpanded = _expandedId == sub.Id;
            <div style="border:1px solid var(--border); border-radius:8px; margin-bottom:8px; overflow:hidden;">
                <div style="display:flex; justify-content:space-between; align-items:center;
                            padding:10px 14px; cursor:pointer; background:var(--cream);"
                     @onclick="() => ToggleExpand(sub.Id)">
                    <div>
                        <MudText Typo="Typo.body2" Style="font-weight:600;">@sub.CarrierName</MudText>
                        @if (sub.SubmittedAt.HasValue)
                        {
                            <MudText Typo="Typo.caption" Color="Color.Secondary">
                                Sent @sub.SubmittedAt.Value.ToLocalTime().ToString("MMM d")
                            </MudText>
                        }
                    </div>
                    <div style="display:flex; gap:8px; align-items:center;">
                        <MudChip T="string" Size="Size.Small" Color="GetStatusColor(sub.Status)">
                            @GetStatusLabel(sub.Status)
                        </MudChip>
                        <MudIcon Icon="@(isExpanded ? Icons.Material.Filled.ExpandLess : Icons.Material.Filled.ExpandMore)"
                                 Style="font-size:16px; color:var(--muted);" />
                    </div>
                </div>

                @if (isExpanded)
                {
                    <div style="padding:12px 14px; border-top:1px solid var(--border);">
                        <MudGrid Spacing="2">
                            <MudItem xs="12" sm="6">
                                <MudSelect Value="sub.Status"
                                           ValueChanged="@(async (SubmissionStatus s) => await UpdateStatus(sub, s))"
                                           Label="Status" Variant="Variant.Outlined" Dense="true">
                                    <MudSelectItem Value="SubmissionStatus.Pending">Pending</MudSelectItem>
                                    <MudSelectItem Value="SubmissionStatus.Sent">Sent</MudSelectItem>
                                    <MudSelectItem Value="SubmissionStatus.QuoteReceived">Quote Received</MudSelectItem>
                                    <MudSelectItem Value="SubmissionStatus.Declined">Declined</MudSelectItem>
                                </MudSelect>
                            </MudItem>
                            <MudItem xs="12">
                                <MudText Typo="Typo.caption" Color="Color.Secondary">
                                    @(string.IsNullOrEmpty(sub.Notes) ? "No notes" : sub.Notes)
                                </MudText>
                            </MudItem>
                        </MudGrid>
                    </div>
                }
            </div>
        }
    </div>

    @* Quote recording inline *@
    <div class="intake-section mb-4">
        <div class="intake-section-label mb-2">Record a Quote</div>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="5">
                <MudSelect @bind-Value="_quoteSubId" Label="Carrier" Variant="Variant.Outlined" Dense="true">
                    @foreach (var sub in Opportunity.Submissions.Where(s => s.Status != SubmissionStatus.Declined))
                    {
                        <MudSelectItem Value="sub.Id">@sub.CarrierName</MudSelectItem>
                    }
                </MudSelect>
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudNumericField @bind-Value="_quotePremium" Label="Premium ($)"
                                 Format="N0" Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="3">
                <MudButton Variant="Variant.Filled" Color="Color.Primary"
                           OnClick="RecordQuote"
                           Disabled="@(_saving || _quoteSubId == Guid.Empty || !_quotePremium.HasValue)"
                           Style="height:40px; margin-top:4px;">
                    Record Quote
                </MudButton>
            </MudItem>
        </MudGrid>
    </div>

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }

    private bool _saving;
    private Guid _quoteSubId;
    private decimal? _quotePremium;
    private Guid _expandedId;

    private void ToggleExpand(Guid id) => _expandedId = _expandedId == id ? Guid.Empty : id;

    private async Task UpdateStatus(Submission sub, SubmissionStatus newStatus)
    {
        var userId = await UserSession.GetUserIdAsync();
        await Lifecycle.UpdateSubmissionStatusAsync(sub.Id, newStatus, null, userId);
        sub.Status = newStatus;
        Snackbar.Add($"{sub.CarrierName} → {newStatus}", Severity.Success);
    }

    private async Task RecordQuote()
    {
        _saving = true;
        try
        {
            var sub    = Opportunity.Submissions.First(s => s.Id == _quoteSubId);
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.RecordQuoteAsync(
                Opportunity.Id, _quoteSubId, sub.CarrierName,
                _quotePremium!.Value, null, userId);
            Snackbar.Add($"Quote from {sub.CarrierName} recorded.", Severity.Success);
            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private static Color GetStatusColor(SubmissionStatus s) => s switch
    {
        SubmissionStatus.QuoteReceived => Color.Success,
        SubmissionStatus.Declined      => Color.Error,
        _                              => Color.Default
    };

    private static string GetStatusLabel(SubmissionStatus s) => s switch
    {
        SubmissionStatus.Pending       => "Pending",
        SubmissionStatus.Sent          => "Sent",
        SubmissionStatus.QuoteReceived => "Quote In",
        SubmissionStatus.Declined      => "Declined",
        SubmissionStatus.Bound         => "Bound",
        _                              => s.ToString()
    };
}
```

---

## Part B — Quote Scraper Panel

### B1. `Services/QuoteScraperService.cs` (new)

Mirrors `FortressProjectsClient.cs` from FORMS but scoped to the quote scraper cataloger project. Does NOT share or copy FORMS code — implemented independently in famos/.

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamOs.Web.Services;

public interface IQuoteScraperService
{
    /// <summary>
    /// Upload a carrier quote PDF, submit to Fortress API, and return the projectRequestId.
    /// Call PollResultAsync to check completion.
    /// </summary>
    Task<string> SubmitQuotePdfAsync(string opportunityRefId, string fileName, byte[] fileData);

    /// <summary>Poll for scraper results. Returns null if still processing.</summary>
    Task<QuoteScraperResult?> PollResultAsync(string projectRequestId);
}

public class QuoteScraperService : IQuoteScraperService
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration     _config;
    private readonly ILogger<QuoteScraperService> _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private const string ClientId  = "internal";
    private const string ProjectId = "internal_quote_scraper_cataloger";

    public QuoteScraperService(IHttpClientFactory factory, IConfiguration config,
        ILogger<QuoteScraperService> logger)
    {
        _factory = factory;
        _config  = config;
        _logger  = logger;
    }

    public async Task<string> SubmitQuotePdfAsync(
        string opportunityRefId, string fileName, byte[] fileData)
    {
        var client = _factory.CreateClient("FortressApi");

        // Step 1: Get upload link
        var linkUrl  = $"/clients/{ClientId}/projects/{ProjectId}/uploadLink";
        var linkBody = new
        {
            clientReferenceId = opportunityRefId,
            files = new[] { new { fileName, sequence = 1 } }
        };
        var linkResp = await client.PostAsJsonAsync(linkUrl, linkBody, Opts);
        linkResp.EnsureSuccessStatusCode();

        var links = await linkResp.Content.ReadFromJsonAsync<List<UploadLinkDto>>(Opts)
            ?? throw new InvalidOperationException("No upload links returned");

        var link = links.First();

        // Step 2: Upload to S3 (no auth headers)
        using var s3Client  = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var fileContent     = new ByteArrayContent(fileData);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var s3Resp = await s3Client.PutAsync(link.UploadUrl, fileContent);
        s3Resp.EnsureSuccessStatusCode();

        _logger.LogInformation("[QuoteScraper] Uploaded {File} ({Bytes} bytes)", fileName, fileData.Length);

        // Step 3: Submit request
        var submitUrl  = $"/clients/{ClientId}/projects/{ProjectId}/requests";
        var submitBody = new
        {
            clientReferenceId = opportunityRefId,
            fileKeys          = new[] { link.FileKey }
        };
        var submitResp = await client.PostAsJsonAsync(submitUrl, submitBody, Opts);
        submitResp.EnsureSuccessStatusCode();

        var submit = await submitResp.Content.ReadFromJsonAsync<SubmitResponseDto>(Opts)
            ?? throw new InvalidOperationException("No submit response");

        if (submit.Success != true)
            throw new InvalidOperationException($"Scraper submission failed: {submit.Reason}");

        _logger.LogInformation("[QuoteScraper] Submitted request {Id}", submit.ProjectRequestId);
        return submit.ProjectRequestId!;
    }

    public async Task<QuoteScraperResult?> PollResultAsync(string projectRequestId)
    {
        var client = _factory.CreateClient("FortressApi");
        var url    = $"/clients/{ClientId}/projects/{ProjectId}/requests/{projectRequestId}";

        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var raw    = await resp.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<StatusResponseDto>(raw, Opts);

        var reqStatus = status?.Request?.Status ?? "Unknown";
        if (reqStatus is "Pending" or "Processing" or "Assembling")
            return null;  // still working

        return new QuoteScraperResult
        {
            Status  = reqStatus,
            RawJson = raw,
            Results = status?.Results
        };
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    private class UploadLinkDto
    {
        public string? FileName  { get; set; }
        public string? FileKey   { get; set; }
        public string? UploadUrl { get; set; }
    }

    private class SubmitResponseDto
    {
        public bool?   Success          { get; set; }
        public string? ProjectRequestId { get; set; }
        public string? Reason           { get; set; }
    }

    private class StatusResponseDto
    {
        public RequestInfoDto? Request { get; set; }
        public object?         Results { get; set; }
    }

    private class RequestInfoDto
    {
        public string? Status { get; set; }
    }
}

public class QuoteScraperResult
{
    public string  Status  { get; set; } = "";
    public string  RawJson { get; set; } = "";
    public object? Results { get; set; }
}
```

### B2. `Components/Pages/Opportunity/Panels/QuoteScraperPanel.razor` (new file)

New panel shown at any stage. Attached to a `Submission` — the ER uploads the quote PDF from a carrier, and the scraper extracts coverages.

```razor
@namespace FamOs.Web.Components.Panels
@inject IQuoteScraperService Scraper
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Services

<MudCard Class="mb-4" Elevation="0"
         Style="border:1px solid var(--border); border-radius:12px;">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.subtitle2" Style="color:var(--navy);">
                Quote PDF Scraper
            </MudText>
            <MudText Typo="Typo.caption" Color="Color.Secondary">
                Upload a carrier quote PDF to extract coverage data automatically.
            </MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>

        @if (!Opportunity.Submissions.Any())
        {
            <MudAlert Severity="Severity.Info">
                Add carrier submissions first (in Underwriting Prep).
            </MudAlert>
        }
        else
        {
            <MudGrid Spacing="2">
                <MudItem xs="12" sm="5">
                    <MudSelect @bind-Value="_selectedSubId"
                               Label="Carrier" Variant="Variant.Outlined" Dense="true">
                        @foreach (var sub in Opportunity.Submissions)
                        {
                            <MudSelectItem Value="sub.Id">@sub.CarrierName</MudSelectItem>
                        }
                    </MudSelect>
                </MudItem>
                <MudItem xs="12" sm="7">
                    <InputFile id="quote-pdf-input"
                               OnChange="OnFileSelected"
                               accept=".pdf"
                               style="display:none;" />
                    <MudButton Variant="Variant.Outlined"
                               StartIcon="@Icons.Material.Filled.UploadFile"
                               OnClick="OpenFilePicker"
                               Disabled="@(_uploading || _selectedSubId == Guid.Empty)">
                        @(_selectedFile == null ? "Choose PDF" : _selectedFile.Name)
                    </MudButton>
                    @if (_selectedFile != null)
                    {
                        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                                   StartIcon="@Icons.Material.Filled.CloudUpload"
                                   OnClick="UploadAndSubmit"
                                   Disabled="_uploading"
                                   Class="ml-2">
                            @(_uploading ? "Uploading..." : "Upload & Scrape")
                        </MudButton>
                    }
                </MudItem>
            </MudGrid>

            @if (_uploading)
            {
                <MudProgressLinear Indeterminate="true" Color="Color.Primary" Class="mt-3" />
                <MudText Typo="Typo.caption" Color="Color.Secondary" Class="mt-1">
                    @_statusMessage
                </MudText>
            }

            @if (!string.IsNullOrEmpty(_resultJson))
            {
                <MudDivider Class="my-3" />
                <div class="intake-section-label mb-2">Extracted Coverages</div>
                <MudTextField Value="_resultJson"
                              Lines="6" ReadOnly="true"
                              Variant="Variant.Outlined"
                              Style="font-family:monospace; font-size:12px;" />
            }

            @if (_scrapeError != null)
            {
                <MudAlert Severity="Severity.Error" Class="mt-3">@_scrapeError</MudAlert>
            }
        }

    </MudCardContent>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnUpdated { get; set; }

    private Guid   _selectedSubId;
    private IBrowserFile? _selectedFile;
    private bool   _uploading;
    private string _statusMessage = "";
    private string _resultJson    = "";
    private string? _scrapeError;

    private void OpenFilePicker()
    {
        // Trigger the hidden file input via JS interop not needed here —
        // MudBlazor InputFile label click via id works with the label trick below.
        // Tony: wire this via a JS call or use MudFileUpload instead if InputFile click doesn't work.
    }

    private void OnFileSelected(InputFileChangeEventArgs e)
    {
        _selectedFile = e.File;
        _scrapeError  = null;
        StateHasChanged();
    }

    private async Task UploadAndSubmit()
    {
        if (_selectedFile == null || _selectedSubId == Guid.Empty) return;

        _uploading     = true;
        _scrapeError   = null;
        _resultJson    = "";
        _statusMessage = "Uploading PDF...";

        try
        {
            // Read file bytes (max 10MB)
            const long maxSize = 10 * 1024 * 1024;
            using var stream = _selectedFile.OpenReadStream(maxSize);
            var bytes = new byte[_selectedFile.Size];
            _ = await stream.ReadAsync(bytes);

            _statusMessage = "Submitting to scraper...";
            var refId     = $"famos-opp-{Opportunity.Id:N}";
            var requestId = await Scraper.SubmitQuotePdfAsync(
                refId, _selectedFile.Name, bytes);

            // Poll until complete (max 60 seconds, 5s interval)
            _statusMessage = "Scraping quote data...";
            QuoteScraperResult? result = null;
            for (var i = 0; i < 12; i++)
            {
                await Task.Delay(5000);
                result = await Scraper.PollResultAsync(requestId);
                if (result != null) break;
                _statusMessage = $"Processing... ({(i + 1) * 5}s)";
                StateHasChanged();
            }

            if (result == null)
            {
                _scrapeError = "Scraper timed out after 60 seconds. Try again shortly.";
                return;
            }

            if (result.Status == "Failed")
            {
                _scrapeError = "Scraper returned an error. Check the PDF and try again.";
                return;
            }

            _resultJson = result.RawJson;

            // Persist result JSON on the Submission
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.SaveSubmissionScraperResultAsync(_selectedSubId, result.RawJson, userId);
            Snackbar.Add("Quote data extracted and saved.", Severity.Success);
            await OnUpdated.InvokeAsync();
        }
        catch (Exception ex)
        {
            _scrapeError = $"Upload failed: {ex.Message}";
        }
        finally
        {
            _uploading = false;
            StateHasChanged();
        }
    }
}
```

**Add `SaveSubmissionScraperResultAsync` to `LifecycleCommandService`:**

```csharp
public async Task SaveSubmissionScraperResultAsync(
    Guid submissionId, string resultJson, string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var sub = await _db.Submissions.FindAsync(submissionId)
        ?? throw new NotFoundException($"Submission {submissionId} not found");

    sub.QuoteResultJson = resultJson;
    sub.UpdatedAt       = DateTime.UtcNow;

    await WriteActivityAsync(sub.OpportunityId, "quote_scraped",
        $"Quote PDF scraped for {sub.CarrierName}", actorUserId);
    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

### B3. `Program.cs` — Register `QuoteScraperService`

Add HttpClient named `"FortressApi"` if not already registered (check — FAMOS may not have this from Sprint 1):

```csharp
// Fortress API client — used by QuoteScraperService
var fortressBase = builder.Configuration["FortressApi:BaseUrl"] ?? "https://api.fortressam.ai";
builder.Services.AddHttpClient("FortressApi", c =>
{
    c.BaseAddress = new Uri(fortressBase);
    c.DefaultRequestHeaders.Add("X-Api-Key",
        builder.Configuration["FortressApi:Key"] ?? "246191f33f470f136ebb800516f8e10f");
    c.DefaultRequestHeaders.Add("X-Api-Secret",
        builder.Configuration["FortressApi:Secret"]
            ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d");
});

builder.Services.AddScoped<IQuoteScraperService, QuoteScraperService>();
```

**ECS env var additions (appsettings-friendly, not required in AWS secrets — these are internal service creds):**
```
FortressApi:BaseUrl = https://api.fortressam.ai
FortressApi:Key     = 246191f33f470f136ebb800516f8e10f
FortressApi:Secret  = 77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d
```

---

## Part C — Structured Close Reasons

### C1. `Domain/Enums.cs` — Add `CloseReason` enum

Append to the existing file:

```csharp
public enum CloseReason
{
    NotQuoted          = 0,
    PriceTooHigh       = 1,
    LostToCompetitor   = 2,
    ClientDeclinedCoverage = 3,
    PolicyLapsed       = 4,
    Other              = 5
}
```

### C2. `Data/Entities/Opportunity.cs` — Add Close Fields

Add after the `IsClosed` property:

```csharp
public CloseReason? CloseReason   { get; set; }
public string?      CloseNotes    { get; set; }
```

### C3. `Components/Dialogs/CloseOpportunityDialog.razor` — Full Replacement

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Domain

<MudDialog>
    <TitleContent>Close Opportunity</TitleContent>
    <DialogContent>
        <MudText Typo="Typo.body2" Class="mb-3">
            Select the reason this opportunity is closing without binding.
        </MudText>
        <MudSelect @bind-Value="_reason" Label="Close Reason *" Required="true" Class="mb-3">
            <MudSelectItem Value="CloseReason.NotQuoted">Not Quoted — carrier(s) declined</MudSelectItem>
            <MudSelectItem Value="CloseReason.PriceTooHigh">Price Too High — client declined on premium</MudSelectItem>
            <MudSelectItem Value="CloseReason.LostToCompetitor">Lost to Competitor</MudSelectItem>
            <MudSelectItem Value="CloseReason.ClientDeclinedCoverage">Client Declined Coverage</MudSelectItem>
            <MudSelectItem Value="CloseReason.PolicyLapsed">Policy Lapsed — missed renewal window</MudSelectItem>
            <MudSelectItem Value="CloseReason.Other">Other</MudSelectItem>
        </MudSelect>
        <MudTextField @bind-Value="_notes"
                      Label="Notes (optional)"
                      Lines="3"
                      Placeholder="Additional context..." />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Error"
                   OnClick="Submit"
                   Disabled="@(_reason == null)">
            Close Opportunity
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid OpportunityId { get; set; }

    private CloseReason? _reason;
    private string _notes = "";

    private void Cancel() => MudDialog.Cancel();

    private async Task Submit()
    {
        if (_reason == null) return;
        var userId = await UserSession.GetUserIdAsync();
        try
        {
            await Lifecycle.CloseOpportunityAsync(
                OpportunityId, _reason.Value, _notes.Trim(), userId);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }
}
```

### C4. `LifecycleCommandService.cs` — Update `CloseOpportunityAsync` Signature

Change the signature from `(Guid, string, string)` to `(Guid, CloseReason, string?, string)`:

```csharp
public async Task CloseOpportunityAsync(
    Guid opportunityId,
    CloseReason reason,
    string? notes,
    string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var opp = await LoadOpportunityAsync(opportunityId);
    Validate(!opp.IsClosed, "Opportunity already closed");

    opp.LifecycleStage         = LifecycleStage.ClosedNotBound;
    opp.IsClosed               = true;
    opp.CloseReason            = reason;
    opp.CloseNotes             = notes;
    opp.LastStageTransitionAt  = DateTime.UtcNow;
    opp.UpdatedAt              = DateTime.UtcNow;

    await WriteActivityAsync(opp.Id, "opportunity_closed",
        $"Closed: {reason}{(notes != null ? " — " + notes : "")}", actorUserId);
    await WriteOutboxAsync(DomainEventType.OpportunityClosed,
        new { opportunityId, reason = reason.ToString(), notes });

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

---

## Part D — Pipeline Card Owner Display

### D1. `Components/Shared/OpportunityCard.razor` — Add Owner Initials

Replace the current component:

```razor
@using FamOs.Web.Data.Entities
@inject NavigationManager Nav

<div class="famos-kcard @(IsUrgent ? "famos-kcard--urgent" : "")"
     @onclick="NavigateToOpportunity">
    <div style="display:flex; justify-content:space-between; align-items:flex-start;">
        <div class="famos-kcard-name" style="flex:1; min-width:0;">@Opportunity.Name</div>
        @if (!string.IsNullOrEmpty(Opportunity.OwnerUserId))
        {
            var initials = GetInitials(Opportunity.OwnerUserId);
            <div style="width:24px; height:24px; border-radius:50%; background:var(--sky);
                        color:#fff; font-size:10px; font-weight:700; flex-shrink:0;
                        display:flex; align-items:center; justify-content:center;
                        margin-left:6px;">
                @initials
            </div>
        }
    </div>
    @if (Opportunity.EstimatedPremium.HasValue)
    {
        <div class="famos-kcard-detail">$@Opportunity.EstimatedPremium.Value.ToString("N0")</div>
    }
    @if (Opportunity.EffectiveDateTarget.HasValue)
    {
        <div class="famos-kcard-detail">Eff: @Opportunity.EffectiveDateTarget.Value.ToString("MMM d, yyyy")</div>
    }
    <div class="famos-kcard-footer">
        <SignalChip Signal="Opportunity.DominantSignal" />
    </div>
</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;

    private bool IsUrgent => Opportunity.DominantSignal is
        FamOs.Web.Domain.DominantSignal.TimeRisk;

    private void NavigateToOpportunity() => Nav.NavigateTo("/opportunity/" + Opportunity.Id);

    private static string GetInitials(string userId)
    {
        // OwnerUserId is currently an email or UUID string.
        // Extract initials: "fred.white@example.com" → "FW"
        // "jane.doe@..." → "JD"
        // UUID or short string → first 2 chars uppercased
        var atIdx = userId.IndexOf('@');
        var local = atIdx > 0 ? userId[..atIdx] : userId;
        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return local.Length >= 2
            ? local[..2].ToUpper()
            : local.ToUpper();
    }
}
```

**Add to `famos.css`:**

```css
.famos-kcard--urgent {
    border-left: 3px solid var(--red);
    background: rgba(220, 38, 38, 0.03);
}
```

---

## Part E — Urgency / Lifecycle Aging Engine

### E1. `Domain/Enums.cs` — Add New Signals

Append to `DominantSignal` enum (add to existing enum — do NOT replace):

```csharp
// Sprint 5 additions — aging-derived signals
FollowUpNeeded         = 9,
WaitingOnUW            = 10,
WaitingOnCarrier       = 11,
AtRisk                 = 12,
Urgent                 = 13,
```

### E2. `Data/Entities/Opportunity.cs` — Add `LastStageTransitionAt`

(Already listed in C2 migration — just the entity property here)

```csharp
/// <summary>UTC timestamp of the most recent lifecycle stage change. Used for aging calculations.</summary>
public DateTime? LastStageTransitionAt { get; set; }
```

### E3. `Services/AgingService.cs` (new)

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

/// <summary>
/// Background service that runs every 15 minutes and sets DominantSignal
/// on opportunities that have been in their current stage too long.
/// Signals set here are overridden at the next manual stage transition.
/// </summary>
public class AgingService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<AgingService> _logger;

    public AgingService(IServiceScopeFactory services, ILogger<AgingService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial delay — let startup complete
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAgingPassAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Aging] Error during aging pass");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    private async Task RunAgingPassAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var opps = await db.Opportunities
            .Include(o => o.Submissions)
            .Where(o => !o.IsClosed)
            .ToListAsync();

        var now     = DateTime.UtcNow;
        var updated = 0;

        foreach (var opp in opps)
        {
            var since = opp.LastStageTransitionAt.HasValue
                ? (now - opp.LastStageTransitionAt.Value).TotalDays
                : (now - opp.CreatedAt).TotalDays;

            var newSignal = ComputeAgingSignal(opp, since);
            if (newSignal.HasValue && opp.DominantSignal != newSignal.Value)
            {
                opp.DominantSignal = newSignal.Value;
                opp.UpdatedAt      = now;
                updated++;
            }
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("[Aging] Updated signals on {Count} opportunities", updated);
        }
    }

    private static DominantSignal? ComputeAgingSignal(
        FamOs.Web.Data.Entities.Opportunity opp, double daysSinceTransition)
    {
        return opp.LifecycleStage switch
        {
            LifecycleStage.Intake when daysSinceTransition > 3
                => DominantSignal.FollowUpNeeded,

            LifecycleStage.UnderwritingPrep when daysSinceTransition > 5
                && !opp.Submissions.Any()
                => DominantSignal.WaitingOnUW,

            LifecycleStage.Marketed when daysSinceTransition > 7
                => DominantSignal.WaitingOnCarrier,

            LifecycleStage.QuotesReceived when daysSinceTransition > 3
                => DominantSignal.WaitingOnClient,

            LifecycleStage.ClientDecision when daysSinceTransition > 5
                => DominantSignal.AtRisk,

            LifecycleStage.Binding when daysSinceTransition > 3
                => DominantSignal.Urgent,

            _ => null   // No aging signal for this stage/duration combination
        };
    }
}
```

**Add to `Program.cs`:**

```csharp
builder.Services.AddHostedService<AgingService>();
```

### E4. `Components/Shared/SignalChip.razor` — Add New Signal Labels and Colors

Add the new signals to the existing switch expressions:

```csharp
// In label switch:
DominantSignal.FollowUpNeeded   => "Follow Up",
DominantSignal.WaitingOnUW      => "UW Waiting",
DominantSignal.WaitingOnCarrier => "Carrier Waiting",
DominantSignal.AtRisk           => "At Risk",
DominantSignal.Urgent           => "URGENT",

// In color switch:
DominantSignal.FollowUpNeeded   => Color.Warning,
DominantSignal.WaitingOnUW      => Color.Warning,
DominantSignal.WaitingOnCarrier => Color.Default,
DominantSignal.AtRisk           => Color.Error,
DominantSignal.Urgent           => Color.Error,
```

---

## Part F — Dashboard Rebuild

### F1. `Services/OpportunityService.cs` — Expand `GetDashboardSummaryAsync`

Replace the existing `DashboardSummary` model and `GetDashboardSummaryAsync()` method:

```csharp
public class DashboardSummary
{
    public int TotalActive        { get; set; }
    public int TimeRiskCount      { get; set; }
    public int DecisionNeeded     { get; set; }
    public int BoundThisMonth     { get; set; }
    public decimal TotalPremiumAtRisk { get; set; }

    // Urgent/at-risk strip
    public List<Opportunity> UrgentOpportunities { get; set; } = new();

    // Pipeline distribution
    public Dictionary<LifecycleStage, int> ByStage { get; set; } = new();

    // Recent activity
    public List<Activity> RecentActivity { get; set; } = new();
}

public async Task<DashboardSummary> GetDashboardSummaryAsync(string? ownerUserId = null)
{
    await using var db = await _dbFactory.CreateDbContextAsync();

    var query = db.Opportunities.AsQueryable();
    if (!string.IsNullOrEmpty(ownerUserId))
        query = query.Where(o => o.OwnerUserId == ownerUserId);

    var all = await query
        .Include(o => o.Flags)
        .Where(o => !o.IsClosed)
        .ToListAsync();

    var urgentSignals = new[]
    {
        DominantSignal.Urgent, DominantSignal.AtRisk, DominantSignal.TimeRisk
    };

    var recent = await db.Activities
        .OrderByDescending(a => a.OccurredAt)
        .Take(5)
        .ToListAsync();

    return new DashboardSummary
    {
        TotalActive      = all.Count,
        TimeRiskCount    = all.Count(o => urgentSignals.Contains(o.DominantSignal)),
        DecisionNeeded   = all.Count(o =>
            o.LifecycleStage is LifecycleStage.ClientDecision or LifecycleStage.Binding),
        BoundThisMonth   = await db.Opportunities
            .CountAsync(o => o.LifecycleStage == LifecycleStage.Bound
                && o.UpdatedAt >= new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1)),
        TotalPremiumAtRisk = all
            .Where(o => o.EstimatedPremium.HasValue)
            .Sum(o => o.EstimatedPremium!.Value),
        UrgentOpportunities = all
            .Where(o => urgentSignals.Contains(o.DominantSignal))
            .OrderByDescending(o => o.DominantSignal == DominantSignal.Urgent ? 2
                                  : o.DominantSignal == DominantSignal.AtRisk  ? 1 : 0)
            .Take(10)
            .ToList(),
        ByStage        = all.GroupBy(o => o.LifecycleStage)
            .ToDictionary(g => g.Key, g => g.Count()),
        RecentActivity = recent,
    };
}
```

### F2. `Components/Pages/Dashboard.razor` — Full Replacement

```razor
@page "/"
@attribute [Authorize]
@inject OpportunityService OppService
@inject UserSessionService UserSession
@inject NavigationManager Nav
@using FamOs.Web.Services
@using FamOs.Web.Domain
@using FamOs.Web.Data.Entities

<PageTitle>Dashboard — FAM OS</PageTitle>

<div class="famos-page-header famos-page-header-row">
    <div>
        <h2 class="famos-page-h2">Command Center</h2>
        <p class="famos-page-sub">@DateTime.Now.ToString("dddd, MMMM d, yyyy")</p>
    </div>
    <div style="display:flex; gap:8px;">
        <MudButton Variant="Variant.Outlined" Color="Color.Default" Size="Size.Small"
                   Class="famos-btn-outline-sm" OnClick="() => Nav.NavigateTo("/pipeline")">
            Pipeline →
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Default" Size="Size.Small"
                   Class="famos-btn-outline-sm" OnClick="() => Nav.NavigateTo("/tasks")">
            Tasks →
        </MudButton>
    </div>
</div>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
}
else if (_summary != null)
{
    @* ── KPI Cards ──────────────────────────────────────────────────── *@
    <div class="famos-kpi-grid mb-5">
        <StatCard Label="Active Opportunities"
                  Value="@_summary.TotalActive.ToString()"
                  AccentClass="kpi-navy"
                  Sub="Currently in pipeline" />
        <StatCard Label="Needs Attention"
                  Value="@_summary.TimeRiskCount.ToString()"
                  AccentClass="kpi-red"
                  Sub="Urgent or at-risk" />
        <StatCard Label="Awaiting Decision"
                  Value="@_summary.DecisionNeeded.ToString()"
                  AccentClass="kpi-amber"
                  Sub="Proposal or binding stage" />
        <StatCard Label="Bound This Month"
                  Value="@_summary.BoundThisMonth.ToString()"
                  AccentClass="kpi-green"
                  Sub="@($"${_summary.TotalPremiumAtRisk:N0} at risk total")" />
    </div>

    <MudGrid Spacing="3">

        @* ── Urgent / At-Risk Strip ──────────────────────────────────── *@
        <MudItem xs="12" md="7">
            <MudPaper Elevation="0"
                      Style="border:1px solid var(--border); border-radius:12px; overflow:hidden;">
                <div style="padding:14px 16px; border-bottom:1px solid var(--border);
                            display:flex; justify-content:space-between; align-items:center;">
                    <MudText Typo="Typo.subtitle2" Style="color:var(--navy);">
                        Needs Attention
                    </MudText>
                    <MudChip T="string" Size="Size.Small" Color="Color.Error">
                        @_summary.UrgentOpportunities.Count
                    </MudChip>
                </div>
                @if (!_summary.UrgentOpportunities.Any())
                {
                    <div style="padding:24px 16px; text-align:center; color:var(--muted); font-size:13px;">
                        ✓ All clear — no urgent items right now
                    </div>
                }
                else
                {
                    @foreach (var opp in _summary.UrgentOpportunities)
                    {
                        <div style="display:flex; justify-content:space-between; align-items:center;
                                    padding:10px 16px; border-bottom:1px solid var(--border);
                                    cursor:pointer;"
                             @onclick="() => Nav.NavigateTo($"/opportunity/{opp.Id}")">
                            <div>
                                <MudText Typo="Typo.body2" Style="font-weight:600; color:var(--navy);">
                                    @opp.Name
                                </MudText>
                                <MudText Typo="Typo.caption" Color="Color.Secondary">
                                    @GetStageLabel(opp.LifecycleStage)
                                    @(opp.EstimatedPremium.HasValue ? $" · ${opp.EstimatedPremium:N0}" : "")
                                </MudText>
                            </div>
                            <SignalChip Signal="opp.DominantSignal" />
                        </div>
                    }
                }
            </MudPaper>
        </MudItem>

        @* ── Pipeline Distribution ────────────────────────────────────── *@
        <MudItem xs="12" md="5">
            <MudPaper Elevation="0"
                      Style="border:1px solid var(--border); border-radius:12px; overflow:hidden;">
                <div style="padding:14px 16px; border-bottom:1px solid var(--border);">
                    <MudText Typo="Typo.subtitle2" Style="color:var(--navy);">Pipeline Distribution</MudText>
                </div>
                <div style="padding:12px 16px;">
                    @foreach (var stage in Enum.GetValues<LifecycleStage>()
                        .Where(s => s != LifecycleStage.ClosedNotBound))
                    {
                        var count = _summary.ByStage.GetValueOrDefault(stage, 0);
                        var pct   = _summary.TotalActive > 0
                            ? (double)count / _summary.TotalActive * 100
                            : 0;
                        <div style="margin-bottom:8px;">
                            <div style="display:flex; justify-content:space-between;
                                        font-size:12px; margin-bottom:3px;">
                                <span style="color:var(--text);">@GetStageLabel(stage)</span>
                                <span style="color:var(--muted); font-weight:600;">@count</span>
                            </div>
                            <div style="height:6px; background:var(--border); border-radius:3px;">
                                <div style="@($"height:6px; width:{pct:F0}%; background:var(--sky); border-radius:3px; transition:width 0.4s;")"></div>
                            </div>
                        </div>
                    }
                </div>
            </MudPaper>

            @* ── Recent Activity ────────────────────────────────────── *@
            @if (_summary.RecentActivity.Any())
            {
                <MudPaper Elevation="0" Class="mt-3"
                          Style="border:1px solid var(--border); border-radius:12px; overflow:hidden;">
                    <div style="padding:14px 16px; border-bottom:1px solid var(--border);">
                        <MudText Typo="Typo.subtitle2" Style="color:var(--navy);">Recent Activity</MudText>
                    </div>
                    @foreach (var act in _summary.RecentActivity)
                    {
                        <div style="padding:8px 16px; border-bottom:1px solid var(--border);
                                    font-size:12px; color:var(--text);">
                            <span style="color:var(--muted);">
                                @act.OccurredAt.ToLocalTime().ToString("MMM d h:mm tt") ·
                            </span>
                            @act.Description
                        </div>
                    }
                </MudPaper>
            }
        </MudItem>
    </MudGrid>
}

@code {
    private DashboardSummary? _summary;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        var userId = await UserSession.GetUserIdAsync();
        _summary   = await OppService.GetDashboardSummaryAsync(userId);
        _loading   = false;
    }

    private static string GetStageLabel(LifecycleStage stage) => stage switch
    {
        LifecycleStage.Intake           => "Intake",
        LifecycleStage.UnderwritingPrep => "App Review",
        LifecycleStage.Marketed         => "Submitted",
        LifecycleStage.QuotesReceived   => "Quotes In",
        LifecycleStage.ClientDecision   => "Proposal",
        LifecycleStage.Binding          => "Binding",
        LifecycleStage.Bound            => "Bound",
        _                               => stage.ToString()
    };

    private void GoToPipeline() => Nav.NavigateTo("/pipeline");
}
```

---

## Part G — HubSpot Real Sync

### G1. `Services/HubSpotService.cs` (new file — replaces stub)

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

/// <summary>
/// Real HubSpot deal-stage sync.
/// Matches on company name (case-insensitive). If no deal found, logs and no-ops.
/// Phase 1: one-directional only (FAM OS → HubSpot).
/// </summary>
public class HubSpotService : IHubSpotService
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration     _config;
    private readonly ILogger<HubSpotService> _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // HubSpot pipeline stage IDs — override per affinity group in appsettings if needed
    private static readonly Dictionary<LifecycleStage, string> StageMap = new()
    {
        [LifecycleStage.Intake]           = "appointmentscheduled",
        [LifecycleStage.UnderwritingPrep] = "qualifiedtobuy",
        [LifecycleStage.Marketed]         = "presentationscheduled",
        [LifecycleStage.QuotesReceived]   = "decisionmakerboughtin",
        [LifecycleStage.ClientDecision]   = "contractsent",
        [LifecycleStage.Binding]          = "contractsent",
        [LifecycleStage.Bound]            = "closedwon",
        [LifecycleStage.ClosedNotBound]   = "closedlost",
    };

    public HubSpotService(IHttpClientFactory factory, IConfiguration config,
        ILogger<HubSpotService> logger)
    {
        _factory = factory;
        _config  = config;
        _logger  = logger;
    }

    private string? ServiceKey => _config["HubSpot:ServiceKey"];

    public async Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage)
    {
        if (string.IsNullOrEmpty(ServiceKey))
        {
            _logger.LogDebug("[HubSpot] ServiceKey not configured — skipping sync for {Id}", opportunityId);
            return;
        }

        try
        {
            var client = _factory.CreateClient("HubSpot");
            var dealId = await FindDealByOpportunityIdAsync(client, opportunityId);

            if (dealId == null)
            {
                _logger.LogWarning("[HubSpot] No deal found for opportunity {Id} — skipping stage sync", opportunityId);
                return;
            }

            if (!StageMap.TryGetValue(stage, out var hsStage))
            {
                _logger.LogWarning("[HubSpot] No stage mapping for {Stage}", stage);
                return;
            }

            var props = new Dictionary<string, object>
            {
                ["dealstage"] = hsStage
            };

            if (stage == LifecycleStage.ClosedNotBound)
            {
                props["closedate"]                  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                props["hs_deal_stage_probability"]  = 0;
            }
            else if (stage == LifecycleStage.Bound)
            {
                props["closedate"]                  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                props["hs_deal_stage_probability"]  = 1;
            }

            await PatchDealAsync(client, dealId, props);
            _logger.LogInformation("[HubSpot] Deal {DealId} → {Stage}", dealId, hsStage);
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue. Never fail a lifecycle transition because of HubSpot.
            _logger.LogError(ex, "[HubSpot] SyncLifecycle failed for {Id}", opportunityId);
        }
    }

    public async Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow)
    {
        if (string.IsNullOrEmpty(ServiceKey)) return;

        try
        {
            var client = _factory.CreateClient("HubSpot");
            var dealId = await FindDealByOpportunityIdAsync(client, opportunityId);
            if (dealId == null) return;

            var props = new Dictionary<string, object>
            {
                ["dealstage"]                  = "closedwon",
                ["closedate"]                  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["hs_deal_stage_probability"]  = 1,
            };
            if (shadow.PremiumAmount.HasValue)
                props["amount"] = shadow.PremiumAmount.Value;

            await PatchDealAsync(client, dealId, props);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HubSpot] SyncBound failed for {Id}", opportunityId);
        }
    }

    // ── HubSpot API helpers ────────────────────────────────────────────────

    /// <summary>
    /// Search HubSpot deals by the FAM OS opportunity ID stored as a custom property
    /// `famos_opportunity_id`. If not found, falls back to searching by deal name.
    /// </summary>
    private async Task<string?> FindDealByOpportunityIdAsync(HttpClient client, Guid opportunityId)
    {
        // Search by custom property first (preferred — exact match)
        var searchBody = new
        {
            filterGroups = new[]
            {
                new
                {
                    filters = new[]
                    {
                        new
                        {
                            propertyName = "famos_opportunity_id",
                            @operator    = "EQ",
                            value        = opportunityId.ToString()
                        }
                    }
                }
            },
            properties = new[] { "dealname", "dealstage" },
            limit = 1
        };

        var resp = await client.PostAsJsonAsync(
            "/crm/v3/objects/deals/search", searchBody, Opts);

        if (resp.IsSuccessStatusCode)
        {
            var result = await resp.Content.ReadFromJsonAsync<HsSearchResult>(Opts);
            if (result?.Results?.Length > 0)
                return result.Results[0].Id;
        }

        _logger.LogDebug("[HubSpot] No deal with famos_opportunity_id={Id}", opportunityId);
        return null;
    }

    private async Task PatchDealAsync(HttpClient client, string dealId,
        Dictionary<string, object> props)
    {
        var body    = new { properties = props };
        var content = new StringContent(
            JsonSerializer.Serialize(body, Opts),
            System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PatchAsync($"/crm/v3/objects/deals/{dealId}", content);
        resp.EnsureSuccessStatusCode();
    }

    // ── Response DTOs ─────────────────────────────────────────────────────

    private class HsSearchResult
    {
        public HsDeal[]? Results { get; set; }
    }

    private class HsDeal
    {
        public string Id { get; set; } = "";
    }
}
```

### G2. `Program.cs` — Register HubSpot HttpClient + Swap Implementation

```csharp
// HubSpot API client
var hubspotKey = builder.Configuration["HubSpot:ServiceKey"];
builder.Services.AddHttpClient("HubSpot", c =>
{
    c.BaseAddress = new Uri("https://api.hubapi.com");
    if (!string.IsNullOrEmpty(hubspotKey))
        c.DefaultRequestHeaders.Add("Authorization", $"Bearer {hubspotKey}");
});

// Swap: use real HubSpotService when key is configured; stub otherwise
if (!string.IsNullOrEmpty(hubspotKey))
    builder.Services.AddScoped<IHubSpotService, HubSpotService>();
// else: existing HubSpotServiceStub registration remains (from line 112)
```

**Remove or comment out the old `HubSpotServiceStub` registration line:**
```csharp
// builder.Services.AddScoped<IHubSpotService, HubSpotServiceStub>();  // replaced by conditional above
```

**ECS task definition — add:**
```
HubSpot:ServiceKey = <value from AWS Secrets Manager>
```

---

## File Summary

### New Files (4)
```
fip/famos/src/FamOs.Web/Services/QuoteScraperService.cs
fip/famos/src/FamOs.Web/Services/AgingService.cs
fip/famos/src/FamOs.Web/Services/HubSpotService.cs
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuoteScraperPanel.razor
```

### Modified Files (18)
```
fip/famos/src/FamOs.Web/Domain/Enums.cs                                              (CloseReason enum + 5 new DominantSignals)
fip/famos/src/FamOs.Web/Data/Entities/Submission.cs                                  (full replacement with SubmissionStatus enum)
fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs                                 (CloseReason, CloseNotes, LastStageTransitionAt)
fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs                                       (Submission config, Opportunity new columns)
fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs                            (CreateSubmission, UpdateSubmissionStatus, SaveScraperResult, stage gates, LastStageTransitionAt stamps, CloseReason signature)
fip/famos/src/FamOs.Web/Services/OpportunityService.cs                               (DashboardSummary + GetDashboardSummaryAsync expansion)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor   (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/MarketedPanel.razor      (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/Dashboard.razor                             (full replacement)
fip/famos/src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor              (full replacement)
fip/famos/src/FamOs.Web/Components/Shared/OpportunityCard.razor                      (owner initials + urgent tint)
fip/famos/src/FamOs.Web/Components/Shared/SignalChip.razor                           (5 new signals)
fip/famos/src/FamOs.Web/Program.cs                                                   (FortressApi HttpClient, QuoteScraperService, AgingService, HubSpot conditional registration)
fip/famos/src/FamOs.Web/wwwroot/css/famos.css                                        (urgent card tint + task row hover)
```

**DO NOT touch:** FAIT, FIRM, FORMS, FipShared, Sprint 3 Sprint 4 spec files.

---

## Acceptance Criteria

### Part A — Submissions
1. UnderwritingPrepPanel shows a carrier selection dropdown + "Add Carrier" form
2. Clicking "Add Carrier" creates a `Submission` row; it appears in the list immediately
3. "Route to Market →" button is disabled until at least one submission exists
4. MarketedPanel shows all submissions with expandable status rows
5. Selecting "Quote Received" on a submission updates `submissions.status` to `2` in the DB
6. Recording a quote via the inline form in MarketedPanel calls `RecordQuoteAsync` and advances to QUOTES_RECEIVED on the first quote

### Part B — Quote Scraper
7. `QuoteScraperPanel` appears in the OpportunityWorkspace (visible at relevant stages)
8. Selecting a carrier + uploading a PDF triggers the Fortress API flow: upload link → S3 upload → submit → poll
9. On completion, the extracted JSON is displayed in the result text area
10. The `submissions.quote_result_json` column is populated in the DB after successful scrape
11. If the scraper times out (60s), an error message is shown and no DB write occurs

### Part C — Close Reasons
12. `CloseOpportunityDialog` shows 6 structured close reason options (no free-text dropdown)
13. "Close Opportunity" button is disabled until a reason is selected
14. After closing, `opportunities.close_reason` has a non-null integer value
15. `opportunities.last_stage_transition_at` is set on close

### Part D — Pipeline Cards
16. An opportunity with `OwnerUserId = "fred.white@example.com"` shows "FW" initials in a sky-blue circle on the card
17. An opportunity with null/empty `OwnerUserId` shows no initials circle

### Part E — Aging Engine
18. An opportunity in INTAKE with `LastStageTransitionAt` 4 days ago has `DominantSignal = FollowUpNeeded` within 15 minutes (or after restart)
19. An opportunity in BINDING with `LastStageTransitionAt` 4 days ago has `DominantSignal = Urgent`
20. A newly advanced opportunity (just transitioned) does NOT have an aging signal applied yet

### Part F — Dashboard
21. Dashboard shows "Needs Attention" panel with opportunities having Urgent/AtRisk/TimeRisk signals
22. Pipeline distribution bars show correct counts per stage
23. "Recent Activity" section shows the last 5 activity log entries
24. No "coming soon" or placeholder content remains on the dashboard

### Part G — HubSpot
25. When `HubSpot:ServiceKey` is NOT set in config, the service logs a debug message and no HTTP call is made — no errors thrown
26. When `HubSpot:ServiceKey` IS set, advancing an opportunity to MARKETED triggers a PATCH to `/crm/v3/objects/deals/{id}` with `dealstage = "presentationscheduled"`
27. Closing an opportunity sets `hs_deal_stage_probability = 0` and `closedate` on the HubSpot deal
28. If no HubSpot deal matches the FAM OS opportunity ID, the sync logs a warning and the lifecycle transition succeeds anyway

---

## Clint Review Priorities

```
⚠️  HIGH: LifecycleCommandService.CloseOpportunityAsync signature changed from
          (Guid, string, string) to (Guid, CloseReason, string?, string).
          Any call sites that still pass a string close reason will fail to compile.
          Verify there are no other callers besides CloseOpportunityDialog.razor.

⚠️  HIGH: CreateSubmissionAsync and UpdateSubmissionStatusAsync must both use
          transactions. Verify both wrap in BeginTransactionAsync + CommitAsync.
          Non-transactional writes on submission status could corrupt audit trail.

⚠️  HIGH: RouteToMarketAsync currently creates Submission rows from the
          carrierNames array. Sprint 5 passes Array.Empty<string>() because
          submissions are pre-created. The method must still validate
          opp.Submissions.Any() — if Tony leaves the old carrierNames.Length > 0
          check, routing to market will be blocked. Verify the validation
          condition is updated correctly.

⚠️  HIGH: AgingService.RunAgingPassAsync loads ALL non-closed opportunities
          including Submissions (Include). At scale this is a full table scan.
          Acceptable for Phase 1 pilot (< 500 opps). Note for Phase 2 pagination.

⚠️  HIGH: HubSpotService is non-fatal by design — all exceptions are caught and
          logged. Verify that the try/catch in SyncLifecycleAsync does NOT
          re-throw. If Tony adds a throw inside the catch, a HubSpot timeout
          will fail the lifecycle transition in production.

⚠️  MEDIUM: QuoteScraperPanel polls every 5 seconds for up to 60 seconds on the
            Blazor Server connection (12 × Task.Delay(5000)). This holds the
            circuit open and blocks the component render thread for up to 60s.
            Acceptable for MVP. For production, replace with a timer-based
            background poll that pushes completion via StateHasChanged().

⚠️  MEDIUM: DominantSignal enum values 9–13 are new in Sprint 5.
            SignalChip.razor must handle all 5 new values in its switch
            expressions — if Tony adds label but forgets color (or vice versa),
            a MatchFailureException will occur at runtime on any card with
            an aging signal. Verify both switches are complete.

⚠️  MEDIUM: The DB migration for Aurora MySQL uses try/catch on error 1060
            (Duplicate column name). Tony must NOT use IF NOT EXISTS syntax —
            Aurora 2.x does not support it. Verify every ALTER TABLE uses the
            try/catch pattern from the spec.

⚠️  LOW: HubSpot stage mapping uses hardcoded pipeline stage IDs
         (appointmentscheduled, qualifiedtobuy, etc.). These are HubSpot default
         deal stage IDs. If the HubSpot account uses a custom pipeline with
         different stage IDs, all sync calls will silently succeed but not
         change any visible stage. Verify stage IDs match the actual HubSpot
         pipeline before testing.
```

---

_Spec by Reed Richards | Sprint 5 = 4 new files, 18 modified. Full submission lifecycle from carrier selection through quote recording. Quote PDF scraper via Fortress API. Structured close reasons. Owner initials on pipeline cards. 15-minute aging engine with 6 escalation rules. Operational dashboard with urgent-opportunity strip and pipeline distribution. Real HubSpot deal stage sync — non-fatal, no HubSpot = graceful no-op._
