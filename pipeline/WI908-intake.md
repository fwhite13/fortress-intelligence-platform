# WI#908 — HOTFIX: Blazor 500 on All Routes (AuthorizeRouteView rendermode boundary)

**Priority:** CRITICAL — site is 500 for all users including Fred
**Filed:** 2026-03-19 20:36 EDT

## Root Cause

`Routes.razor` applies `@rendermode="InteractiveServer"` directly on `<AuthorizeRouteView>`.
Blazor does not allow passing `RenderFragment<T>` parameters (like `<NotAuthorized>`) across a rendermode boundary — this throws:

```
System.InvalidOperationException: Cannot pass RenderFragment<T> parameter 'NotAuthorized' 
to component 'AuthorizeRouteView' with rendermode 'InteractiveServerRenderMode'. 
Templated content can't be passed across a rendermode boundary, because it is 
arbitrary code and cannot be serialized.
```

This 500s every single request, even for authenticated users.

## Fix

**File:** `famos/src/FamOs.Web/Components/Routes.razor`

Remove `@rendermode="InteractiveServer"` from `<AuthorizeRouteView>` and move rendermode to the per-page or layout level instead. The standard .NET 8 Blazor pattern for global InteractiveServer is to set it in `App.razor` or via `AddInteractiveServerRenderMode()` in `Program.cs`, NOT on `AuthorizeRouteView`.

### Option A (preferred) — rendermode on HeadOutlet + Routes in App.razor
In `App.razor`, the `<Routes>` component should have `@rendermode="InteractiveServer"`:
```razor
<Routes @rendermode="InteractiveServer" />
```
And `Routes.razor` should have NO rendermode on `AuthorizeRouteView`:
```razor
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
```

### Verify App.razor first
Check if `<Routes>` in `App.razor` already has `@rendermode` — if it does, just remove from `Routes.razor`. If not, add it there.

## Test
After fix:
1. `curl -skL -o /dev/null -w "%{http_code}" https://famos.dev.fortressam.ai/` → should be 200 (after auth redirect) not 500
2. Fred can log in and navigate normally
3. `/_blazor` still returns 302

## Files to change
- `famos/src/FamOs.Web/Components/Routes.razor` — remove `@rendermode` from `AuthorizeRouteView`
- `famos/src/FamOs.Web/Components/App.razor` — add `@rendermode="InteractiveServer"` to `<Routes>` if not already present

## Deploy
Standard pipeline. No migration needed.
