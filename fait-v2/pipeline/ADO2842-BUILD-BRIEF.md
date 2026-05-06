# ADO#2842 — FAIT v2: Blazor Server App Shell
## Claude Code Build Brief

**Date:** 2026-05-06  
**Author:** Tony Stark (software-engineer subagent)  
**ADO WI:** #2842 — FAIT v2: Create Blazor Server app shell in FIP monorepo with Entra SSO and FIP waffle nav  
**Target directory:** `~/projects/fip/fait-v2/`  
**Monorepo root:** `~/projects/fip/`

---

## Mandatory Rules (NON-NEGOTIABLE)

- **Entra-only auth** — NO Cognito anywhere. Use `Microsoft.Identity.Web` for Entra SSO.
- **Dockerfile.debian** — create `Dockerfile.debian` (not `Dockerfile`); use `mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim` base
- **GuidFormat=None** on ALL MySqlConnector connections
- **MudBlazor v7** — use v7.* only
- **App project:** `FortressAI.V2.Web` in `~/projects/fip/fait-v2/src/FortressAI.V2.Web/`
- **CSS-class driven UI** — no inline styles, no MudBlazor default prop overrides; all styling via CSS classes
- **FipShared reference** — use `~/projects/fip/shared/FipShared/FipShared.csproj` for the FipNavBar component (waffle nav)

---

## What to Build

Create a new Blazor Server application `FortressAI.V2.Web` in `~/projects/fip/fait-v2/src/FortressAI.V2.Web/`.

**This is a SHELL only.** You are building the scaffolding and infrastructure — not implementing any actual AI/memory/agent logic. Every feature page is a stub with a title and placeholder content.

---

## Directory Structure to Create

```
~/projects/fip/fait-v2/
├── src/
│   └── FortressAI.V2.Web/
│       ├── FortressAI.V2.Web.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Components/
│       │   ├── App.razor
│       │   ├── Routes.razor
│       │   ├── _Imports.razor
│       │   ├── Layout/
│       │   │   ├── MainLayout.razor
│       │   │   └── MainLayout.razor.css
│       │   └── Pages/
│       │       ├── Dashboard.razor
│       │       ├── Onboarding.razor
│       │       ├── Memory.razor
│       │       ├── Tasks.razor
│       │       ├── Workspace.razor
│       │       └── Connectors.razor
│       ├── Theme/
│       │   └── FipTheme.cs
│       ├── wwwroot/
│       │   ├── css/
│       │   │   └── app.css
│       │   └── favicon.ico  (copy from fait if possible, else omit)
│       └── Properties/
│           └── launchSettings.json
├── Dockerfile.debian
└── pipeline/
    └── ADO2842-BUILD-BRIEF.md  (already exists)
```

---

## 1. Project File: FortressAI.V2.Web.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Entra SSO -->
    <PackageReference Include="Microsoft.Identity.Web" Version="3.*" />
    <PackageReference Include="Microsoft.Identity.Web.UI" Version="3.*" />
    
    <!-- MudBlazor v7 -->
    <PackageReference Include="MudBlazor" Version="7.*" />
    
    <!-- Auth components -->
    <PackageReference Include="Microsoft.AspNetCore.Components.Authorization" Version="8.0.*" />
    
    <!-- DB (shell only — for future use) -->
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.*" />
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.*" />
    <PackageReference Include="MySqlConnector" Version="2.*" />
    
    <!-- Logging -->
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
    <PackageReference Include="Serilog.Sinks.Console" Version="6.*" />
    
    <!-- Data Protection -->
    <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="8.0.*" />
  </ItemGroup>

  <ItemGroup>
    <!-- FIP shared nav/waffle component -->
    <ProjectReference Include="..\..\..\shared\FipShared\FipShared.csproj" />
  </ItemGroup>
</Project>
```

---

## 2. Program.cs

Wire Entra SSO using Microsoft.Identity.Web. Follow the exact FIP pattern — shared cookie `.FortressAI.Session`, cross-subdomain.

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using MudBlazor.Services;
using Serilog;
using FortressAI.V2.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Logging.ClearProviders();
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console());

// Razor Components + Interactive Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Entra SSO via Microsoft.Identity.Web
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Override the cookie to use the shared FIP session cookie
builder.Services.Configure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = ".FortressAI.Session";
        options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/signin";
        options.AccessDeniedPath = "/access-denied";
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllersWithViews();

// MudBlazor
builder.Services.AddMudServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Forward headers (ALB / ECS pattern)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health endpoint — public
app.MapGet("/health", () => "OK").AllowAnonymous();

// MicrosoftIdentity challenge/callback endpoints
app.MapControllers();

// Blazor components — all routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();
```

---

## 3. appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
    ],
    "Enrich": ["FromLogContext"]
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "7152ea12-c930-44b0-bb52-069152161c5b",
    "ClientId": "PLACEHOLDER_NEEDS_REAL_ENTRA_APP_REGISTRATION",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-callback-oidc",
    "ClientSecret": "PLACEHOLDER_SET_IN_KEY_VAULT_OR_ENV"
  },
  "Auth": {
    "CookieDomain": ""
  },
  "FIP": {
    "ComingSoonApps": "",
    "LoginUrl": "https://fait.fortressintelligence.com"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=fait_v2_dev;User=root;Password=dev;GuidFormat=None;"
  },
  "AWS": {
    "Region": "us-east-1"
  }
}
```

---

## 4. appsettings.Development.json

```json
{
  "AzureAd": {
    "TenantId": "7152ea12-c930-44b0-bb52-069152161c5b",
    "ClientId": "PLACEHOLDER_NEEDS_REAL_ENTRA_APP_REGISTRATION",
    "ClientSecret": "PLACEHOLDER_DEV"
  },
  "Auth": {
    "CookieDomain": ""
  }
}
```

---

## 5. App.razor

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&display=swap" />
    <link rel="stylesheet" href="_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="css/app.css" />
    <title>FAIT v2 — Fortress AI</title>
    <HeadOutlet />
</head>
<body>
    <Routes />
    <script src="_framework/blazor.server.js"></script>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
</body>
</html>
```

---

## 6. Routes.razor

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

Also create a simple `RedirectToLogin.razor` component in Components/:

```razor
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        Nav.NavigateTo($"MicrosoftIdentity/Account/SignIn?returnUrl={Uri.EscapeDataString(Nav.Uri)}", forceLoad: true);
    }
}
```

---

## 7. _Imports.razor

```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using FortressAI.V2.Web
@using FortressAI.V2.Web.Components
@using FortressAI.V2.Web.Components.Layout
@using FortressAI.V2.Web.Theme
@using FipShared.Components
@using FipShared.Models
@using MudBlazor
```

---

## 8. Theme/FipTheme.cs

FAIT v2 uses the Fortress brand colors with dark/light toggle capability. Use these exact colors:
- Blue: `#0066CC`
- Dark: `#1A1A2E`
- Gold accent: `#d4af37`

```csharp
using MudBlazor;

namespace FortressAI.V2.Web.Theme;

/// <summary>
/// FAIT v2 — Fortress brand theme with dark/light toggle support.
/// Brand colors: Blue #0066CC, Dark #1A1A2E, Gold #d4af37
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#0066CC",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1A1A2E",
            Background = "#f8f9fa",
            Surface = "#ffffff",
            AppbarBackground = "#1A1A2E",
            AppbarText = "#ffffff",
            DrawerBackground = "#1A1A2E",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#1A1A2E",
            TextSecondary = "#6b7280",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "#6b7280",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#0066CC",
            TableLines = "#e5e7eb",
            TableHover = "#f3f4f6",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#0066CC",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1A1A2E",
            Background = "#0d0d1a",
            Surface = "#1A1A2E",
            AppbarBackground = "#0d0d1a",
            AppbarText = "#f0f0f0",
            DrawerBackground = "#0d0d1a",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#f0f0f0",
            TextSecondary = "#9ca3af",
            TextDisabled = "rgba(255,255,255,0.38)",
            ActionDefault = "#9ca3af",
            Success = "#34d399",
            Warning = "#fbbf24",
            Error = "#f87171",
            Info = "#60a5fa",
            TableLines = "#374151",
            TableHover = "#1f2937",
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "sans-serif" },
                FontSize = "0.9375rem",
                LineHeight = 1.6,
            },
            H4 = new H4 { FontWeight = 700 },
            H5 = new H5 { FontWeight = 600 },
            H6 = new H6 { FontWeight = 600 },
            Button = new MudBlazor.Button
            {
                FontFamily = new[] { "Inter", "sans-serif" },
                FontWeight = 500,
                TextTransform = "none",
                FontSize = "0.9rem",
            },
            Caption = new Caption { FontSize = "0.75rem" }
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "56px",
            DrawerWidthLeft = "260px",
        }
    };
}
```

---

## 9. Components/Layout/MainLayout.razor

This is the main shell. Key requirements:
- FipNavBar (waffle nav) at top — `ActiveModule` should be a NEW enum value for FAIT v2; for now use `FipModule.FAIT` as the closest match until the enum is updated
- Left drawer with sidebar nav
- Dark/light theme toggle stored in user preference (use localStorage via JS interop for now — simple toggle in header)
- Auth guard: if user is NOT provisioned (stub check for now — always true), redirect to `/onboarding`; otherwise show main layout

```razor
@using Microsoft.AspNetCore.Components.Authorization
@using FortressAI.V2.Web.Theme
@using FipShared.Components
@using FipShared.Models
@inherits LayoutComponentBase
@inject NavigationManager Nav
@inject IJSRuntime JS
@implements IDisposable

<MudThemeProvider Theme="_theme" @bind-IsDarkMode="_isDarkMode" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <FipNavBar ActiveModule="FipModule.FAIT"
               ModuleDisplayName="FAIT v2"
               UserInitial="@_userInitial"
               UserName="@_userName"
               UserEmail="@_userEmail"
               OnMenuClick="ToggleDrawer"
               OnSignOut="Logout"
               IsDev="@_isDev" />

    <MudDrawer @bind-Open="_drawerOpen"
               Variant="DrawerVariant.Responsive"
               Breakpoint="Breakpoint.Md"
               ClipMode="DrawerClipMode.Always"
               Elevation="2"
               Class="fait-v2-drawer">

        <div class="fait-v2-drawer__content">
            <MudDrawerHeader Class="fait-v2-drawer__header">
                <div class="fait-v2-drawer__brand">
                    <MudText Typo="Typo.h6" Class="fait-v2-drawer__title">FAIT v2</MudText>
                    <MudText Typo="Typo.caption" Class="fait-v2-drawer__subtitle">Fortress AI</MudText>
                </div>
            </MudDrawerHeader>

            <div class="fait-v2-drawer__nav">
                <MudNavMenu>
                    <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudNavLink>
                    <MudNavLink Href="/memory" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Memory">Memory</MudNavLink>
                    <MudNavLink Href="/tasks" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Schedule">Scheduled Tasks</MudNavLink>
                    <MudNavLink Href="/workspace" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.FolderOpen">Workspace</MudNavLink>
                    <MudNavLink Href="/connectors" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Cable">Connectors</MudNavLink>
                </MudNavMenu>
            </div>

            <div class="fait-v2-drawer__footer">
                <MudIconButton Icon="@(_isDarkMode ? Icons.Material.Filled.LightMode : Icons.Material.Filled.DarkMode)"
                               Color="Color.Inherit"
                               OnClick="ToggleDarkMode"
                               Size="Size.Small"
                               aria-label="Toggle dark mode"
                               Class="fait-v2-drawer__theme-toggle" />
                <MudText Typo="Typo.caption" Class="fait-v2-drawer__footer-text">Fortress Intelligence Platform</MudText>
            </div>
        </div>
    </MudDrawer>

    <MudMainContent Class="fait-v2-main">
        @Body
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawerOpen = true;
    private bool _isDarkMode = false;
    private bool _isDev = false;
    private string _userInitial = "F";
    private string _userName = "User";
    private string _userEmail = "";

    private MudTheme _theme = FipTheme.Create();

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        if (AuthenticationState != null)
        {
            var authState = await AuthenticationState;
            var user = authState.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                _userEmail = user.FindFirst("preferred_username")?.Value
                          ?? user.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                          ?? "";
                _userName = user.FindFirst("name")?.Value
                         ?? user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                         ?? _userEmail.Split('@')[0];
                _userInitial = _userName.Length > 0 ? _userName[0].ToString().ToUpperInvariant() : "F";
            }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                var saved = await JS.InvokeAsync<string?>("localStorage.getItem", "fait-v2-dark-mode");
                _isDarkMode = saved == "true";
                StateHasChanged();
            }
            catch { /* localStorage not available in prerender */ }
        }
    }

    private async Task ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        await JS.InvokeVoidAsync("localStorage.setItem", "fait-v2-dark-mode", _isDarkMode.ToString().ToLower());
    }

    private void ToggleDrawer()
    {
        _drawerOpen = !_drawerOpen;
    }

    private void Logout()
    {
        Nav.NavigateTo("MicrosoftIdentity/Account/SignOut", forceLoad: true);
    }

    public void Dispose() { }
}
```

---

## 10. Route Pages (Stubs)

### Components/Pages/Dashboard.razor
```razor
@page "/"
@attribute [Authorize]

<PageTitle>Dashboard — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="fait-v2-page">
    <MudText Typo="Typo.h5" Class="fait-v2-page__title">Dashboard</MudText>
    <MudText Typo="Typo.body2" Class="fait-v2-page__subtitle">Your AI assistant is ready.</MudText>
    
    <MudPaper Class="fait-v2-chat-placeholder" Elevation="0">
        <MudStack AlignItems="AlignItems.Center" Justify="Justify.Center" Class="fait-v2-chat-placeholder__inner">
            <MudIcon Icon="@Icons.Material.Filled.AutoAwesome" Class="fait-v2-chat-placeholder__icon" />
            <MudText Typo="Typo.h6" Class="fait-v2-chat-placeholder__title">Main Assistant Chat</MudText>
            <MudText Typo="Typo.body2" Class="fait-v2-chat-placeholder__text">Persistent AI assistant — coming in Phase B</MudText>
        </MudStack>
    </MudPaper>
</MudContainer>
```

### Components/Pages/Onboarding.razor
```razor
@page "/onboarding"

<PageTitle>Onboarding — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Small" Class="fait-v2-page fait-v2-onboarding">
    <MudText Typo="Typo.h5" Class="fait-v2-page__title">Welcome to FAIT v2</MudText>
    <MudText Typo="Typo.body2" Class="fait-v2-page__subtitle">Let's set up your AI assistant.</MudText>
    
    <MudPaper Class="fait-v2-onboarding__stepper" Elevation="1">
        <MudStepper>
            <MudStep Title="Role & Responsibilities">
                <MudText Typo="Typo.body1">Step 1: Tell us about your role.</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary">— Wizard placeholder, Phase A2 —</MudText>
            </MudStep>
            <MudStep Title="Preferences">
                <MudText Typo="Typo.body1">Step 2: Set your preferences.</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary">— Wizard placeholder, Phase A2 —</MudText>
            </MudStep>
            <MudStep Title="Use Cases">
                <MudText Typo="Typo.body1">Step 3: Choose your use cases.</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary">— Wizard placeholder, Phase A2 —</MudText>
            </MudStep>
            <MudStep Title="Personalization">
                <MudText Typo="Typo.body1">Step 4: Personalize your assistant.</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary">— Wizard placeholder, Phase A2 —</MudText>
            </MudStep>
        </MudStepper>
    </MudPaper>
</MudContainer>
```

### Components/Pages/Memory.razor
```razor
@page "/memory"
@attribute [Authorize]

<PageTitle>Memory — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="fait-v2-page">
    <MudText Typo="Typo.h5" Class="fait-v2-page__title">Memory Management</MudText>
    <MudText Typo="Typo.body2" Class="fait-v2-page__subtitle">View and manage your AI assistant's memory.</MudText>
    
    <MudPaper Class="fait-v2-placeholder-card" Elevation="0">
        <MudText Typo="Typo.body2" Color="Color.Secondary">Memory Management UI — coming in Phase B2</MudText>
    </MudPaper>
</MudContainer>
```

### Components/Pages/Tasks.razor
```razor
@page "/tasks"
@attribute [Authorize]

<PageTitle>Scheduled Tasks — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="fait-v2-page">
    <MudText Typo="Typo.h5" Class="fait-v2-page__title">Scheduled Tasks</MudText>
    <MudText Typo="Typo.body2" Class="fait-v2-page__subtitle">Automate recurring work with your AI assistant.</MudText>
    
    <MudPaper Class="fait-v2-placeholder-card" Elevation="0">
        <MudText Typo="Typo.body2" Color="Color.Secondary">Scheduled Tasks UI — coming in Phase F1</MudText>
    </MudPaper>
</MudContainer>
```

### Components/Pages/Workspace.razor
```razor
@page "/workspace"
@attribute [Authorize]

<PageTitle>Workspace — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="fait-v2-page">
    <MudText Typo="Typo.h5" Class="fait-v2-page__title">Workspace Explorer</MudText>
    <MudText Typo="Typo.body2" Class="fait-v2-page__subtitle">Browse your files, artifacts, and projects.</MudText>
    
    <MudPaper Class="fait-v2-placeholder-card" Elevation="0">
        <MudText Typo="Typo.body2" Color="Color.Secondary">Workspace Explorer — coming in Phase D2</MudText>
    </MudPaper>
</MudContainer>
```

### Components/Pages/Connectors.razor
```razor
@page "/connectors"
@attribute [Authorize]

<PageTitle>Connectors — FAIT v2</PageTitle>

<MudContainer MaxWidth="MaxWidth.Large" Class="fait-v2-page">
    <MudText Typo="Typo.h5" Class="fait-v2-page__title">Connector Management</MudText>
    <MudText Typo="Typo.body2" Class="fait-v2-page__subtitle">Manage your MCP connectors and integrations.</MudText>
    
    <MudPaper Class="fait-v2-placeholder-card" Elevation="0">
        <MudText Typo="Typo.body2" Color="Color.Secondary">Connector Management UI — coming in Phase C2</MudText>
    </MudPaper>
</MudContainer>
```

---

## 11. wwwroot/css/app.css

All CSS must use class-based selectors only. No inline styles. Cover:
- CSS custom properties (vars) for the Fortress brand palette
- Drawer layout classes
- Page layout classes
- Placeholder card styling
- Responsive nav bar
- Dark mode support via `.mud-dark` parent selector

```css
/* FAIT v2 — app.css
   All styling is class-driven. No inline styles.
   CSS custom properties for Fortress brand palette.
*/

:root {
    --fait-v2-primary: #0066CC;
    --fait-v2-dark: #1A1A2E;
    --fait-v2-gold: #d4af37;
    --fait-v2-bg: #f8f9fa;
    --fait-v2-surface: #ffffff;
    --fait-v2-text: #1A1A2E;
    --fait-v2-text-secondary: #6b7280;
    --fait-v2-drawer-bg: #1A1A2E;
    --fait-v2-drawer-text: #f0f0f0;
    --appbar-height: 56px;
}

/* Dark mode overrides */
.mud-dark {
    --fait-v2-bg: #0d0d1a;
    --fait-v2-surface: #1A1A2E;
    --fait-v2-text: #f0f0f0;
    --fait-v2-text-secondary: #9ca3af;
    --fait-v2-drawer-bg: #0d0d1a;
}

/* Main content area */
.fait-v2-main {
    padding-top: calc(var(--appbar-height) + 16px) !important;
    padding-left: 16px;
    padding-right: 16px;
    min-height: 100vh;
    background-color: var(--fait-v2-bg);
}

/* Drawer */
.fait-v2-drawer .mud-drawer {
    background-color: var(--fait-v2-drawer-bg) !important;
}

.fait-v2-drawer__content {
    display: flex;
    flex-direction: column;
    height: 100%;
    background-color: var(--fait-v2-drawer-bg);
}

.fait-v2-drawer__header {
    padding: 16px;
    border-bottom: 1px solid rgba(255,255,255,0.1);
}

.fait-v2-drawer__brand {
    display: flex;
    flex-direction: column;
}

.fait-v2-drawer__title {
    color: var(--fait-v2-gold) !important;
    font-weight: 700;
}

.fait-v2-drawer__subtitle {
    color: rgba(240,240,240,0.6) !important;
    font-size: 0.7rem;
}

.fait-v2-drawer__nav {
    flex: 1;
    overflow-y: auto;
    padding: 8px 0;
}

.fait-v2-drawer__footer {
    padding: 12px 16px;
    border-top: 1px solid rgba(255,255,255,0.1);
    display: flex;
    align-items: center;
    gap: 8px;
}

.fait-v2-drawer__footer-text {
    color: rgba(240,240,240,0.5) !important;
    font-size: 0.7rem;
}

.fait-v2-drawer__theme-toggle {
    color: rgba(240,240,240,0.7) !important;
}

/* Pages */
.fait-v2-page {
    padding: 24px 0;
}

.fait-v2-page__title {
    color: var(--fait-v2-text);
    font-weight: 700;
    margin-bottom: 4px;
}

.fait-v2-page__subtitle {
    color: var(--fait-v2-text-secondary);
    margin-bottom: 24px;
}

/* Chat placeholder */
.fait-v2-chat-placeholder {
    background: var(--fait-v2-surface);
    border: 1px dashed rgba(0, 102, 204, 0.3);
    border-radius: 12px;
    min-height: 400px;
}

.fait-v2-chat-placeholder__inner {
    min-height: 400px;
    padding: 32px;
}

.fait-v2-chat-placeholder__icon {
    font-size: 48px !important;
    color: var(--fait-v2-primary);
    opacity: 0.4;
    margin-bottom: 16px;
}

.fait-v2-chat-placeholder__title {
    color: var(--fait-v2-text);
    opacity: 0.7;
    margin-bottom: 8px;
}

.fait-v2-chat-placeholder__text {
    color: var(--fait-v2-text-secondary);
}

/* Generic placeholder card */
.fait-v2-placeholder-card {
    background: var(--fait-v2-surface);
    border: 1px dashed rgba(0,0,0,0.15);
    border-radius: 8px;
    padding: 48px;
    text-align: center;
    margin-top: 16px;
}

.mud-dark .fait-v2-placeholder-card {
    border-color: rgba(255,255,255,0.15);
}

/* Onboarding */
.fait-v2-onboarding {
    margin-top: 40px;
}

.fait-v2-onboarding__stepper {
    padding: 24px;
    margin-top: 24px;
}

/* Responsive */
@media (max-width: 768px) {
    .fait-v2-main {
        padding-left: 8px;
        padding-right: 8px;
    }
    
    .fait-v2-chat-placeholder {
        min-height: 300px;
    }
    
    .fait-v2-chat-placeholder__inner {
        min-height: 300px;
    }
}
```

---

## 12. Dockerfile.debian

**IMPORTANT:** Create this file at `~/projects/fip/fait-v2/Dockerfile.debian` (NOT `Dockerfile`).
MCR Alpine base is blocked on WSL2 — bookworm-slim base is mandatory.

```dockerfile
# FAIT v2 — Dockerfile.debian
# MCR blocked on WSL2 with Alpine — use this file (debian bookworm base), NOT Dockerfile
# Build from monorepo root: docker build -f fait-v2/Dockerfile.debian -t fait-v2 .

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

# Copy shared library first (for layer caching)
COPY shared/FipShared/ shared/FipShared/

# Copy fait-v2 app
COPY fait-v2/src/FortressAI.V2.Web/ fait-v2/src/FortressAI.V2.Web/

# Restore and build
WORKDIR /src/fait-v2/src/FortressAI.V2.Web
RUN dotnet restore FortressAI.V2.Web.csproj
RUN dotnet publish FortressAI.V2.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FortressAI.V2.Web.dll"]
```

---

## 13. Properties/launchSettings.json

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    },
    "https": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "applicationUrl": "https://localhost:7200;http://localhost:5200",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

---

## 14. Build Verification

After creating all files, run:

```bash
cd ~/projects/fip/fait-v2/src/FortressAI.V2.Web
dotnet build FortressAI.V2.Web.csproj
```

The build MUST succeed with zero errors. Warnings are acceptable.

If `dotnet restore` fails due to NuGet feed issues, try:
```bash
dotnet restore --ignore-failed-sources
```

---

## Important Notes for Build Report

1. **ClientId is a placeholder** — `PLACEHOLDER_NEEDS_REAL_ENTRA_APP_REGISTRATION`. Fred must register a new Entra app for FAIT v2 in tenant `7152ea12-c930-44b0-bb52-069152161c5b` and replace this value before the app will authenticate users. Redirect URIs to register:
   - `https://localhost:7200/signin-oidc` (dev)
   - `https://fait-v2.dev.fortressam.ai/signin-oidc` (staging, when ECS is up)

2. **FipModule enum** — currently `FipShared.Models.FipModule` does not have a `FAITv2` value. The layout uses `FipModule.FAIT` with `ModuleDisplayName="FAIT v2"` as a workaround. A follow-up WI should add `FAITv2` to the enum and update all apps.

3. **MudStepper** — MudBlazor v7 stepper API may differ from v6. If `MudStepper`/`MudStep` don't compile, fall back to a simple `MudPaper` with numbered steps as a div list. The onboarding page is a stub placeholder.

4. **GuidFormat=None** — included in the connection string stub. Must be on all future `MySqlConnectionStringBuilder` usages when DB is wired.

5. **Microsoft.Identity.Web version** — use `3.*` which supports .NET 8. If there are any compatibility issues, pin to `3.3.4`.

---

## Output Instructions

After building:
1. Save the build output to `~/projects/fip/fait-v2/pipeline/ADO2842-BUILD-RESULT.txt`
2. Run: `cd ~/projects/fip && git add fait-v2/ && git status`
3. Run: `cd ~/projects/fip && git commit -m "feat(fait-v2): ADO#2842 — Blazor Server app shell, Entra SSO, FIP waffle nav, route stubs, Dockerfile.debian"`
4. Output the commit hash
