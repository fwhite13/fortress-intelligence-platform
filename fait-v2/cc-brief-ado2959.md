# Fix Brief: ADO#2959 — Spinner loop on load — assistant initialization check broken

## Working Directory
`/home/fredw/projects/fip/fait-v2/`

## Task Overview
Fix the infinite "Starting your assistant..." spinner on FAIT v2 dashboard load. Two files need to be changed.

---

## Fix 1: `src/FortressAI.V2.Web/Components/Routes.razor`

Remove the provisioning redirect logic from Routes.razor entirely. The `@code` block, `@inject FaitV2DbContext Db`, `@inject NavigationManager Nav` directives, and the `@using Microsoft.EntityFrameworkCore` should all be removed (EF is only used in the removed code block).

The correct final content of Routes.razor is:

```razor
<Router AppAssembly="typeof(App).Assembly"
        AdditionalAssemblies="new[] { typeof(FipShared.Components.FipNavBar).Assembly }">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @if (!context.User.Identity?.IsAuthenticated ?? true)
                {
                    <RedirectToLogin />
                }
                else
                {
                    <p>You are not authorized to view this page.</p>
                }
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
    <NotFound>
        <PageTitle>Not Found</PageTitle>
        <LayoutView Layout="typeof(Layout.MainLayout)">
            <p>Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>
```

Replace the ENTIRE file with this content (no @using, no @inject, no @code block).

---

## Fix 2: `src/FortressAI.V2.Web/Services/ProvisioningStatusService.cs`

Replace the entire file content with the following (switching from IHttpContextAccessor to AuthenticationStateProvider):

```csharp
using FortressAI.V2.Web.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class ProvisioningStatusService : IProvisioningStatusService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<ProvisioningStatusService> _logger;

    public ProvisioningStatusService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<ProvisioningStatusService> logger)
    {
        _dbFactory = dbFactory;
        _authStateProvider = authStateProvider;
        _logger = logger;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            var entraOid = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                        ?? user.FindFirst("oid")?.Value;

            if (string.IsNullOrEmpty(entraOid))
            {
                // No authenticated user — return true, auth middleware handles redirect
                return true;
            }

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var dbUser = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);

            return dbUser?.OnboardingCompletedAt != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProvisioningStatusService.CheckReadyAsync failed — defaulting to ready=true");
            return true;
        }
    }
}
```

---

## Verification Steps
After making these changes, run `dotnet build` from `/home/fredw/projects/fip/fait-v2/` and confirm 0 errors, 0 warnings.

DO NOT touch any other files. Only modify these two files:
1. `src/FortressAI.V2.Web/Components/Routes.razor`
2. `src/FortressAI.V2.Web/Services/ProvisioningStatusService.cs`
