# FAM OS — Sprint 2 Spec: Lifecycle Engine + Pipeline Board

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-18  
**Status:** Ready for Tony (CC) — after Sprint 1 deploy verified  
**Prerequisite:** Sprint 1 app running at `famos.dev.fortressam.ai`  
**Reference:** `FAMOS-ARCHITECTURE-SPEC.md` (full engine + signal resolver design)

---

## Sprint 2 Deliverable

Sprint 1 gave us a running skeleton. Sprint 2 makes it operational:

- **Opportunity creation** (INTAKE form)
- **Pipeline Board** (Kanban, 7 stage columns, Opportunity cards with signal chips)
- **Opportunity Workspace** (stage-specific panels, action buttons that fire lifecycle commands)
- **Signal chip UI** (color-coded dominant signal on every card and workspace header)
- **Basic Dashboard** (signal summary counts, urgent opportunities list)
- **Opportunity close/park** (loss capture)

Sprint 2 makes the lifecycle engine usable by a real ER.

---

## Parallelization Map

**Two parallel groups, then sequential finisher:**

### Parallel Group A — Data + Service Layer (no shared Razor files)

**A1 — `OpportunityService.cs`** (query + list methods)
Files: `Services/OpportunityService.cs`
Depends on: Sprint 1 `FamOsDbContext` (already exists)

**A2 — No new files in engine** — `LifecycleCommandService` and `SignalResolver` are already written in Sprint 1. Group A only adds the query service.

### Sequential (Group B — all Blazor UI, after A1 done)

B1 — Pipeline Board (`Pipeline.razor` — replaces Sprint 1 stub)  
B2 — Opportunity Workspace (`OpportunityWorkspace.razor` + panel components)  
B3 — Opportunity Create modal (`OpportunityCreateDialog.razor`)  
B4 — Dashboard (`Dashboard.razor` — replaces Sprint 1 stub)  
B5 — Signal chip component (`SignalChip.razor`)

**Why not parallel?** All Blazor components `@inject` `OpportunityService` — they all depend on A1. The Blazor components also share `SignalChip.razor` (B5 must be written before B1/B2 use it, or done as part of B1).

**Recommended CC order:** A1 → B5 → B1 → B2 → B3 → B4.

---

## File List

### New Files

```
fip/famos/src/FamOs.Web/Services/OpportunityService.cs
fip/famos/src/FamOs.Web/Components/Shared/SignalChip.razor
fip/famos/src/FamOs.Web/Components/Shared/OpportunityCard.razor
fip/famos/src/FamOs.Web/Components/Dialogs/OpportunityCreateDialog.razor
fip/famos/src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/IntakePanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/MarketedPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuotesReceivedPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ClientDecisionPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/BindingPanel.razor
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/BoundPanel.razor
```

### Modified Files

```
fip/famos/src/FamOs.Web/Components/Pages/Pipeline.razor   (replace stub with full Kanban)
fip/famos/src/FamOs.Web/Components/Pages/Dashboard.razor  (replace stub with signal summary)
fip/famos/src/FamOs.Web/Program.cs                        (add OpportunityService scoped registration)
```

**DO NOT touch:** FipShared, FAIT, FIRM, FORMS, Sprint 1 entity files, Sprint 1 LifecycleCommandService.

---

## 1. `Services/OpportunityService.cs`

All read queries and opportunity creation. Command execution stays in `LifecycleCommandService`.

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class OpportunityService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly LifecycleCommandService _lifecycle;
    private readonly ILogger<OpportunityService> _logger;

    public OpportunityService(IDbContextFactory<FamOsDbContext> dbFactory,
        LifecycleCommandService lifecycle, ILogger<OpportunityService> logger)
    {
        _dbFactory = dbFactory;
        _lifecycle = lifecycle;
        _logger    = logger;
    }

    /// <summary>All active (non-closed) opportunities for the pipeline board.</summary>
    public async Task<List<Opportunity>> GetPipelineAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Where(o => !o.IsClosed)
            .OrderBy(o => o.UrgencyScore).ThenBy(o => o.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>Opportunities grouped by lifecycle stage for Kanban rendering.</summary>
    public async Task<Dictionary<LifecycleStage, List<Opportunity>>> GetPipelineByStageAsync()
    {
        var all = await GetPipelineAsync();
        return all
            .GroupBy(o => o.LifecycleStage)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Single opportunity with all navigation properties loaded.</summary>
    public async Task<Opportunity?> GetByIdAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Submissions)
            .Include(o => o.Quotes)
            .Include(o => o.Proposals)
            .Include(o => o.PolicyShadow)
            .Include(o => o.Activities.OrderByDescending(a => a.OccurredAt).Take(50))
            .Include(o => o.Tasks.Where(t => t.Status == "open"))
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    /// <summary>
    /// Create a new opportunity in INTAKE stage.
    /// Returns the new opportunity's ID.
    /// </summary>
    public async Task<Guid> CreateOpportunityAsync(string name, string ownerUserId,
        decimal? estimatedPremium, DateOnly? effectiveDateTarget)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var opp = new Opportunity
        {
            Name                = name,
            LifecycleStage      = LifecycleStage.Intake,
            DominantSignal      = DominantSignal.WaitingOnClient,
            DominantSignalReason = "Awaiting required intake information",
            OwnerUserId         = ownerUserId,
            EstimatedPremium    = estimatedPremium,
            EffectiveDateTarget = effectiveDateTarget,
        };
        db.Opportunities.Add(opp);

        db.Activities.Add(new Activity {
            OpportunityId = opp.Id,
            EventType     = "opportunity_created",
            Description   = $"Opportunity created: {name}",
            ActorUserId   = ownerUserId,
        });

        await db.SaveChangesAsync();
        _logger.LogInformation("[FAM OS] Opportunity created: {Id} — {Name}", opp.Id, name);
        return opp.Id;
    }

    /// <summary>Signal summary for dashboard.</summary>
    public async Task<DashboardSummary> GetDashboardSummaryAsync(string? ownerUserId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Opportunities.Where(o => !o.IsClosed);
        if (!string.IsNullOrEmpty(ownerUserId))
            query = query.Where(o => o.OwnerUserId == ownerUserId);

        var opps = await query.ToListAsync();

        return new DashboardSummary
        {
            TotalActive    = opps.Count,
            TimeRiskCount  = opps.Count(o => o.DominantSignal == DominantSignal.TimeRisk),
            DecisionNeeded = opps.Count(o => o.DominantSignal is
                DominantSignal.DecisionRequired or DominantSignal.AwaitingClientDecision),
            BindingCount   = opps.Count(o => o.LifecycleStage == LifecycleStage.Binding),
            BoundThisMonth = opps.Count(o =>
                o.LifecycleStage == LifecycleStage.Bound &&
                o.UpdatedAt >= DateTime.UtcNow.AddDays(-30)),
        };
    }
}

public record DashboardSummary
{
    public int TotalActive    { get; init; }
    public int TimeRiskCount  { get; init; }
    public int DecisionNeeded { get; init; }
    public int BindingCount   { get; init; }
    public int BoundThisMonth { get; init; }
}
```

**Add to `Program.cs`:**
```csharp
builder.Services.AddScoped<OpportunityService>();
```

---

## 2. `Components/Shared/SignalChip.razor`

Used on both Pipeline cards and Opportunity Workspace header.

```razor
@using FamOs.Web.Domain

<span class="famos-signal-chip @GetCssClass()">@GetLabel()</span>

@code {
    [Parameter, EditorRequired] public DominantSignal Signal { get; set; }

    private string GetLabel() => Signal switch
    {
        DominantSignal.WaitingOnClient        => "Waiting on Client",
        DominantSignal.UnderwritingInProgress => "Underwriting",
        DominantSignal.WaitingOnMarket        => "Waiting on Market",
        DominantSignal.DecisionRequired       => "Decision Required",
        DominantSignal.AwaitingClientDecision => "Awaiting Client",
        DominantSignal.BindingInProgress      => "Binding",
        DominantSignal.PostBindProcessing     => "Post-Bind",
        DominantSignal.TimeRisk               => "⚠ Time Risk",
        DominantSignal.Parked                 => "Parked",
        _                                     => Signal.ToString()
    };

    private string GetCssClass() => Signal switch
    {
        DominantSignal.TimeRisk               => "famos-signal-time-risk",
        DominantSignal.WaitingOnClient        => "famos-signal-waiting-on-client",
        DominantSignal.DecisionRequired       => "famos-signal-decision-required",
        DominantSignal.WaitingOnMarket        => "famos-signal-waiting-on-market",
        DominantSignal.AwaitingClientDecision => "famos-signal-awaiting-client",
        DominantSignal.BindingInProgress      => "famos-signal-binding",
        DominantSignal.UnderwritingInProgress => "famos-signal-underwriting",
        DominantSignal.PostBindProcessing     => "famos-signal-post-bind",
        DominantSignal.Parked                 => "famos-signal-parked",
        _                                     => "famos-signal-parked"
    };
}
```

---

## 3. `Components/Shared/OpportunityCard.razor`

Used in Pipeline Kanban columns.

```razor
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@inject NavigationManager Nav

<MudCard Class="mb-2 cursor-pointer" Style="border-radius: 6px;"
         @onclick="() => Nav.NavigateTo($"/opportunity/{Opportunity.Id}")">
    <MudCardContent Class="pa-3">
        <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 6px;">
            <MudText Typo="Typo.subtitle2" Style="font-size:13px; font-weight:600; line-height:1.3;">
                @Opportunity.Name
            </MudText>
        </div>
        <SignalChip Signal="Opportunity.DominantSignal" />
        @if (Opportunity.EstimatedPremium.HasValue)
        {
            <div style="margin-top:6px; font-size:12px; color:var(--color-text-secondary);">
                $@Opportunity.EstimatedPremium.Value.ToString("N0")
            </div>
        }
        @if (Opportunity.EffectiveDateTarget.HasValue)
        {
            <div style="font-size:11px; color:var(--color-text-muted); margin-top:2px;">
                Eff: @Opportunity.EffectiveDateTarget.Value.ToString("MMM d, yyyy")
            </div>
        }
    </MudCardContent>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
}
```

---

## 4. `Components/Pages/Pipeline.razor` (replaces Sprint 1 stub)

```razor
@page "/pipeline"
@attribute [Authorize]
@inject OpportunityService OppService
@inject IDialogService DialogService
@inject NavigationManager Nav
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Services

<PageTitle>Pipeline — FAM OS</PageTitle>

<div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:16px;">
    <MudText Typo="Typo.h5">Pipeline</MudText>
    <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Filled.Add"
               OnClick="OpenCreateDialog">
        New Opportunity
    </MudButton>
</div>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
}
else
{
    <div class="famos-pipeline-board">
        @foreach (var col in _columns)
        {
            <div class="famos-pipeline-column">
                <div class="famos-pipeline-column-header">
                    @col.DisplayName
                    <span style="float:right; font-weight:400; color:var(--color-text-muted);">
                        @GetStageCount(col.Stage)
                    </span>
                </div>
                <div style="padding:8px;">
                    @foreach (var opp in GetStageOpportunities(col.Stage))
                    {
                        <OpportunityCard Opportunity="opp" />
                    }
                    @if (!GetStageOpportunities(col.Stage).Any())
                    {
                        <div style="padding:16px;text-align:center;color:var(--color-text-muted);font-size:12px;">
                            No opportunities
                        </div>
                    }
                </div>
            </div>
        }
    </div>
}

@code {
    private bool _loading = true;
    private Dictionary<LifecycleStage, List<Opportunity>> _byStage = new();

    private static readonly (LifecycleStage Stage, string DisplayName)[] _columns =
    {
        (LifecycleStage.Intake,           "Intake"),
        (LifecycleStage.UnderwritingPrep, "App Review"),
        (LifecycleStage.Marketed,         "Submitted"),
        (LifecycleStage.QuotesReceived,   "Quotes In"),
        (LifecycleStage.ClientDecision,   "Proposal"),
        (LifecycleStage.Binding,          "Binding"),
        (LifecycleStage.Bound,            "Bound"),
    };

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading  = true;
        _byStage  = await OppService.GetPipelineByStageAsync();
        _loading  = false;
    }

    private List<Opportunity> GetStageOpportunities(LifecycleStage stage)
        => _byStage.TryGetValue(stage, out var list) ? list : new();

    private int GetStageCount(LifecycleStage stage)
        => _byStage.TryGetValue(stage, out var list) ? list.Count : 0;

    private async Task OpenCreateDialog()
    {
        var dialog = await DialogService.ShowAsync<OpportunityCreateDialog>("New Opportunity");
        var result = await dialog.Result;
        if (!result.Canceled)
        {
            var oppId = (Guid)result.Data!;
            Nav.NavigateTo($"/opportunity/{oppId}");
        }
    }
}
```

---

## 5. `Components/Dialogs/OpportunityCreateDialog.razor`

```razor
@inject OpportunityService OppService
@inject UserSessionService UserSession
@using MudBlazor

<MudDialog>
    <TitleContent>New Opportunity</TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_name" Label="Account Name" Required="true"
                      Immediate="true" Class="mb-3" />
        <MudNumericField @bind-Value="_premium" Label="Estimated Premium ($)"
                         Format="N0" Class="mb-3" />
        <MudDatePicker @bind-Date="_effectiveDate" Label="Target Effective Date"
                       Class="mb-3" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="Submit" Disabled="@string.IsNullOrWhiteSpace(_name)">
            Create
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    private string _name = "";
    private decimal? _premium;
    private DateTime? _effectiveDate;
    private bool _saving;

    private void Cancel() => MudDialog.Cancel();

    private async Task Submit()
    {
        if (string.IsNullOrWhiteSpace(_name)) return;
        _saving = true;
        var userId = await UserSession.GetUserIdAsync();
        DateOnly? effDate = _effectiveDate.HasValue
            ? DateOnly.FromDateTime(_effectiveDate.Value)
            : null;
        var id = await OppService.CreateOpportunityAsync(_name.Trim(), userId, _premium, effDate);
        MudDialog.Close(DialogResult.Ok(id));
    }
}
```

---

## 6. `Components/Dialogs/CloseOpportunityDialog.razor`

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@using FamOs.Web.Domain

<MudDialog>
    <TitleContent>Close Opportunity</TitleContent>
    <DialogContent>
        <MudText Typo="Typo.body2" Class="mb-3">
            Select the reason this opportunity is closing without binding.
        </MudText>
        <MudSelect @bind-Value="_reason" Label="Close Reason" Required="true">
            <MudSelectItem Value="@("Pricing")">Pricing</MudSelectItem>
            <MudSelectItem Value="@("Coverage gap")">Coverage gap</MudSelectItem>
            <MudSelectItem Value="@("No response")">No response</MudSelectItem>
            <MudSelectItem Value="@("Timing")">Timing</MudSelectItem>
            <MudSelectItem Value="@("Lost relationship")">Lost relationship</MudSelectItem>
        </MudSelect>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Error"
                   OnClick="Submit" Disabled="@string.IsNullOrEmpty(_reason)">
            Close Opportunity
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid OpportunityId { get; set; }

    private string _reason = "";

    private void Cancel() => MudDialog.Cancel();

    private async Task Submit()
    {
        var userId = await UserSession.GetUserIdAsync();
        try
        {
            await Lifecycle.CloseOpportunityAsync(OpportunityId, _reason, userId);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (LifecycleValidationException ex)
        {
            // Surface validation error in dialog — do not close
        }
    }
}
```

---

## 7. `Components/Pages/Opportunity/OpportunityWorkspace.razor`

```razor
@page "/opportunity/{Id:guid}"
@attribute [Authorize]
@inject OpportunityService OppService
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject NavigationManager Nav
@inject IDialogService DialogService
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Services

<PageTitle>@(_opp?.Name ?? "Opportunity") — FAM OS</PageTitle>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
}
else if (_opp == null)
{
    <MudAlert Severity="Severity.Error">Opportunity not found.</MudAlert>
}
else
{
    <!-- Lifecycle header -->
    <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:20px; flex-wrap:wrap; gap:8px;">
        <div>
            <MudText Typo="Typo.h5" Style="margin-bottom:4px;">@_opp.Name</MudText>
            <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
                <MudChip Color="Color.Primary" Size="Size.Small">@GetStageLabel(_opp.LifecycleStage)</MudChip>
                <SignalChip Signal="_opp.DominantSignal" />
                @if (!string.IsNullOrEmpty(_opp.DominantSignalReason))
                {
                    <MudText Typo="Typo.caption" Color="Color.Secondary">@_opp.DominantSignalReason</MudText>
                }
            </div>
        </div>
        <div style="display:flex; gap:8px;">
            @if (!_opp.IsClosed)
            {
                <MudButton Variant="Variant.Outlined" Color="Color.Default" Size="Size.Small"
                           OnClick="ParkOpportunity">Park</MudButton>
                <MudButton Variant="Variant.Outlined" Color="Color.Error" Size="Size.Small"
                           OnClick="CloseOpportunity">Close</MudButton>
            }
        </div>
    </div>

    <!-- Stage-specific panel -->
    @switch (_opp.LifecycleStage)
    {
        case LifecycleStage.Intake:
            <IntakePanel Opportunity="_opp" OnAdvanced="Reload" />
            break;
        case LifecycleStage.UnderwritingPrep:
            <UnderwritingPrepPanel Opportunity="_opp" OnAdvanced="Reload" />
            break;
        case LifecycleStage.Marketed:
            <MarketedPanel Opportunity="_opp" OnAdvanced="Reload" />
            break;
        case LifecycleStage.QuotesReceived:
            <QuotesReceivedPanel Opportunity="_opp" OnAdvanced="Reload" />
            break;
        case LifecycleStage.ClientDecision:
            <ClientDecisionPanel Opportunity="_opp" OnAdvanced="Reload" />
            break;
        case LifecycleStage.Binding:
            <BindingPanel Opportunity="_opp" OnAdvanced="Reload" />
            break;
        case LifecycleStage.Bound:
            <BoundPanel Opportunity="_opp" />
            break;
        default:
            <MudText Color="Color.Secondary">This opportunity is @_opp.LifecycleStage.</MudText>
            break;
    }

    <!-- Activity timeline (last 20 events) -->
    @if (_opp.Activities.Any())
    {
        <MudText Typo="Typo.h6" Class="mt-6 mb-3">Activity</MudText>
        <MudTimeline TimelinePosition="TimelinePosition.Left">
            @foreach (var act in _opp.Activities.OrderByDescending(a => a.OccurredAt).Take(20))
            {
                <MudTimelineItem Color="Color.Primary" Size="Size.Small">
                    <MudText Typo="Typo.body2">@act.Description</MudText>
                    <MudText Typo="Typo.caption" Color="Color.Secondary">
                        @act.OccurredAt.ToLocalTime().ToString("MMM d, h:mm tt")
                    </MudText>
                </MudTimelineItem>
            }
        </MudTimeline>
    }
}

@code {
    [Parameter] public Guid Id { get; set; }

    private Opportunity? _opp;
    private bool _loading = true;

    protected override async Task OnInitializedAsync() => await LoadAsync();
    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _opp     = await OppService.GetByIdAsync(Id);
        _loading = false;
    }

    private async Task Reload() => await LoadAsync();

    private async Task ParkOpportunity()
    {
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.ParkOpportunityAsync(Id, userId);
            Snackbar.Add("Opportunity parked.", Severity.Info);
            await Reload();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task CloseOpportunity()
    {
        var dialog = await DialogService.ShowAsync<CloseOpportunityDialog>(
            "Close Opportunity",
            new DialogParameters { ["OpportunityId"] = Id });
        var result = await dialog.Result;
        if (!result.Canceled)
        {
            Snackbar.Add("Opportunity closed.", Severity.Info);
            Nav.NavigateTo("/pipeline");
        }
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
        LifecycleStage.ClosedNotBound   => "Closed",
        _                               => stage.ToString()
    };
}
```

---

## 8. Stage Panel Components

Each panel is self-contained. They receive `Opportunity` as a parameter and an `OnAdvanced` callback to trigger a workspace reload.

### Shared panel interface

All panels implement this pattern:
```csharp
[Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
[Parameter] public EventCallback OnAdvanced { get; set; }
```

### `Panels/IntakePanel.razor`

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Intake</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Gather required underwriting data to pursue this opportunity.
            </MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        <MudAlert Severity="Severity.Info" Class="mb-3">
            Collect policy documents, loss runs, and driver schedule before pursuing.
        </MudAlert>
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="PursueOpportunity" Disabled="_saving">
            Pursue Opportunity
        </MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }
    private bool _saving;

    private async Task PursueOpportunity()
    {
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.PursueOpportunityAsync(Opportunity.Id, userId);
            Snackbar.Add("Advanced to Underwriting Prep.", Severity.Success);
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

### `Panels/UnderwritingPrepPanel.razor`

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Underwriting Prep</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Complete underwriting data gathering and select carriers.
            </MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        <MudText Typo="Typo.body2">Enter the carriers to route this account to:</MudText>
        <MudTextField @bind-Value="_carriers" Label="Carriers (comma-separated)"
                      Class="mt-2" HelperText="e.g. Progressive, Great West, Canal" />
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="RouteToMarket" Disabled="@(_saving || string.IsNullOrWhiteSpace(_carriers))">
            Route to Market
        </MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }
    private string _carriers = "";
    private bool _saving;

    private async Task RouteToMarket()
    {
        _saving = true;
        try
        {
            var userId     = await UserSession.GetUserIdAsync();
            var carrierList = _carriers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            await Lifecycle.RouteToMarketAsync(Opportunity.Id, carrierList, userId);
            Snackbar.Add($"Routed to {carrierList.Length} carrier(s).", Severity.Success);
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

### `Panels/MarketedPanel.razor`

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Submitted to Market</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Awaiting carrier quotes. Record quotes as they arrive.
            </MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        @if (Opportunity.Submissions.Any())
        {
            <MudText Typo="Typo.subtitle2" Class="mb-2">Active Submissions</MudText>
            @foreach (var sub in Opportunity.Submissions)
            {
                <div style="display:flex;justify-content:space-between;padding:6px 0;border-bottom:1px solid var(--color-border);">
                    <MudText Typo="Typo.body2">@sub.CarrierName</MudText>
                    <MudChip Size="Size.Small" Color="Color.Default">@sub.Status</MudChip>
                </div>
            }
        }
        <MudDivider Class="my-3" />
        <MudText Typo="Typo.subtitle2" Class="mb-2">Record a Quote</MudText>
        <MudSelect @bind-Value="_selectedSubmissionId" Label="Carrier" Class="mb-2">
            @foreach (var sub in Opportunity.Submissions)
            {
                <MudSelectItem Value="sub.Id">@sub.CarrierName</MudSelectItem>
            }
        </MudSelect>
        <MudNumericField @bind-Value="_quotePremium" Label="Premium ($)" Format="N0" Class="mb-2" />
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="RecordQuote"
                   Disabled="@(_saving || _selectedSubmissionId == Guid.Empty || !_quotePremium.HasValue)">
            Record Quote
        </MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }
    private Guid _selectedSubmissionId;
    private decimal? _quotePremium;
    private bool _saving;

    private async Task RecordQuote()
    {
        _saving = true;
        try
        {
            var sub    = Opportunity.Submissions.First(s => s.Id == _selectedSubmissionId);
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.RecordQuoteAsync(Opportunity.Id, _selectedSubmissionId,
                sub.CarrierName, _quotePremium!.Value, null, userId);
            Snackbar.Add("Quote recorded.", Severity.Success);
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

### `Panels/QuotesReceivedPanel.razor`

Shows quote comparison table, lets user mark recommended quote and send proposal.

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Quote Comparison</MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        <MudTable Items="Opportunity.Quotes" Hover="true" Dense="true" Class="mb-3">
            <HeaderContent>
                <MudTh>Carrier</MudTh>
                <MudTh>Premium</MudTh>
                <MudTh>Recommended</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd>@context.CarrierName</MudTd>
                <MudTd>$@context.PremiumAmount.ToString("N0")</MudTd>
                <MudTd>
                    <MudRadio Value="context.Id" @bind-Value="_recommendedId"
                              Color="Color.Primary" />
                </MudTd>
            </RowTemplate>
        </MudTable>
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="SendProposal"
                   Disabled="@(_saving || _recommendedId == Guid.Empty)">
            Send Proposal
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Default"
                   OnClick="RecordAnotherQuote">
            + Add Quote
        </MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }
    private Guid _recommendedId;
    private bool _saving;
    private bool _showAddQuote;

    private async Task SendProposal()
    {
        _saving = true;
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            await Lifecycle.SendProposalAsync(Opportunity.Id, _recommendedId, userId);
            Snackbar.Add("Proposal sent.", Severity.Success);
            await OnAdvanced.InvokeAsync();
        }
        catch (LifecycleValidationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _saving = false; }
    }

    private void RecordAnotherQuote()
    {
        // Navigate back to carrier selection — in Sprint 3 this opens an inline drawer
        // For now, user can go back to Pipeline and re-enter Marketed panel (not ideal but functional)
        Snackbar.Add("Sprint 3: inline add-quote form. Use Pipeline to navigate.", Severity.Info);
    }
}
```

### `Panels/ClientDecisionPanel.razor`

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Client Decision</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Proposal sent. Awaiting client acceptance or re-market request.
            </MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        @if (Opportunity.ProposalSentAt.HasValue)
        {
            <MudText Typo="Typo.body2">
                Proposal sent @Opportunity.ProposalSentAt.Value.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt")
            </MudText>
        }
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Success"
                   OnClick="RequestBind" Disabled="_saving">
            Request Bind
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Default"
                   OnClick="ReopenMarket" Disabled="_saving">
            Reopen Market
        </MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }
    private bool _saving;

    private async Task RequestBind()
    {
        _saving = true;
        try
        {
            var userId      = await UserSession.GetUserIdAsync();
            var winningQuote = Opportunity.Quotes.FirstOrDefault(q => q.IsRecommended)
                ?? Opportunity.Quotes.First();
            await Lifecycle.RequestBindAsync(Opportunity.Id, winningQuote.Id, userId);
            Snackbar.Add("Bind requested.", Severity.Success);
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
}
```

### `Panels/BindingPanel.razor`

```razor
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Binding in Progress</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary">
                Awaiting carrier binder confirmation. Record when received.
            </MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        <MudDatePicker @bind-Date="_effectiveDate" Label="Policy Effective Date" />
    </MudCardContent>
    <MudCardActions>
        <MudButton Variant="Variant.Filled" Color="Color.Success"
                   OnClick="RecordBinder"
                   Disabled="@(_saving || !_effectiveDate.HasValue)">
            Binder Received — Mark Bound
        </MudButton>
    </MudCardActions>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }
    private DateTime? _effectiveDate;
    private bool _saving;

    private async Task RecordBinder()
    {
        _saving = true;
        try
        {
            var userId    = await UserSession.GetUserIdAsync();
            var effDate   = DateOnly.FromDateTime(_effectiveDate!.Value);
            await Lifecycle.RecordBinderReceivedAsync(Opportunity.Id, effDate, userId);
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

### `Panels/BoundPanel.razor`

```razor
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain

<MudCard Class="mb-4">
    <MudCardHeader>
        <CardHeaderContent>
            <MudText Typo="Typo.h6">Bound — Post-Bind Processing</MudText>
        </CardHeaderContent>
    </MudCardHeader>
    <MudCardContent>
        @if (Opportunity.PolicyShadow != null)
        {
            <MudText Typo="Typo.body2">
                Policy effective: @Opportunity.PolicyShadow.PolicyEffectiveDate?.ToString("MMM d, yyyy")
            </MudText>
            <MudText Typo="Typo.body2">
                Carrier: @Opportunity.PolicyShadow.CarrierName
            </MudText>
            @if (Opportunity.PolicyShadow.PremiumAmount.HasValue)
            {
                <MudText Typo="Typo.body2">
                    Premium: $@Opportunity.PolicyShadow.PremiumAmount.Value.ToString("N0")
                </MudText>
            }
        }
        <MudAlert Severity="Severity.Info" Class="mt-3">
            Complete post-bind tasks: deliver policy, issue certificates, confirm coverage start.
        </MudAlert>
    </MudCardContent>
</MudCard>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
}
```

---

## 9. `Components/Pages/Dashboard.razor` (replaces Sprint 1 stub)

```razor
@page "/"
@attribute [Authorize]
@inject OpportunityService OppService
@inject UserSessionService UserSession
@inject NavigationManager Nav
@using FamOs.Web.Domain
@using FamOs.Web.Services

<PageTitle>Dashboard — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Dashboard</MudText>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
}
else if (_summary != null)
{
    <MudGrid Spacing="3" Class="mb-6">
        <MudItem xs="12" sm="6" md="3">
            <MudCard Elevation="1">
                <MudCardContent Class="text-center pa-4">
                    <MudText Typo="Typo.h4" Style="color:var(--mud-palette-primary)">
                        @_summary.TotalActive
                    </MudText>
                    <MudText Typo="Typo.body2" Color="Color.Secondary">Active Opportunities</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
        <MudItem xs="12" sm="6" md="3">
            <MudCard Elevation="1" Style="border-left: 4px solid #DC2626;">
                <MudCardContent Class="text-center pa-4">
                    <MudText Typo="Typo.h4" Style="color:#DC2626">@_summary.TimeRiskCount</MudText>
                    <MudText Typo="Typo.body2" Color="Color.Secondary">Time Risk</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
        <MudItem xs="12" sm="6" md="3">
            <MudCard Elevation="1" Style="border-left: 4px solid #D97706;">
                <MudCardContent Class="text-center pa-4">
                    <MudText Typo="Typo.h4" Style="color:#D97706">@_summary.DecisionNeeded</MudText>
                    <MudText Typo="Typo.body2" Color="Color.Secondary">Decision Needed</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
        <MudItem xs="12" sm="6" md="3">
            <MudCard Elevation="1" Style="border-left: 4px solid #059669;">
                <MudCardContent Class="text-center pa-4">
                    <MudText Typo="Typo.h4" Style="color:#059669">@_summary.BoundThisMonth</MudText>
                    <MudText Typo="Typo.body2" Color="Color.Secondary">Bound This Month</MudText>
                </MudCardContent>
            </MudCard>
        </MudItem>
    </MudGrid>

    <MudButton Variant="Variant.Outlined" Color="Color.Primary"
               OnClick="@(() => Nav.NavigateTo("/pipeline"))">
        Open Pipeline →
    </MudButton>
}

@code {
    private DashboardSummary? _summary;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        _summary = await OppService.GetDashboardSummaryAsync();
        _loading = false;
    }
}
```

---

## 10. `Program.cs` Change

Add one line to the service registrations section (after `LifecycleCommandService`):

```csharp
builder.Services.AddScoped<OpportunityService>();
```

---

## Acceptance Criteria

1. Pipeline Board loads at `/pipeline` — 7 columns visible (Intake through Bound)
2. "New Opportunity" button opens dialog; submitting creates an opportunity and navigates to workspace
3. Opportunity workspace shows correct stage panel based on `LifecycleStage`
4. Clicking "Pursue Opportunity" on an INTAKE opportunity → stage advances to UnderwritingPrep, activity log entry created
5. Full lifecycle path walkable: INTAKE → UW_PREP → MARKETED → QUOTES_RECEIVED → CLIENT_DECISION → BINDING → BOUND
6. `GET /health` still returns 200 after Sprint 2 deploy
7. `/_content/FipShared/css/fip-tokens.css` returns 200
8. Signal chip displays correct color and label for each `DominantSignal` value
9. Dashboard shows correct counts (TotalActive, TimeRiskCount, DecisionNeeded, BoundThisMonth)
10. Closing an opportunity removes it from the active Pipeline Board

---

## Clint Review Priorities

```
⚠️  HIGH: OpportunityWorkspace switch on LifecycleStage — verify ALL stage cases covered.
          Missing ClosedNotBound case will show blank content when viewing a closed
          opportunity. The default: branch handles it, but confirm it renders gracefully.

⚠️  HIGH: LifecycleCommandService uses optimistic concurrency (Version column as
          IsConcurrencyToken). EF Core will throw DbUpdateConcurrencyException if
          two users hit the same opportunity simultaneously. Verify OpportunityWorkspace
          catches this and shows a "please refresh" message (not an unhandled exception).

⚠️  MEDIUM: SignalChip is used in both Pipeline and Workspace — any render issue will
            affect both screens. Test with all 9 DominantSignal values.

⚠️  MEDIUM: ClientDecisionPanel.RequestBind falls back to Quotes.First() if no
            recommended quote. This should never happen if SendProposal ran correctly
            (it marks a recommended quote), but confirm the fallback is safe.

⚠️  LOW: Dashboard summary shows "my opportunities" only or all? Current implementation
         shows all (ownerUserId = null). For the pilot (single ER), this is fine.
         Sprint 3 can add per-user filtering.
```

---

_Spec by Reed Richards | Sprint 2 = 13 new files, 3 modified. Full lifecycle execution via UI. Signal chips. Pipeline Kanban. Dashboard summary._
