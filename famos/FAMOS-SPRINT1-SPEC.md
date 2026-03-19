# FAM OS — Sprint 1 Spec: Foundation

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-18  
**Status:** Ready for Tony (CC)  
**Goal:** Running, authenticated, navigable app at `https://famos.dev.fortressam.ai`  
**Reference:** `FAMOS-ARCHITECTURE-SPEC.md` (full data model + engine design)

---

## Sprint 1 Deliverable

A deployed Blazor Server app at `https://famos.dev.fortressam.ai` that:
- Authenticates via the shared FIP `.FortressAI.Session` cookie
- Shows the FipNavBar (FAMOS module)
- Has left-nav drawer with Dashboard, Pipeline, Task Center stubs
- Has all EF Core entities and `FamOsDbContext` in place (tables created on startup)
- Has `LifecycleCommandService` and `SignalResolver` wired (but no UI calling them yet)
- Has `OutboxProcessorService` and `SignalRecomputeService` background workers running
- Has `GET /health` returning 200
- Passes post-deploy verification checklist

Sprint 1 does **not** include: Opportunity CRUD, Pipeline Board, Lifecycle UI, signal chips. That's Sprint 2.

---

## Parallelization Map

**No parallelism in Sprint 1 — sequential single CC session.**

All files share `Program.cs`, `FamOsDbContext`, and `FamOs.Web.csproj`. A single CC session is the correct approach.

**Sequence:**
1. Project scaffold + `.csproj` + `Program.cs`
2. Domain enums + entities + `FamOsDbContext`
3. `LifecycleCommandService` + `SignalResolver` + `TransitionRules`
4. Background services (`OutboxProcessorService`, `SignalRecomputeService`)
5. `UserSessionService`
6. `FipTheme.cs`
7. Blazor components (App, Routes, MainLayout, NavMenu, stub pages)
8. `Dockerfile` + `buildspec.yml`
9. Wiring in `Program.cs` (DB init, service registration, middleware pipeline)

---

## File List — New Files to Create

```
fip/famos/src/FamOs.Web/FamOs.Web.csproj
fip/famos/src/FamOs.Web/Program.cs
fip/famos/src/FamOs.Web/appsettings.json
fip/famos/src/FamOs.Web/appsettings.Development.json
fip/famos/src/FamOs.Web/Domain/Enums.cs
fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs
fip/famos/src/FamOs.Web/Domain/SignalResolver.cs
fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs
fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs
fip/famos/src/FamOs.Web/Data/Entities/Submission.cs
fip/famos/src/FamOs.Web/Data/Entities/Quote.cs
fip/famos/src/FamOs.Web/Data/Entities/Proposal.cs
fip/famos/src/FamOs.Web/Data/Entities/PolicyShadowRecord.cs
fip/famos/src/FamOs.Web/Data/Entities/Activity.cs
fip/famos/src/FamOs.Web/Data/Entities/FamOsTask.cs
fip/famos/src/FamOs.Web/Data/Entities/OpportunityFlag.cs
fip/famos/src/FamOs.Web/Data/Entities/OutboxEvent.cs
fip/famos/src/FamOs.Web/Services/OutboxProcessorService.cs
fip/famos/src/FamOs.Web/Services/SignalRecomputeService.cs
fip/famos/src/FamOs.Web/Services/UserSessionService.cs
fip/famos/src/FamOs.Web/Services/HubSpotServiceStub.cs
fip/famos/src/FamOs.Web/Services/AmsServiceStub.cs
fip/famos/src/FamOs.Web/Theme/FipTheme.cs
fip/famos/src/FamOs.Web/Components/App.razor
fip/famos/src/FamOs.Web/Components/Routes.razor
fip/famos/src/FamOs.Web/Components/Layout/MainLayout.razor
fip/famos/src/FamOs.Web/Components/Layout/MainLayout.razor.css
fip/famos/src/FamOs.Web/Components/Layout/NavMenu.razor
fip/famos/src/FamOs.Web/Components/Pages/Dashboard.razor
fip/famos/src/FamOs.Web/Components/Pages/Pipeline.razor
fip/famos/src/FamOs.Web/Components/Pages/TaskCenter.razor
fip/famos/src/FamOs.Web/wwwroot/css/famos.css
fip/famos/Dockerfile
fip/famos/buildspec.yml
```

**Total: 35 new files. 0 modified files in other apps.**

**DO NOT touch:** any file outside `fip/famos/`. FAIT, FIRM, FORMS, FipShared are off-limits.

**Exception:** `fip/shared/FipShared/Models/FipModule.cs` — add `FAMOS = 4` to the `FipModule` enum and matching cases in `FullName()`, `ShortName()`, `Url()`. This is the only cross-app change. See exact diff below.

---

## 1. `FamOs.Web.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FamOs.Web</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="9.0.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.*" />
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="9.0.*" />
    <PackageReference Include="MySqlConnector" Version="2.*" />
    <PackageReference Include="MudBlazor" Version="7.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\shared\FipShared\FipShared.csproj" />
  </ItemGroup>
</Project>
```

**Note:** No AWSSDK packages in Sprint 1. No S3. No Bedrock. No split Data project — single project only.

---

## 2. `FipModule.cs` Change (FipShared — only cross-app file)

**File:** `fip/shared/FipShared/Models/FipModule.cs`

Add `FAMOS = 4` and the three matching switch cases:

```csharp
// In enum FipModule:
FAMOS = 4,

// In FullName():
FipModule.FAMOS  => "FAM OS",

// In ShortName():
FipModule.FAMOS  => "FAM OS",

// In Url():
FipModule.FAMOS  => "https://famos.fortressam.ai",
```

---

## 3. `Domain/Enums.cs`

Exact content from architecture spec section 4. Copy verbatim:

```csharp
namespace FamOs.Web.Domain;

public enum LifecycleStage
{
    Intake             = 0,
    UnderwritingPrep   = 1,
    Marketed           = 2,
    QuotesReceived     = 3,
    ClientDecision     = 4,
    Binding            = 5,
    Bound              = 6,
    ClosedNotBound     = 7
}

public enum DominantSignal
{
    Parked                   = 0,
    WaitingOnClient          = 1,
    UnderwritingInProgress   = 2,
    WaitingOnMarket          = 3,
    DecisionRequired         = 4,
    AwaitingClientDecision   = 5,
    BindingInProgress        = 6,
    PostBindProcessing       = 7,
    TimeRisk                 = 8
}

public enum OpportunityFlagType
{
    Parked    = 0,
    Lost      = 1,
    TimeRisk  = 2,
    Stalled   = 3
}

public enum DomainEventType
{
    OpportunityLifecycleChanged = 0,
    QuoteRecorded               = 1,
    ProposalSent                = 2,
    BindRequested               = 3,
    BinderReceived              = 4,
    DominantSignalChanged       = 5,
    OpportunityClosed           = 6,
    OpportunityParked           = 7
}
```

---

## 4. Entity Files

Each entity is a separate file in `Data/Entities/`. Exact C# from architecture spec section 5. Copy each verbatim. Quick reference:

| File | Key fields |
|------|------------|
| `Opportunity.cs` | `Id (Guid)`, `LifecycleStage`, `DominantSignal`, `OwnerUserId (string)`, `Version (int)` — IsConcurrencyToken |
| `Submission.cs` | `Id`, `OpportunityId`, `CarrierName`, `Status` |
| `Quote.cs` | `Id`, `OpportunityId`, `SubmissionId`, `PremiumAmount`, `IsRecommended` |
| `Proposal.cs` | `Id`, `OpportunityId`, `RecommendedQuoteId`, `Status`, `SentAt` |
| `PolicyShadowRecord.cs` | `Id`, `OpportunityId`, `WinningQuoteId`, `SnapshotJson (string)` |
| `Activity.cs` | `Id`, `OpportunityId`, `EventType`, `Description`, `OccurredAt` |
| `FamOsTask.cs` | `Id`, `OpportunityId`, `Title`, `Status`, `AssignedToUserId` |
| `OpportunityFlag.cs` | `Id`, `OpportunityId`, `FlagType (OpportunityFlagType)`, `IsActive` |
| `OutboxEvent.cs` | `Id`, `EventType`, `PayloadJson`, `Processed`, `RetryCount` |

All entities use `Guid` PKs as `char(36)` in MySQL. All navigation properties included.

---

## 5. `Data/FamOsDbContext.cs`

Two DbContext classes in this file:

### 5.1 `FamOsDbContext` (main app data)

Full content from architecture spec section 5.10. Registers all 9 `DbSet<T>` properties and configures all entity mappings in `OnModelCreating`. Copy exactly — table names, index names, column types all matter.

### 5.2 `SharedKeyRingDbContext` (DataProtection — same pattern as FORMS)

```csharp
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamOs.Web.Data;

/// <summary>
/// Separate DbContext for DataProtection key ring.
/// Points to fred_dev.DataProtectionKeys — shared with FAIT/FIRM/FORMS.
/// DO NOT add any other entities here.
/// </summary>
public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options)
        : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
```

---

## 6. `Domain/LifecycleCommandService.cs`

Full content from architecture spec section 6.2. All 9 command methods:
1. `PursueOpportunityAsync`
2. `RouteToMarketAsync`
3. `RecordQuoteAsync`
4. `SendProposalAsync`
5. `ReopenMarketAsync`
6. `RequestBindAsync`
7. `RecordBinderReceivedAsync`
8. `ParkOpportunityAsync`
9. `CloseOpportunityAsync`

Plus exception classes `LifecycleValidationException` and `NotFoundException` at the bottom.

Copy from architecture spec section 6.2 exactly.

---

## 7. `Domain/SignalResolver.cs`

Full content from architecture spec section 7. Eleven ordered rules, pure function. Constructor reads `FamOs:SubmissionAgingDays` and `FamOs:ProposalAgingDays` from config with defaults 7 and 5. Copy exactly.

---

## 8. Background Services

### `Services/OutboxProcessorService.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;

namespace FamOs.Web.Services;

public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<OutboxProcessorService> _logger;

    public OutboxProcessorService(IServiceScopeFactory services,
        ILogger<OutboxProcessorService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait for app to finish startup before first run
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Outbox] Batch processing error");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task ProcessBatchAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var events = await db.OutboxEvents
            .Where(e => !e.Processed && e.RetryCount < 5)
            .OrderBy(e => e.OccurredAt)
            .Take(50)
            .ToListAsync();

        if (!events.Any()) return;

        foreach (var evt in events)
        {
            try
            {
                // Phase 1: log only.
                // Phase 2: route to HubSpot stub, AMS stub, etc.
                _logger.LogInformation("[Outbox] Processing {EventType}: {Payload}",
                    evt.EventType, evt.PayloadJson);

                evt.Processed   = true;
                evt.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                evt.RetryCount++;
                evt.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "[Outbox] Failed event {Id}, retry {N}",
                    evt.Id, evt.RetryCount);
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[Outbox] Processed {Count} events", events.Count);
    }
}
```

### `Services/SignalRecomputeService.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class SignalRecomputeService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<SignalRecomputeService> _logger;

    public SignalRecomputeService(IServiceScopeFactory services,
        ILogger<SignalRecomputeService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RecomputeAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalRecompute] Error during recompute run");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    private async Task RecomputeAllAsync()
    {
        using var scope = _services.CreateScope();
        var db       = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<SignalResolver>();

        var opps = await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Where(o => !o.IsClosed)
            .ToListAsync();

        int changed = 0;
        foreach (var opp in opps)
        {
            var (signal, reason) = resolver.Resolve(opp);
            if (opp.DominantSignal != signal || opp.DominantSignalReason != reason)
            {
                opp.DominantSignal       = signal;
                opp.DominantSignalReason = reason;
                opp.UpdatedAt            = DateTime.UtcNow;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("[SignalRecompute] Updated {Count}/{Total} opportunities",
                changed, opps.Count);
        }
    }
}
```

### `Services/UserSessionService.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FamOs.Web.Services;

public class UserSessionService
{
    private readonly AuthenticationStateProvider _authProvider;

    public UserSessionService(AuthenticationStateProvider authProvider)
    {
        _authProvider = authProvider;
    }

    private ClaimsPrincipal? _user;

    public async Task<ClaimsPrincipal> GetUserAsync()
    {
        if (_user != null) return _user;
        var state = await _authProvider.GetAuthenticationStateAsync();
        _user = state.User;
        return _user;
    }

    public async Task<string> GetUserIdAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? "unknown";
    }

    public async Task<string> GetUserNameAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.Identity?.Name
            ?? "User";
    }

    public async Task<string> GetUserEmailAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? "";
    }
}
```

### `Services/HubSpotServiceStub.cs`

```csharp
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public interface IHubSpotService
{
    Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage);
    Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow);
}

public class HubSpotServiceStub : IHubSpotService
{
    private readonly ILogger<HubSpotServiceStub> _logger;
    public HubSpotServiceStub(ILogger<HubSpotServiceStub> logger) => _logger = logger;

    public Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage)
    {
        _logger.LogInformation("[HubSpot stub] Lifecycle sync: {Id} → {Stage}", opportunityId, stage);
        return Task.CompletedTask;
    }

    public Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow)
    {
        _logger.LogInformation("[HubSpot stub] Policy shadow: {Id}", shadow.Id);
        return Task.CompletedTask;
    }
}
```

### `Services/AmsServiceStub.cs`

```csharp
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IAmsService
{
    Task PushPolicyShadowAsync(PolicyShadowRecord record);
}

public class AmsServiceStub : IAmsService
{
    private readonly ILogger<AmsServiceStub> _logger;
    public AmsServiceStub(ILogger<AmsServiceStub> logger) => _logger = logger;

    public Task PushPolicyShadowAsync(PolicyShadowRecord record)
    {
        _logger.LogInformation("[AMS stub] Policy shadow push: {Id}", record.Id);
        return Task.CompletedTask;
    }
}
```

---

## 9. `Theme/FipTheme.cs`

Copy from FORMS `Theme/FipTheme.cs` verbatim (light mode only, no `PaletteDark`). Change namespace to `FamOs.Web.Theme`.

---

## 10. Blazor Components

### `Components/App.razor`

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>FAM OS</title>
    <base href="/" />
    <link rel="stylesheet" href="/_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="/_content/FipShared/css/fip-tokens.css" />
    <link rel="stylesheet" href="/css/famos.css" />
    <HeadOutlet @rendermode="RenderMode.InteractiveServer" />
</head>
<body>
    <Routes @rendermode="RenderMode.InteractiveServer" />
    <script src="/_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="/_framework/blazor.server.js"></script>
</body>
</html>
```

**Note:** Uses `blazor.server.js` (same as FIRM, not FORMS which uses `blazor.web.js`). This is a pure Blazor Server app.

### `Components/Routes.razor`

```razor
@using Microsoft.AspNetCore.Components.Routing

<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @if (!context.User.Identity?.IsAuthenticated ?? true)
                {
                    <RedirectToLogin />
                }
                else
                {
                    <p>Access denied.</p>
                }
            </NotAuthorized>
        </AuthorizeRouteView>
    </Found>
    <NotFound>
        <LayoutView Layout="typeof(Layout.MainLayout)">
            <p role="alert">Sorry, nothing here.</p>
        </LayoutView>
    </NotFound>
</Router>

@code {
    // Redirect component inline
    private sealed class RedirectToLogin : ComponentBase
    {
        [Inject] NavigationManager Nav { get; set; } = default!;
        protected override void OnInitialized() =>
            Nav.NavigateTo("/auth/redirect-to-login", forceLoad: true);
    }
}
```

### `Components/Layout/MainLayout.razor`

```razor
@inherits LayoutComponentBase
@using FamOs.Web.Theme
@using System.Security.Claims
@using FipShared.Components
@using FipShared.Models
@inject NavigationManager _nav
@inject Microsoft.AspNetCore.Hosting.IWebHostEnvironment HostEnv

<MudThemeProvider Theme="_theme" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <FipNavBar ActiveModule="FipModule.FAMOS"
               UserInitial="@_userInitial"
               UserName="@_userName"
               UserEmail="@_userEmail"
               OnMenuClick="ToggleDrawer"
               OnSignOut="@(() => _nav.NavigateTo("/auth/logout", forceLoad: true))"
               IsDev="@HostEnv.IsDevelopment()" />

    <MudDrawer @bind-Open="_drawerOpen" Variant="DrawerVariant.Responsive"
               Breakpoint="Breakpoint.Md" ClipMode="DrawerClipMode.Always" Elevation="2">
        <div style="display: flex; flex-direction: column; height: 100%;">
            <MudDrawerHeader Style="padding: 12px 16px; background: var(--color-sidebar-bg);">
                <div style="display: flex; align-items: center; gap: 8px;">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" style="color: var(--color-gold)">
                        <path d="M12 2L3 7v5c0 5.25 3.75 10.15 9 11.5C17.25 22.15 21 17.25 21 12V7L12 2z" fill="currentColor" opacity="0.9"/>
                        <path d="M9 12l2 2 4-4" stroke="white" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                    </svg>
                    <span style="color: rgba(248,250,252,0.8); font-size: 14px; font-weight: 500;">Fortress</span>
                </div>
                <div style="color: var(--color-gold); font-size: var(--text-lg); font-weight: var(--font-bold); margin-top: 2px;">
                    FAM OS
                </div>
            </MudDrawerHeader>
            <div style="flex: 1; overflow-y: auto;">
                <NavMenu />
            </div>
            <div class="fip-drawer-footer">
                <MudText Typo="Typo.caption">Fortress Affinity Management OS</MudText>
            </div>
        </div>
    </MudDrawer>

    <MudMainContent Class="pa-4" Style="padding-top: 80px !important;">
        @Body
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawerOpen = true;
    private string _userInitial = "F";
    private string _userName = "";
    private string _userEmail = "";

    private MudTheme _theme = FipTheme.Create();

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var authProvider = ServiceProvider.GetService<AuthenticationStateProvider>();
        if (authProvider != null)
        {
            try
            {
                var auth = await authProvider.GetAuthenticationStateAsync();
                var name = auth.User?.Identity?.Name
                           ?? auth.User?.FindFirst("name")?.Value
                           ?? auth.User?.FindFirst(ClaimTypes.Name)?.Value
                           ?? "";
                var email = auth.User?.FindFirst("email")?.Value
                            ?? auth.User?.FindFirst(ClaimTypes.Email)?.Value
                            ?? "";
                _userName = string.IsNullOrEmpty(name) ? "User" : name;
                _userEmail = email;
                _userInitial = name.Length > 0 ? name[0].ToString().ToUpper() : "F";
            }
            catch
            {
                _userInitial = "F";
            }
        }
    }

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
}
```

### `Components/Layout/MainLayout.razor.css`

```css
/* No <style> blocks in .razor files — scoped CSS only */
.fip-drawer-footer {
    padding: 12px 16px;
    border-top: 1px solid var(--color-border);
    color: var(--color-text-muted);
}
```

### `Components/Layout/NavMenu.razor`

```razor
@using Microsoft.AspNetCore.Components.Routing

<MudNavMenu>
    <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">
        Dashboard
    </MudNavLink>
    <MudNavLink Href="/pipeline" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.ViewKanban">
        Pipeline
    </MudNavLink>
    <MudNavLink Href="/tasks" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.CheckBox">
        Task Center
    </MudNavLink>
    <MudDivider Class="my-2" />
    <MudNavLink Href="/accounts" Disabled="true" Icon="@Icons.Material.Filled.Business">
        Accounts <MudChip Size="Size.Small" Color="Color.Default" Class="ml-1">Soon</MudChip>
    </MudNavLink>
    <MudNavLink Href="/reports" Disabled="true" Icon="@Icons.Material.Filled.BarChart">
        Reports <MudChip Size="Size.Small" Color="Color.Default" Class="ml-1">Soon</MudChip>
    </MudNavLink>
</MudNavMenu>
```

### `Components/Pages/Dashboard.razor`

```razor
@page "/"
@attribute [Authorize]
<PageTitle>Dashboard — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Dashboard</MudText>
<MudText Typo="Typo.body1" Color="Color.Secondary">
    Sprint 2: signal prioritization and pipeline summary will appear here.
</MudText>
```

### `Components/Pages/Pipeline.razor`

```razor
@page "/pipeline"
@attribute [Authorize]
<PageTitle>Pipeline — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Pipeline</MudText>
<MudText Typo="Typo.body1" Color="Color.Secondary">
    Sprint 2: Kanban board with lifecycle stage columns will appear here.
</MudText>
```

### `Components/Pages/TaskCenter.razor`

```razor
@page "/tasks"
@attribute [Authorize]
<PageTitle>Task Center — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Task Center</MudText>
<MudText Typo="Typo.body1" Color="Color.Secondary">
    Sprint 3: task batching and reassignment will appear here.
</MudText>
```

---

## 11. `wwwroot/css/famos.css`

```css
/* FAM OS — application styles */
/* Extends FipShared fip-tokens.css */

/* Pipeline board */
.famos-pipeline-board {
    display: flex;
    gap: 16px;
    overflow-x: auto;
    padding-bottom: 16px;
}

.famos-pipeline-column {
    min-width: 280px;
    max-width: 320px;
    background: var(--color-surface);
    border-radius: 8px;
    border: 1px solid var(--color-border);
}

.famos-pipeline-column-header {
    padding: 12px 16px;
    border-bottom: 1px solid var(--color-border);
    font-weight: var(--font-semibold);
    font-size: var(--text-sm);
    color: var(--color-text-secondary);
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

/* Signal chips */
.famos-signal-chip {
    font-size: 11px;
    font-weight: 600;
    padding: 2px 8px;
    border-radius: 4px;
}

.famos-signal-time-risk     { background: #FEE2E2; color: #991B1B; }
.famos-signal-waiting-on-client { background: #FEF3C7; color: #92400E; }
.famos-signal-decision-required { background: #FED7AA; color: #9A3412; }
.famos-signal-waiting-on-market { background: #EDE9FE; color: #5B21B6; }
.famos-signal-awaiting-client   { background: #DBEAFE; color: #1E40AF; }
.famos-signal-binding           { background: #D1FAE5; color: #065F46; }
.famos-signal-underwriting      { background: #E0F2FE; color: #075985; }
.famos-signal-post-bind         { background: #CCFBF1; color: #134E4A; }
.famos-signal-parked            { background: #F3F4F6; color: #4B5563; }
```

---

## 12. `appsettings.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*",
  "FamOs": {
    "SubmissionAgingDays": 7,
    "ProposalAgingDays": 5
  }
}
```

## 13. `appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=famos_dev;User=root;Password=dev;"
  },
  "FamOs": {
    "SubmissionAgingDays": 1,
    "ProposalAgingDays": 1
  }
}
```

**No Kestrel block.** Do not add one. See FORMS port-fix lesson.

---

## 14. `Program.cs`

Full file. Copy exactly — do not improvise on auth, DB init, or middleware ordering.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;
using FamOs.Web.Data;
using FamOs.Web.Domain;
using FamOs.Web.Services;
using FamOs.Web.Components;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── MudBlazor ──
builder.Services.AddMudServices();

// ── Authentication — FAMOS is a cookie consumer only ──
// FIP portal (FAIT) owns OIDC. FAMOS reads the shared .FortressAI.Session cookie.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.Name = ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── EF Core (Aurora MySQL) — app database ──
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["FAMOS_DB_NAME"] ?? "famos_dev";

string connectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    var csb = new MySqlConnectionStringBuilder
    {
        Server            = dbHost,
        Port              = uint.Parse(dbPort),
        Database          = dbName,
        UserID            = dbUser,
        Password          = dbPass,
        ConnectionTimeout = 10
    };
    connectionString = csb.ConnectionString;
    Console.WriteLine($"Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("Default")
        ?? "Server=localhost;Database=famos_dev;User=root;Password=dev;";
    Console.WriteLine("Using local connection string.");
}

var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<FamOsDbContext>(options =>
    options.UseMySql(connectionString, serverVersion,
        mysql => mysql.EnableRetryOnFailure(3)));

// ── Data Protection: shared key ring (points to fred_dev) ──
var keyRingHost = builder.Configuration["FORTRESS_DB_HOST"];
var keyRingDb   = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";
var keyRingCsb  = new MySqlConnectionStringBuilder
{
    Server            = keyRingHost ?? "localhost",
    Port              = uint.Parse(builder.Configuration["FORTRESS_DB_PORT"] ?? "3306"),
    Database          = keyRingDb,
    UserID            = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql",
    Password          = builder.Configuration["FORTRESS_DB_PASS"] ?? "",
    ConnectionTimeout = 10
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

// ── Application Services ──
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<SignalResolver>();
builder.Services.AddScoped<LifecycleCommandService>();
builder.Services.AddScoped<IHubSpotService, HubSpotServiceStub>();
builder.Services.AddScoped<IAmsService, AmsServiceStub>();

// ── Background Services ──
builder.Services.AddHostedService<OutboxProcessorService>();
builder.Services.AddHostedService<SignalRecomputeService>();

// ── Internal HTTP client (for any Blazor → API calls) ──
var internalBase = builder.Environment.IsDevelopment()
    ? "http://localhost:8080/"
    : "http://localhost:8080/";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(internalBase) });

var app = builder.Build();
var logger = app.Logger;

// ── Database initialization (background — allows health probe to pass immediately) ──
_ = Task.Run(async () =>
{
    await Task.Delay(5000);
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FamOsDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    try
    {
        bool tablesExist = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM opportunities LIMIT 1");
            tablesExist = true;
        }
        catch
        {
            // Expected on first run
        }

        if (!tablesExist)
        {
            try
            {
                var creator = db.Database.GetService<IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync();
                Console.WriteLine("[FAM OS] Database tables created.");
            }
            catch (MySqlException ex) when (ex.Number == 1050)
            {
                logger.LogDebug("CreateTablesAsync: tables already exist (1050), continuing");
            }
        }
        else
        {
            Console.WriteLine("[FAM OS] DB tables already exist.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FAM OS] Database initialization failed");
    }
});

// ── Middleware pipeline ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Health check (ALB probe — no auth) ──
app.MapGet("/health", () => Results.Ok(new {
    status    = "healthy",
    service   = "famos",
    timestamp = DateTime.UtcNow
})).AllowAnonymous();

// ── Auth redirect helper ──
app.MapGet("/auth/redirect-to-login", (HttpContext ctx) =>
{
    var faitUrl = builder.Configuration["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/";
    var returnUrl = Uri.EscapeDataString(ctx.Request.Headers.Referer.FirstOrDefault() ?? "/");
    return Results.Redirect($"{faitUrl}?returnUrl={returnUrl}");
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var faitUrl = builder.Configuration["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/";
    return Results.Redirect(faitUrl);
}).AllowAnonymous().DisableAntiforgery();

// ── Blazor Server ──
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

---

## 15. `Dockerfile`

Copy from FORMS Dockerfile — change project names only:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy shared FipShared project (referenced as ../../../shared/FipShared from famos/src/FamOs.Web)
COPY shared/FipShared/ shared/FipShared/

# Copy project file for restore caching
COPY famos/src/FamOs.Web/FamOs.Web.csproj famos/src/FamOs.Web/
RUN dotnet restore "famos/src/FamOs.Web/FamOs.Web.csproj"

# Copy full famos source
COPY famos/src/ famos/src/

WORKDIR "/src/famos/src/FamOs.Web"
RUN dotnet build "FamOs.Web.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/famos/src/FamOs.Web"
RUN dotnet publish "FamOs.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FamOs.Web.dll"]
```

**Must be built from monorepo root `~/projects/fip/`** — not from `famos/` subdirectory. Same build context as FORMS.

---

## 16. `buildspec.yml`

```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
  build:
    commands:
      - echo Build started on `date`
      - docker build -f famos/Dockerfile -t famos-web:$IMAGE_TAG .
      - docker tag famos-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
  post_build:
    commands:
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
      - aws ecs update-service --cluster fortress-tools-cluster --service famos-dev --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

**Docker build context is monorepo root** — `docker build -f famos/Dockerfile -t famos-web:$IMAGE_TAG .` (the trailing `.` is the repo root, not `famos/`).

---

## 17. Infrastructure Tasks (Rhodey)

These are **not** Tony's tasks. Separate WI for Rhodey:

1. **Create ECR repo** `famos-web` in account 742932328420, region us-east-1
2. **Create ECS service** `famos-dev` on `fortress-tools-cluster` (Fargate, 512 CPU / 1024 MB)
3. **Create database** `famos_dev` on Aurora cluster `fortress-ai-cluster` (same user/pass as other FIP DBs)
4. **ALB listener rule**: `famos.dev.fortressam.ai` → new target group `famos-dev-tg` (port 8080, health: `GET /health`)
5. **Route53**: `famos.dev.fortressam.ai` CNAME → ALB
6. **ECS task definition env vars**:
   ```
   FORTRESS_DB_HOST=fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com
   FORTRESS_DB_PORT=3306
   FORTRESS_DB_USER=fortress_mysql
   FORTRESS_DB_PASS=<from secrets>
   FAMOS_DB_NAME=famos_dev
   FIP_KEYRING_DB_NAME=fred_dev
   Auth__CookieDomain=.dev.fortressam.ai
   FIP__LoginUrl=https://fait.dev.fortressam.ai/
   ASPNETCORE_ENVIRONMENT=Production
   ASPNETCORE_URLS=http://+:8080
   ```
7. **CodeBuild project** for `famos/buildspec.yml` — same pattern as FORMS CodeBuild project
8. **IAM task role** with `rds-db:connect` on Aurora cluster (if using IAM auth) or use password auth

---

## 18. Post-Deploy Verification Checklist

Tony runs this after deploy to `famos.dev.fortressam.ai`:

```
□ GET https://famos.dev.fortressam.ai/health → 200 {"status":"healthy","service":"famos"}
□ GET https://famos.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css → 200
□ GET https://famos.dev.fortressam.ai/_content/MudBlazor/MudBlazor.min.css → 200
□ GET https://famos.dev.fortressam.ai/ (authenticated) → 200, shows Dashboard stub
□ GET https://famos.dev.fortressam.ai/pipeline → Pipeline stub page
□ GET https://famos.dev.fortressam.ai/tasks → Task Center stub page
□ FipNavBar visible with FAM OS active module highlighted
□ Left drawer nav shows: Dashboard, Pipeline, Task Center (active), Accounts/Reports (disabled)
□ CloudWatch logs show "[FAM OS] Database tables created." or "already exist"
□ CloudWatch logs show no startup exceptions
□ Sign-out redirects to https://fait.dev.fortressam.ai/
```

---

## Clint Review Priorities

```
⚠️  HIGH: FipModule.FAMOS = 4 added to FipShared — verify all switch statements
          on FipModule have a case for FAMOS or a default clause. Missing case
          in FipNavBar will cause a compile error or runtime exception on nav render.

⚠️  HIGH: DataProtection SetApplicationName("FortressAI") must match FAIT/FIRM/FORMS
          exactly (case-sensitive). A typo here means FAMOS cannot decrypt the
          shared session cookie. Verify by trying to log in after deploy.

⚠️  HIGH: DisableAutomaticKeyGeneration() must be present. If absent, FAMOS will try
          to write new DataProtection keys to fred_dev, which may conflict with FAIT.

⚠️  MEDIUM: Dockerfile uses net9.0 base image. Verify mcr.microsoft.com/dotnet/aspnet:9.0
            is available in the region. FORMS uses 8.0 — this is intentional (FAM OS targets net9.0).

⚠️  MEDIUM: `blazor.server.js` (not `blazor.web.js`) — correct for pure Blazor Server.
            FORMS uses blazor.web.js because it has WebAssembly interop. FAMOS is Server-only.

⚠️  LOW: No EF migrations — CreateTablesAsync pattern. Verify background task fires
         after 5s delay and doesn't block health probe response.
```

---

## Acceptance Criteria

1. `GET /health` returns `200 {"status":"healthy","service":"famos"}` within 30s of container start
2. `GET /_content/FipShared/css/fip-tokens.css` returns 200
3. Authenticated user (FAIT session cookie) can navigate Dashboard/Pipeline/TaskCenter
4. Unauthenticated user is redirected to `https://fait.dev.fortressam.ai/`
5. CloudWatch logs confirm DB tables created on first boot
6. No `NullReferenceException` or `InvalidOperationException` in CloudWatch on normal navigation
7. FipNavBar renders with FAM OS module highlighted (not another module)

---

_Spec by Reed Richards | Sprint 1 = 35 files, 1 FipShared change. Running app at famos.dev.fortressam.ai. No lifecycle UI yet._
