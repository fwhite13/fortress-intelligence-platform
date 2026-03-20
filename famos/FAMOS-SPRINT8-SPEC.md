# FAM OS Sprint 8 Spec — Pagination, Multi-Affinity, Accounts, Empty States, Error Handling, Performance, HubSpot Two-Way Sync

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19  
**Sprint Goal:** Production-readiness — scale past 25 opportunities, support multiple affinity programs, wire HubSpot both ways, eliminate crashes, eliminate blank screens  
**Prerequisite:** Sprints 5–7 deployed and verified  
**Design System:** `DESIGN-SYSTEM.md` mandatory. `famos-btn-*`, `famos-input`, `famos-select`, `FamosIcons.*`. No inline Variant/Color/Size on MudButton.  
**Spec references:** `FAMOS-ARCHITECTURE-SPEC.md`, `FAMOS-SPRINT6-SPEC.md`, `FAMOS-SPRINT7-SPEC.md`

---

## Sprint 8 Overview

| Part | Feature | New Files | Modified Files |
|------|---------|-----------|----------------|
| A | Pagination — Pipeline + Task Center | 1 | 4 |
| B | Multi-affinity — per-login affinity selection | 1 | 4 |
| C | Accounts page (`/accounts`) | 2 | 3 |
| D | Empty states — all panels | 0 | 9 |
| E | Error handling — panel error boundaries | 1 | 3 |
| F | Performance — N+1 audit + query fixes | 0 | 3 |
| G | HubSpot two-way sync — real IHubSpotService | 0 | 2 |

**Total: 5 new files, 28 modified files.** Two CC sessions — Session 1: Parts A, B, C (DB + services + pages). Session 2: Parts D, E, F, G (panels + service changes, no DB migrations).

---

## Parallelization Map

**Two sequential CC sessions.**

**Session 1 (Parts A, B, C):**
1. Part B — `UserAffinityService.cs` + `appsettings.json` affinity mapping + `UserSessionService` extension + `MainLayout.razor` affinity resolution
2. Part A — `OpportunityService.GetPipelinePaginatedAsync` + `TaskService.GetOpenTasksPagedAsync` + `Pipeline.razor` pagination + `TaskCenter.razor` pagination  
3. Part C — `AccountSyncService.cs` + `accounts` DB table + `AccountsPanel.razor` + `NavMenu.razor` Accounts link activation

**Session 2 (Parts D, E, F, G):**
4. Part D — Empty state additions to all 9 panels (IntakePanel, UnderwritingPrepPanel, MarketedPanel, QuotesReceivedPanel, QuoteScraperPanel, ContactsPanel, DocumentsPanel, TaskCenter, Pipeline column empty state)
5. Part E — `PanelErrorBoundary.razor` + wrap panels in `OpportunityWorkspace.razor` + global `ErrorPage.razor`
6. Part F — query fix: split `GetByIdAsync` into lean + full loads; `GetPipelineAsync` remove over-eager includes
7. Part G — `HubSpotService.cs` owner-change + close push additions; `IHubSpotService` interface extension; wire from `LifecycleCommandService`

---

## DB Changes

**Session 1 only.** Aurora MySQL try/catch on 1060 — no `IF NOT EXISTS`.

```csharp
// Part B — affinity_id on users (in-memory mapping, no DB column needed — see Part B)

// Part C — accounts cache table
await _db.Database.ExecuteSqlRawAsync("""
    CREATE TABLE IF NOT EXISTS accounts (
        id              CHAR(36) NOT NULL PRIMARY KEY,
        affinity_id     VARCHAR(50) NOT NULL DEFAULT '',
        company_name    VARCHAR(255) NOT NULL DEFAULT '',
        hubspot_id      VARCHAR(50) NULL,
        city            VARCHAR(100) NULL,
        state           VARCHAR(10) NULL,
        active_opp_count INT NOT NULL DEFAULT 0,
        last_synced_at  DATETIME NULL,
        created_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
        INDEX idx_accounts_affinity (affinity_id),
        INDEX idx_accounts_name (company_name)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
""");
```

---

## Part A — Pagination

### Context

`GetPipelineAsync()` currently does `ToListAsync()` on ALL non-closed opportunities with no limit. At 200 opportunities (Steve's target), the pipeline board loads 200 cards into memory and renders all 7 columns at once. `GetOpenTasksForUserAsync()` has the same problem. Both need server-side pagination.

**Pipeline:** Paginate per-stage. Each column loads up to 25 cards; a "Load more" button appears if there are more. This matches the visual kanban layout — loading page 2 of "Intake" doesn't affect "Binding".

**Task Center:** Single flat list, paginated globally. Page controls at the bottom.

### A1. `Services/OpportunityService.cs` — Add Paginated Methods

Add two new methods. Do NOT modify `GetPipelineAsync()` or `GetPipelineByStageAsync()` — they are used by `DashboardSummary` aggregation and must stay.

```csharp
public const int PipelinePageSize = 25;

/// <summary>
/// Returns one page of opportunities for a single lifecycle stage.
/// Used by Pipeline board per-column pagination.
/// </summary>
public async Task<OpportunityPage> GetStagePageAsync(
    LifecycleStage stage, int pageIndex, string? affinityId = null)
{
    await using var db = await _dbFactory.CreateDbContextAsync();

    var query = db.Opportunities
        .Include(o => o.Flags.Where(f => f.IsActive))
        .Where(o => !o.IsClosed && o.LifecycleStage == stage);

    if (!string.IsNullOrEmpty(affinityId))
        query = query.Where(o => o.AffinityId == affinityId);

    var total = await query.CountAsync();
    var items = await query
        .OrderByDescending(o => o.UrgencyScore)
        .ThenByDescending(o => o.UpdatedAt)
        .Skip(pageIndex * PipelinePageSize)
        .Take(PipelinePageSize)
        .ToListAsync();

    return new OpportunityPage
    {
        Items      = items,
        TotalCount = total,
        PageIndex  = pageIndex,
        PageSize   = PipelinePageSize,
        HasMore    = (pageIndex + 1) * PipelinePageSize < total,
    };
}

/// <summary>
/// Returns paginated stage counts for all pipeline columns (cheap query — counts only).
/// </summary>
public async Task<Dictionary<LifecycleStage, int>> GetStageSummaryAsync(string? affinityId = null)
{
    await using var db = await _dbFactory.CreateDbContextAsync();

    var query = db.Opportunities.Where(o => !o.IsClosed);
    if (!string.IsNullOrEmpty(affinityId))
        query = query.Where(o => o.AffinityId == affinityId);

    return await query
        .GroupBy(o => o.LifecycleStage)
        .Select(g => new { Stage = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.Stage, x => x.Count);
}
```

**Add model:**
```csharp
public class OpportunityPage
{
    public List<Opportunity> Items      { get; init; } = new();
    public int  TotalCount              { get; init; }
    public int  PageIndex               { get; init; }
    public int  PageSize                { get; init; }
    public bool HasMore                 { get; init; }
}
```

**Note on `AffinityId` column:** `Opportunity.AffinityId` is added in Part B. The query filter is a no-op when `affinityId` is null (shows all). Tony must add the column before Part A queries compile.

### A2. `Services/TaskService.cs` — Add Paged Method

```csharp
public const int TaskPageSize = 25;

/// <summary>Returns one page of open tasks for a user, sorted by due-date urgency.</summary>
public async Task<TaskPage> GetOpenTasksPagedAsync(string userId, int pageIndex)
{
    await using var db = await _dbFactory.CreateDbContextAsync();

    var query = db.Tasks
        .Include(t => t.Opportunity)
        .Where(t => t.Status == "open"
            && t.Opportunity.OwnerUserId == userId
            && !t.Opportunity.IsClosed);

    var total = await query.CountAsync();
    var items = await query
        .OrderBy(t => t.DueAt.HasValue ? 0 : 1)
        .ThenBy(t => t.DueAt)
        .ThenBy(t => t.CreatedAt)
        .Skip(pageIndex * TaskPageSize)
        .Take(TaskPageSize)
        .Select(t => new TaskWithOpportunity(t, t.Opportunity))
        .ToListAsync();

    return new TaskPage
    {
        Items      = items,
        TotalCount = total,
        PageIndex  = pageIndex,
        PageSize   = TaskPageSize,
        HasMore    = (pageIndex + 1) * TaskPageSize < total,
    };
}
```

**Add model:**
```csharp
public class TaskPage
{
    public List<TaskWithOpportunity> Items { get; init; } = new();
    public int  TotalCount { get; init; }
    public int  PageIndex  { get; init; }
    public int  PageSize   { get; init; }
    public bool HasMore    { get; init; }
}
```

### A3. `Data/Entities/Opportunity.cs` — Add `AffinityId` Column

```csharp
/// <summary>Affinity program this opportunity belongs to (e.g. "tig", "iaapa", "nbais").</summary>
public string AffinityId { get; set; } = "tig";
```

**Add to `FamOsDbContext.cs` Opportunity entity config:**
```csharp
e.Property(x => x.AffinityId).HasMaxLength(50).HasColumnName("affinity_id")
    .HasDefaultValue("tig");
```

**Add DB migration to startup:**
```csharp
await TryAddColumnAsync(
    "ALTER TABLE opportunities ADD COLUMN affinity_id VARCHAR(50) NOT NULL DEFAULT 'tig'");
```

### A4. `Components/Pages/Pipeline.razor` — Add Per-Column Pagination

Replace the existing `GetPipelineByStageAsync()` load with per-column paginated loads.

```razor
@page "/pipeline"
@attribute [Authorize]
@inject OpportunityService OppService
@inject IDialogService DialogService
@inject NavigationManager Nav
@inject UserAffinityService AffinitySvc
@using FamOs.Web.Data.Entities
@using FamOs.Web.Services
@using FamOs.Web.Domain
@using FamOs.Web.Theme

<PageTitle>Pipeline — FAM OS</PageTitle>

<div class="famos-page-header famos-page-header-row mb-4">
    <div>
        <h2 class="famos-page-h2">Pipeline</h2>
        <p class="famos-page-sub">@_stageSummary.Values.Sum() active opportunities</p>
    </div>
    <MudButton Class="famos-btn-primary" OnClick="OpenCreateDialog"
               StartIcon="@FamosIcons.Add">
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
            var page = _pages.TryGetValue(col.Stage, out var p) ? p : null;
            <div class="famos-pipeline-column">
                <div class="famos-pipeline-column-header">
                    <span class="famos-kcol-dot" style="background:@GetStageColor(col.Stage);"></span>
                    <span class="famos-kcol-label">@col.DisplayName</span>
                    <span class="famos-kcol-count">
                        @(_stageSummary.TryGetValue(col.Stage, out var cnt) ? cnt : 0)
                    </span>
                </div>
                <div style="padding:8px;">
                    @if (page == null || !page.Items.Any())
                    {
                        <div class="famos-pipeline-empty">
                            No opportunities
                        </div>
                    }
                    else
                    {
                        @foreach (var opp in page.Items)
                        {
                            <OpportunityCard Opportunity="opp" />
                        }
                        @if (page.HasMore)
                        {
                            <div style="text-align:center; margin-top:6px;">
                                <MudButton Class="famos-btn-outline-sm"
                                           OnClick="() => LoadMoreStage(col.Stage)"
                                           Disabled="_loadingMore.Contains(col.Stage)">
                                    @(_loadingMore.Contains(col.Stage) ? "Loading..." : $"Load more ({page.TotalCount - page.Items.Count} remaining)")
                                </MudButton>
                            </div>
                        }
                    }
                </div>
            </div>
        }
    </div>
}

@code {
    private bool _loading = true;
    private Dictionary<LifecycleStage, int>              _stageSummary = new();
    private Dictionary<LifecycleStage, OpportunityPage>  _pages        = new();
    private HashSet<LifecycleStage>                      _loadingMore  = new();

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

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        var affinityId = await AffinitySvc.GetCurrentAffinityIdAsync();

        // Load counts first (cheap) — one query
        _stageSummary = await OppService.GetStageSummaryAsync(affinityId);

        // Load page 0 of each stage in parallel
        var tasks = _columns
            .Select(col => OppService.GetStagePageAsync(col.Stage, 0, affinityId))
            .ToArray();
        var pages = await Task.WhenAll(tasks);

        _pages = _columns
            .Zip(pages, (col, page) => (col.Stage, page))
            .ToDictionary(x => x.Stage, x => x.page);

        _loading = false;
    }

    private async Task LoadMoreStage(LifecycleStage stage)
    {
        if (!_pages.TryGetValue(stage, out var current)) return;
        _loadingMore.Add(stage);
        StateHasChanged();

        var affinityId = await AffinitySvc.GetCurrentAffinityIdAsync();
        var nextPage = await OppService.GetStagePageAsync(
            stage, current.PageIndex + 1, affinityId);

        // Append new items to existing page
        _pages[stage] = new OpportunityPage
        {
            Items      = current.Items.Concat(nextPage.Items).ToList(),
            TotalCount = nextPage.TotalCount,
            PageIndex  = nextPage.PageIndex,
            PageSize   = nextPage.PageSize,
            HasMore    = nextPage.HasMore,
        };

        _loadingMore.Remove(stage);
        StateHasChanged();
    }

    private async Task OpenCreateDialog()
    {
        var dialog = await DialogService.ShowAsync<OpportunityCreateDialog>("New Opportunity");
        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            var oppId = (Guid)result.Data!;
            Nav.NavigateTo($"/opportunity/{oppId}");
        }
    }

    private static string GetStageColor(LifecycleStage stage) => stage switch
    {
        LifecycleStage.Intake           => "#1d4ed8",
        LifecycleStage.UnderwritingPrep => "#6d28d9",
        LifecycleStage.Marketed         => "#d97706",
        LifecycleStage.QuotesReceived   => "#0369a1",
        LifecycleStage.ClientDecision   => "#9333ea",
        LifecycleStage.Binding          => "#0090d0",
        LifecycleStage.Bound            => "#059669",
        _                               => "#6b7585",
    };
}
```

**Add to `famos.css`:**
```css
.famos-pipeline-empty {
    padding: 16px;
    text-align: center;
    color: var(--muted);
    font-size: 12px;
    font-style: italic;
}
```

### A5. `Components/Pages/TaskCenter.razor` — Add Pagination Controls

Replace the current single-load with paginated load. Keep the filter — filter is client-side on the loaded page; typing in filter triggers a fresh page-0 server query with a debounce.

Replace the `@code` block's data loading:

```csharp
// Replace in @code:
private List<TaskWithOpportunity> _tasks     = new();
private bool   _loading    = true;
private string _filterText = "";
private int    _page       = 0;
private int    _totalCount = 0;
private bool   _hasMore    = false;

private IEnumerable<TaskWithOpportunity> FilteredTasks =>
    string.IsNullOrWhiteSpace(_filterText)
        ? _tasks
        : _tasks.Where(t =>
            t.Opportunity.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase) ||
            t.Task.Title.Contains(_filterText, StringComparison.OrdinalIgnoreCase));

protected override async Task OnInitializedAsync() => await LoadPageAsync(0);

private async Task LoadPageAsync(int pageIndex)
{
    _loading = true;
    var userId = await UserSession.GetUserIdAsync();
    var result = await TaskSvc.GetOpenTasksPagedAsync(userId, pageIndex);

    if (pageIndex == 0)
        _tasks = result.Items;
    else
        _tasks.AddRange(result.Items);

    _page       = pageIndex;
    _totalCount = result.TotalCount;
    _hasMore    = result.HasMore;
    _loading    = false;
}

private async Task LoadMore() => await LoadPageAsync(_page + 1);
```

Add after the task list, before `@code`:
```razor
@if (_hasMore)
{
    <div style="text-align:center; margin-top:12px;">
        <MudButton Class="famos-btn-outline"
                   OnClick="LoadMore"
                   Disabled="_loading">
            Load more (@(_totalCount - _tasks.Count) remaining)
        </MudButton>
    </div>
}
```

---

## Part B — Multi-Affinity

### Context

`AffinityConfig` is bound to a single block in `appsettings.json` — TIG only. When IAAPA and NBAIS go live, they get separate ECS task definitions with different `AffinityConfig` env vars. But there's also a use case where a single deployment serves multiple affinity groups (e.g., a Fortress AM admin who can see all groups). Sprint 8 adds the infrastructure for per-user affinity routing without requiring separate deployments.

**Design:** `appsettings.json` gets an `AffinityGroups` array (multi-affinity config). A `UserAffinityService` resolves which affinity group a logged-in user belongs to, based on a claim (`affinity_id` custom Entra claim) or a `UserAffinityMap` in appsettings. Falls back to the single `AffinityConfig.AffinityId` if no mapping is found (backward compatible).

### B1. `AffinityConfig.cs` — Add `AffinityGroups` Array

```csharp
public class AffinityConfig
{
    public string  AffinityId    { get; set; } = "famos";
    public string  DisplayName   { get; set; } = "Fortress Affinity Management OS";
    public string  PortalName    { get; set; } = "FAM OS";
    public string  LogoPath      { get; set; } = "";
    public string? PrimaryColor  { get; set; }
    public string? AccentColor   { get; set; }
    public List<AffinityUser>  Users         { get; set; } = new();

    /// <summary>
    /// All affinity groups served by this deployment.
    /// If empty, falls back to the single-affinity AffinityId/DisplayName/PortalName values.
    /// </summary>
    public List<AffinityGroupConfig> AffinityGroups { get; set; } = new();

    /// <summary>
    /// Maps Entra user email → affinity group ID.
    /// Used when affinityId Entra claim is not present.
    /// </summary>
    public Dictionary<string, string> UserAffinityMap { get; set; } = new();
}

public class AffinityGroupConfig
{
    public string  AffinityId   { get; set; } = "";
    public string  DisplayName  { get; set; } = "";
    public string  PortalName   { get; set; } = "";
    public string  LogoPath     { get; set; } = "";
    public string? PrimaryColor { get; set; }
}
```

### B2. `appsettings.json` — Add Multi-Affinity Seed

```json
"AffinityConfig": {
    "AffinityId": "tig",
    "DisplayName": "Titan Insurance Group",
    "PortalName": "Titan Dashboard",
    "LogoPath": "/images/affinity/tig-logo.svg",
    "Users": [
        { "UserId": "lauren.tig@titaninsurancegroup.com", "DisplayName": "Lauren", "Initials": "LL" },
        { "UserId": "fred.white@fortressam.ai",           "DisplayName": "Fred",   "Initials": "FW" }
    ],
    "AffinityGroups": [
        {
            "AffinityId":  "tig",
            "DisplayName": "Titan Insurance Group",
            "PortalName":  "Titan Dashboard",
            "LogoPath":    "/images/affinity/tig-logo.svg"
        },
        {
            "AffinityId":  "iaapa",
            "DisplayName": "IAAPA",
            "PortalName":  "IAAPA Dashboard",
            "LogoPath":    "/images/affinity/iaapa-logo.svg"
        },
        {
            "AffinityId":  "nbais",
            "DisplayName": "NBAIS",
            "PortalName":  "NBAIS Dashboard",
            "LogoPath":    "/images/affinity/nbais-logo.svg"
        }
    ],
    "UserAffinityMap": {
        "lauren.tig@titaninsurancegroup.com": "tig",
        "fred.white@fortressam.ai": "tig"
    }
}
```

### B3. `Services/UserAffinityService.cs` (new)

```csharp
using Microsoft.Extensions.Options;

namespace FamOs.Web.Services;

/// <summary>
/// Resolves the affinity group ID for the currently logged-in user.
///
/// Resolution order:
/// 1. Entra custom claim "affinity_id" on the access token
/// 2. UserAffinityMap in appsettings (email → affinityId)
/// 3. Single AffinityConfig.AffinityId fallback (backward compat)
/// </summary>
public class UserAffinityService
{
    private readonly UserSessionService _session;
    private readonly AffinityConfig     _config;

    public UserAffinityService(UserSessionService session, IOptions<AffinityConfig> config)
    {
        _session = session;
        _config  = config.Value;
    }

    public async Task<string> GetCurrentAffinityIdAsync()
    {
        var user = await _session.GetUserAsync();

        // 1. Entra custom claim (requires Entra app manifest to include affinity_id)
        var claimValue = user.FindFirst("affinity_id")?.Value;
        if (!string.IsNullOrEmpty(claimValue))
            return claimValue;

        // 2. Email-based map in appsettings
        var email = await _session.GetUserEmailAsync();
        if (!string.IsNullOrEmpty(email)
            && _config.UserAffinityMap.TryGetValue(email, out var mapped))
            return mapped;

        // 3. Fallback to single-tenant AffinityId
        return _config.AffinityId;
    }

    /// <summary>Returns the full AffinityGroupConfig for the current user's affinity.</summary>
    public async Task<AffinityGroupConfig?> GetCurrentAffinityConfigAsync()
    {
        var affinityId = await GetCurrentAffinityIdAsync();
        return _config.AffinityGroups.FirstOrDefault(g => g.AffinityId == affinityId)
            ?? new AffinityGroupConfig
            {
                AffinityId  = _config.AffinityId,
                DisplayName = _config.DisplayName,
                PortalName  = _config.PortalName,
                LogoPath    = _config.LogoPath,
            };
    }
}
```

**Add to `Program.cs`:**
```csharp
builder.Services.AddScoped<UserAffinityService>();
```

### B4. `Components/Layout/MainLayout.razor` — Use AffinityService for Branding

Replace `AffinityOptions.Value` direct access with `UserAffinityService` resolution.

Add inject:
```razor
@inject UserAffinityService AffinitySvc
```

In `OnInitializedAsync`, replace the existing affinity assignment:
```csharp
// Replace: _affinity = AffinityOptions.Value;
// With:
var affinityConfig = await AffinitySvc.GetCurrentAffinityConfigAsync();
_affinity = new AffinityConfig
{
    AffinityId  = affinityConfig?.AffinityId  ?? AffinityOptions.Value.AffinityId,
    DisplayName = affinityConfig?.DisplayName ?? AffinityOptions.Value.DisplayName,
    PortalName  = affinityConfig?.PortalName  ?? AffinityOptions.Value.PortalName,
    LogoPath    = affinityConfig?.LogoPath    ?? AffinityOptions.Value.LogoPath,
};
```

**Note:** `MainLayout._affinity` is already used to display the logo and portal name. No other changes needed to the layout template.

### B5. `Services/OpportunityService.cs` — Filter by AffinityId in `CreateOpportunityAsync`

When an opportunity is created, stamp it with the creator's affinity:

```csharp
// CreateOpportunityAsync: resolve affinityId and stamp on Opportunity
// Inject UserAffinityService into OpportunityService constructor and add:

public OpportunityService(
    IDbContextFactory<FamOsDbContext> dbFactory,
    LifecycleCommandService lifecycle,
    UserAffinityService affinity,       // ← add
    ILogger<OpportunityService> logger)
{
    _dbFactory = dbFactory;
    _lifecycle = lifecycle;
    _affinity  = affinity;
    _logger    = logger;
}
private readonly UserAffinityService _affinity;
```

In `CreateOpportunityAsync`, after `OwnerUserId = ownerUserId`:
```csharp
AffinityId = await _affinity.GetCurrentAffinityIdAsync(),
```

---

## Part C — Accounts Page

### Context

The `/accounts` page was stubbed as "Coming Soon" in the NavMenu. In Sprint 8 it becomes real. It shows companies synced from HubSpot — an `accounts` table is the local cache. Sync happens on demand (button) or passively (15-minute background refresh). Each account row shows: company name, city/state, active opportunity count, last synced date, and a "View Pipeline" link that navigates to the filtered pipeline.

There is no HubSpot contact/person management here — only companies (HubSpot `companies` object). Click-through opens the pipeline filtered to that company name.

### C1. `Data/Entities/Account.cs` (new)

```csharp
namespace FamOs.Web.Data.Entities;

/// <summary>
/// Local cache of HubSpot company records for an affinity group.
/// Refreshed by AccountSyncService. Source of truth is HubSpot.
/// </summary>
public class Account
{
    public Guid    Id              { get; set; } = Guid.NewGuid();
    public string  AffinityId      { get; set; } = "";
    public string  CompanyName     { get; set; } = "";
    public string? HubSpotId       { get; set; }
    public string? City            { get; set; }
    public string? State           { get; set; }
    public int     ActiveOppCount  { get; set; } = 0;
    public DateTime? LastSyncedAt  { get; set; }
    public DateTime  CreatedAt     { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt     { get; set; } = DateTime.UtcNow;
}
```

### C2. `Data/FamOsDbContext.cs` — Add Account DbSet + Config

```csharp
public DbSet<Account> Accounts => Set<Account>();
```

```csharp
m.Entity<Account>(e => {
    e.ToTable("accounts");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnType("char(36)");
    e.HasIndex(x => new { x.AffinityId, x.CompanyName });
});
```

### C3. `Services/AccountSyncService.cs` (new)

Syncs HubSpot companies into the `accounts` table. Runs on-demand (called from Accounts page) and as a background job (15-minute interval, same pattern as `SignalRecomputeService`).

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IAccountSyncService
{
    /// <summary>Sync HubSpot companies for a given affinity group into the accounts table.</summary>
    Task SyncAsync(string affinityId, CancellationToken ct = default);
    Task RefreshOppCountsAsync(string affinityId);
}

public class AccountSyncService : BackgroundService, IAccountSyncService
{
    private readonly IServiceScopeFactory   _services;
    private readonly ILogger<AccountSyncService> _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public AccountSyncService(IServiceScopeFactory services,
        ILogger<AccountSyncService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    // ── BackgroundService ─────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), ct); // let startup settle

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var config = scope.ServiceProvider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<AffinityConfig>>()
                    .Value;

                foreach (var group in config.AffinityGroups.Any()
                    ? config.AffinityGroups.Select(g => g.AffinityId)
                    : new[] { config.AffinityId })
                {
                    await SyncCoreAsync(scope, group, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[AccountSync] Background sync error");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    // ── IAccountSyncService ───────────────────────────────────────────────

    public async Task SyncAsync(string affinityId, CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        await SyncCoreAsync(scope, affinityId, ct);
    }

    public async Task RefreshOppCountsAsync(string affinityId)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var accounts = await db.Accounts
            .Where(a => a.AffinityId == affinityId)
            .ToListAsync();

        foreach (var account in accounts)
        {
            account.ActiveOppCount = await db.Opportunities
                .CountAsync(o => !o.IsClosed
                    && o.AffinityId == affinityId
                    && EF.Functions.Like(o.Name, $"%{account.CompanyName}%"));
        }

        await db.SaveChangesAsync();
    }

    // ── Core sync logic ───────────────────────────────────────────────────

    private async Task SyncCoreAsync(IServiceScope scope, string affinityId,
        CancellationToken ct)
    {
        var config = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var serviceKey = config["HubSpot:ServiceKey"];

        if (string.IsNullOrEmpty(serviceKey))
        {
            _logger.LogDebug("[AccountSync] HubSpot:ServiceKey not set — skipping sync for {Aff}", affinityId);
            return;
        }

        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var client  = factory.CreateClient("HubSpot");
        var db      = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        // Fetch companies from HubSpot (paginated, max 100 per request)
        var companies = new List<HsCompany>();
        string? after = null;

        do
        {
            var url = "/crm/v3/objects/companies?limit=100&properties=name,city,state" +
                (after != null ? $"&after={after}" : "");
            var resp = await client.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("[AccountSync] HubSpot companies fetch failed: {Status}", resp.StatusCode);
                break;
            }

            var page = await resp.Content.ReadFromJsonAsync<HsCompanyPage>(Opts, ct);
            if (page?.Results != null)
                companies.AddRange(page.Results);

            after = page?.Paging?.Next?.After;
        } while (after != null && companies.Count < 1000); // safety cap

        _logger.LogInformation("[AccountSync] Fetched {Count} companies for {Aff}", companies.Count, affinityId);

        // Upsert into accounts table
        var now = DateTime.UtcNow;
        foreach (var company in companies)
        {
            if (string.IsNullOrEmpty(company.Properties?.Name)) continue;

            var existing = await db.Accounts
                .FirstOrDefaultAsync(a => a.HubSpotId == company.Id && a.AffinityId == affinityId, ct);

            if (existing == null)
            {
                db.Accounts.Add(new Account
                {
                    AffinityId   = affinityId,
                    CompanyName  = company.Properties.Name,
                    HubSpotId    = company.Id,
                    City         = company.Properties.City,
                    State        = company.Properties.State,
                    LastSyncedAt = now,
                });
            }
            else
            {
                existing.CompanyName  = company.Properties.Name;
                existing.City         = company.Properties.City;
                existing.State        = company.Properties.State;
                existing.LastSyncedAt = now;
                existing.UpdatedAt    = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await RefreshOppCountsAsync(affinityId);
        _logger.LogInformation("[AccountSync] Sync complete for {Aff}", affinityId);
    }

    // ── HubSpot DTOs ──────────────────────────────────────────────────────

    private class HsCompanyPage
    {
        public List<HsCompany>? Results { get; set; }
        public HsPaging?        Paging  { get; set; }
    }

    private class HsCompany
    {
        public string?             Id         { get; set; }
        public HsCompanyProps?     Properties { get; set; }
    }

    private class HsCompanyProps
    {
        public string? Name  { get; set; }
        public string? City  { get; set; }
        public string? State { get; set; }
    }

    private class HsPaging
    {
        public HsPagingNext? Next { get; set; }
    }

    private class HsPagingNext
    {
        public string? After { get; set; }
    }
}
```

**Add to `Program.cs`:**
```csharp
builder.Services.AddSingleton<IAccountSyncService, AccountSyncService>();
builder.Services.AddHostedService(sp => (AccountSyncService)sp.GetRequiredService<IAccountSyncService>());
```

### C4. `Components/Pages/Accounts.razor` (new)

```razor
@page "/accounts"
@attribute [Authorize]
@inject IAccountSyncService AccountSync
@inject UserAffinityService AffinitySvc
@inject NavigationManager Nav
@inject ISnackbar Snackbar
@using FamOs.Web.Data.Entities
@using FamOs.Web.Services
@using FamOs.Web.Theme
@using Microsoft.EntityFrameworkCore
@inject FamOs.Web.Data.FamOsDbContext Db

<PageTitle>Accounts — FAM OS</PageTitle>

<div class="famos-page-header famos-page-header-row mb-4">
    <div>
        <h2 class="famos-page-h2">Accounts</h2>
        <p class="famos-page-sub">@_filteredAccounts.Count of @_accounts.Count member companies</p>
    </div>
    <div style="display:flex; gap:8px;">
        <MudTextField Class="famos-input-filter"
                      @bind-Value="_search" @bind-Value:event="oninput"
                      Placeholder="Search accounts..."
                      Adornment="Adornment.Start"
                      AdornmentIcon="@FamosIcons.Search"
                      Clearable="true" />
        <MudButton Class="famos-btn-outline-sm"
                   StartIcon="@FamosIcons.Download"
                   OnClick="SyncNow"
                   Disabled="_syncing">
            @(_syncing ? "Syncing..." : "Sync from HubSpot")
        </MudButton>
    </div>
</div>

@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" />
}
else if (!_accounts.Any())
{
    <div class="famos-empty-state" style="margin-top:40px;">
        <MudIcon Icon="@FamosIcons.Accounts" Class="famos-empty-icon" />
        <div>No accounts synced yet.</div>
        <div style="margin-top:8px;">
            <MudButton Class="famos-btn-primary" OnClick="SyncNow" Disabled="_syncing">
                Sync from HubSpot
            </MudButton>
        </div>
    </div>
}
else
{
    <MudPaper Elevation="0" Style="border:1px solid var(--border); border-radius:12px; overflow:hidden;">

        @* Header row *@
        <div class="famos-account-table-header">
            <div class="famos-account-col-name">Company</div>
            <div class="famos-account-col-location">Location</div>
            <div class="famos-account-col-opps">Active Opps</div>
            <div class="famos-account-col-sync">Last Synced</div>
            <div class="famos-account-col-action"></div>
        </div>

        @foreach (var account in _filteredAccounts)
        {
            <div class="famos-account-row"
                 @onclick="() => GoToPipeline(account.CompanyName)">
                <div class="famos-account-col-name">
                    <span style="font-weight:600; color:var(--navy);">@account.CompanyName</span>
                </div>
                <div class="famos-account-col-location">
                    @if (!string.IsNullOrEmpty(account.City) || !string.IsNullOrEmpty(account.State))
                    {
                        <span class="famos-meta-text">
                            @(account.City)@(account.City != null && account.State != null ? ", " : "")@(account.State)
                        </span>
                    }
                    else
                    {
                        <span class="famos-meta-text">—</span>
                    }
                </div>
                <div class="famos-account-col-opps">
                    @if (account.ActiveOppCount > 0)
                    {
                        <MudChip T="string" Size="Size.Small"
                                 Style="background:rgba(0,144,208,0.1); color:var(--sky); font-weight:600; font-size:11px;">
                            @account.ActiveOppCount active
                        </MudChip>
                    }
                    else
                    {
                        <span class="famos-meta-text">No active opps</span>
                    }
                </div>
                <div class="famos-account-col-sync">
                    <span class="famos-meta-text">
                        @(account.LastSyncedAt.HasValue
                            ? account.LastSyncedAt.Value.ToLocalTime().ToString("MMM d, h:mm tt")
                            : "Never")
                    </span>
                </div>
                <div class="famos-account-col-action">
                    <MudIcon Icon="@FamosIcons.ChevronRight"
                             Style="font-size:16px; color:var(--muted);" />
                </div>
            </div>
        }

    </MudPaper>

    @if (_lastSyncTime.HasValue)
    {
        <div class="famos-meta-text" style="margin-top:8px; text-align:right;">
            Last HubSpot sync: @_lastSyncTime.Value.ToLocalTime().ToString("MMM d, yyyy 'at' h:mm tt")
        </div>
    }
}

@code {
    private List<Account> _accounts         = new();
    private bool          _loading          = true;
    private bool          _syncing          = false;
    private string        _search           = "";
    private DateTime?     _lastSyncTime;

    private List<Account> _filteredAccounts =>
        string.IsNullOrWhiteSpace(_search)
            ? _accounts
            : _accounts.Where(a =>
                a.CompanyName.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                (a.City?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (a.State?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var affinityId = await AffinitySvc.GetCurrentAffinityIdAsync();
        _accounts = await Db.Accounts
            .Where(a => a.AffinityId == affinityId)
            .OrderBy(a => a.CompanyName)
            .ToListAsync();
        _lastSyncTime = _accounts.Max(a => a.LastSyncedAt);
        _loading = false;
    }

    private async Task SyncNow()
    {
        _syncing = true;
        try
        {
            var affinityId = await AffinitySvc.GetCurrentAffinityIdAsync();
            await AccountSync.SyncAsync(affinityId);
            await LoadAsync();
            Snackbar.Add("Accounts synced from HubSpot.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Sync failed: {ex.Message}", Severity.Error);
        }
        finally { _syncing = false; }
    }

    private void GoToPipeline(string companyName)
    {
        // Navigate to pipeline with filter pre-applied via query string
        Nav.NavigateTo($"/pipeline?company={Uri.EscapeDataString(companyName)}");
    }
}
```

**Add to `famos.css`:**
```css
.famos-account-table-header,
.famos-account-row {
    display: grid;
    grid-template-columns: 2fr 1fr 120px 140px 32px;
    gap: 12px;
    padding: 10px 16px;
    align-items: center;
}
.famos-account-table-header {
    background: var(--cream);
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.6px;
    color: var(--muted);
    border-bottom: 1px solid var(--border);
}
.famos-account-row {
    border-bottom: 1px solid var(--border);
    cursor: pointer;
    transition: background 0.1s;
}
.famos-account-row:last-child { border-bottom: none; }
.famos-account-row:hover { background: var(--cream); }
```

### C5. `Components/Layout/NavMenu.razor` — Activate Accounts Link

Replace the disabled Accounts `<span>` with a real `<NavLink>`:

```razor
@* Replace the disabled Accounts span: *@
<NavLink href="/accounts" Match="NavLinkMatch.Prefix"
         class="famos-nav-item" ActiveClass="famos-nav-item--active">
    <span class="famos-nav-icon">
        <MudIcon Icon="@FamosIcons.Accounts" Size="Size.Small" />
    </span>
    Accounts
</NavLink>
```

Remove the "Coming Soon" section label and the disabled `famos-nav-item--disabled` spans for both Accounts and Reports (Reports stays disabled for now — move it to a separate disabled item without the section label).

---

## Part D — Empty States

All empty states must follow the `famos-empty-state` CSS class pattern established in Sprint 6. Where a CTA makes sense (e.g., "Add Contact"), include a CTA button.

**Panels that need empty state work:**

### D1. `IntakePanel.razor`
Already has a "Pursue Opportunity" button as the sole action. No empty state needed — the intake form is always shown when stage = INTAKE.

### D2. `UnderwritingPrepPanel.razor`
The submissions list section already has conditional rendering. Add an explicit empty state when `Opportunity.Submissions` is empty:

```razor
@* In the "Current Submissions" section, replace the bare @if: *@
@if (!Opportunity.Submissions.Any())
{
    <div class="famos-empty-state mb-3">
        <MudIcon Icon="@FamosIcons.Document" Class="famos-empty-icon" />
        <div>No submissions yet — add carriers below to get started.</div>
    </div>
}
else { /* existing submission list */ }
```

### D3. `MarketedPanel.razor`
If `Opportunity.Submissions` is empty (shouldn't happen — stage gate prevents it — but defensive):

```razor
@if (!Opportunity.Submissions.Any())
{
    <div class="famos-empty-state">
        <MudIcon Icon="@FamosIcons.Warning" Class="famos-empty-icon" />
        <div>No submissions found. This may be a data issue — contact support.</div>
    </div>
}
```

### D4. `QuotesReceivedPanel.razor`
Already has `famos-empty-state` from Sprint 7. Verify the icon is `FamosIcons.Dollar` and the message is "No quotes yet. Record quotes from the submission status panel above." ✓ No change needed if Sprint 7 spec was followed.

### D5. `QuoteScraperPanel.razor`
Already has the "Add carrier submissions first" alert. No change needed.

### D6. `ContactsPanel.razor`
Already has `famos-empty-state` from Sprint 6. Verify icon is `FamosIcons.Person`. ✓ No change needed if Sprint 6 spec was followed.

### D7. `DocumentsPanel.razor`
Already has `famos-empty-state` from Sprint 6. Verify icon is `FamosIcons.Document`. ✓ No change needed.

### D8. `TaskCenter.razor`
The existing empty state uses `MudIcon` with `Icons.Material.Filled.CheckCircle`. Replace with `FamosIcons.CheckCircle` to comply with design system:

```razor
@* In the empty state MudPaper: *@
<MudIcon Icon="@FamosIcons.CheckCircle"
         Style="font-size:48px; color:var(--green); margin-bottom:12px;" />
```

### D9. `Pipeline.razor` column empty state
Replace the inline style div with the CSS class:

```razor
@* Replace: *@
<div style="padding:16px;text-align:center;color:var(--color-text-muted);font-size:12px;">
    No opportunities
</div>
@* With: *@
<div class="famos-pipeline-empty">No opportunities in this stage.</div>
```

---

## Part E — Error Handling

### Context

A Blazor Server component that throws an unhandled exception crashes the entire circuit — the whole page goes blank. For an OpportunityWorkspace with 6 panels, one bad panel shouldn't kill the workspace. `ErrorBoundary` is built into Blazor 6+ and works by catching exceptions during render and showing fallback content.

### E1. `Components/Shared/PanelErrorBoundary.razor` (new)

```razor
@inherits ErrorBoundary
@using FamOs.Web.Theme

@if (CurrentException != null)
{
    <div class="famos-error-card">
        <MudIcon Icon="@FamosIcons.Warning"
                 Style="font-size:20px; color:var(--amber); flex-shrink:0;" />
        <div>
            <div style="font-size:13px; font-weight:600; color:var(--navy);">
                Panel error
            </div>
            <div class="famos-meta-text">
                Something went wrong loading this panel.
                <span style="cursor:pointer; color:var(--sky); text-decoration:underline;"
                      @onclick="Recover">
                    Try again
                </span>
            </div>
            @if (ShowDetails)
            {
                <div class="famos-error-detail">@CurrentException.Message</div>
            }
        </div>
    </div>
}
else
{
    @ChildContent
}

@code {
    [Parameter] public bool ShowDetails { get; set; } = false;
}
```

**Add to `famos.css`:**
```css
.famos-error-card {
    display: flex;
    align-items: flex-start;
    gap: 10px;
    padding: 14px 16px;
    border: 1px solid var(--amber);
    border-radius: 8px;
    background: rgba(240, 160, 16, 0.05);
    margin-bottom: 12px;
}
.famos-error-detail {
    font-size: 11px;
    color: var(--muted);
    margin-top: 4px;
    font-family: monospace;
    word-break: break-all;
}
```

### E2. `Components/Pages/Opportunity/OpportunityWorkspace.razor` — Wrap Panels

Wrap every panel in the workspace switch with `<PanelErrorBoundary>`:

```razor
@* Example — apply the same pattern to ALL cases: *@
case LifecycleStage.Intake:
    <PanelErrorBoundary>
        <IntakePanel Opportunity="_opp" OnAdvanced="Reload" />
    </PanelErrorBoundary>
    break;
```

Also wrap the secondary panels section:

```razor
<div class="famos-secondary-panels mt-4">
    @if (_opp.LifecycleStage is LifecycleStage.Marketed or LifecycleStage.QuotesReceived)
    {
        <PanelErrorBoundary>
            <QuoteScraperPanel Opportunity="_opp" OnUpdated="Reload" />
        </PanelErrorBoundary>
    }
    <PanelErrorBoundary><ContactsPanel  Opportunity="_opp" OnUpdated="Reload" /></PanelErrorBoundary>
    <PanelErrorBoundary><DocumentsPanel Opportunity="_opp" OnUpdated="Reload" /></PanelErrorBoundary>
    <PanelErrorBoundary><ActivityPanel  Opportunity="_opp" OnUpdated="Reload" /></PanelErrorBoundary>
</div>
```

### E3. `Components/Routes.razor` — Add Global Error Boundary

Wrap the `<Router>` with a top-level error boundary for uncaught exceptions outside the workspace:

```razor
@* Wrap existing Router with error page: *@
<ErrorBoundary>
    <ChildContent>
        <Router AppAssembly="@typeof(App).Assembly">
            @* existing route content *@
        </Router>
    </ChildContent>
    <ErrorContent Context="ex">
        <div style="padding:40px; text-align:center;">
            <h2 style="color:var(--red);">Something went wrong</h2>
            <p style="color:var(--muted);">An unexpected error occurred. Please refresh the page.</p>
            <p style="font-size:11px; color:var(--muted); font-family:monospace;">@ex.Message</p>
        </div>
    </ErrorContent>
</ErrorBoundary>
```

---

## Part F — Performance

### Context

`GetByIdAsync` currently loads: Flags, Submissions, Quotes, Proposals, PolicyShadow, Activities (take 50), Tasks (open only), Contacts, Documents. That is 9 includes in a single query → EF Core translates this to multiple JOINs or split queries. The critical concern is Activities: the `Take(50)` on a navigation property inside `Include()` works in EF Core 7+ but generates a correlated subquery. Contacts and Documents are new from Sprint 6 and add two more JOINs.

**Specific N+1 risk:** `LoadOpportunityAsync` in `LifecycleCommandService` is called inside `CreateExecutionStrategy().ExecuteAsync()` on every lifecycle command. If Submissions and Quotes are not included, accessing `opp.Submissions` triggers lazy loading (if configured) or throws. The current codebase does NOT have lazy loading enabled (no `UseLazyLoadingProxies`), so accessing a non-included navigation returns an empty collection silently — which is worse than an exception because it causes incorrect validation behavior.

### F1. `Services/OpportunityService.cs` — Split `GetByIdAsync` into Lean + Full

```csharp
/// <summary>
/// Full load — used by OpportunityWorkspace to display all panels.
/// Includes all navigation properties. Use only for single-opportunity display.
/// </summary>
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
        .Include(o => o.Contacts)
        .Include(o => o.Documents)
        .AsSplitQuery()   // ← ADD: split 9-join query into separate round trips
                          //   reduces data duplication from cartesian product
        .FirstOrDefaultAsync(o => o.Id == id);
}

/// <summary>
/// Lean load — used by Dashboard urgent list, Task Center opportunity refs.
/// Does NOT include navigation properties. Never use for workspace display.
/// </summary>
public async Task<Opportunity?> GetByIdLeanAsync(Guid id)
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    return await db.Opportunities
        .Include(o => o.Flags.Where(f => f.IsActive))
        .FirstOrDefaultAsync(o => o.Id == id);
}
```

**Key change:** `AsSplitQuery()` — EF Core 7+ splits multi-include queries into multiple round trips instead of one giant Cartesian join. For 9 includes, this dramatically reduces the data transferred (avoids multiplicative row explosion when Submissions × Quotes × Activities cross-join). The round-trip cost is negligible on a local Aurora connection.

### F2. `Services/OpportunityService.cs` — Fix `GetPipelineAsync` Over-Eager Load

The pipeline board only needs: Name, Stage, Signal, Premium, EffectiveDate, OwnerUserId, Flags, UrgencyScore. It does NOT need Quotes, Submissions, or Activities.

```csharp
public async Task<List<Opportunity>> GetPipelineAsync()
{
    await using var db = await _dbFactory.CreateDbContextAsync();
    return await db.Opportunities
        .Include(o => o.Flags.Where(f => f.IsActive))
        // ← REMOVED: .Include(o => o.Quotes)  — not needed for pipeline cards
        .Where(o => !o.IsClosed)
        .OrderBy(o => o.UrgencyScore).ThenBy(o => o.UpdatedAt)
        .ToListAsync();
}
```

**Impact:** Removes the Quotes JOIN from the pipeline query. For 200 opportunities with 3 quotes each, this eliminates 600 rows from the result set. The pipeline card doesn't render quote data.

### F3. `Services/OpportunityService.cs` — Dashboard Query: Avoid Double Load

`GetDashboardSummaryAsync` currently loads all non-closed opportunities into memory (`ToListAsync()`), then does in-memory LINQ for aggregations. For 200+ opportunities this is fine; for 1000+ it degrades.

Replace the in-memory aggregations with DB-side queries:

```csharp
public async Task<DashboardSummary> GetDashboardSummaryAsync(string? ownerUserId = null)
{
    await using var db = await _dbFactory.CreateDbContextAsync();

    var urgentSignals = new[]
    {
        DominantSignal.Urgent, DominantSignal.AtRisk, DominantSignal.TimeRisk
    };

    var baseQuery = db.Opportunities.Where(o => !o.IsClosed);
    if (!string.IsNullOrEmpty(ownerUserId))
        baseQuery = baseQuery.Where(o => o.OwnerUserId == ownerUserId);

    // All aggregations as separate DB queries — each is cheap (indexed)
    var totalActive     = await baseQuery.CountAsync();
    var timeRiskCount   = await baseQuery.CountAsync(o => urgentSignals.Contains(o.DominantSignal));
    var decisionNeeded  = await baseQuery.CountAsync(o =>
        o.LifecycleStage == LifecycleStage.ClientDecision
        || o.LifecycleStage == LifecycleStage.Binding);
    var monthStart      = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    var boundThisMonth  = await db.Opportunities.CountAsync(o =>
        o.LifecycleStage == LifecycleStage.Bound && o.UpdatedAt >= monthStart);
    var totalPremium    = await baseQuery
        .Where(o => o.EstimatedPremium.HasValue)
        .SumAsync(o => o.EstimatedPremium!.Value);
    var byStage         = await baseQuery
        .GroupBy(o => o.LifecycleStage)
        .Select(g => new { Stage = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.Stage, x => x.Count);

    // Urgent list: load only top 10, only needed columns
    var urgentOpps = await baseQuery
        .Where(o => urgentSignals.Contains(o.DominantSignal))
        .Include(o => o.Flags.Where(f => f.IsActive))
        .OrderByDescending(o =>
            o.DominantSignal == DominantSignal.Urgent ? 2
            : o.DominantSignal == DominantSignal.AtRisk ? 1 : 0)
        .Take(10)
        .ToListAsync();

    // Recent activity: last 5, global (not filtered by owner)
    var recentActivity = await db.Activities
        .OrderByDescending(a => a.OccurredAt)
        .Take(5)
        .ToListAsync();

    return new DashboardSummary
    {
        TotalActive          = totalActive,
        TimeRiskCount        = timeRiskCount,
        DecisionNeeded       = decisionNeeded,
        BoundThisMonth       = boundThisMonth,
        TotalPremiumAtRisk   = totalPremium,
        UrgentOpportunities  = urgentOpps,
        ByStage              = byStage,
        RecentActivity       = recentActivity,
    };
}
```

**Impact:** Replaces one large `ToListAsync()` (loads entire pipeline into memory) with 7 small indexed DB queries. Each query is O(1) or O(log n) with the existing indexes on `lifecycle_stage`, `dominant_signal`, `is_closed`. Total DB round trips: 8 (was 2 — but the 2 queries were much heavier).

---

## Part G — HubSpot Two-Way Sync

### Context

`HubSpotService.cs` was added in Sprint 5 with `SyncLifecycleAsync` (lifecycle stage → HubSpot deal stage) and `SyncBoundAsync` (on bind). The `IHubSpotService` interface has exactly those two methods. Sprint 8 adds two new sync triggers: owner assignment and opportunity close — both should push updates to HubSpot in real-time.

### G1. `Services/HubSpotServiceStub.cs` + `IHubSpotService` — Add New Methods

**Update `IHubSpotService`:**
```csharp
public interface IHubSpotService
{
    /// <summary>Push lifecycle stage change to HubSpot deal.</summary>
    Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage);

    /// <summary>Push bound policy details to HubSpot deal.</summary>
    Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow);

    /// <summary>Push owner change to HubSpot deal (updates deal owner property).</summary>
    Task SyncOwnerAsync(Guid opportunityId, string newOwnerUserId);

    /// <summary>Push close event to HubSpot deal (closedate + stage).</summary>
    Task SyncClosedAsync(Guid opportunityId, CloseReason reason);
}
```

**Update `HubSpotServiceStub.cs`:**
```csharp
public Task SyncOwnerAsync(Guid opportunityId, string newOwnerUserId)
{
    _logger.LogInformation("[HubSpot stub] Owner sync: {Id} → {Owner}", opportunityId, newOwnerUserId);
    return Task.CompletedTask;
}

public Task SyncClosedAsync(Guid opportunityId, CloseReason reason)
{
    _logger.LogInformation("[HubSpot stub] Closed sync: {Id} — {Reason}", opportunityId, reason);
    return Task.CompletedTask;
}
```

### G2. `Services/HubSpotService.cs` — Implement New Methods

Add to the existing `HubSpotService` class:

```csharp
public async Task SyncOwnerAsync(Guid opportunityId, string newOwnerUserId)
{
    if (string.IsNullOrEmpty(ServiceKey)) return;
    try
    {
        var client = _factory.CreateClient("HubSpot");
        var dealId = await FindDealByOpportunityIdAsync(client, opportunityId);
        if (dealId == null)
        {
            _logger.LogWarning("[HubSpot] No deal for {Id} — skipping owner sync", opportunityId);
            return;
        }

        // HubSpot deal owner requires the HubSpot user ID — we map by email
        // If the owner email matches a HubSpot user, use their ID; otherwise skip
        var hubspotOwnerId = await ResolveHubSpotUserIdAsync(client, newOwnerUserId);
        if (hubspotOwnerId == null)
        {
            _logger.LogInformation("[HubSpot] No HubSpot user found for {Email} — skipping owner sync", newOwnerUserId);
            return;
        }

        var props = new Dictionary<string, object> { ["hubspot_owner_id"] = hubspotOwnerId };
        await PatchDealAsync(client, dealId, props);
        _logger.LogInformation("[HubSpot] Deal {DealId} owner → {Owner}", dealId, newOwnerUserId);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[HubSpot] SyncOwner failed for {Id}", opportunityId);
    }
}

public async Task SyncClosedAsync(Guid opportunityId, CloseReason reason)
{
    if (string.IsNullOrEmpty(ServiceKey)) return;
    try
    {
        var client = _factory.CreateClient("HubSpot");
        var dealId = await FindDealByOpportunityIdAsync(client, opportunityId);
        if (dealId == null) return;

        var props = new Dictionary<string, object>
        {
            ["dealstage"]                 = "closedlost",
            ["closedate"]                 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["hs_deal_stage_probability"] = 0,
            ["closed_lost_reason"]        = reason.ToString(),
        };
        await PatchDealAsync(client, dealId, props);
        _logger.LogInformation("[HubSpot] Deal {DealId} closed-lost — {Reason}", dealId, reason);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[HubSpot] SyncClosed failed for {Id}", opportunityId);
    }
}

// ── New helper: resolve HubSpot user ID from email ─────────────────────

private async Task<string?> ResolveHubSpotUserIdAsync(HttpClient client, string email)
{
    try
    {
        var resp = await client.GetAsync($"/settings/v3/users?email={Uri.EscapeDataString(email)}");
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<HsUsersResult>(json, Opts);
        return result?.Results?.FirstOrDefault(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))?.Id;
    }
    catch
    {
        return null;
    }
}

private class HsUsersResult
{
    public List<HsUser>? Results { get; set; }
}

private class HsUser
{
    public string? Id    { get; set; }
    public string? Email { get; set; }
}
```

### G3. `Domain/LifecycleCommandService.cs` — Wire HubSpot Calls for Owner + Close

Inject `IHubSpotService` into `LifecycleCommandService` if not already there (it should be from Sprint 5). Add calls in two commands:

**In `AssignOwnerAsync` (added Sprint 6)** — after `SaveChangesAsync()`:
```csharp
// Non-blocking: don't await inside the transaction; fire-and-forget after commit
_ = _hubspot.SyncOwnerAsync(opportunityId, newOwnerUserId)
    .ContinueWith(t => {
        if (t.IsFaulted)
            _logger.LogError(t.Exception, "[HubSpot] SyncOwner fire-and-forget failed");
    });
```

**In `CloseOpportunityAsync`** — after `SaveChangesAsync()`:
```csharp
_ = _hubspot.SyncClosedAsync(opportunityId, reason)
    .ContinueWith(t => {
        if (t.IsFaulted)
            _logger.LogError(t.Exception, "[HubSpot] SyncClosed fire-and-forget failed");
    });
```

**Note on fire-and-forget:** HubSpot sync is non-fatal and must never block a lifecycle command. The `ContinueWith` pattern logs failures without propagating exceptions. This is the same pattern used in Sprint 5 for lifecycle stage sync.

---

## File Summary

### New Files (5)
```
fip/famos/src/FamOs.Web/Services/UserAffinityService.cs
fip/famos/src/FamOs.Web/Services/AccountSyncService.cs
fip/famos/src/FamOs.Web/Data/Entities/Account.cs
fip/famos/src/FamOs.Web/Components/Pages/Accounts.razor
fip/famos/src/FamOs.Web/Components/Shared/PanelErrorBoundary.razor
```

### Modified Files (28)
```
// Session 1
fip/famos/src/FamOs.Web/AffinityConfig.cs                          (AffinityGroups array, UserAffinityMap, AffinityGroupConfig class)
fip/famos/src/FamOs.Web/appsettings.json                           (AffinityGroups seed + UserAffinityMap)
fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs               (add AffinityId)
fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs                     (Account DbSet + config, AffinityId column mapping)
fip/famos/src/FamOs.Web/Services/OpportunityService.cs             (GetStagePageAsync, GetStageSummaryAsync, OpportunityPage model, GetByIdAsync AsSplitQuery, GetPipelineAsync remove Quotes include, GetDashboardSummaryAsync DB-side aggregations, inject UserAffinityService, stamp AffinityId on create)
fip/famos/src/FamOs.Web/Services/TaskService.cs                    (GetOpenTasksPagedAsync, TaskPage model)
fip/famos/src/FamOs.Web/Components/Pages/Pipeline.razor            (paginated per-column loads, LoadMore, UserAffinityService)
fip/famos/src/FamOs.Web/Components/Pages/TaskCenter.razor          (paged load, LoadMore, FamosIcons.CheckCircle)
fip/famos/src/FamOs.Web/Components/Layout/MainLayout.razor         (use UserAffinityService for branding)
fip/famos/src/FamOs.Web/Components/Layout/NavMenu.razor            (activate Accounts link)
fip/famos/src/FamOs.Web/Program.cs                                 (register UserAffinityService, IAccountSyncService, AccountSyncService hosted service)

// Session 2
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor  (empty state for submissions)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/MarketedPanel.razor          (defensive empty state)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuoteScraperPanel.razor      (verify empty state)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ContactsPanel.razor          (verify empty state)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/DocumentsPanel.razor         (verify empty state)
fip/famos/src/FamOs.Web/Components/Pages/TaskCenter.razor                                (FamosIcons.CheckCircle in empty state — already in Session 1 list)
fip/famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor          (wrap all panels in PanelErrorBoundary)
fip/famos/src/FamOs.Web/Components/Routes.razor                                          (global ErrorBoundary wrapper)
fip/famos/src/FamOs.Web/Services/HubSpotService.cs                                       (SyncOwnerAsync, SyncClosedAsync, ResolveHubSpotUserIdAsync)
fip/famos/src/FamOs.Web/Services/HubSpotServiceStub.cs                                   (add stub implementations of new methods)
fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs                                (fire-and-forget HubSpot calls in AssignOwnerAsync + CloseOpportunityAsync)
fip/famos/src/FamOs.Web/wwwroot/css/famos.css                                            (pipeline-empty, account table, error-card CSS)
```

**DO NOT touch:** FAIT, FIRM, FORMS, FipShared, Sprint 5–7 entity files beyond what's listed.

---

## Acceptance Criteria

### Part A — Pagination
1. Pipeline board loads in < 2 seconds with 200 opportunities (verify in dev with seeded data)
2. Each pipeline column shows max 25 cards; "Load more (N remaining)" button appears when more exist
3. Clicking "Load more" appends next 25 cards without reloading the full page
4. Task Center loads first 25 tasks; "Load more" appends subsequent pages
5. `GetPipelineAsync()` no longer includes Quotes (verify via EF Core query log — no JOIN to `quotes`)
6. `GetByIdAsync` uses `AsSplitQuery()` (verify via EF Core query log — multiple SELECT statements, not one giant JOIN)

### Part B — Multi-Affinity
7. User `lauren.tig@titaninsurancegroup.com` sees "Titan Dashboard" branding and TIG logo
8. Adding a new user `demo.iaapa@iaapa.org → "iaapa"` to `UserAffinityMap` and redeploying causes that user to see IAAPA branding
9. A user with an `affinity_id` Entra claim overrides the `UserAffinityMap` lookup
10. New opportunities created by a TIG user have `opportunities.affinity_id = 'tig'`
11. Pipeline board for a TIG user shows only TIG opportunities (affinityId filter active)

### Part C — Accounts
12. `/accounts` page renders; shows empty state with "Sync from HubSpot" button when no accounts exist
13. "Sync from HubSpot" fetches companies from HubSpot API and populates `accounts` table
14. Company rows show name, location, active opp count, last synced timestamp
15. Search filters company names in real-time (client-side on loaded data)
16. Clicking a company row navigates to `/pipeline?company={name}`
17. Accounts nav link is active (no longer "Coming Soon")
18. Background sync runs every 15 minutes; `accounts.last_synced_at` updates on each run

### Part D — Empty States
19. All 9 panels show icon + message when their primary data collection is empty
20. All icons use `FamosIcons.*` constants — no `Icons.Material.*` directly in components

### Part E — Error Handling
21. If `ContactsPanel` throws an unhandled exception, the workspace shows the error card for that panel only — not a blank screen
22. The error card shows "Try again" link; clicking it calls `Recover()` on the error boundary and re-renders the panel
23. An exception in `Routes.razor`-level navigation shows the global error message (not a blank page)

### Part F — Performance
24. Dashboard `GetDashboardSummaryAsync` does NOT load all opportunities into memory — verify via EF Core query log that there is no `ToListAsync()` loading the full pipeline
25. `GetByIdAsync` emits multiple SELECT statements (AsSplitQuery) — verify in query log
26. `GetPipelineAsync` does NOT JOIN to `quotes` table

### Part G — HubSpot
27. Assigning an owner in the workspace triggers `SyncOwnerAsync` — verify via log entry `[HubSpot] Deal {id} owner → {email}`
28. Closing an opportunity triggers `SyncClosedAsync` — verify via log entry `[HubSpot] Deal {id} closed-lost`
29. When `HubSpot:ServiceKey` is not set, `SyncOwnerAsync` and `SyncClosedAsync` return immediately with no HTTP calls (same graceful no-op as existing methods)
30. A HubSpot API failure in `SyncOwnerAsync` does NOT prevent the owner assignment from completing in FAM OS

---

## Clint Review Priorities

```
⚠️  HIGH: OpportunityService now takes UserAffinityService as a constructor
          parameter. Verify DI registration in Program.cs registers UserAffinityService
          BEFORE OpportunityService (or as scoped — order doesn't matter for scoped,
          but both must be registered). If UserAffinityService is missing, the app
          will throw at startup, not at runtime.

⚠️  HIGH: GetStagePageAsync filters by AffinityId when provided. The AffinityId
          column is added in Sprint 8 with DEFAULT 'tig'. Any opportunity created
          before Sprint 8 deployment will have affinity_id = 'tig'. If a TIG user
          logs in, they will see pre-Sprint-8 opportunities (correct). If an IAAPA
          user logs in, they will see NO opportunities (correct — their data
          hasn't been migrated). Verify this is the desired behavior before
          going live with IAAPA.

⚠️  HIGH: AccountSyncService is registered as both IAccountSyncService (singleton)
          and a HostedService. The singleton pattern with IServiceScopeFactory
          (same as SignalRecomputeService) is correct for background services.
          Verify Tony uses IServiceScopeFactory.CreateScope() for every DB
          operation inside AccountSyncService — NOT a constructor-injected
          FamOsDbContext (scoped lifetime in singleton = runtime exception).

⚠️  HIGH: PanelErrorBoundary inherits ErrorBoundary. Verify @inherits ErrorBoundary
          is in the component directive (not @code). ErrorBoundary is
          Microsoft.AspNetCore.Components.ErrorBoundary — no using statement
          needed (it's in the global Blazor namespace). Verify the Recover()
          call in the @onclick is the ErrorBoundary.Recover() method, not a
          custom method.

⚠️  HIGH: HubSpotService.SyncOwnerAsync uses the HubSpot Settings API
          (/settings/v3/users) to resolve owner email → HubSpot user ID.
          This endpoint requires the `settings.users.read` OAuth scope.
          The current HubSpot:ServiceKey may be a private app token that
          doesn't have this scope. If the endpoint returns 403, the method
          logs and skips — no crash. But verify the token has the required
          scope before testing owner sync.

⚠️  MEDIUM: AsSplitQuery() on GetByIdAsync changes EF Core's query emission
            from one large JOIN to multiple round trips. This is almost always
            faster for 5+ includes, but it means the data is NOT read in a
            single transaction — if the DB is modified between queries (unlikely
            on a per-user workspace load), you could get an inconsistent snapshot.
            For Phase 1 (single ER per opportunity, low concurrency), this is
            acceptable. Note for Phase 2 multi-user.

⚠️  MEDIUM: Accounts.razor injects FamOsDbContext directly (not via factory).
            In Blazor Server, component lifecycle may cause concurrency issues
            with a directly-injected DbContext if multiple renders happen
            simultaneously. Use IDbContextFactory<FamOsDbContext> and
            CreateDbContextAsync() pattern instead — same as all other services.
            Tony should change the @inject FamOsDbContext Db to
            @inject IDbContextFactory<FamOsDbContext> DbFactory and use
            await DbFactory.CreateDbContextAsync() in LoadAsync().

⚠️  LOW: UserAffinityService caches nothing — calls GetUserAsync() and
         GetUserEmailAsync() on every request. These are fast (in-memory
         ClaimsPrincipal lookups), but the affinity resolution is called
         from MainLayout.OnInitializedAsync AND from Pipeline/Dashboard/Accounts
         on every page load. Consider caching the result in a scoped service
         field after the first resolution. Not required for Phase 1.

⚠️  LOW: Pipeline.razor now loads all 7 stage columns in parallel
         (Task.WhenAll). Each GetStagePageAsync creates its own DbContext
         (IDbContextFactory). 7 parallel DB connections on startup is
         acceptable for Aurora Serverless but verify the connection pool
         ceiling (default: 100) is not approached in production when
         multiple users load the pipeline simultaneously.
```

---

_Spec by Reed Richards | Sprint 8 = 5 new files, 28 modified. Pagination on pipeline + task center. Multi-affinity branding per login. Accounts page with HubSpot company sync. Empty states on all 9 panels. Panel-level error boundaries. AsSplitQuery + dashboard query fixes. HubSpot owner + close two-way sync._
