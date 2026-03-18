# CC Brief: WI832 — FAIT Cowork Sprint 1

## Context

You are building a brand-new service called **FAIT Cowork** in the FIP monorepo at `/home/fredw/projects/fip/`.

This creates:
- **21 new files** in `fip/cowork/`
- **1 modified file** in `fip/shared/FipShared/Models/FipModule.cs`

Two containers:
- **CoworkWeb** — .NET 9 Blazor Server app (port 8080), FIP cookie auth, FipNavBar, task UI
- **CoworkAgent** — Node.js TypeScript Express API (port 3000), Agent SDK, SSE streaming, CloudWatch audit

Working directory for all operations: `/home/fredw/projects/fip/`

---

## ⚠️ CRITICAL CONSTRAINTS — DO NOT MISS THESE

### 1. DataProtection — BOTH lines MANDATORY in Program.cs
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")          // EXACT STRING — matches FAIT/FIRM/FORMS
    .DisableAutomaticKeyGeneration();           // FIP portal owns key creation
```
Missing either line = shared .FortressAI.Session cookie broken for ALL FIP apps. This is non-negotiable.

### 2. iframe sandbox — allow-scripts ONLY
In `TaskPage.razor`:
```razor
<iframe srcdoc="@_outputHtmlDecoded"
        sandbox="allow-scripts"
        ...
```
NOT `allow-same-origin allow-scripts`. ONLY `allow-scripts`. Adding `allow-same-origin` with same-origin iframe bypasses the entire sandbox — security issue.

### 3. COWORK_INTERNAL_SECRET — from env var, NEVER hardcoded
- `InternalTokenService.cs`: read from `config["CoworkAgent:InternalSecret"]` — throw if missing
- `auth.ts` (Node.js): read from `process.env.COWORK_INTERNAL_SECRET` — throw if missing

### 4. Bash env in runner.ts — NO secrets in the env object
The `env` object passed to `query()` options must ONLY contain:
- `COWORK_TASK_ID`, `COWORK_USER_ID`, `COWORK_USER_EMAIL`
NEVER: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `COWORK_INTERNAL_SECRET`, `FORGE_API_KEY`
The Agent SDK may expose env to Claude via Bash tool — secrets must never be in there.

### 5. Use blazor.server.js — NOT blazor.web.js
Cowork is pure Blazor Server. FORMS uses `blazor.web.js` (static SSR). Cowork does NOT.

### 6. Do NOT touch any existing fip/ files except FipModule.cs
The only existing file you modify is `fip/shared/FipShared/Models/FipModule.cs`.

---

## Task 1: Modify fip/shared/FipShared/Models/FipModule.cs

Add `Cowork` enum entry and three switch cases. Current file:
```csharp
namespace FipShared.Models;

public enum FipModule
{
    FAIT,
    FIRM,
    FORMS
}

public static class FipModuleExtensions
{
    public static string FullName(this FipModule module) => module switch
    {
        FipModule.FAIT  => "Fortress AI Tools",
        FipModule.FIRM  => "Fortress Intelligence & Risk Management",
        FipModule.FORMS => "Fortress Form Tools",
        _               => module.ToString()
    };

    public static string ShortName(this FipModule module) => module switch
    {
        FipModule.FAIT  => "FAIT",
        FipModule.FIRM  => "FIRM",
        FipModule.FORMS => "FORMS",
        _               => module.ToString()
    };

    public static string Url(this FipModule module) => module switch
    {
        FipModule.FAIT  => "https://fait.fortressintelligence.com",
        FipModule.FIRM  => "https://firm.fortressintelligence.com",
        FipModule.FORMS => "https://forms.fortressintelligence.com",
        _               => "#"
    };
}
```

Replace with:
```csharp
namespace FipShared.Models;

public enum FipModule
{
    FAIT,
    FIRM,
    FORMS,
    Cowork
}

public static class FipModuleExtensions
{
    public static string FullName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "Fortress AI Tools",
        FipModule.FIRM   => "Fortress Intelligence & Risk Management",
        FipModule.FORMS  => "Fortress Form Tools",
        FipModule.Cowork => "FAIT Cowork",
        _                => module.ToString()
    };

    public static string ShortName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "FAIT",
        FipModule.FIRM   => "FIRM",
        FipModule.FORMS  => "FORMS",
        FipModule.Cowork => "Cowork",
        _                => module.ToString()
    };

    public static string Url(this FipModule module) => module switch
    {
        FipModule.FAIT   => "https://fait.fortressintelligence.com",
        FipModule.FIRM   => "https://firm.fortressintelligence.com",
        FipModule.FORMS  => "https://forms.fortressintelligence.com",
        FipModule.Cowork => "https://cowork.fortressintelligence.com",
        _                => "#"
    };
}
```

---

## Task 2: Create fip/cowork/src/CoworkWeb/CoworkWeb.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>CoworkWeb</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MudBlazor" Version="7.*" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" Version="*" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Npgsql" Version="*" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="*" />
    <ProjectReference Include="..\..\..\shared\FipShared\FipShared.csproj" />
  </ItemGroup>
</Project>
```

Note: The ProjectReference `..\..\..\shared\FipShared\FipShared.csproj` is relative to `fip/cowork/src/CoworkWeb/`. This matches the exact pattern FAIT uses (verified: FAIT's csproj at `fip/fait/src/FortressAI.Web/` uses `..\..\..\shared\FipShared\FipShared.csproj` — same depth).

---

## Task 3: Create fip/cowork/src/CoworkWeb/Program.cs

```csharp
using CoworkWeb.Services;
using CoworkWeb.Data;
using FipShared.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── MudBlazor ────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();

// ── Blazor Server ────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Scoped services ──────────────────────────────────────────────────────────
builder.Services.AddScoped<CoworkSessionService>();
builder.Services.AddScoped<AgentApiClient>();
builder.Services.AddSingleton<InternalTokenService>();

// ── HTTP client for CoworkAgent API ──────────────────────────────────────────
builder.Services.AddHttpClient("cowork-agent", client =>
{
    var agentUrl = builder.Configuration["CoworkAgent:BaseUrl"] ?? "http://cowork-agent:3000";
    client.BaseAddress = new Uri(agentUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// ── Authentication: pure cookie consumer — FIP portal owns Entra OIDC ────────
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

// ── Data protection: shared FIP key ring ─────────────────────────────────────
// CRITICAL: Must use SetApplicationName("FortressAI") — exact string — and DisableAutomaticKeyGeneration.
// Missing either line breaks shared .FortressAI.Session cookie for ALL FIP apps.
var keyRingConnStr = builder.Configuration.GetConnectionString("KeyRingDb")
    ?? throw new InvalidOperationException("ConnectionStrings:KeyRingDb is required");

builder.Services.AddDbContext<SharedKeyRingDbContext>(opt =>
    opt.UseNpgsql(keyRingConnStr));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Auth routes ───────────────────────────────────────────────────────────────
app.MapGet("/auth/redirect-to-login", (HttpContext ctx, IConfiguration config) =>
{
    var fipLoginUrl = config["FIP:LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var coworkCallbackUrl = config["FIP:CoworkCallbackUrl"]?.TrimEnd('/') ?? "https://cowork.dev.fortressam.ai/auth/cowork-session";
    var redirectUrl = $"{fipLoginUrl}/auth/firm-callback?returnUrl={Uri.EscapeDataString(coworkCallbackUrl)}";
    return Results.Redirect(redirectUrl);
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/cowork-session", async (HttpContext ctx) =>
{
    var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!authResult.Succeeded) return Results.Redirect("/");
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var fipLoginUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:LoginUrl"]
        ?? "https://fip.dev.fortressam.ai";
    return Results.Redirect(fipLoginUrl);
}).DisableAntiforgery();

// ── Blazor ────────────────────────────────────────────────────────────────────
app.MapRazorComponents<CoworkWeb.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

---

## Task 4: Create Three Services

### fip/cowork/src/CoworkWeb/Services/CoworkSessionService.cs

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CoworkWeb.Services;

/// <summary>
/// Scoped service that holds the current user's FIP identity for Blazor Server circuits.
/// Populated in MainLayout.razor during OnInitializedAsync — same pattern as FAIT's UserSessionService.
/// </summary>
public sealed class CoworkSessionService
{
    public string UserId   { get; private set; } = string.Empty;
    public string Email    { get; private set; } = string.Empty;
    public string Name     { get; private set; } = string.Empty;
    public bool   IsLoaded { get; private set; }

    public event Action? OnSessionChanged;

    public void SetFromClaims(ClaimsPrincipal user)
    {
        UserId  = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.FindFirst("oid")?.Value
               ?? string.Empty;
        Email   = user.FindFirst(ClaimTypes.Email)?.Value
               ?? user.FindFirst("preferred_username")?.Value
               ?? string.Empty;
        Name    = user.FindFirst(ClaimTypes.Name)?.Value
               ?? user.FindFirst("name")?.Value
               ?? Email;
        IsLoaded = true;
        OnSessionChanged?.Invoke();
    }

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}
```

### fip/cowork/src/CoworkWeb/Services/InternalTokenService.cs

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoworkWeb.Services;

/// <summary>
/// Issues short-lived (5-minute) signed JWTs for Blazor-to-Node.js API calls.
/// The Node.js CoworkAgent validates these with the same shared secret.
/// </summary>
public sealed class InternalTokenService
{
    private readonly string _secret;
    private readonly SigningCredentials _signingCredentials;

    public InternalTokenService(IConfiguration config)
    {
        _secret = config["CoworkAgent:InternalSecret"]
            ?? throw new InvalidOperationException("CoworkAgent:InternalSecret not configured");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string Issue(string userId, string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer:   "cowork-web",
            audience: "cowork-agent",
            claims:   claims,
            notBefore: DateTime.UtcNow,
            expires:   DateTime.UtcNow.AddMinutes(5),
            signingCredentials: _signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### fip/cowork/src/CoworkWeb/Services/AgentApiClient.cs

```csharp
using System.Net.Http.Headers;

namespace CoworkWeb.Services;

/// <summary>
/// Proxies requests from Blazor to the CoworkAgent Node.js API.
/// Injects a short-lived internal JWT per request to carry user identity.
/// </summary>
public sealed class AgentApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly InternalTokenService _tokens;
    private readonly CoworkSessionService _session;

    public AgentApiClient(
        IHttpClientFactory httpClientFactory,
        InternalTokenService tokens,
        CoworkSessionService session)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _session = session;
    }

    public async Task<string> StartTaskAsync(string prompt, IEnumerable<(string Name, Stream Data, string ContentType)> files, CancellationToken ct = default)
    {
        var client = CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(prompt), "prompt");

        foreach (var (name, data, contentType) in files)
        {
            var fileContent = new StreamContent(data);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "files", name);
        }

        var resp = await client.PostAsync("/tasks", form, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<StartTaskResponse>(ct: ct);
        return body?.TaskId ?? throw new InvalidOperationException("Agent API did not return taskId");
    }

    public async Task<Stream> OpenStreamAsync(string taskId, CancellationToken ct = default)
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/tasks/{taskId}/stream");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("cowork-agent");
        var token = _tokens.Issue(_session.UserId, _session.Email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private record StartTaskResponse(string TaskId);
}
```

---

## Task 5: Create Layout Components

### fip/cowork/src/CoworkWeb/Components/App.razor

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>FAIT Cowork</title>
    <link rel="stylesheet" href="css/cowork.css" />
    <link href="_content/FipShared/css/fip-tokens.css" rel="stylesheet" />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&display=swap" rel="stylesheet" />
    <HeadOutlet @rendermode="RenderMode.InteractiveServer" />
</head>
<body>
    <Routes @rendermode="RenderMode.InteractiveServer" />
    <script src="_framework/blazor.server.js"></script>
</body>
</html>
```

### fip/cowork/src/CoworkWeb/Components/Layout/MainLayout.razor

```razor
@inherits LayoutComponentBase
@inject CoworkSessionService Session
@inject AuthenticationStateProvider AuthStateProvider
@inject NavigationManager Nav

@if (!Session.IsLoaded)
{
    <div style="display:flex;align-items:center;justify-content:center;height:100vh;font-family:var(--font-primary);color:var(--color-text-secondary);">
        Loading…
    </div>
}
else
{
    <FipNavBar ActiveModule="FipModule.Cowork"
               UserInitial="@Session.Initial"
               UserName="@Session.Name"
               UserEmail="@Session.Email"
               OnSignOut="@HandleSignOut" />

    <main style="padding: 0; min-height: calc(100vh - 48px);">
        @Body
    </main>
}

@code {
    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        Session.SetFromClaims(authState.User);
    }

    private void HandleSignOut() => Nav.NavigateTo("/auth/logout", forceLoad: true);
}
```

### fip/cowork/src/CoworkWeb/Components/Layout/MainLayout.razor.css

```css
/* MainLayout scoped styles — minimal, layout handled by global fip-tokens */
:deep(.mud-main-content) {
    padding: 0;
}
```

---

## Task 6: Create Pages

### fip/cowork/src/CoworkWeb/Components/Pages/Index.razor

```razor
@page "/"
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        Nav.NavigateTo("/tasks/new", replace: true);
    }
}
```

### fip/cowork/src/CoworkWeb/Components/Pages/NewTask.razor

```razor
@page "/tasks/new"
@inject AgentApiClient AgentApi
@inject NavigationManager Nav
@inject CoworkSessionService Session

<PageTitle>New Task — FAIT Cowork</PageTitle>

<div style="max-width: 720px; margin: 0 auto; padding: 40px 16px;">

    <h1 style="font-size: var(--text-2xl); font-weight: var(--font-semibold); color: var(--color-text-primary); margin-bottom: 8px;">
        New Task
    </h1>
    <p style="color: var(--color-text-secondary); margin-bottom: 32px; font-size: var(--text-base);">
        Describe what you need. Claude will plan and complete the task step by step.
    </p>

    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 24px;">
        @foreach (var hint in _hints)
        {
            <div style="padding: 12px 14px; border: 1px solid var(--color-border); border-radius: var(--radius-md); background: var(--color-surface);">
                <div style="font-weight: var(--font-medium); font-size: var(--text-sm); color: var(--color-text-primary);">@hint.Title</div>
                <div style="font-size: var(--text-xs); color: var(--color-text-muted); margin-top: 2px;">@hint.Example</div>
            </div>
        }
    </div>

    <MudTextField @bind-Value="_prompt"
                  Label="Describe your task"
                  Placeholder="e.g. Create an HTML prototype of a fund performance dashboard with Q1 metrics and a chart placeholder."
                  Lines="5"
                  Variant="Variant.Outlined"
                  Class="mb-3"
                  FullWidth="true" />

    <div style="margin-bottom: 24px;">
        <InputFile id="file-upload" multiple OnChange="HandleFilesSelected" style="display:none;" />
        <label for="file-upload" style="display:block; padding: 16px; border: 2px dashed var(--color-border); border-radius: var(--radius-md); text-align: center; cursor: pointer; font-size: var(--text-sm); color: var(--color-text-muted);">
            @if (_files.Count == 0)
            {
                <span>Drop files here or click to upload</span>
                <br />
                <span style="font-size: var(--text-xs);">PDF, .docx, .xlsx, .txt, .png, .jpg — max 10MB each, up to 5 files</span>
            }
            else
            {
                @string.Join(", ", _files.Select(f => f.Name))
            }
        </label>
    </div>

    <MudButton Variant="Variant.Filled"
               Color="Color.Warning"
               Disabled="@(string.IsNullOrWhiteSpace(_prompt) || _submitting)"
               OnClick="HandleSubmit"
               FullWidth="false"
               Style="background: var(--color-btn-gold-bg); color: var(--color-btn-gold-text); font-weight: var(--font-semibold); padding: 10px 28px;">
        @(_submitting ? "Starting…" : "Start Task →")
    </MudButton>

    @if (_error is not null)
    {
        <div style="margin-top: 12px; color: var(--color-error); font-size: var(--text-sm);">@_error</div>
    }
</div>

@code {
    private string _prompt = string.Empty;
    private List<IBrowserFile> _files = new();
    private bool _submitting;
    private string? _error;

    private static readonly (string Title, string Example)[] _hints =
    [
        ("HTML Prototype",      "\"Build a fund dashboard page with charts\""),
        ("Document from Notes", "\"Turn my scattered bullet points into a report\""),
        ("Summarize Files",     "\"Summarize this PDF and pull key numbers\""),
        ("Data Analysis",       "\"Analyze this spreadsheet for trends\""),
    ];

    private void HandleFilesSelected(InputFileChangeEventArgs e)
    {
        _files = e.GetMultipleFiles(5).ToList();
    }

    private async Task HandleSubmit()
    {
        if (string.IsNullOrWhiteSpace(_prompt) || _submitting) return;
        _submitting = true;
        _error = null;

        try
        {
            var fileStreams = _files.Select(f => (
                Name: f.Name,
                Data: f.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024),
                ContentType: f.ContentType
            ));

            var taskId = await AgentApi.StartTaskAsync(_prompt, fileStreams);
            Nav.NavigateTo($"/tasks/{taskId}");
        }
        catch (Exception ex)
        {
            _error = $"Failed to start task: {ex.Message}";
            _submitting = false;
        }
    }
}
```

### fip/cowork/src/CoworkWeb/Components/Pages/TaskPage.razor

CRITICAL: sandbox="allow-scripts" ONLY — NOT "allow-scripts allow-same-origin"

```razor
@page "/tasks/{TaskId}"
@inject AgentApiClient AgentApi
@inject CoworkSessionService Session
@implements IAsyncDisposable

<PageTitle>Task — FAIT Cowork</PageTitle>

<div style="max-width: 800px; margin: 0 auto; padding: 32px 16px; font-family: var(--font-primary);">

    <div style="display:flex; align-items:center; gap:10px; margin-bottom:24px;">
        <div style="width:10px;height:10px;border-radius:50%;background:@(_done ? "var(--color-success)" : "var(--color-gold)");"></div>
        <span style="font-size:var(--text-sm);color:var(--color-text-secondary);">
            @(_done ? "Completed" : "In progress…")
        </span>
    </div>

    <div style="background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-lg);padding:20px;margin-bottom:24px;">
        <h2 style="font-size:var(--text-lg);font-weight:var(--font-semibold);color:var(--color-text-primary);margin:0 0 16px 0;">
            Task Progress
        </h2>
        @if (_steps.Count == 0 && !_done)
        {
            <div style="color:var(--color-text-muted);font-size:var(--text-sm);">Starting…</div>
        }
        <div style="display:flex;flex-direction:column;gap:8px;">
            @{int stepNum = 1;}
            @foreach (var step in _steps)
            {
                <div style="display:flex;gap:10px;align-items:flex-start;">
                    <span style="flex-shrink:0;width:20px;height:20px;border-radius:50%;background:var(--color-primary);color:#fff;font-size:11px;font-weight:var(--font-bold);display:flex;align-items:center;justify-content:center;">
                        @(stepNum++)
                    </span>
                    <span style="font-size:var(--text-sm);color:var(--color-text-primary);line-height:1.5;">@step</span>
                </div>
            }
        </div>
        @if (!_done)
        {
            <div style="margin-top:12px;color:var(--color-text-muted);font-size:var(--text-sm);">
                <span style="animation:pulse 1.5s infinite;">●</span> Claude is working…
            </div>
        }
    </div>

    @if (_outputText is not null || _outputHtmlBase64 is not null)
    {
        <div style="background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-lg);padding:20px;">
            <h2 style="font-size:var(--text-lg);font-weight:var(--font-semibold);color:var(--color-text-primary);margin:0 0 16px 0;">
                Output
            </h2>

            @if (_outputHtmlBase64 is not null)
            {
                <div style="margin-bottom:16px;">
                    <div style="font-size:var(--text-sm);font-weight:var(--font-medium);color:var(--color-text-secondary);margin-bottom:6px;">HTML Preview</div>
                    <iframe srcdoc="@_outputHtmlDecoded"
                            sandbox="allow-scripts"
                            style="width:100%;height:400px;border:1px solid var(--color-border);border-radius:var(--radius-md);"
                            title="Task output preview">
                    </iframe>
                </div>
            }

            @if (_outputText is not null)
            {
                <div style="background:var(--color-bg-page);border:1px solid var(--color-border);border-radius:var(--radius-md);padding:16px;margin-bottom:16px;">
                    <pre style="white-space:pre-wrap;font-family:var(--font-primary);font-size:var(--text-sm);color:var(--color-text-primary);margin:0;line-height:1.6;">@_outputText</pre>
                </div>
            }

            @foreach (var file in _outputFiles)
            {
                <div style="margin-bottom:8px;">
                    <a href="@file.Url" download="@file.Name"
                       style="color:var(--color-text-link);font-size:var(--text-sm);text-decoration:underline;">
                        ⬇ @file.Name
                    </a>
                </div>
            }
        </div>
    }

    @if (_error is not null)
    {
        <div style="padding:12px 16px;background:var(--color-error-bg);border:1px solid var(--color-error);border-radius:var(--radius-md);color:var(--color-error);font-size:var(--text-sm);">
            ⚠ @_error
        </div>
    }
</div>

@code {
    [Parameter] public string TaskId { get; set; } = string.Empty;

    private List<string> _steps = new();
    private bool _done;
    private string? _outputText;
    private string? _outputHtmlBase64;
    private string? _outputHtmlDecoded => _outputHtmlBase64 is null ? null
        : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_outputHtmlBase64));
    private List<(string Name, string Url)> _outputFiles = new();
    private string? _error;
    private CancellationTokenSource _cts = new();

    protected override async Task OnInitializedAsync()
    {
        _ = ConsumeStreamAsync(_cts.Token);
    }

    private async Task ConsumeStreamAsync(CancellationToken ct)
    {
        try
        {
            using var stream = await AgentApi.OpenStreamAsync(TaskId, ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                if (!line.StartsWith("data: ")) continue;
                var json = line["data: ".Length..];

                var chunk = System.Text.Json.JsonSerializer.Deserialize<SseChunk>(json);
                if (chunk is null) continue;

                await InvokeAsync(() =>
                {
                    ProcessChunk(chunk);
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                _error = $"Stream error: {ex.Message}";
                _done = true;
                StateHasChanged();
            });
        }
    }

    private void ProcessChunk(SseChunk chunk)
    {
        switch (chunk.Type)
        {
            case "step":
                if (!string.IsNullOrWhiteSpace(chunk.Text))
                    _steps.Add(chunk.Text);
                break;

            case "result":
                _done = true;
                _outputText = chunk.Text;
                break;

            case "html_output":
                _done = true;
                _outputHtmlBase64 = chunk.Base64;
                break;

            case "file_output":
                if (chunk.FileName is not null && chunk.DownloadUrl is not null)
                    _outputFiles.Add((chunk.FileName, chunk.DownloadUrl));
                break;

            case "error":
                _done = true;
                _error = chunk.Text ?? "Task failed";
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }

    private record SseChunk(
        string Type,
        string? Text = null,
        string? Base64 = null,
        string? FileName = null,
        string? DownloadUrl = null
    );
}
```

---

## Task 7: CSS + DbContext

### fip/cowork/src/CoworkWeb/wwwroot/css/cowork.css

```css
/* FAIT Cowork — global styles */
/* Design tokens from FipShared RCL */
@import url('/_content/FipShared/css/fip-tokens.css');

*, *::before, *::after {
    box-sizing: border-box;
}

html, body {
    margin: 0;
    padding: 0;
    font-family: var(--font-primary, 'Inter', sans-serif);
    font-size: 16px;
    background: var(--color-bg-page, #f4f5f7);
    color: var(--color-text-primary, #1a1a2e);
}

@keyframes pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.3; }
}

/* MudBlazor overrides to fit FIP design tokens */
.mud-button-root {
    font-family: var(--font-primary) !important;
}
```

### fip/cowork/src/CoworkWeb/Data/SharedKeyRingDbContext.cs

```csharp
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoworkWeb.Data;

/// <summary>
/// Minimal DbContext for DataProtection key ring persistence.
/// Connects to the shared FIP key ring database — same DB used by FAIT, FIRM, FORMS.
/// CoworkWeb reads keys only (DisableAutomaticKeyGeneration — FIP portal owns key creation).
/// </summary>
public sealed class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options)
        : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
```

---

## Task 8: Node.js package.json + tsconfig.json

### fip/cowork/src/CoworkAgent/package.json

```json
{
  "name": "cowork-agent",
  "version": "1.0.0",
  "private": true,
  "scripts": {
    "dev": "tsx watch src/server.ts",
    "build": "tsc",
    "start": "node dist/server.js"
  },
  "dependencies": {
    "@anthropic-ai/claude-agent-sdk": "0.2.77",
    "express": "^4.18.0",
    "multer": "^1.4.5-lts.1",
    "jsonwebtoken": "^9.0.2",
    "@aws-sdk/client-cloudwatch-logs": "^3.750.0"
  },
  "devDependencies": {
    "@types/express": "^4.17.21",
    "@types/multer": "^1.4.12",
    "@types/jsonwebtoken": "^9.0.7",
    "@types/node": "^22.0.0",
    "tsx": "^4.19.2",
    "typescript": "^5.7.2"
  }
}
```

NOTE: Check npm for the actual latest @anthropic-ai/claude-agent-sdk version. At time of spec authoring, use the latest stable 0.x or 1.x. If the package doesn't exist on npm as "@anthropic-ai/claude-agent-sdk", use "@anthropic-ai/sdk" with agent patterns instead, and document the substitution.

### fip/cowork/src/CoworkAgent/tsconfig.json

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "outDir": "dist",
    "rootDir": "src",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "resolveJsonModule": true,
    "declaration": true,
    "sourceMap": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist"]
}
```

---

## Task 9: Server + Auth Middleware

### fip/cowork/src/CoworkAgent/src/server.ts

```typescript
import express from 'express';
import multer from 'multer';
import { tasksRouter } from './routes/tasks.js';
import { authMiddleware } from './middleware/auth.js';

const app = express();

app.use(express.json());
app.use(authMiddleware);
app.use('/tasks', tasksRouter);

const port = parseInt(process.env.PORT ?? '3000', 10);
app.listen(port, () => {
  console.log(`CoworkAgent listening on :${port}`);
});
```

### fip/cowork/src/CoworkAgent/src/middleware/auth.ts

CRITICAL: COWORK_INTERNAL_SECRET from env var only — throw if missing

```typescript
import { Request, Response, NextFunction } from 'express';
import jwt from 'jsonwebtoken';

const SECRET = process.env.COWORK_INTERNAL_SECRET;
if (!SECRET) throw new Error('COWORK_INTERNAL_SECRET env var required');

export interface AuthedRequest extends Request {
  userId: string;
  userEmail: string;
}

export function authMiddleware(req: Request, res: Response, next: NextFunction): void {
  const auth = req.headers.authorization;
  if (!auth?.startsWith('Bearer ')) {
    res.status(401).json({ error: 'Missing internal auth token' });
    return;
  }

  try {
    const token = auth.slice(7);
    const payload = jwt.verify(token, SECRET, {
      issuer:   'cowork-web',
      audience: 'cowork-agent',
    }) as { sub: string; email: string };

    (req as AuthedRequest).userId    = payload.sub;
    (req as AuthedRequest).userEmail = payload.email;
    next();
  } catch {
    res.status(401).json({ error: 'Invalid internal auth token' });
  }
}
```

---

## Task 10: Routes + Agent Runner

### fip/cowork/src/CoworkAgent/src/routes/tasks.ts

```typescript
import express from 'express';
import multer from 'multer';
import path from 'path';
import fs from 'fs/promises';
import { runTask } from '../agent/runner.js';
import type { AuthedRequest } from '../middleware/auth.js';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/' });

interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'html_output' | 'file_output' | 'error';
  text?: string;
  toolName?: string;
  base64?: string;
  fileName?: string;
  downloadUrl?: string;
}

// In-memory task store (Sprint 1; replaced with Redis in Sprint 2)
const taskStreams = new Map<string, AsyncGenerator<SseChunk>>();

// POST /tasks — create and start a new task
router.post('/', upload.array('files', 5), async (req, res) => {
  const authed = req as AuthedRequest;
  const { prompt } = req.body as { prompt: string };

  if (!prompt?.trim()) {
    res.status(400).json({ error: 'prompt required' });
    return;
  }

  const taskId = crypto.randomUUID();
  const workingDir = `/tmp/cowork-${taskId}`;
  await fs.mkdir(workingDir, { recursive: true });

  const files = req.files as Express.Multer.File[] | undefined;
  if (files) {
    for (const file of files) {
      const dest = path.join(workingDir, file.originalname);
      await fs.rename(file.path, dest);
    }
  }

  async function* generateChunks(): AsyncGenerator<SseChunk> {
    yield* runTask({
      taskId,
      userId:      authed.userId,
      userEmail:   authed.userEmail,
      prompt,
      workingDir,
      maxBudgetUsd: parseFloat(process.env.COWORK_MAX_BUDGET_USD ?? '0.50'),
      maxTurns:     parseInt(process.env.COWORK_MAX_TURNS ?? '30', 10),
    });
  }

  taskStreams.set(taskId, generateChunks());
  res.json({ taskId });
});

// GET /tasks/:id/stream — SSE stream
router.get('/:id/stream', async (req, res) => {
  const { id } = req.params;
  const gen = taskStreams.get(id);

  if (!gen) {
    res.status(404).json({ error: 'Task not found' });
    return;
  }

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  try {
    for await (const chunk of gen) {
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);

      if (chunk.type === 'result' || chunk.type === 'error') {
        taskStreams.delete(id);
        break;
      }
    }
  } catch (err: any) {
    res.write(`data: ${JSON.stringify({ type: 'error', text: err.message })}\n\n`);
  } finally {
    res.end();
  }
});

export { router as tasksRouter };
```

### fip/cowork/src/CoworkAgent/src/agent/runner.ts

CRITICAL: env object passed to query() must NOT contain any secrets.

```typescript
import path from 'path';
import fs from 'fs/promises';
import { query } from '@anthropic-ai/claude-agent-sdk';
import { auditLog } from './audit.js';
import { queryForgeContext } from '../services/forgeClient.js';

interface TaskParams {
  taskId:      string;
  userId:      string;
  userEmail:   string;
  prompt:      string;
  workingDir:  string;
  maxBudgetUsd: number;
  maxTurns:    number;
}

interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'html_output' | 'file_output' | 'error';
  text?: string;
  toolName?: string;
  base64?: string;
  fileName?: string;
  downloadUrl?: string;
}

const SYSTEM_PROMPT = `You are FAIT Cowork — an AI assistant at Fortress Asset Management.
You complete business tasks for non-technical users: creating HTML prototypes, drafting documents,
summarizing files, and analyzing data.

Your working directory contains the user's uploaded files. You create output files there.
Explain each step as you work — users see your progress in real time.

When creating HTML, use inline CSS only (no external CDN links — the output must be self-contained).
When finished, explicitly state the name(s) of the output file(s) you created.

Data sovereignty: You run on Fortress AM's private AWS infrastructure. No data leaves Fortress AM.`;

export async function* runTask(params: TaskParams): AsyncGenerator<SseChunk> {
  await auditLog({ event: 'task_started', ...params });

  let forgeContext = '';
  try {
    forgeContext = await queryForgeContext(params.prompt, params.userId, params.userEmail);
  } catch {
    // Non-fatal — task runs without FORGE context if fetch fails
  }

  const systemPrompt = forgeContext
    ? `${SYSTEM_PROMPT}\n\n## Relevant Knowledge from FORGE\n${forgeContext}`
    : SYSTEM_PROMPT;

  try {
    for await (const message of query({
      prompt: params.prompt,
      options: {
        cwd: params.workingDir,
        allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
        maxBudgetUsd: params.maxBudgetUsd,
        maxTurns: params.maxTurns,
        systemPrompt,
        env: {
          // SAFE non-secret identifiers only — secrets must NEVER be in here
          // The Agent SDK may expose this env to Claude via Bash tool
          COWORK_TASK_ID:    params.taskId,
          COWORK_USER_ID:    params.userId,
          COWORK_USER_EMAIL: params.userEmail,
        },
        hooks: {
          preToolCall: async (toolName: string, toolInput: any) => {
            await auditLog({
              event: 'tool_call',
              taskId: params.taskId,
              userId: params.userId,
              data: { tool: toolName, input: safeSerialize(toolInput) },
            });
            return { action: 'allow' as const };
          },
        },
      },
    })) {
      if ('result' in message) {
        const outputs = await collectOutputFiles(params.workingDir);
        for (const chunk of outputs) yield chunk;

        await auditLog({ event: 'task_completed', taskId: params.taskId, userId: params.userId });
        yield { type: 'result', text: typeof message.result === 'string' ? message.result : JSON.stringify(message.result) };
      } else if (message.type === 'assistant') {
        for (const block of (message.content as any[]) ?? []) {
          if (block.type === 'text' && block.text?.trim()) {
            yield { type: 'step', text: block.text };
          } else if (block.type === 'tool_use') {
            yield { type: 'tool_call', toolName: block.name, text: describeToolCall(block) };
          }
        }
      }
    }
  } catch (error: any) {
    await auditLog({ event: 'task_failed', taskId: params.taskId, userId: params.userId, data: { error: error.message } });
    yield { type: 'error', text: error.message ?? 'Task failed' };
  }
}

async function collectOutputFiles(workingDir: string): Promise<SseChunk[]> {
  const chunks: SseChunk[] = [];
  try {
    const entries = await fs.readdir(workingDir, { withFileTypes: true });
    for (const entry of entries) {
      if (!entry.isFile()) continue;
      const filePath = path.join(workingDir, entry.name);

      if (entry.name.endsWith('.html')) {
        const content = await fs.readFile(filePath, 'utf-8');
        chunks.push({
          type: 'html_output',
          base64: Buffer.from(content).toString('base64'),
          fileName: entry.name,
        });
      }

      chunks.push({
        type: 'file_output',
        fileName: entry.name,
        downloadUrl: `/tasks/files/${path.basename(workingDir)}/${entry.name}`,
      });
    }
  } catch { /* Non-fatal */ }
  return chunks;
}

function describeToolCall(block: any): string {
  if (block.name === 'Read')  return `Reading ${block.input?.file_path ?? 'file'}`;
  if (block.name === 'Write') return `Writing ${block.input?.file_path ?? 'file'}`;
  if (block.name === 'Edit')  return `Editing ${block.input?.file_path ?? 'file'}`;
  if (block.name === 'Bash')  return `Running: ${(block.input?.command ?? '').slice(0, 80)}`;
  return `Using ${block.name}`;
}

function safeSerialize(input: any): any {
  try {
    return JSON.parse(JSON.stringify(input));
  } catch {
    return String(input);
  }
}
```

---

## Task 11: Audit + FORGE Client

### fip/cowork/src/CoworkAgent/src/agent/audit.ts

```typescript
import { CloudWatchLogsClient, PutLogEventsCommand } from '@aws-sdk/client-cloudwatch-logs';

const client = new CloudWatchLogsClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
const LOG_GROUP = '/cowork/tasks';

interface AuditEntry {
  event: string;
  taskId?: string;
  userId?: string;
  userEmail?: string;
  prompt?: string;
  data?: any;
}

export async function auditLog(entry: AuditEntry): Promise<void> {
  const streamName = entry.taskId ?? 'system';
  const message = JSON.stringify({
    timestamp: new Date().toISOString(),
    ...entry,
    // Redact: never log file contents, API keys, or JWTs
    prompt: entry.prompt ? entry.prompt.slice(0, 200) : undefined,
  });

  try {
    await client.send(new PutLogEventsCommand({
      logGroupName:  LOG_GROUP,
      logStreamName: streamName,
      logEvents: [{
        timestamp: Date.now(),
        message,
      }],
    }));
  } catch (err: any) {
    // Non-fatal — audit failure must not break task execution
    console.error('Audit log failed:', err.message);
  }
}
```

### fip/cowork/src/CoworkAgent/src/services/forgeClient.ts

```typescript
const FORGE_API_URL = process.env.FORGE_API_URL ?? 'https://fait.dev.fortressam.ai';
const FORGE_API_KEY = process.env.FORGE_API_KEY ?? '';

/**
 * Query FORGE for context relevant to the task prompt.
 * Passes x-user-id so FAIT can scope KB results to the user in future sprints.
 */
export async function queryForgeContext(prompt: string, userId: string, userEmail: string): Promise<string> {
  if (!FORGE_API_KEY) return '';

  const resp = await fetch(`${FORGE_API_URL}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type':  'application/json',
      'x-api-key':     FORGE_API_KEY,
      'x-user-id':     userId,
      'x-user-email':  userEmail,
    },
    body: JSON.stringify({
      query: prompt.slice(0, 500),
      topK: 3,
      kbTypes: ['document', 'note'],
    }),
  });

  if (!resp.ok) return '';

  const { results } = await resp.json() as { results: Array<{ content: string; source: string }> };
  if (!results?.length) return '';

  return results.map((r, i) => `[${i + 1}] Source: ${r.source}\n${r.content.slice(0, 500)}`).join('\n\n');
}
```

---

## Task 12: Dockerfiles + buildspec.yml

### fip/cowork/Dockerfile.web

Build context is the fip/ monorepo root (run as: `docker build -f cowork/Dockerfile.web .` from fip/)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY shared/FipShared/FipShared.csproj ./shared/FipShared/
COPY cowork/src/CoworkWeb/CoworkWeb.csproj ./cowork/src/CoworkWeb/
RUN dotnet restore ./cowork/src/CoworkWeb/CoworkWeb.csproj

COPY shared/FipShared/ ./shared/FipShared/
COPY cowork/src/CoworkWeb/ ./cowork/src/CoworkWeb/
RUN dotnet publish ./cowork/src/CoworkWeb/CoworkWeb.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "CoworkWeb.dll"]
```

### fip/cowork/Dockerfile.agent

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY cowork/src/CoworkAgent/package.json cowork/src/CoworkAgent/package-lock.json ./
RUN npm ci
COPY cowork/src/CoworkAgent/ ./
RUN npm run build

FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json .
EXPOSE 3000
CMD ["node", "dist/server.js"]
```

### fip/cowork/buildspec.yml

```yaml
version: 0.2
phases:
  pre_build:
    commands:
      - aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
      - COMMIT_HASH=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c 1-7)
      - IMAGE_TAG=${COMMIT_HASH:=latest}
  build:
    commands:
      - docker build -f cowork/Dockerfile.web -t cowork-web .
      - docker build -f cowork/Dockerfile.agent -t cowork-agent .
      - docker tag cowork-web:latest   742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG
      - docker tag cowork-agent:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:$IMAGE_TAG
      - docker tag cowork-web:latest   742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:latest
      - docker tag cowork-agent:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:latest
  post_build:
    commands:
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:$IMAGE_TAG
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:latest
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:latest
      - printf '[{"name":"cowork-web","imageUri":"%s"},{"name":"cowork-agent","imageUri":"%s"}]' 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:$IMAGE_TAG > imagedefinitions.json
artifacts:
  files: imagedefinitions.json
```

---

## Additional Notes

### ProjectReference path fix
The `CoworkWeb.csproj` ProjectReference must navigate correctly from `fip/cowork/src/CoworkWeb/` to `fip/shared/FipShared/`. Check how FAIT does it:

```bash
grep -r "ProjectReference" /home/fredw/projects/fip/fait/src/FortressAI.Web/FortressAI.Web.csproj
```

Use the same relative path pattern but adjusted for the cowork directory depth.

### Check @anthropic-ai/claude-agent-sdk on npm
```bash
npm view @anthropic-ai/claude-agent-sdk version 2>/dev/null || echo "Package may not exist — check npm"
```
If it doesn't exist, use the Agent SDK pattern from `@anthropic-ai/sdk` with the appropriate streaming/tool-use methods.

### Index.razor conflict
NewTask.razor has `@page "/"` — remove that route from NewTask.razor if Index.razor also has `@page "/"`. Only one component should handle the root route. Keep `@page "/"` in Index.razor (redirect), and `@page "/tasks/new"` in NewTask.razor only.

---

## Summary of Files to Create/Modify

```
MODIFY:
  fip/shared/FipShared/Models/FipModule.cs

CREATE (21 files):
  fip/cowork/src/CoworkWeb/CoworkWeb.csproj
  fip/cowork/src/CoworkWeb/Program.cs
  fip/cowork/src/CoworkWeb/Services/CoworkSessionService.cs
  fip/cowork/src/CoworkWeb/Services/InternalTokenService.cs
  fip/cowork/src/CoworkWeb/Services/AgentApiClient.cs
  fip/cowork/src/CoworkWeb/Components/App.razor
  fip/cowork/src/CoworkWeb/Components/Layout/MainLayout.razor
  fip/cowork/src/CoworkWeb/Components/Layout/MainLayout.razor.css
  fip/cowork/src/CoworkWeb/Components/Pages/Index.razor
  fip/cowork/src/CoworkWeb/Components/Pages/NewTask.razor
  fip/cowork/src/CoworkWeb/Components/Pages/TaskPage.razor
  fip/cowork/src/CoworkWeb/wwwroot/css/cowork.css
  fip/cowork/src/CoworkWeb/Data/SharedKeyRingDbContext.cs
  fip/cowork/src/CoworkAgent/package.json
  fip/cowork/src/CoworkAgent/tsconfig.json
  fip/cowork/src/CoworkAgent/src/server.ts
  fip/cowork/src/CoworkAgent/src/middleware/auth.ts
  fip/cowork/src/CoworkAgent/src/routes/tasks.ts
  fip/cowork/src/CoworkAgent/src/agent/runner.ts
  fip/cowork/src/CoworkAgent/src/agent/audit.ts
  fip/cowork/src/CoworkAgent/src/services/forgeClient.ts
  fip/cowork/Dockerfile.web
  fip/cowork/Dockerfile.agent
  fip/cowork/buildspec.yml
```

Wait — that's 24 files listed above (21 new + 1 modified = 22 total). The spec says 21 new files. The spec notes `tokenValidator.ts` was merged into `middleware/auth.ts`. The file count above matches: 21 new files in cowork/ + 1 modified in shared/.

---

## Verification Checks After Writing All Files

Run these from `/home/fredw/projects/fip/`:

```bash
# 1. DataProtection — both lines
grep -n "SetApplicationName\|DisableAutomaticKeyGeneration" cowork/src/CoworkWeb/Program.cs

# 2. iframe sandbox
grep -n "sandbox" cowork/src/CoworkWeb/Components/Pages/TaskPage.razor

# 3. JWT secret from env var
grep -n "InternalSecret\|COWORK_INTERNAL_SECRET" cowork/src/CoworkWeb/Services/InternalTokenService.cs cowork/src/CoworkAgent/src/middleware/auth.ts

# 4. FipModule Cowork
grep -n "Cowork" shared/FipShared/Models/FipModule.cs

# 5. File count
find cowork/ -type f | wc -l

# 6. blazor.server.js (not blazor.web.js)
grep "blazor" cowork/src/CoworkWeb/Components/App.razor
```

Please implement all files exactly as specified. Pay particular attention to the CRITICAL constraints marked above.
