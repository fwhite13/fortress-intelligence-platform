# FAM OS Sprint 7 Spec — Proposal Workflow, Bind Execution, BoundPanel, ClosedNotBoundPanel

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19  
**Sprint Goal:** Complete the full insurance lifecycle — from quote selection through proposal, client acceptance, bind execution, and policy shadow — leaving no stub panels  
**Prerequisite:** Sprints 5 and 6 deployed and verified  
**Design System:** ALL components must comply with `DESIGN-SYSTEM.md`. `famos-btn-*`, `famos-input`, `famos-select` classes only. `FamosIcons.*` for all icons. No inline Variant/Color/Size on MudButton.  
**Spec references:** `FAMOS-ARCHITECTURE-SPEC.md`, `FAMOS-SPRINT5-SPEC.md`, `FAMOS-SPRINT6-SPEC.md`

---

## What This Sprint Completes

After Sprint 7, every lifecycle stage has a fully functional panel. This is the last sprint before Phase 1 is considered **operationally complete** per Steve's spec.

| Stage | Before Sprint 7 | After Sprint 7 |
|-------|----------------|----------------|
| QUOTES_RECEIVED | Basic quote table, "Send Proposal" fires `SendProposalAsync` | Full proposal creation from selected quote; proposal preview before send |
| CLIENT_DECISION | Shows proposal sent timestamp; Request Bind / Reopen Market | Shows full proposal details; records client acceptance/decline; stage gate on Accepted status |
| BINDING | One date picker + "Mark Bound" | Full bind tracking: request submitted flag, confirmation number, policy number, expected bind date, notes |
| BOUND | 3 text fields from PolicyShadowRecord | Complete policy shadow card: all fields, renewal timer countdown |
| CLOSED_NOT_BOUND | No panel (falls to default `<MudAlert>`) | ClosedNotBoundPanel with close reason, notes, lost competitor summary |

---

## Entity Delta Analysis

Before designing anything: audit what exists vs. what the spec requires.

### `Proposal` — needs enhancement
Current entity has: `Id`, `OpportunityId`, `RecommendedQuoteId`, `Version`, `Status` (string), `SentAt`, `ClientDecisionAt`, `DeclineReason`.

**Missing fields per spec:**
- `CarrierName` — needed for display without a Quote join
- `CoverageTypes` — summary for proposal display
- `ProposalDate` — when the proposal was *created* (vs. `SentAt` = when it was sent to client)
- `Notes` — ER notes on the proposal

**Approach:** Add the missing columns to the existing `proposals` table via migration. Do NOT drop and recreate — Sprint 2 created this table and data may exist in dev.

### `PolicyShadowRecord` — needs enhancement
Current entity has: `Id`, `OpportunityId`, `WinningQuoteId`, `CarrierName`, `PolicyEffectiveDate`, `PremiumAmount`, `RenewalTimerStart`, `SnapshotJson`.

**Missing fields per spec:**
- `PolicyNumber` — carrier-issued policy number (assigned at bind)
- `ExpirationDate` — policy expiration date
- `CoverageType` — primary coverage type string
- `BoundAt` — UTC timestamp when bound (currently only `PolicyEffectiveDate` = the policy start date)

**Approach:** Add columns via migration.

### `Opportunity` — needs one new column
- `BindConfirmationNumber` — carrier bind confirmation number (stored on opportunity, not policy shadow, because it's a pre-bind record)
- `BindRequestSubmittedAt` — when the ER submitted the bind request to the carrier

---

## DB Changes

**Aurora MySQL compat: try/catch on error 1060. Never `IF NOT EXISTS`.**

```csharp
// Sprint 7 migrations — add to startup after existing Sprint 5/6 migration calls

// Proposal enhancements
await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN carrier_name VARCHAR(200) NULL");
await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN coverage_types VARCHAR(200) NULL");
await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN proposal_date DATETIME NULL");
await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN notes LONGTEXT NULL");

// PolicyShadowRecord enhancements
await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN policy_number VARCHAR(100) NULL");
await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN expiration_date DATE NULL");
await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN coverage_type VARCHAR(100) NULL");
await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN bound_at DATETIME NULL");

// Opportunity bind tracking
await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN bind_confirmation_number VARCHAR(100) NULL");
await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN bind_request_submitted_at DATETIME NULL");
```

---

## Parallelization Map

**All sequential — single CC session.** All parts touch `LifecycleCommandService.cs` and entity files.

Execution order:
1. Entity enhancements: `Proposal.cs`, `PolicyShadowRecord.cs`, `Opportunity.cs`
2. `FamOsDbContext.cs` — add new column mappings
3. `LifecycleCommandService.cs` — new/modified commands: `CreateProposalAsync`, `MarkProposalSentAsync`, `RecordClientResponseAsync`, `UpdateBindTrackingAsync`, `RecordBinderReceivedAsync` (modify to accept new fields)
4. `QuotesReceivedPanel.razor` — full replacement: quote comparison + proposal creation
5. `ProposalPanel.razor` — new file: shows active proposal, mark-sent, record client response
6. `ClientDecisionPanel.razor` — full replacement: shows ProposalPanel, client acceptance/decline actions
7. `BindingPanel.razor` — full replacement: bind tracking fields + confirm bound
8. `BoundPanel.razor` — full replacement: complete policy shadow display + renewal timer
9. `ClosedNotBoundPanel.razor` — new file
10. `OpportunityWorkspace.razor` — add `ClosedNotBound` case to switch

---

## Part A — Entity Enhancements

### A1. `Data/Entities/Proposal.cs` — Add Missing Fields

```csharp
namespace FamOs.Web.Data.Entities;

public class Proposal
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     RecommendedQuoteId  { get; set; }
    public int      Version             { get; set; } = 1;

    /// <summary>"draft" | "sent" | "accepted" | "declined"</summary>
    public string   Status              { get; set; } = "draft";

    // Sprint 7 additions
    public string?  CarrierName         { get; set; }
    public string?  CoverageTypes       { get; set; }
    public DateTime ProposalDate        { get; set; } = DateTime.UtcNow;
    public string?  Notes               { get; set; }

    public DateTime? SentAt             { get; set; }
    public DateTime? ClientDecisionAt   { get; set; }
    public string?   DeclineReason      { get; set; }

    // Audit
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
```

### A2. `Data/Entities/PolicyShadowRecord.cs` — Add Missing Fields

```csharp
namespace FamOs.Web.Data.Entities;

public class PolicyShadowRecord
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid?    WinningQuoteId      { get; set; }

    // Carrier/policy identity
    public string?  CarrierName         { get; set; }
    public string?  PolicyNumber        { get; set; }    // Sprint 7
    public string?  CoverageType        { get; set; }    // Sprint 7

    // Dates
    public DateOnly? PolicyEffectiveDate { get; set; }
    public DateOnly? ExpirationDate      { get; set; }   // Sprint 7
    public DateOnly? RenewalTimerStart   { get; set; }
    public DateTime? BoundAt             { get; set; }   // Sprint 7

    // Financials
    public decimal?  PremiumAmount      { get; set; }

    // Coverage snapshot
    public string?   SnapshotJson       { get; set; }

    // Audit
    public DateTime  CreatedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
```

### A3. `Data/Entities/Opportunity.cs` — Add Bind Tracking Fields

```csharp
// Add after ClientDecisionAt property:
public string?   BindConfirmationNumber   { get; set; }
public DateTime? BindRequestSubmittedAt   { get; set; }
```

### A4. `Data/FamOsDbContext.cs` — Update Column Mappings

In the `Proposal` entity config, add:
```csharp
e.Property(x => x.Notes).HasColumnType("longtext");
e.Property(x => x.CarrierName).HasMaxLength(200);
e.Property(x => x.CoverageTypes).HasMaxLength(200);
e.Property(x => x.ProposalDate).HasColumnName("proposal_date");
```

In the `PolicyShadowRecord` entity config, add:
```csharp
e.Property(x => x.PolicyNumber).HasMaxLength(100).HasColumnName("policy_number");
e.Property(x => x.ExpirationDate).HasColumnType("date").HasColumnName("expiration_date");
e.Property(x => x.CoverageType).HasMaxLength(100).HasColumnName("coverage_type");
e.Property(x => x.BoundAt).HasColumnType("datetime").HasColumnName("bound_at");
```

In the `Opportunity` entity config, add:
```csharp
e.Property(x => x.BindConfirmationNumber).HasMaxLength(100).HasColumnName("bind_confirmation_number");
e.Property(x => x.BindRequestSubmittedAt).HasColumnType("datetime").HasColumnName("bind_request_submitted_at");
```

---

## Part B — Lifecycle Commands

### B1. `Domain/LifecycleCommandService.cs` — New and Modified Commands

#### New: `CreateProposalAsync`

Creates a proposal in draft status without advancing the lifecycle stage. Called from `QuotesReceivedPanel` before sending.

```csharp
/// <summary>
/// Creates a draft proposal linked to a recommended quote.
/// Does NOT advance lifecycle stage. Must call MarkProposalSentAsync to advance.
/// </summary>
public async Task<Guid> CreateProposalAsync(
    Guid opportunityId,
    Guid recommendedQuoteId,
    string? notes,
    string actorUserId)
{
    return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.QuotesReceived,
            "Proposals can only be created at the Quotes Received stage");
        Validate(opp.Quotes.Any(q => q.Id == recommendedQuoteId),
            "Recommended quote not found on this opportunity");

        // Mark quote as recommended
        foreach (var q in opp.Quotes) q.IsRecommended = false;
        var winningQuote = opp.Quotes.First(q => q.Id == recommendedQuoteId);
        winningQuote.IsRecommended = true;

        var proposal = new Proposal
        {
            OpportunityId      = opportunityId,
            RecommendedQuoteId = recommendedQuoteId,
            CarrierName        = winningQuote.CarrierName,
            CoverageTypes      = winningQuote.CoverageDetails,
            ProposalDate       = DateTime.UtcNow,
            Status             = "draft",
            Notes              = notes?.Trim(),
        };
        _db.Proposals.Add(proposal);
        opp.UpdatedAt = DateTime.UtcNow;

        await WriteActivityAsync(opp.Id, "proposal_created",
            $"Proposal drafted: {winningQuote.CarrierName} ${winningQuote.PremiumAmount:N0}",
            actorUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return proposal.Id;
    });
}
```

#### New: `MarkProposalSentAsync`

Marks proposal as sent AND advances lifecycle to `ClientDecision`. Replaces the current `SendProposalAsync` call flow from the panel.

```csharp
/// <summary>
/// Marks a draft proposal as sent and advances lifecycle to CLIENT_DECISION.
/// </summary>
public async Task MarkProposalSentAsync(
    Guid opportunityId,
    Guid proposalId,
    string actorUserId)
{
    await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.QuotesReceived,
            "MarkProposalSent requires stage QUOTES_RECEIVED");

        var proposal = opp.Proposals.FirstOrDefault(p => p.Id == proposalId)
            ?? throw new LifecycleValidationException("Proposal not found on this opportunity");
        Validate(proposal.Status == "draft",
            "Only draft proposals can be marked as sent");

        proposal.Status = "sent";
        proposal.SentAt = DateTime.UtcNow;

        opp.LifecycleStage        = LifecycleStage.ClientDecision;
        opp.ProposalSentAt        = DateTime.UtcNow;
        opp.LastStageTransitionAt = DateTime.UtcNow;
        opp.UpdatedAt             = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "proposal_sent",
            $"Proposal sent to client: {proposal.CarrierName}", actorUserId);
        await WriteOutboxAsync(DomainEventType.ProposalSent, new
        {
            opportunity_id = opportunityId,
            proposal_id    = proposalId,
            sent_at        = DateTime.UtcNow
        });
        await CreateTasksForStageAsync(opp.Id, LifecycleStage.ClientDecision);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    });
}
```

#### New: `RecordClientResponseAsync`

Records client acceptance or decline on a proposal.

```csharp
/// <summary>
/// Records client acceptance or decline on a sent proposal.
/// On acceptance: advances lifecycle to BINDING.
/// On decline: marks proposal declined; opportunity stays at CLIENT_DECISION for re-proposal or reopen.
/// </summary>
public async Task RecordClientResponseAsync(
    Guid opportunityId,
    Guid proposalId,
    bool accepted,
    string? declineReason,
    string actorUserId)
{
    await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.ClientDecision,
            "RecordClientResponse requires stage CLIENT_DECISION");

        var proposal = opp.Proposals.FirstOrDefault(p => p.Id == proposalId)
            ?? throw new LifecycleValidationException("Proposal not found");
        Validate(proposal.Status == "sent",
            "Can only record response on a sent proposal");

        proposal.Status            = accepted ? "accepted" : "declined";
        proposal.ClientDecisionAt  = DateTime.UtcNow;
        proposal.DeclineReason     = accepted ? null : declineReason;

        if (accepted)
        {
            // Find winning quote from proposal
            var winningQuoteId = proposal.RecommendedQuoteId;
            Validate(opp.Quotes.Any(q => q.Id == winningQuoteId), "Winning quote not found");

            opp.LifecycleStage        = LifecycleStage.Binding;
            opp.ClientDecisionAt      = DateTime.UtcNow;
            opp.LastStageTransitionAt = DateTime.UtcNow;
            opp.UpdatedAt             = DateTime.UtcNow;
            opp.Version++;

            await RecomputeSignalAsync(opp);
            await WriteActivityAsync(opp.Id, "client_accepted",
                $"Client accepted proposal: {proposal.CarrierName}", actorUserId);
            await WriteOutboxAsync(DomainEventType.BindRequested, new
            {
                opportunity_id   = opportunityId,
                winning_quote_id = winningQuoteId,
                proposal_id      = proposalId
            });
            await CreateTasksForStageAsync(opp.Id, LifecycleStage.Binding);
        }
        else
        {
            await WriteActivityAsync(opp.Id, "client_declined",
                $"Client declined proposal: {declineReason ?? "no reason given"}", actorUserId);
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    });
}
```

#### New: `UpdateBindTrackingAsync`

Saves bind-in-progress fields without advancing the lifecycle. Called from `BindingPanel` save-draft action.

```csharp
/// <summary>
/// Saves bind tracking fields (confirmation number, submitted flag, notes).
/// Does NOT advance lifecycle. Stage must be BINDING.
/// </summary>
public async Task UpdateBindTrackingAsync(
    Guid opportunityId,
    string? confirmationNumber,
    bool bindRequestSubmitted,
    string actorUserId)
{
    await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.Binding,
            "UpdateBindTracking requires stage BINDING");

        opp.BindConfirmationNumber = confirmationNumber?.Trim();
        if (bindRequestSubmitted && !opp.BindRequestSubmittedAt.HasValue)
            opp.BindRequestSubmittedAt = DateTime.UtcNow;

        opp.UpdatedAt = DateTime.UtcNow;

        await WriteActivityAsync(opp.Id, "bind_tracking_updated",
            $"Bind tracking updated — confirmation: {confirmationNumber ?? "none"}",
            actorUserId);

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    });
}
```

#### Modified: `RecordBinderReceivedAsync`

Update to accept `policyNumber`, `expirationDate`, and `coverageType` for the policy shadow record.

**Change signature from:**
```csharp
public async Task RecordBinderReceivedAsync(Guid opportunityId, DateOnly effectiveDate, string actorUserId)
```

**To:**
```csharp
public async Task RecordBinderReceivedAsync(
    Guid opportunityId,
    DateOnly effectiveDate,
    DateOnly? expirationDate,
    string? policyNumber,
    string? coverageType,
    string actorUserId)
```

**Update the `PolicyShadowRecord` construction inside the method:**
```csharp
var shadow = new PolicyShadowRecord
{
    OpportunityId       = opportunityId,
    WinningQuoteId      = winningQuote?.Id,
    CarrierName         = winningQuote?.CarrierName,
    PolicyEffectiveDate = effectiveDate,
    ExpirationDate      = expirationDate,
    PolicyNumber        = policyNumber?.Trim(),
    CoverageType        = coverageType?.Trim(),
    PremiumAmount       = winningQuote?.PremiumAmount,
    RenewalTimerStart   = effectiveDate,
    SnapshotJson        = winningQuote?.CoverageDetails,
    BoundAt             = DateTime.UtcNow,        // ← add
};
```

**Note on `RequestBindAsync`:** The existing `RequestBindAsync` advances to BINDING via `ClientDecisionPanel`'s "Request Bind" button. With Sprint 7, client acceptance is now recorded via `RecordClientResponseAsync(accepted: true)`, which also advances to BINDING. The old `RequestBindAsync` should be **kept as-is** for backward compatibility (do NOT remove it) but the new `ClientDecisionPanel` will call `RecordClientResponseAsync` instead. Both paths lead to BINDING.

**Update `LoadOpportunityWithDetailsAsync`** to include `Proposals`:

```csharp
private async Task<Opportunity> LoadOpportunityWithDetailsAsync(Guid id)
{
    return await _db.Opportunities
        .Include(o => o.Submissions)
        .Include(o => o.Quotes)
        .Include(o => o.Contacts)
        .Include(o => o.Proposals)         // ← ensure this is included
        .Include(o => o.Tasks.Where(t => t.Status == "open"))
        .Include(o => o.Flags)
        .FirstOrDefaultAsync(o => o.Id == id)
        ?? throw new NotFoundException($"Opportunity {id} not found");
}
```

#### Keep `SendProposalAsync` — Do NOT Remove

The existing `SendProposalAsync(Guid, Guid, string)` is kept. It is a valid one-step "select quote + send immediately" path. New panels use the two-step `CreateProposalAsync` + `MarkProposalSentAsync` flow, but the old method stays for backward compatibility with any existing calls.

---

## Part C — Panel Replacements and Additions

### C1. `Components/Pages/Opportunity/Panels/QuotesReceivedPanel.razor` — Full Replacement

The existing panel has a basic quote table and a "Send Proposal" button that fires `SendProposalAsync` (one-step). Replace with a two-step flow: select quote → preview proposal → send.

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Theme

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Quote Comparison</span>
        @if (Opportunity.Quotes.Any())
        {
            <span class="famos-meta-text">
                @Opportunity.Quotes.Count quote@(Opportunity.Quotes.Count == 1 ? "" : "s") received
            </span>
        }
    </div>

    @if (!Opportunity.Quotes.Any())
    {
        <div class="famos-empty-state">
            <MudIcon Icon="@FamosIcons.Dollar" Class="famos-empty-icon" />
            <div>No quotes yet. Record quotes from the submission status panel above.</div>
        </div>
    }
    else
    {
        @* Quote comparison table *@
        <div class="famos-quote-table mb-4">
            <div class="famos-quote-header-row">
                <div class="famos-quote-col-carrier">Carrier</div>
                <div class="famos-quote-col-premium">Premium</div>
                <div class="famos-quote-col-coverage">Coverage Notes</div>
                <div class="famos-quote-col-select">Recommend</div>
            </div>
            @foreach (var quote in Opportunity.Quotes.OrderBy(q => q.PremiumAmount))
            {
                var isSelected = _selectedQuoteId == quote.Id;
                <div class="@($"famos-quote-row{(isSelected ? " famos-quote-row--selected" : "")}")"
                     @onclick="() => SelectQuote(quote.Id)">
                    <div class="famos-quote-col-carrier">
                        <span style="font-weight: 600; color: var(--navy);">@quote.CarrierName</span>
                    </div>
                    <div class="famos-quote-col-premium">
                        <span class="famos-quote-premium">$@quote.PremiumAmount.ToString("N0")</span>
                    </div>
                    <div class="famos-quote-col-coverage">
                        <span class="famos-meta-text">@(quote.CoverageDetails ?? "—")</span>
                    </div>
                    <div class="famos-quote-col-select">
                        <div class="@($"famos-radio-circle{(isSelected ? " famos-radio-circle--active" : "")}")">
                            @if (isSelected)
                            {
                                <MudIcon Icon="@FamosIcons.Check" Style="font-size:12px; color:#fff;" />
                            }
                        </div>
                    </div>
                </div>
            }
        </div>

        @* Proposal preview *@
        @if (_selectedQuoteId != Guid.Empty)
        {
            var selected = Opportunity.Quotes.First(q => q.Id == _selectedQuoteId);
            <div class="famos-proposal-preview mb-4">
                <div class="famos-section-label mb-2">Proposal Preview</div>
                <div class="famos-proposal-preview-card">
                    <div style="display:flex; justify-content:space-between; align-items:flex-start; flex-wrap:wrap; gap:8px;">
                        <div>
                            <div style="font-size:15px; font-weight:700; color:var(--navy);">
                                @selected.CarrierName
                            </div>
                            <div class="famos-meta-text">
                                @(selected.CoverageDetails ?? "Coverage details not recorded")
                            </div>
                        </div>
                        <div style="text-align:right;">
                            <div style="font-size:20px; font-weight:700; color:var(--green);">
                                $@selected.PremiumAmount.ToString("N0")
                            </div>
                            <div class="famos-meta-text">annual premium</div>
                        </div>
                    </div>
                    <MudTextField Class="famos-input"
                                  @bind-Value="_proposalNotes"
                                  Placeholder="Add notes to this proposal (optional)..."
                                  Lines="2" />
                </div>
            </div>
        }

        <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
            <MudButton Class="famos-btn-primary"
                       StartIcon="@FamosIcons.ChevronRight"
                       OnClick="CreateAndSend"
                       Disabled="@(_saving || _selectedQuoteId == Guid.Empty)">
                Create & Send Proposal →
            </MudButton>
            <MudButton Class="famos-btn-outline"
                       OnClick="CreateDraft"
                       Disabled="@(_saving || _selectedQuoteId == Guid.Empty)">
                Save as Draft
            </MudButton>
            @if (_selectedQuoteId == Guid.Empty)
            {
                <span class="famos-meta-text">Select a quote to continue</span>
            }
        </div>

        @if (_draftCreated)
        {
            <MudAlert Severity="Severity.Success" Class="mt-3">
                Draft proposal saved. Go to Client Decision stage to send it.
            </MudAlert>
        }
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }

    private Guid   _selectedQuoteId;
    private string _proposalNotes = "";
    private bool   _saving;
    private bool   _draftCreated;

    protected override void OnInitialized()
    {
        // Pre-select previously recommended quote if any
        var recommended = Opportunity.Quotes.FirstOrDefault(q => q.IsRecommended);
        if (recommended != null)
            _selectedQuoteId = recommended.Id;
    }

    private void SelectQuote(Guid quoteId)
    {
        _selectedQuoteId = quoteId;
        _draftCreated    = false;
    }

    private async Task CreateAndSend()
    {
        _saving = true;
        try
        {
            var userId     = await UserSession.GetUserIdAsync();
            var proposalId = await Lifecycle.CreateProposalAsync(
                Opportunity.Id, _selectedQuoteId, _proposalNotes.Trim(), userId);
            await Lifecycle.MarkProposalSentAsync(Opportunity.Id, proposalId, userId);
            Snackbar.Add("Proposal sent to client.", Severity.Success);
            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private async Task CreateDraft()
    {
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.CreateProposalAsync(
                Opportunity.Id, _selectedQuoteId, _proposalNotes.Trim(), userId);
            _draftCreated = true;
            Snackbar.Add("Draft proposal saved.", Severity.Info);
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }
}
```

**Add to `famos.css`:**
```css
/* Quote comparison table */
.famos-quote-table { border: 1px solid var(--border); border-radius: 8px; overflow: hidden; }
.famos-quote-header-row,
.famos-quote-row {
    display: grid;
    grid-template-columns: 1fr 100px 1fr 60px;
    gap: 12px;
    padding: 10px 14px;
    align-items: center;
}
.famos-quote-header-row {
    background: var(--cream);
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.6px;
    color: var(--muted);
    border-bottom: 1px solid var(--border);
}
.famos-quote-row {
    border-bottom: 1px solid var(--border);
    cursor: pointer;
    transition: background 0.1s;
}
.famos-quote-row:last-child { border-bottom: none; }
.famos-quote-row:hover { background: var(--cream); }
.famos-quote-row--selected { background: rgba(0,144,208,0.05); border-left: 3px solid var(--sky); }
.famos-quote-premium { font-size: 14px; font-weight: 700; color: var(--green); }

.famos-radio-circle {
    width: 20px; height: 20px; border-radius: 50%;
    border: 2px solid var(--border);
    display: flex; align-items: center; justify-content: center;
    margin: 0 auto;
}
.famos-radio-circle--active { background: var(--sky); border-color: var(--sky); }

/* Proposal preview card */
.famos-proposal-preview-card {
    padding: 14px;
    border: 1px solid var(--sky);
    border-radius: 8px;
    background: rgba(0,144,208,0.03);
    display: flex;
    flex-direction: column;
    gap: 10px;
}

.famos-section-label {
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.7px;
    color: var(--muted);
}
```

### C2. `Components/Pages/Opportunity/Panels/ClientDecisionPanel.razor` — Full Replacement

Shows the active sent proposal, provides "Accept" / "Decline" / "Reopen Market" actions.

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Theme

<div class="famos-panel">

    @{
        var sentProposal = Opportunity.Proposals.FirstOrDefault(p => p.Status == "sent");
        var acceptedProposal = Opportunity.Proposals.FirstOrDefault(p => p.Status == "accepted");
    }

    @if (sentProposal == null && acceptedProposal == null)
    {
        <div class="famos-empty-state">
            <MudIcon Icon="@FamosIcons.Document" Class="famos-empty-icon" />
            <div>No sent proposal found. Return to Quotes Received to send a proposal.</div>
        </div>
    }
    else
    {
        var activeProposal = acceptedProposal ?? sentProposal!;

        <div class="famos-panel-header">
            <span class="famos-panel-title">Proposal</span>
            <span class="@GetProposalStatusClass(activeProposal.Status)">
                @GetProposalStatusLabel(activeProposal.Status)
            </span>
        </div>

        @* Proposal details card *@
        <div class="famos-proposal-detail-card mb-4">
            <div style="display:flex; justify-content:space-between; align-items:flex-start; flex-wrap:wrap; gap:8px;">
                <div>
                    <div class="famos-contact-name" style="font-size:15px;">
                        @(activeProposal.CarrierName ?? "Carrier not recorded")
                    </div>
                    @if (!string.IsNullOrEmpty(activeProposal.CoverageTypes))
                    {
                        <div class="famos-meta-text">@activeProposal.CoverageTypes</div>
                    }
                    @if (activeProposal.SentAt.HasValue)
                    {
                        <div class="famos-meta-text">
                            Sent @activeProposal.SentAt.Value.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt")
                        </div>
                    }
                </div>
                @{
                    var quote = Opportunity.Quotes.FirstOrDefault(q => q.Id == activeProposal.RecommendedQuoteId);
                }
                @if (quote != null)
                {
                    <div style="text-align:right;">
                        <div style="font-size:20px; font-weight:700; color:var(--green);">
                            $@quote.PremiumAmount.ToString("N0")
                        </div>
                        <div class="famos-meta-text">annual premium</div>
                    </div>
                }
            </div>
            @if (!string.IsNullOrEmpty(activeProposal.Notes))
            {
                <div class="famos-meta-text" style="margin-top:8px; font-style:italic;">
                    @activeProposal.Notes
                </div>
            }
        </div>

        @* Response recording — only for sent proposals *@
        @if (activeProposal.Status == "sent")
        {
            <div class="famos-section-label mb-2">Record Client Response</div>
            <div style="display:flex; gap:8px; flex-wrap:wrap; align-items:center;">
                <MudButton Class="famos-btn-primary"
                           StartIcon="@FamosIcons.Check"
                           OnClick="() => RecordResponse(true)"
                           Disabled="_saving">
                    Client Accepted →
                </MudButton>
                <MudButton Class="famos-btn-danger"
                           OnClick="OpenDeclineDialog"
                           Disabled="_saving">
                    Client Declined
                </MudButton>
                <MudButton Class="famos-btn-outline"
                           OnClick="ReopenMarket"
                           Disabled="_saving">
                    Reopen Market
                </MudButton>
            </div>
        }
        else if (activeProposal.Status == "accepted")
        {
            <div style="display:flex; align-items:center; gap:8px; padding:10px 14px;
                        border:1px solid var(--green); border-radius:8px; background:rgba(5,150,105,0.05);">
                <MudIcon Icon="@FamosIcons.CheckCircle" Style="color:var(--green); font-size:18px;" />
                <MudText Typo="Typo.body2" Style="color:var(--green); font-weight:600;">
                    Client accepted — advancing to binding
                </MudText>
            </div>
        }

        @* Decline dialog state *@
        @if (_showDeclineForm)
        {
            <div class="mt-3" style="padding:12px 14px; border:1px solid var(--red); border-radius:8px; background:rgba(220,38,38,0.03);">
                <div class="famos-section-label mb-2">Decline Reason</div>
                <MudTextField Class="famos-input"
                              @bind-Value="_declineReason"
                              Placeholder="Why did the client decline? (optional)"
                              Lines="2" />
                <div style="display:flex; gap:8px; margin-top:8px;">
                    <MudButton Class="famos-btn-danger"
                               OnClick="() => RecordResponse(false)"
                               Disabled="_saving">
                        Confirm Decline
                    </MudButton>
                    <MudButton Class="famos-btn-outline"
                               OnClick="() => _showDeclineForm = false">
                        Cancel
                    </MudButton>
                </div>
            </div>
        }
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }

    private bool   _saving;
    private bool   _showDeclineForm;
    private string _declineReason = "";

    private void OpenDeclineDialog() => _showDeclineForm = true;

    private async Task RecordResponse(bool accepted)
    {
        var proposal = Opportunity.Proposals.FirstOrDefault(p => p.Status == "sent");
        if (proposal == null) return;

        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.RecordClientResponseAsync(
                Opportunity.Id, proposal.Id, accepted, _declineReason, userId);

            if (accepted)
                Snackbar.Add("Client accepted — advancing to Binding.", Severity.Success);
            else
                Snackbar.Add("Decline recorded. Reopen market or create a new proposal.", Severity.Info);

            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private async Task ReopenMarket()
    {
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.ReopenMarketAsync(Opportunity.Id, userId);
            Snackbar.Add("Market reopened.", Severity.Info);
            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private static string GetProposalStatusLabel(string status) => status switch
    {
        "draft"    => "Draft",
        "sent"     => "Sent — Awaiting Response",
        "accepted" => "Accepted",
        "declined" => "Declined",
        _          => status
    };

    private static string GetProposalStatusClass(string status) => status switch
    {
        "accepted" => "famos-contact-badge-primary",
        "declined" => "famos-status-pill famos-pill-default",
        _          => "famos-meta-text"
    };
}
```

**Add to `famos.css`:**
```css
.famos-proposal-detail-card {
    padding: 14px;
    border: 1px solid var(--border);
    border-radius: 8px;
    background: var(--white);
    display: flex;
    flex-direction: column;
    gap: 8px;
}
```

### C3. `Components/Pages/Opportunity/Panels/BindingPanel.razor` — Full Replacement

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Theme

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Bind Execution</span>
        @if (Opportunity.BindRequestSubmittedAt.HasValue)
        {
            <span class="famos-meta-text">
                Request submitted @Opportunity.BindRequestSubmittedAt.Value.ToLocalTime().ToString("MMM d")
            </span>
        }
    </div>

    @* Bind tracking fields *@
    <div class="famos-section-label mb-2">Bind Request Tracking</div>
    <MudGrid Spacing="2" Class="mb-4">
        <MudItem xs="12" sm="6">
            <MudTextField Class="famos-input"
                          @bind-Value="_confirmationNumber"
                          Label="Carrier Confirmation Number"
                          Placeholder="e.g. GW-2026-99341" />
        </MudItem>
        <MudItem xs="12" sm="6">
            <MudDatePicker @bind-Date="_expectedBindDate"
                           Label="Expected Bind Date" Class="mb-0" />
        </MudItem>
        <MudItem xs="12">
            <label style="display:flex; align-items:center; gap:8px; cursor:pointer; font-size:13px; color:var(--text);">
                <input type="checkbox" @bind="_bindRequestSubmitted" style="accent-color:var(--sky);" />
                Bind request submitted to carrier
            </label>
        </MudItem>
    </MudGrid>

    <MudButton Class="famos-btn-outline"
               OnClick="SaveTracking"
               Disabled="_saving">
        Save Tracking
    </MudButton>

    <MudDivider Class="my-4" />

    @* Confirm Bound — requires effective date, optionally policy number *@
    <div class="famos-section-label mb-2">Confirm Binder Received</div>
    <MudGrid Spacing="2" Class="mb-3">
        <MudItem xs="12" sm="5">
            <MudDatePicker @bind-Date="_effectiveDate"
                           Label="Policy Effective Date *" />
        </MudItem>
        <MudItem xs="12" sm="5">
            <MudDatePicker @bind-Date="_expirationDate"
                           Label="Policy Expiration Date" />
        </MudItem>
        <MudItem xs="12" sm="5">
            <MudTextField Class="famos-input"
                          @bind-Value="_policyNumber"
                          Label="Policy Number" />
        </MudItem>
        <MudItem xs="12" sm="5">
            <MudTextField Class="famos-input"
                          @bind-Value="_coverageType"
                          Label="Coverage Type"
                          Placeholder="e.g. Commercial Auto + GL" />
        </MudItem>
    </MudGrid>

    <MudButton Class="famos-btn-primary"
               OnClick="ConfirmBound"
               Disabled="@(_saving || !_effectiveDate.HasValue)">
        Binder Received — Mark Bound ✓
    </MudButton>

    @if (!_effectiveDate.HasValue)
    {
        <div class="famos-meta-text mt-1">Policy effective date required to mark bound</div>
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }

    private bool _saving;

    // Tracking fields (pre-populated from existing opportunity data)
    private string    _confirmationNumber   = "";
    private DateTime? _expectedBindDate;
    private bool      _bindRequestSubmitted;

    // Bound confirmation fields
    private DateTime? _effectiveDate;
    private DateTime? _expirationDate;
    private string    _policyNumber   = "";
    private string    _coverageType   = "";

    protected override void OnInitialized()
    {
        _confirmationNumber   = Opportunity.BindConfirmationNumber ?? "";
        _bindRequestSubmitted = Opportunity.BindRequestSubmittedAt.HasValue;
    }

    private async Task SaveTracking()
    {
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.UpdateBindTrackingAsync(
                Opportunity.Id,
                _confirmationNumber.Trim(),
                _bindRequestSubmitted,
                userId);
            Snackbar.Add("Bind tracking saved.", Severity.Success);
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private async Task ConfirmBound()
    {
        if (!_effectiveDate.HasValue) return;
        _saving = true;
        try
        {
            var userId     = await UserSession.GetUserIdAsync();
            var effDate    = DateOnly.FromDateTime(_effectiveDate.Value);
            var expDate    = _expirationDate.HasValue
                ? DateOnly.FromDateTime(_expirationDate.Value)
                : (DateOnly?)null;

            await Lifecycle.RecordBinderReceivedAsync(
                Opportunity.Id,
                effDate,
                expDate,
                _policyNumber.Trim().Length > 0 ? _policyNumber.Trim() : null,
                _coverageType.Trim().Length > 0 ? _coverageType.Trim() : null,
                userId);

            Snackbar.Add("Policy bound! Post-bind tasks created.", Severity.Success);
            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }
}
```

### C4. `Components/Pages/Opportunity/Panels/BoundPanel.razor` — Full Replacement

```razor
@namespace FamOs.Web.Components.Panels
@using FamOs.Web.Data.Entities
@using FamOs.Web.Theme

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Policy Summary</span>
        <span style="color:var(--green); font-size:12px; font-weight:700;">
            <MudIcon Icon="@FamosIcons.CheckCircle"
                     Style="font-size:14px; vertical-align:middle; margin-right:3px;" />
            BOUND
        </span>
    </div>

    @if (Opportunity.PolicyShadow == null)
    {
        <div class="famos-empty-state">
            <MudIcon Icon="@FamosIcons.Document" Class="famos-empty-icon" />
            <div>Policy shadow record not found. Contact support.</div>
        </div>
    }
    else
    {
        var shadow = Opportunity.PolicyShadow;

        <div class="famos-policy-card mb-4">
            <div class="famos-policy-grid">

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Carrier</div>
                    <div class="famos-policy-value">@(shadow.CarrierName ?? "—")</div>
                </div>

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Policy Number</div>
                    <div class="famos-policy-value">@(shadow.PolicyNumber ?? "Pending")</div>
                </div>

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Annual Premium</div>
                    <div class="famos-policy-value famos-policy-value--premium">
                        @(shadow.PremiumAmount.HasValue
                            ? $"${shadow.PremiumAmount.Value:N0}"
                            : "—")
                    </div>
                </div>

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Coverage Type</div>
                    <div class="famos-policy-value">@(shadow.CoverageType ?? "—")</div>
                </div>

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Effective Date</div>
                    <div class="famos-policy-value">
                        @(shadow.PolicyEffectiveDate?.ToString("MMMM d, yyyy") ?? "—")
                    </div>
                </div>

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Expiration Date</div>
                    <div class="famos-policy-value">
                        @(shadow.ExpirationDate?.ToString("MMMM d, yyyy") ?? "Not recorded")
                    </div>
                </div>

                <div class="famos-policy-field">
                    <div class="famos-policy-label">Bound Date</div>
                    <div class="famos-policy-value">
                        @(shadow.BoundAt.HasValue
                            ? shadow.BoundAt.Value.ToLocalTime().ToString("MMMM d, yyyy")
                            : shadow.CreatedAt.ToLocalTime().ToString("MMMM d, yyyy"))
                    </div>
                </div>

                @* Renewal timer *@
                @if (shadow.RenewalTimerStart.HasValue)
                {
                    var renewalDate  = shadow.RenewalTimerStart.Value.AddYears(1).AddDays(-60);
                    var today        = DateOnly.FromDateTime(DateTime.Today);
                    var daysToRenewal = renewalDate.DayNumber - today.DayNumber;
                    var renewalColor  = daysToRenewal < 30 ? "var(--red)"
                                      : daysToRenewal < 90 ? "var(--amber)"
                                      : "var(--muted)";
                    <div class="famos-policy-field" style="grid-column: 1 / -1;">
                        <div class="famos-policy-label">Renewal Window Opens</div>
                        <div class="famos-policy-value" style="color:@renewalColor;">
                            @renewalDate.ToString("MMMM d, yyyy")
                            @if (daysToRenewal > 0)
                            {
                                <span class="famos-meta-text" style="margin-left:6px;">
                                    (@daysToRenewal days away)
                                </span>
                            }
                            else
                            {
                                <span style="margin-left:6px; font-weight:700; color:var(--red);">
                                    RENEWAL WINDOW OPEN
                                </span>
                            }
                        </div>
                    </div>
                }

            </div>
        </div>

        <div class="famos-section-label mb-2">Post-Bind Checklist</div>
        <div style="display:flex; flex-direction:column; gap:6px;">
            @foreach (var item in PostBindItems)
            {
                <div style="display:flex; align-items:center; gap:8px; font-size:13px; color:var(--text);">
                    <MudIcon Icon="@FamosIcons.Check"
                             Style="font-size:14px; color:var(--muted);" />
                    @item
                </div>
            }
        </div>
    }

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;

    private static readonly string[] PostBindItems =
    {
        "Deliver binder / policy documents to client",
        "Issue certificates of insurance",
        "Confirm premium payment arrangement",
        "Update policy record in Epic/AMS",
        "File signed ACORD application",
    };
}
```

**Add to `famos.css`:**
```css
.famos-policy-card {
    border: 1px solid var(--border);
    border-radius: 10px;
    overflow: hidden;
}
.famos-policy-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 0;
}
.famos-policy-field {
    padding: 12px 16px;
    border-bottom: 1px solid var(--border);
    border-right: 1px solid var(--border);
}
.famos-policy-field:nth-child(even) { border-right: none; }
.famos-policy-field:last-child,
.famos-policy-field:nth-last-child(2) { border-bottom: none; }
.famos-policy-label {
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.6px;
    color: var(--muted);
    margin-bottom: 3px;
}
.famos-policy-value {
    font-size: 13px;
    font-weight: 600;
    color: var(--navy);
}
.famos-policy-value--premium {
    font-size: 16px;
    font-weight: 700;
    color: var(--green);
}
```

### C5. `Components/Pages/Opportunity/Panels/ClosedNotBoundPanel.razor` (new)

```razor
@namespace FamOs.Web.Components.Panels
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Theme

<div class="famos-panel">

    <div class="famos-panel-header">
        <span class="famos-panel-title">Closed — Not Bound</span>
        <span style="color:var(--red); font-size:12px; font-weight:600;">Closed</span>
    </div>

    <div class="famos-closed-card mb-4">

        <div class="famos-policy-field" style="border-bottom:1px solid var(--border); padding-bottom:10px; margin-bottom:10px;">
            <div class="famos-policy-label">Close Reason</div>
            <div class="famos-policy-value" style="color:var(--red);">
                @GetCloseReasonLabel(Opportunity.CloseReason)
            </div>
        </div>

        @if (!string.IsNullOrEmpty(Opportunity.CloseNotes))
        {
            <div class="famos-policy-field" style="border-bottom:1px solid var(--border); padding-bottom:10px; margin-bottom:10px;">
                <div class="famos-policy-label">Notes</div>
                <div style="font-size:13px; color:var(--text); font-style:italic;">
                    @Opportunity.CloseNotes
                </div>
            </div>
        }

        @if (Opportunity.CloseReason == CloseReason.LostToCompetitor)
        {
            <div>
                <div class="famos-policy-label mb-1">Lost To Competitor</div>
                <div class="famos-meta-text">
                    The client selected an alternative carrier or broker.
                    Review the carrier submissions to identify which markets were competitive.
                </div>
                @if (Opportunity.Submissions.Any(s => s.Status == SubmissionStatus.QuoteReceived))
                {
                    <div class="mt-2">
                        <div class="famos-section-label mb-1">Competitive Quotes Received</div>
                        @foreach (var sub in Opportunity.Submissions.Where(s => s.Status == SubmissionStatus.QuoteReceived))
                        {
                            <div class="famos-meta-text">· @sub.CarrierName</div>
                        }
                    </div>
                }
            </div>
        }

        <div style="margin-top:12px; padding-top:12px; border-top:1px solid var(--border);">
            <div class="famos-policy-label mb-1">Opportunity Summary</div>
            <div class="famos-meta-text">
                @if (Opportunity.EstimatedPremium.HasValue)
                {
                    <span>Est. premium: $@Opportunity.EstimatedPremium.Value.ToString("N0") · </span>
                }
                <span>Submissions: @Opportunity.Submissions.Count · </span>
                <span>Quotes received: @Opportunity.Quotes.Count</span>
            </div>
        </div>

    </div>

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;

    private static string GetCloseReasonLabel(CloseReason? reason) => reason switch
    {
        CloseReason.NotQuoted              => "Not Quoted — carrier(s) declined",
        CloseReason.PriceTooHigh           => "Price Too High",
        CloseReason.LostToCompetitor       => "Lost to Competitor",
        CloseReason.ClientDeclinedCoverage => "Client Declined Coverage",
        CloseReason.PolicyLapsed           => "Policy Lapsed",
        CloseReason.Other                  => "Other",
        null                               => "Not recorded",
        _                                  => reason.ToString()!
    };
}
```

**Add to `famos.css`:**
```css
.famos-closed-card {
    border: 1px solid var(--border);
    border-radius: 10px;
    padding: 16px;
    background: var(--cream);
}
```

### C6. `Components/Pages/Opportunity/OpportunityWorkspace.razor` — Add ClosedNotBound Case + Proposals Include

**Add to the `@switch` block** (after the `Bound` case):

```razor
case LifecycleStage.ClosedNotBound:
    <ClosedNotBoundPanel Opportunity="_opp" />
    break;
```

**Add Proposals to `OpportunityService.GetByIdAsync`** — include `Proposals` navigation:

```csharp
// In GetByIdAsync query chain, add:
.Include(o => o.Proposals)
```

---

## File Summary

### New Files (2)
```
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ClosedNotBoundPanel.razor
```
*(QuotesReceivedPanel and ClientDecisionPanel and BindingPanel and BoundPanel are all **full replacements** of existing files, not new files.)*

Wait — `QuoteScraperPanel.razor` was added in Sprint 5. All panel files except `ClosedNotBoundPanel.razor` already exist. New files:
```
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ClosedNotBoundPanel.razor
```

### Modified Files (12)
```
fip/famos/src/FamOs.Web/Data/Entities/Proposal.cs                         (add CarrierName, CoverageTypes, ProposalDate, Notes)
fip/famos/src/FamOs.Web/Data/Entities/PolicyShadowRecord.cs               (add PolicyNumber, ExpirationDate, CoverageType, BoundAt)
fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs                      (add BindConfirmationNumber, BindRequestSubmittedAt)
fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs                            (column mappings for new fields)
fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs                 (CreateProposalAsync, MarkProposalSentAsync, RecordClientResponseAsync, UpdateBindTrackingAsync, RecordBinderReceivedAsync signature update, LoadOpportunityWithDetailsAsync includes)
fip/famos/src/FamOs.Web/Services/OpportunityService.cs                    (Include Proposals in GetByIdAsync)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuotesReceivedPanel.razor   (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ClientDecisionPanel.razor   (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/BindingPanel.razor          (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/BoundPanel.razor            (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor         (add ClosedNotBound case)
fip/famos/src/FamOs.Web/wwwroot/css/famos.css                             (quote table, proposal preview, policy grid, closed card CSS)
```

**DO NOT touch:** FAIT, FIRM, FORMS, FipShared, Sprint 5–6 service files, Sprint 4 task/intake specs.

**Total: 1 new file, 12 modified files.**

---

## Acceptance Criteria

### Part A — Entity Changes
1. `proposals` table has `carrier_name`, `coverage_types`, `proposal_date`, `notes` columns after startup
2. `policy_shadow_records` table has `policy_number`, `expiration_date`, `coverage_type`, `bound_at` columns
3. `opportunities` table has `bind_confirmation_number`, `bind_request_submitted_at` columns
4. All migrations are idempotent — running startup twice does NOT fail

### Part B — Lifecycle Commands
5. `CreateProposalAsync` creates a `proposals` row with `status = 'draft'`; does not advance lifecycle stage
6. `MarkProposalSentAsync` sets `status = 'sent'`, `sent_at = NOW()`, and advances opportunity to `ClientDecision`
7. `RecordClientResponseAsync(accepted=true)` sets proposal `status = 'accepted'` and advances opportunity to `Binding`
8. `RecordClientResponseAsync(accepted=false)` sets proposal `status = 'declined'` and keeps opportunity at `ClientDecision`
9. `UpdateBindTrackingAsync` saves `bind_confirmation_number` and `bind_request_submitted_at` without changing lifecycle stage
10. `RecordBinderReceivedAsync` creates `PolicyShadowRecord` with `policy_number`, `expiration_date`, `coverage_type`, `bound_at` populated

### Part C — Panels
11. `QuotesReceivedPanel` shows all quotes in a comparison grid; selecting a quote shows the proposal preview card
12. "Create & Send Proposal" calls `CreateProposalAsync` then `MarkProposalSentAsync` and navigates to `ClientDecision` panel
13. "Save as Draft" calls `CreateProposalAsync` only; panel stays at `QuotesReceived`; confirmation message shown
14. `ClientDecisionPanel` shows the sent proposal's carrier name and premium amount
15. "Client Accepted →" calls `RecordClientResponseAsync(true)` and advances to `BindingPanel`
16. "Client Declined" shows the decline reason input inline; "Confirm Decline" records the decline; opportunity stays at `ClientDecision`
17. `BindingPanel` shows two sections: "Bind Request Tracking" (save-without-advance) and "Confirm Binder Received" (advance to Bound)
18. "Save Tracking" saves confirmation number without advancing; page does not reload
19. "Binder Received — Mark Bound" requires effective date; on success advances to `BoundPanel`
20. `BoundPanel` shows all `PolicyShadowRecord` fields; renewal window countdown shown in correct color (green/amber/red)
21. `ClosedNotBoundPanel` shows close reason label and close notes; shows competitor summary section when reason = `LostToCompetitor`
22. `OpportunityWorkspace` shows `ClosedNotBoundPanel` for `ClosedNotBound` stage (no more fallthrough `<MudAlert>`)

---

## Clint Review Priorities

```
⚠️  HIGH: RecordClientResponseAsync(accepted=true) advances to BINDING — this
          replaces what RequestBindAsync used to do from ClientDecisionPanel.
          Verify the existing RequestBindAsync is NOT removed (kept for backward
          compat) but that the new ClientDecisionPanel calls
          RecordClientResponseAsync, not RequestBindAsync.
          If Tony removes RequestBindAsync, any API-level callers (if any exist)
          will fail.

⚠️  HIGH: MarkProposalSentAsync calls LoadOpportunityWithDetailsAsync which must
          include Proposals. Verify the include is in LoadOpportunityWithDetailsAsync.
          If Proposals is not included, the proposal lookup
          (opp.Proposals.FirstOrDefault(...)) will always return null and throw
          LifecycleValidationException at runtime.

⚠️  HIGH: RecordBinderReceivedAsync signature change: 5 parameters → 7.
          This is a breaking change. The only call site is BindingPanel.razor.
          Verify no other component or test calls RecordBinderReceivedAsync
          with the old 3-parameter signature. If Tony forgets to update the
          call site in BindingPanel.razor, it will fail to compile.

⚠️  HIGH: CreateProposalAsync marks a quote IsRecommended = false on ALL quotes,
          then sets the selected quote to true. If called twice with different
          quotes, the previous recommendation is cleared. This is by design —
          confirm it does not break any code that relies on "the recommended
          quote is always the one from the most recent SendProposalAsync call."
          The old SendProposalAsync did the same thing; this is not a regression.

⚠️  MEDIUM: BoundPanel renewal timer uses DateOnly.DayNumber arithmetic
            (renewalDate.DayNumber - today.DayNumber). This is correct C# for
            counting days between DateOnly values. Confirm the build target is
            .NET 6+ (it is — this project uses .NET 9). DayNumber is available.

⚠️  MEDIUM: ClosedNotBoundPanel references SubmissionStatus enum from Sprint 5.
            If Sprint 5 entity changes are not deployed before Sprint 7, the
            `s.Status == SubmissionStatus.QuoteReceived` comparison will not
            compile. Verify Sprint 5 is deployed first.

⚠️  MEDIUM: DESIGN SYSTEM — BindingPanel uses a native HTML <input type="checkbox">
            (not MudBlazor) to avoid inline Color/Size on MudCheckBox. This is
            acceptable — the design system only mandates CSS class usage on
            MudButton, MudTextField, and MudSelect. However, verify the checkbox
            renders consistently across browsers with `accent-color: var(--sky)`.

⚠️  LOW: QuotesReceivedPanel pre-selects the recommended quote on init.
         If no quote is recommended, _selectedQuoteId remains Guid.Empty and
         the proposal preview is hidden. This is correct behavior. Verify the
         ER can always select any quote even if a previous recommendation exists.

⚠️  LOW: PolicyShadowRecord.BoundAt is populated by RecordBinderReceivedAsync
         as DateTime.UtcNow. The BoundPanel falls back to shadow.CreatedAt if
         BoundAt is null (for records created before Sprint 7). This fallback
         is safe and intentional — do not remove it.
```

---

_Spec by Reed Richards | Sprint 7 = 1 new file, 12 modified. Completes the full insurance lifecycle: quote selection → proposal draft/send → client acceptance → bind execution tracking → policy shadow creation. After Sprint 7, every lifecycle stage panel is fully functional. Phase 1 operationally complete._
