# FAM OS Sprint 4 Spec — Intake Form + Task Center

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19  
**Sprint Goal:** Make ERs productive at the two points that matter most — starting an opportunity correctly (Intake) and knowing what to do next (Task Center)  
**Prerequisite:** Sprint 3 deployed and verified  
**Spec references:** `FAMOS-ARCHITECTURE-SPEC.md`, `FAMOS-SPRINT1-SPEC.md`, `FAMOS-SPRINT2-SPEC.md`

---

## Why These Two Features

Every sprint choice has an opportunity cost. Here's the reasoning:

**Intake form** is the entry point for every opportunity. The current `IntakePanel` is a stub — one "Pursue Opportunity" button. ERs have no structured place to record what they know about the account at INTAKE: fleet size, effective date, loss history, contact info. Without this, the pipeline fills up with bare-minimum records and the lifecycle engine has nothing to evaluate.

**Task Center** is the answer to "what do I do right now?" across all stages. Tasks are already in the data model (`FamOsTask`), already loaded in `GetByIdAsync()`, already shown (partially) in the `OpportunityWorkspace` — but there is no way to view all open tasks, complete them, or add new ones outside of the workspace. The `TaskCenter` page is a complete stub. ERs managing 50–100 opportunities need a unified work queue, not 50 browser tabs.

**Why not submission/quote/proposal first?** Those workflows exist in the panels from Sprint 2 — rough but functional. An ER can record a quote and send a proposal today. The workflows are incomplete, but unblocked. Intake and Task Center are 100% missing.

---

## Sprint 4 Scope

**Part A — Intake Form** (~40% of sprint effort)
- Replace `IntakePanel` stub with a multi-section questionnaire form
- Fields scoped to Phase 1 TIG trucking program (generic enough to extend later)
- Persist responses as `Opportunity.IntakeResponsesJson` (JSON string, already in architecture spec)
- Add `IntakeResponsesJson` field to `Opportunity` entity (not yet in Sprint 1 implementation — needs migration)
- "Pursue Opportunity" button remains; validates required fields before allowing advance

**Part B — Task Center** (~60% of sprint effort)
- Full `TaskCenter.razor` page: all open tasks across all opportunities, filterable by opportunity/stage/due date
- Task completion inline (click → done)
- Task creation: add a task to any opportunity from the Task Center (not just from the workspace)
- Task auto-generation: lifecycle transitions fire `CreateTasksForStageAsync()` — predefined tasks per stage
- Open task count badge on nav menu item

---

## Parallelization Map

**Sequential — single CC session.** Both parts share `OpportunityService` additions and `FamOsDbContext` entity changes. The task auto-generation hooks into `LifecycleCommandService`, which also touches the entity model. Run sequentially.

**Order:**
1. Entity change: add `IntakeResponsesJson` to `Opportunity` + `AffinityGroupId` FK (if not already present from Sprint 1)
2. `TaskService.cs` (new) — all task read/write/complete/create methods
3. `LifecycleCommandService.cs` — add `CreateTasksForStageAsync()` calls inside each command
4. `IntakePanel.razor` — replace stub with full form
5. `TaskCenter.razor` — replace stub with full work queue
6. `OpportunityWorkspace.razor` — minor: wire task count badge and task completion
7. `MainLayout.razor` / `NavMenu.razor` — task count badge on nav item

---

## File List

### New Files
```
fip/famos/src/FamOs.Web/Services/TaskService.cs
fip/famos/src/FamOs.Web/Domain/StageTaskTemplates.cs
```

### Modified Files
```
fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs        (add IntakeResponsesJson)
fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs              (add column mapping)
fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs   (add CreateTasksForStageAsync calls)
fip/famos/src/FamOs.Web/Services/OpportunityService.cs      (add task-aware query methods)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/IntakePanel.razor   (full replacement)
fip/famos/src/FamOs.Web/Components/Pages/TaskCenter.razor   (full replacement)
fip/famos/src/FamOs.Web/Components/Layout/NavMenu.razor     (task count badge)
```

**DO NOT touch:** FAIT, FIRM, FORMS, FipShared, any Sprint 3 style files beyond what's listed above.

**Total: 2 new files, 7 modified files.**

---

## Part A — Intake Form

### A1. `Opportunity.cs` — Add `IntakeResponsesJson`

Add one property after `EffectiveDateTarget`:

```csharp
/// <summary>
/// JSON object mapping intake field IDs to user-entered values.
/// Structure: { "fleet_size": "42", "dot_number": "123456", ... }
/// Schema is affinity-group-specific; Phase 1 hardcoded for trucking program.
/// </summary>
public string? IntakeResponsesJson { get; set; }
```

### A2. `FamOsDbContext.cs` — Add Column Mapping

In the `Opportunity` entity configuration block, add:

```csharp
e.Property(x => x.IntakeResponsesJson)
    .HasColumnName("intake_responses_json")
    .HasColumnType("mediumtext");
```

The `DatabaseInitializationService`/`CreateTablesAsync` pattern will add this column automatically on next startup if the table was previously created without it — **only if the app is running `CreateTablesAsync` on each boot**. Verify this is still the case from Sprint 1. If tables already exist with the column missing, the startup migration logic needs an `ALTER TABLE IF NOT EXISTS COLUMN` guard.

**Add to `CreateTablesAsync` idempotent column-add block:**

```sql
-- Run after table creation check, in the same startup method
ALTER TABLE opportunities
    ADD COLUMN IF NOT EXISTS intake_responses_json MEDIUMTEXT NULL;
```

Add this SQL execution inside the existing DB initialization background task in `Program.cs`, after the `CreateTablesAsync()` call:

```csharp
// Sprint 4: add intake_responses_json column if missing (idempotent)
await db.Database.ExecuteSqlRawAsync(
    "ALTER TABLE opportunities ADD COLUMN IF NOT EXISTS intake_responses_json MEDIUMTEXT NULL");
```

### A3. `IntakePanel.razor` — Full Replacement

Replace the stub. The intake form captures the key underwriting data an ER needs before pursuing the opportunity. Phase 1 hardcodes the trucking program fields — the dynamic questionnaire renderer is Sprint 5+.

```razor
@namespace FamOs.Web.Components.Panels
@inject LifecycleCommandService Lifecycle
@inject UserSessionService UserSession
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using System.Text.Json

<div class="intake-form-container">

    @* ── Header ──────────────────────────────────────────────────── *@
    <div class="section-header mb-4">
        <MudText Typo="Typo.h6" Style="color: var(--navy);">Intake Questionnaire</MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">
            Complete before pursuing. Required fields marked with *.
        </MudText>
    </div>

    @* ── Section 1: Account Information ─────────────────────────── *@
    <div class="intake-section mb-4">
        <div class="intake-section-label">Account Information</div>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_contactName"
                    Label="Primary Contact Name *"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_contactEmail"
                    Label="Contact Email"
                    InputType="InputType.Email"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_contactPhone"
                    Label="Contact Phone"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_stateOfDomicile"
                    Label="State of Domicile *"
                    Placeholder="e.g. TX"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
        </MudGrid>
    </div>

    @* ── Section 2: Fleet Information ───────────────────────────── *@
    <div class="intake-section mb-4">
        <div class="intake-section-label">Fleet Information</div>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="4">
                <MudNumericField @bind-Value="_fleetSize"
                    Label="Total Power Units *"
                    Variant="Variant.Outlined" Dense="true" Min="1" />
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudNumericField @bind-Value="_trailerCount"
                    Label="Trailers"
                    Variant="Variant.Outlined" Dense="true" Min="0" />
            </MudItem>
            <MudItem xs="12" sm="4">
                <MudTextField @bind-Value="_dotNumber"
                    Label="DOT Number *"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_commodities"
                    Label="Primary Commodities Hauled"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudTextField @bind-Value="_operatingRadius"
                    Label="Operating Radius"
                    Placeholder="e.g. Regional, National"
                    Variant="Variant.Outlined" Dense="true" />
            </MudItem>
        </MudGrid>
    </div>

    @* ── Section 3: Coverage Requirements ───────────────────────── *@
    <div class="intake-section mb-4">
        <div class="intake-section-label">Coverage Requirements</div>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="6">
                <MudNumericField @bind-Value="_liabilityLimit"
                    Label="Liability Limit Required ($)"
                    Format="N0" Variant="Variant.Outlined" Dense="true" />
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudSelect @bind-Value="_cargoType"
                    Label="Cargo Type"
                    Variant="Variant.Outlined" Dense="true">
                    <MudSelectItem Value="@("")">— Select —</MudSelectItem>
                    <MudSelectItem Value="@("dry_van")">Dry Van</MudSelectItem>
                    <MudSelectItem Value="@("refrigerated")">Refrigerated</MudSelectItem>
                    <MudSelectItem Value="@("flatbed")">Flatbed</MudSelectItem>
                    <MudSelectItem Value="@("tanker")">Tanker</MudSelectItem>
                    <MudSelectItem Value="@("intermodal")">Intermodal</MudSelectItem>
                    <MudSelectItem Value="@("other")">Other</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12">
                <MudCheckBox @bind-Value="_needsCargo" Label="Cargo coverage needed" Dense="true" />
                <MudCheckBox @bind-Value="_needsPhysicalDamage" Label="Physical damage needed" Dense="true" />
                <MudCheckBox @bind-Value="_needsGeneralLiability" Label="General liability needed" Dense="true" />
            </MudItem>
        </MudGrid>
    </div>

    @* ── Section 4: Loss History ─────────────────────────────────── *@
    <div class="intake-section mb-4">
        <div class="intake-section-label">Loss History</div>
        <MudGrid Spacing="2">
            <MudItem xs="12" sm="6">
                <MudSelect @bind-Value="_lossRunsAvailable"
                    Label="Years of Loss Runs Available *"
                    Variant="Variant.Outlined" Dense="true">
                    <MudSelectItem Value="@("")">— Select —</MudSelectItem>
                    <MudSelectItem Value="@("0")">None available</MudSelectItem>
                    <MudSelectItem Value="@("1")">1 year</MudSelectItem>
                    <MudSelectItem Value="@("2")">2 years</MudSelectItem>
                    <MudSelectItem Value="@("3")">3 years</MudSelectItem>
                    <MudSelectItem Value="@("4+")">4+ years</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12" sm="6">
                <MudSelect @bind-Value="_priorCarrier"
                    Label="Currently Insured With"
                    Variant="Variant.Outlined" Dense="true">
                    <MudSelectItem Value="@("")">Unknown / New venture</MudSelectItem>
                    <MudSelectItem Value="@("great_west")">Great West Casualty</MudSelectItem>
                    <MudSelectItem Value="@("progressive")">Progressive Commercial</MudSelectItem>
                    <MudSelectItem Value="@("canal")">Canal Insurance</MudSelectItem>
                    <MudSelectItem Value="@("other")">Other</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12">
                <MudTextField @bind-Value="_lossNotes"
                    Label="Loss history notes (claims in last 3 years)"
                    Lines="2" Variant="Variant.Outlined" />
            </MudItem>
        </MudGrid>
    </div>

    @* ── Validation errors ───────────────────────────────────────── *@
    @if (_validationErrors.Any())
    {
        <MudAlert Severity="Severity.Error" Class="mb-3">
            @foreach (var err in _validationErrors)
            {
                <div>• @err</div>
            }
        </MudAlert>
    }

    @* ── Save + Pursue ───────────────────────────────────────────── *@
    <div style="display:flex; gap:8px; flex-wrap:wrap;">
        <MudButton Variant="Variant.Outlined" Color="Color.Default"
                   OnClick="SaveDraft" Disabled="_saving">
            Save Draft
        </MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="PursueOpportunity" Disabled="_saving">
            Save & Pursue Opportunity →
        </MudButton>
    </div>

</div>

@code {
    [Parameter, EditorRequired] public Opportunity Opportunity { get; set; } = default!;
    [Parameter] public EventCallback OnAdvanced { get; set; }

    private bool _saving;
    private List<string> _validationErrors = new();

    // Section 1: Account
    private string _contactName          = "";
    private string _contactEmail         = "";
    private string _contactPhone         = "";
    private string _stateOfDomicile      = "";

    // Section 2: Fleet
    private int?   _fleetSize            = null;
    private int?   _trailerCount         = null;
    private string _dotNumber            = "";
    private string _commodities          = "";
    private string _operatingRadius      = "";

    // Section 3: Coverage
    private decimal? _liabilityLimit     = null;
    private string   _cargoType          = "";
    private bool     _needsCargo         = false;
    private bool     _needsPhysicalDamage = false;
    private bool     _needsGeneralLiability = false;

    // Section 4: Loss history
    private string _lossRunsAvailable    = "";
    private string _priorCarrier         = "";
    private string _lossNotes            = "";

    protected override void OnInitialized()
    {
        // Populate from existing responses if already saved
        if (!string.IsNullOrEmpty(Opportunity.IntakeResponsesJson))
        {
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    Opportunity.IntakeResponsesJson) ?? new();

                _contactName           = Get(d, "contact_name");
                _contactEmail          = Get(d, "contact_email");
                _contactPhone          = Get(d, "contact_phone");
                _stateOfDomicile       = Get(d, "state_of_domicile");
                _dotNumber             = Get(d, "dot_number");
                _commodities           = Get(d, "commodities");
                _operatingRadius       = Get(d, "operating_radius");
                _cargoType             = Get(d, "cargo_type");
                _priorCarrier          = Get(d, "prior_carrier");
                _lossRunsAvailable     = Get(d, "loss_runs_available");
                _lossNotes             = Get(d, "loss_notes");

                if (int.TryParse(Get(d, "fleet_size"), out var fs))    _fleetSize = fs;
                if (int.TryParse(Get(d, "trailer_count"), out var tc)) _trailerCount = tc;
                if (decimal.TryParse(Get(d, "liability_limit"), out var ll)) _liabilityLimit = ll;
                if (bool.TryParse(Get(d, "needs_cargo"), out var nc))  _needsCargo = nc;
                if (bool.TryParse(Get(d, "needs_pd"), out var npd))    _needsPhysicalDamage = npd;
                if (bool.TryParse(Get(d, "needs_gl"), out var ngl))    _needsGeneralLiability = ngl;
            }
            catch { /* Ignore deserialization errors — start fresh */ }
        }
    }

    private static string Get(Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : "";

    private Dictionary<string, string> BuildResponseDict() => new()
    {
        ["contact_name"]        = _contactName,
        ["contact_email"]       = _contactEmail,
        ["contact_phone"]       = _contactPhone,
        ["state_of_domicile"]   = _stateOfDomicile,
        ["fleet_size"]          = _fleetSize?.ToString() ?? "",
        ["trailer_count"]       = _trailerCount?.ToString() ?? "",
        ["dot_number"]          = _dotNumber,
        ["commodities"]         = _commodities,
        ["operating_radius"]    = _operatingRadius,
        ["liability_limit"]     = _liabilityLimit?.ToString() ?? "",
        ["cargo_type"]          = _cargoType,
        ["needs_cargo"]         = _needsCargo.ToString(),
        ["needs_pd"]            = _needsPhysicalDamage.ToString(),
        ["needs_gl"]            = _needsGeneralLiability.ToString(),
        ["loss_runs_available"] = _lossRunsAvailable,
        ["prior_carrier"]       = _priorCarrier,
        ["loss_notes"]          = _lossNotes,
    };

    private List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_contactName))    errors.Add("Primary contact name is required");
        if (!_fleetSize.HasValue || _fleetSize < 1)     errors.Add("Fleet size is required");
        if (string.IsNullOrWhiteSpace(_dotNumber))      errors.Add("DOT number is required");
        if (string.IsNullOrWhiteSpace(_stateOfDomicile)) errors.Add("State of domicile is required");
        if (string.IsNullOrWhiteSpace(_lossRunsAvailable)) errors.Add("Loss runs availability is required");
        return errors;
    }

    private async Task SaveDraft()
    {
        _saving = true;
        _validationErrors.Clear();
        var userId = await UserSession.GetUserIdAsync();
        await Lifecycle.SaveIntakeResponsesAsync(
            Opportunity.Id, BuildResponseDict(), userId);
        Opportunity.IntakeResponsesJson =
            JsonSerializer.Serialize(BuildResponseDict());
        Snackbar.Add("Intake saved.", Severity.Success);
        _saving = false;
    }

    private async Task PursueOpportunity()
    {
        _validationErrors = Validate();
        if (_validationErrors.Any()) return;

        _saving = true;
        var userId = await UserSession.GetUserIdAsync();
        await Lifecycle.SaveIntakeResponsesAsync(
            Opportunity.Id, BuildResponseDict(), userId);
        await Lifecycle.PursueOpportunityAsync(Opportunity.Id, userId);
        Snackbar.Add("Advanced to Underwriting Prep.", Severity.Success);
        await OnAdvanced.InvokeAsync();
        _saving = false;
    }
}
```

### A4. `LifecycleCommandService.cs` — Add `SaveIntakeResponsesAsync`

Add one new method (not a lifecycle command — doesn't change stage, just persists the draft):

```csharp
/// <summary>
/// Saves intake questionnaire responses to the opportunity.
/// Does NOT advance the lifecycle stage. Can be called multiple times (draft saves).
/// </summary>
public async Task SaveIntakeResponsesAsync(
    Guid opportunityId,
    Dictionary<string, string> responses,
    string actorUserId)
{
    await using var tx = await _db.Database.BeginTransactionAsync();
    var opp = await LoadOpportunityAsync(opportunityId);

    opp.IntakeResponsesJson = System.Text.Json.JsonSerializer.Serialize(responses);
    opp.UpdatedAt           = DateTime.UtcNow;

    await WriteActivityAsync(opp.Id, "intake_saved",
        "Intake questionnaire saved", actorUserId);

    await _db.SaveChangesAsync();
    await tx.CommitAsync();
}
```

---

## Part B — Task Center

### B1. `Domain/StageTaskTemplates.cs` — Task Definitions Per Stage

When a lifecycle command fires and an opportunity enters a new stage, predefined tasks are auto-created. These are the "what do I need to do now?" prompts the ER sees.

```csharp
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
```

### B2. `Services/TaskService.cs`

All task read/write/complete operations. Single source of truth for task mutations outside of `LifecycleCommandService`.

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public class TaskService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly ILogger<TaskService> _logger;

    public TaskService(IDbContextFactory<FamOsDbContext> dbFactory,
        ILogger<TaskService> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    /// <summary>All open tasks for a user's opportunities — for the Task Center.</summary>
    public async Task<List<TaskWithOpportunity>> GetOpenTasksForUserAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var results = await db.Tasks
            .Include(t => t.Opportunity)
            .Where(t => t.Status == "open"
                && t.Opportunity.OwnerUserId == userId
                && !t.Opportunity.IsClosed)
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)  // tasks with due dates first
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();

        return results.Select(t => new TaskWithOpportunity(t, t.Opportunity)).ToList();
    }

    /// <summary>All open tasks across all opportunities (admin view).</summary>
    public async Task<List<TaskWithOpportunity>> GetAllOpenTasksAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var results = await db.Tasks
            .Include(t => t.Opportunity)
            .Where(t => t.Status == "open" && !t.Opportunity.IsClosed)
            .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync();
        return results.Select(t => new TaskWithOpportunity(t, t.Opportunity)).ToList();
    }

    /// <summary>Mark a task done.</summary>
    public async Task CompleteTaskAsync(Guid taskId, string actorUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.FindAsync(taskId)
            ?? throw new NotFoundException($"Task {taskId} not found");

        task.Status    = "done";
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("[Task] Completed {TaskId} by {User}", taskId, actorUserId);
    }

    /// <summary>Create a manual task on an opportunity.</summary>
    public async Task<Guid> CreateTaskAsync(
        Guid opportunityId, string title, DateTime? dueAt, string? assignedToUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var task = new FamOsTask
        {
            OpportunityId    = opportunityId,
            Title            = title,
            Status           = "open",
            DueAt            = dueAt,
            AssignedToUserId = assignedToUserId,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    /// <summary>Count of open tasks for a user — for nav badge.</summary>
    public async Task<int> GetOpenTaskCountForUserAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Tasks
            .Where(t => t.Status == "open"
                && t.Opportunity.OwnerUserId == userId
                && !t.Opportunity.IsClosed)
            .CountAsync();
    }
}

public record TaskWithOpportunity(FamOsTask Task, Opportunity Opportunity);
```

**Add to `Program.cs`:**
```csharp
builder.Services.AddScoped<TaskService>();
```

### B3. `LifecycleCommandService.cs` — Auto-Generate Tasks on Stage Transition

Add `CreateTasksForStageAsync()` as a private helper and call it in the relevant command methods.

**New private method (add to the helpers section at the bottom of the class):**

```csharp
private async Task CreateTasksForStageAsync(Guid opportunityId, LifecycleStage stage)
{
    var templates = StageTaskTemplates.ForStage(stage);
    foreach (var title in templates)
    {
        _db.Tasks.Add(new FamOsTask
        {
            OpportunityId = opportunityId,
            Title         = title,
            Status        = "open",
        });
    }
    // Tasks are saved in the calling method's SaveChangesAsync() — do not call SaveChanges here.
    await Task.CompletedTask;
}
```

**Call sites — add `await CreateTasksForStageAsync(...)` before each `SaveChangesAsync()` in:**

| Method | After which line | Stage arg |
|--------|-----------------|-----------|
| `PursueOpportunityAsync` | After `opp.LifecycleStage = LifecycleStage.UnderwritingPrep` | `LifecycleStage.UnderwritingPrep` |
| `RouteToMarketAsync` | After `opp.LifecycleStage = LifecycleStage.Marketed` | `LifecycleStage.Marketed` |
| `RecordQuoteAsync` (first quote only) | After `opp.LifecycleStage = LifecycleStage.QuotesReceived` | `LifecycleStage.QuotesReceived` |
| `SendProposalAsync` | After `opp.LifecycleStage = LifecycleStage.ClientDecision` | `LifecycleStage.ClientDecision` |
| `RequestBindAsync` | After `opp.LifecycleStage = LifecycleStage.Binding` | `LifecycleStage.Binding` |
| `RecordBinderReceivedAsync` | After `opp.LifecycleStage = LifecycleStage.Bound` | `LifecycleStage.Bound` |

**Note:** `CreateTasksForStageAsync` adds `FamOsTask` entities to the tracked DbContext — they are written in the same `SaveChangesAsync()` + `CommitAsync()` call already in each method. No additional save needed.

### B4. `TaskCenter.razor` — Full Replacement

```razor
@page "/tasks"
@attribute [Authorize]
@inject TaskService TaskSvc
@inject UserSessionService UserSession
@inject NavigationManager Nav
@inject ISnackbar Snackbar
@inject IDialogService DialogService
@using FamOs.Web.Data.Entities
@using FamOs.Web.Domain
@using FamOs.Web.Services

<PageTitle>Task Center — FAM OS</PageTitle>

<div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:20px; flex-wrap:wrap; gap:8px;">
    <div>
        <MudText Typo="Typo.h5" Style="color:var(--navy);">Task Center</MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">
            @_tasks.Count open task@(_tasks.Count == 1 ? "" : "s") across @_tasks.Select(t => t.Opportunity.Id).Distinct().Count() opportunities
        </MudText>
    </div>
    <div style="display:flex; gap:8px; align-items:center; flex-wrap:wrap;">
        <MudTextField @bind-Value="_filterText"
            Placeholder="Filter by opportunity or task..."
            Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search"
            Clearable="true" Dense="true" Variant="Variant.Outlined"
            Style="min-width:220px;" />
        <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                   StartIcon="@Icons.Material.Filled.Add"
                   OnClick="OpenAddTaskDialog">
            Add Task
        </MudButton>
    </div>
</div>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" Class="mb-4" />
}
else if (!FilteredTasks.Any())
{
    <MudPaper Class="pa-6 text-center" Elevation="0"
              Style="border:1px solid var(--border); border-radius:12px;">
        <MudIcon Icon="@Icons.Material.Filled.CheckCircle"
                 Style="font-size:48px; color:var(--green); margin-bottom:12px;" />
        <MudText Typo="Typo.h6">
            @(string.IsNullOrEmpty(_filterText) ? "All clear — no open tasks" : "No tasks match your filter")
        </MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">
            @(string.IsNullOrEmpty(_filterText)
                ? "New tasks are auto-generated when opportunities advance through the pipeline."
                : "Try clearing the filter to see all tasks.")
        </MudText>
    </MudPaper>
}
else
{
    @* Group tasks by opportunity *@
    @foreach (var group in FilteredTasks
        .GroupBy(t => t.Opportunity.Id)
        .OrderBy(g => g.Min(t => t.Task.DueAt.HasValue ? 0 : 1))
        .ThenBy(g => g.Min(t => t.Task.DueAt)))
    {
        var opp = group.First().Opportunity;

        <MudPaper Class="mb-3" Elevation="0"
                  Style="border:1px solid var(--border); border-radius:12px; overflow:hidden;">
            @* Opportunity header row *@
            <div style="display:flex; justify-content:space-between; align-items:center;
                        padding:10px 16px; background:var(--cream); border-bottom:1px solid var(--border);
                        cursor:pointer;"
                 @onclick="() => Nav.NavigateTo($"/opportunity/{opp.Id}")">
                <div style="display:flex; align-items:center; gap:10px;">
                    <MudText Typo="Typo.subtitle2" Style="color:var(--navy); font-weight:600;">
                        @opp.Name
                    </MudText>
                    <MudChip T="string" Size="Size.Small" Color="Color.Primary"
                             Style="font-size:10px; height:18px;">
                        @GetStageLabel(opp.LifecycleStage)
                    </MudChip>
                    <SignalChip Signal="opp.DominantSignal" />
                </div>
                <div style="display:flex; align-items:center; gap:6px; color:var(--muted); font-size:12px;">
                    <span>@group.Count() task@(group.Count() == 1 ? "" : "s")</span>
                    <MudIcon Icon="@Icons.Material.Filled.OpenInNew" Style="font-size:14px;" />
                </div>
            </div>

            @* Task rows *@
            @foreach (var item in group.OrderBy(t => t.Task.DueAt.HasValue ? 0 : 1).ThenBy(t => t.Task.DueAt))
            {
                var isOverdue = item.Task.DueAt.HasValue && item.Task.DueAt.Value < DateTime.UtcNow;
                <div style="display:flex; align-items:center; padding:10px 16px;
                            border-bottom:1px solid var(--border); gap:10px;"
                     class="task-row">
                    <MudCheckBox T="bool" Value="false"
                                 ValueChanged="@(async _ => await CompleteTask(item.Task.Id))"
                                 Color="Color.Primary" Dense="true" />
                    <div style="flex:1; min-width:0;">
                        <MudText Typo="Typo.body2"
                                 Style="@(isOverdue ? "color: var(--red);" : "")">
                            @item.Task.Title
                        </MudText>
                    </div>
                    @if (item.Task.DueAt.HasValue)
                    {
                        <MudText Typo="Typo.caption"
                                 Style="@($"color:{(isOverdue ? "var(--red)" : "var(--muted)")}; white-space:nowrap;")">
                            @(isOverdue ? "Overdue · " : "")@item.Task.DueAt.Value.ToLocalTime().ToString("MMM d")
                        </MudText>
                    }
                </div>
            }
        </MudPaper>
    }
}

@code {
    private List<TaskWithOpportunity> _tasks = new();
    private bool _loading = true;
    private string _filterText = "";

    private IEnumerable<TaskWithOpportunity> FilteredTasks =>
        string.IsNullOrWhiteSpace(_filterText)
            ? _tasks
            : _tasks.Where(t =>
                t.Opportunity.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
                t.Task.Title.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

    protected override async Task OnInitializedAsync()
    {
        var userId = await UserSession.GetUserIdAsync();
        _tasks   = await TaskSvc.GetOpenTasksForUserAsync(userId);
        _loading = false;
    }

    private async Task CompleteTask(Guid taskId)
    {
        var userId = await UserSession.GetUserIdAsync();
        await TaskSvc.CompleteTaskAsync(taskId, userId);
        _tasks.RemoveAll(t => t.Task.Id == taskId);
        Snackbar.Add("Task done.", Severity.Success);
        StateHasChanged();
    }

    private async Task OpenAddTaskDialog()
    {
        var dialog = await DialogService.ShowAsync<AddTaskDialog>("Add Task");
        var result = await dialog.Result;
        if (!result.Canceled)
        {
            // Reload to show newly created task
            var userId = await UserSession.GetUserIdAsync();
            _tasks = await TaskSvc.GetOpenTasksForUserAsync(userId);
            StateHasChanged();
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
        _                               => stage.ToString()
    };
}
```

**Add to `famos.css`:**
```css
.task-row:hover {
    background: var(--cream);
}
.task-row:last-child {
    border-bottom: none;
}
```

### B5. `AddTaskDialog.razor` (new file)

```
fip/famos/src/FamOs.Web/Components/Dialogs/AddTaskDialog.razor
```

```razor
@inject TaskService TaskSvc
@inject OpportunityService OppService
@inject UserSessionService UserSession
@using FamOs.Web.Data.Entities

<MudDialog>
    <TitleContent>Add Task</TitleContent>
    <DialogContent>
        <MudAutocomplete T="Opportunity"
            Label="Opportunity *"
            @bind-Value="_selectedOpp"
            SearchFunc="SearchOpps"
            ToStringFunc="@(o => o?.Name ?? "")"
            Variant="Variant.Outlined" Dense="true" Class="mb-3" />
        <MudTextField @bind-Value="_title"
            Label="Task *"
            Placeholder="What needs to be done?"
            Variant="Variant.Outlined" Class="mb-3" />
        <MudDatePicker @bind-Date="_dueDate"
            Label="Due Date (optional)"
            Variant="Variant.Outlined" Class="mb-3" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="Submit"
                   Disabled="@(_selectedOpp == null || string.IsNullOrWhiteSpace(_title))">
            Add Task
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    private Opportunity? _selectedOpp;
    private string _title = "";
    private DateTime? _dueDate;
    private List<Opportunity> _allOpps = new();

    protected override async Task OnInitializedAsync()
    {
        var pipeline = await OppService.GetPipelineAsync();
        _allOpps = pipeline;
    }

    private Task<IEnumerable<Opportunity>> SearchOpps(string text, CancellationToken ct)
    {
        var results = string.IsNullOrWhiteSpace(text)
            ? _allOpps
            : _allOpps.Where(o => o.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(results);
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task Submit()
    {
        if (_selectedOpp == null || string.IsNullOrWhiteSpace(_title)) return;
        var dueAt = _dueDate.HasValue ? (DateTime?)_dueDate.Value : null;
        await TaskSvc.CreateTaskAsync(_selectedOpp.Id, _title.Trim(), dueAt, null);
        MudDialog.Close(DialogResult.Ok(true));
    }
}
```

**Add to file list (forgot to include above):**
```
fip/famos/src/FamOs.Web/Components/Dialogs/AddTaskDialog.razor   ← new file
```

### B6. `NavMenu.razor` — Task Count Badge

In the Task Center nav link, show a badge with the open task count. Replace the existing plain `MudNavLink` for tasks:

```razor
@* Add at top of @code section: *@
@inject TaskService TaskSvc
@inject UserSessionService UserSession

@* Replace Tasks nav link: *@
<MudNavLink Href="/tasks" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.CheckBox">
    <div style="display:flex; justify-content:space-between; align-items:center; width:100%;">
        <span>Task Center</span>
        @if (_openTaskCount > 0)
        {
            <MudBadge Content="@_openTaskCount" Color="Color.Error"
                      Style="font-size:10px;" />
        }
    </div>
</MudNavLink>

@* In @code: *@
@code {
    private int _openTaskCount = 0;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var userId = await UserSession.GetUserIdAsync();
            _openTaskCount = await TaskSvc.GetOpenTaskCountForUserAsync(userId);
        }
        catch { /* Non-fatal — badge just won't show */ }
    }
}
```

---

## Acceptance Criteria

### Part A — Intake Form
1. Opening an INTAKE-stage opportunity shows the 4-section intake form (Account, Fleet, Coverage, Loss History)
2. "Save Draft" persists the form data; refreshing the page reloads the saved values
3. "Save & Pursue Opportunity →" with empty required fields shows validation errors and does NOT advance the stage
4. "Save & Pursue Opportunity →" with all required fields saves the form AND advances to UNDERWRITING_PREP
5. After pursuing, `opportunities.intake_responses_json` in the DB contains valid JSON with all entered values
6. After pursuing, `UnderwritingPrepPanel` is shown with 4 auto-generated tasks in the activity log

### Part B — Task Center
7. `/tasks` shows a grouped list of all open tasks for the logged-in user's opportunities
8. Opportunities with tasks closer to due date appear first; undated tasks appear last
9. Clicking the checkbox on a task marks it "done" and removes it from the list immediately (optimistic UI)
10. Clicking an opportunity name/header navigates to that `OpportunityWorkspace`
11. "Add Task" button opens dialog; submitting creates a task visible in the list
12. Task count badge in NavMenu shows the correct count; count decreases when tasks are completed
13. Advancing an opportunity to UNDERWRITING_PREP creates 4 tasks (confirm by checking `/tasks` or the activity log)
14. Advancing to MARKETED creates 2 tasks; QUOTES_RECEIVED creates 3 tasks; etc. (per `StageTaskTemplates`)
15. Filtering by text narrows results to matching opportunity names or task titles
16. Empty state (no open tasks) shows a "All clear" illustration

---

## Clint Review Priorities

```
⚠️  HIGH: `SaveIntakeResponsesAsync` must use a transaction + SaveChangesAsync,
          same as all other LifecycleCommandService methods. If Tony writes it
          without a transaction, a mid-save crash could leave the opportunity
          in a half-updated state. Verify the transaction wraps both the
          `opp.IntakeResponsesJson =` assignment and SaveChangesAsync.

⚠️  HIGH: `CreateTasksForStageAsync` adds entities to the tracked DbContext
          but does NOT call SaveChangesAsync — by design, it relies on the
          calling method's save. If Tony adds a SaveChangesAsync inside it,
          the outer transaction won't cover it. Confirm no SaveChangesAsync
          inside `CreateTasksForStageAsync`.

⚠️  HIGH: The ALTER TABLE for `intake_responses_json` must run AFTER
          `CreateTablesAsync` and must be idempotent (`ADD COLUMN IF NOT EXISTS`).
          MySQL syntax: `ALTER TABLE opportunities ADD COLUMN IF NOT EXISTS
          intake_responses_json MEDIUMTEXT NULL`. Verify the column exists in
          the Aurora instance before testing the intake form.

⚠️  MEDIUM: `TaskCenter.razor` checkbox ValueChanged fires on render as well as
            on user interaction in some MudBlazor versions. Verify that completing
            a task is only triggered by a user click, not on initial render.
            A safe guard: check that the current value is `false` before calling
            `CompleteTaskAsync` (i.e., only complete when checking the box ON,
            not unchecking).

⚠️  MEDIUM: `GetOpenTaskCountForUserAsync` is called in `NavMenu.razor`
            `OnInitializedAsync`. This fires on every page navigation in Blazor
            Server (layout re-renders). If this query is slow (e.g. missing index),
            every page load will incur a DB round-trip. Verify the index
            `idx_task_opp` exists on `tasks.opportunity_id` and that the
            query plan uses it. Consider caching the count in a scoped service
            for the session duration.

⚠️  LOW: `AddTaskDialog` uses `MudAutocomplete` with a full `GetPipelineAsync`
         load on init. If there are many opportunities, this is a large fetch
         on dialog open. Acceptable for Phase 1 (ERs manage 100-200 opportunities
         max), but note for Phase 2 pagination.
```

---

_Spec by Reed Richards | Sprint 4 = 3 new files, 7 modified. Intake form replaces stub; Task Center goes from empty to full work queue. Task auto-generation on lifecycle transitions. Nav badge shows live open task count._
