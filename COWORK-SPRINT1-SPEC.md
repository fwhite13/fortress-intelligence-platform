# FAIT Cowork Sprint 1 — Implementation Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Architecture ref:** `COWORK-ARCHITECTURE-SPEC.md`

---

## Pre-Read: What Was Read

- `COWORK-ARCHITECTURE-SPEC.md` — full architecture decisions
- `fip/fait/src/FortressAI.Web/Program.cs` — exact FIP auth pattern (lines 148–244): `AddCookie`, `AddDataProtection`, `PersistKeysToDbContext<SharedKeyRingDbContext>`, `SetApplicationName("FortressAI")`, `DisableAutomaticKeyGeneration`
- `fip/fait/src/FortressAI.Web/Services/UserSessionService.cs` — scoped session service pattern for Blazor Server circuits
- `fip/fait/src/FortressAI.Web/Services/AppKeyAuthHandler.cs` — current x-api-key approach (shared key, not per-user — Cowork changes this)
- `fip/shared/FipShared/Models/FipModule.cs` — enum to extend
- `fip/shared/FipShared/wwwroot/css/fip-tokens.css` — design tokens (navy/gold, Inter)
- `fip/fait/src/FortressAI.Shared/Models/AppUser.cs` — user model fields

**Per-user FORGE key decision (from Fred):** The FAIT API's existing `AppKeyAuthHandler` uses a shared service-account key. Cowork must carry user identity (userId + email from FIP cookie claims) through the entire call chain — Blazor → Node.js API → FORGE — from day 1. The mechanism: Blazor signs a short-lived internal JWT carrying the user's FIP identity; the Node.js API validates it and attaches `x-user-id` to FORGE requests. The FAIT backend will need to add `x-user-id`-scoped KB filtering in a future sprint (filed as a follow-on). Sprint 1 passes the header; FAIT ignores it today but will respect it tomorrow.

---

## Architecture: .NET Blazor + Node.js API, Two Containers

```
fip/cowork/
├── src/
│   ├── CoworkWeb/              ← .NET 9 Blazor Server app (Container 1)
│   │   ├── CoworkWeb.csproj
│   │   ├── Program.cs
│   │   ├── Components/
│   │   │   ├── App.razor
│   │   │   ├── Layout/
│   │   │   │   ├── MainLayout.razor
│   │   │   │   └── MainLayout.razor.css
│   │   │   └── Pages/
│   │   │       ├── Index.razor          ← redirect to /tasks/new
│   │   │       ├── NewTask.razor        ← task creation
│   │   │       └── TaskPage.razor       ← task execution + output
│   │   ├── Services/
│   │   │   ├── CoworkSessionService.cs  ← scoped; holds current user
│   │   │   ├── AgentApiClient.cs        ← HTTP client for Node.js agent API
│   │   │   └── InternalTokenService.cs  ← signs/validates internal JWTs
│   │   └── wwwroot/
│   │       └── css/
│   │           └── cowork.css           ← minimal; imports fip-tokens
│   └── CoworkAgent/            ← Node.js TypeScript API (Container 2)
│       ├── package.json
│       ├── tsconfig.json
│       └── src/
│           ├── server.ts               ← Express app, routes
│           ├── routes/
│           │   └── tasks.ts            ← POST /tasks, GET /tasks/:id/stream
│           ├── agent/
│           │   ├── runner.ts           ← runTask() using Agent SDK
│           │   ├── toolset.ts          ← tool whitelist definitions
│           │   └── audit.ts            ← CloudWatch audit log
│           ├── services/
│           │   ├── fileService.ts      ← upload, temp dir management
│           │   ├── forgeClient.ts      ← FORGE kb-search wrapper
│           │   └── tokenValidator.ts   ← validate internal JWT from Blazor
│           └── middleware/
│               └── auth.ts             ← JWT validation middleware
├── Dockerfile.web               ← Blazor container
├── Dockerfile.agent             ← Node.js container
└── buildspec.yml                ← Two ECR images, one pipeline
```

---

## How the Two Containers Communicate

```
Browser
  │ HTTPS (FIP cookie)
  ▼
[CoworkWeb — Blazor Server, port 8080]
  │ POST /tasks  (HTTP, internal network)
  │ Headers:
  │   Authorization: Bearer <internal-jwt>   ← signed with shared secret
  │   Content-Type: multipart/form-data
  ▼
[CoworkAgent — Node.js Express, port 3000]
  │ Validates JWT, extracts userId + email
  │ Spawns Agent SDK task
  │
  │ GET /tasks/:id/stream (SSE)
  │ (Blazor proxies SSE to browser via HttpClient + StreamingResponse)
  ▼
[AWS Bedrock — claude-sonnet-4-6, us-east-1]
```

**Why HTTP not SignalR between containers?** SignalR adds a hub abstraction that's useful for browser-to-server real-time but adds complexity for server-to-server calls. Simple HTTP POST to start a task + SSE stream for progress is cleaner and easier to debug. Blazor proxies the SSE stream from the Node.js container to the browser — the browser never talks directly to Node.js.

---

## Section 1: FipModule.Cowork

**File:** `fip/shared/FipShared/Models/FipModule.cs`

```csharp
namespace FipShared.Models;

public enum FipModule
{
    FAIT,
    FIRM,
    FORMS,
    Cowork   // ← new
}

public static class FipModuleExtensions
{
    public static string FullName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "Fortress AI Tools",
        FipModule.FIRM   => "Fortress Intelligence & Risk Management",
        FipModule.FORMS  => "Fortress Form Tools",
        FipModule.Cowork => "FAIT Cowork",       // ← new
        _                => module.ToString()
    };

    public static string ShortName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "FAIT",
        FipModule.FIRM   => "FIRM",
        FipModule.FORMS  => "FORMS",
        FipModule.Cowork => "Cowork",            // ← new
        _                => module.ToString()
    };

    public static string Url(this FipModule module) => module switch
    {
        FipModule.FAIT   => "https://fait.fortressintelligence.com",
        FipModule.FIRM   => "https://firm.fortressintelligence.com",
        FipModule.FORMS  => "https://forms.fortressintelligence.com",
        FipModule.Cowork => "https://cowork.fortressintelligence.com", // ← new
        _                => "#"
    };
}
```

**That is the only change to `FipShared`.** `FipNavBar.razor` already calls `Enum.GetValues<FipModule>()` in the waffle menu loop — Cowork appears automatically.

---

## Section 2: CoworkWeb — .NET Blazor Server

### `CoworkWeb.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>CoworkWeb</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- MudBlazor for FIP chrome (nav bar reuse) -->
    <PackageReference Include="MudBlazor" Version="7.*" />
    <!-- Cookie auth + data protection (same as FAIT) -->
    <PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" Version="*" />
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Npgsql" Version="*" />
    <!-- Internal JWT signing for Blazor → Node.js auth -->
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="*" />
    <!-- FipShared RCL (FipNavBar, FipModule) -->
    <ProjectReference Include="..\..\shared\FipShared\FipShared.csproj" />
  </ItemGroup>
</Project>
```

### `Program.cs`

Full program — heavily annotated to show where it copies FAIT vs where it differs.

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
    client.Timeout = TimeSpan.FromMinutes(5);  // Agent tasks can run for minutes
});

// ── Authentication: pure cookie consumer — FIP portal owns Entra OIDC ────────
// EXACT copy of FAIT Program.cs lines 154–171. Only cookie name and domain change.
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
    options.Cookie.Name = ".FortressAI.Session";           // Same cookie as FAIT/FIRM/FORMS
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;  // All routes require auth
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── Data protection: shared FIP key ring ─────────────────────────────────────
// EXACT copy of FAIT Program.cs lines 234–242.
// MUST use the shared key ring DB — if we generate our own keys, the shared
// .FortressAI.Session cookie will fail to decrypt.
var keyRingConnStr = builder.Configuration.GetConnectionString("KeyRingDb")
    ?? builder.Configuration["FIP_KEYRING_DB_NAME"] is { } dbName
        ? $"Host=...;Database={dbName};..." // real connstr from env
        : throw new InvalidOperationException("KeyRingDb connection string required");

builder.Services.AddDbContext<SharedKeyRingDbContext>(opt =>
    opt.UseNpgsql(keyRingConnStr));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")          // MUST match — same as FAIT/FIRM/FORMS
    .DisableAutomaticKeyGeneration();           // FIP portal owns key creation

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

// ── Auth routes (mirrors FAIT pattern) ───────────────────────────────────────
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

### `Services/InternalTokenService.cs`

This service generates and validates short-lived JWTs for Blazor → Node.js auth. The token carries the user's FIP identity so Node.js knows who is making each request.

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CoworkWeb.Services;

/// <summary>
/// Issues short-lived (5-minute) signed JWTs for Blazor-to-Node.js API calls.
/// The Node.js CoworkAgent validates these with the same shared secret.
///
/// Token payload:
///   sub   = FIP user ID (GUID string)
///   email = user email from Entra claims
///   iat   = issued at
///   exp   = issued at + 5 minutes
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

    /// <summary>Issue a token for the given user, valid for 5 minutes.</summary>
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

### `Services/CoworkSessionService.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CoworkWeb.Services;

/// <summary>
/// Scoped service that holds the current user's FIP identity for Blazor Server circuits.
/// Populated in MainLayout.razor.cs during OnInitializedAsync — same pattern as FAIT's UserSessionService.
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

### `Services/AgentApiClient.cs`

The HTTP client that Blazor uses to talk to the Node.js CoworkAgent API. Handles the internal JWT header injection.

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

    /// <summary>
    /// POST /tasks — start a new agent task.
    /// Returns the taskId from the agent API.
    /// </summary>
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

    /// <summary>
    /// GET /tasks/:id/stream — returns the SSE stream from the agent API.
    /// Caller reads from the stream and writes each line to the Blazor component.
    /// </summary>
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

### `Components/App.razor`

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

**Use `blazor.server.js` (not `blazor.web.js`).** FAIT and FIRM use `blazor.server.js`; FORMS uses `blazor.web.js`. Cowork is pure Blazor Server — use `blazor.server.js`.

### `Components/Layout/MainLayout.razor`

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
    <!-- FIP nav bar (from FipShared RCL) -->
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

### `Components/Pages/NewTask.razor`

```razor
@page "/tasks/new"
@page "/"
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

    <!-- Task type hints (non-interactive, just UX guidance) -->
    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 24px;">
        @foreach (var hint in _hints)
        {
            <div style="padding: 12px 14px; border: 1px solid var(--color-border); border-radius: var(--radius-md); background: var(--color-surface);">
                <div style="font-weight: var(--font-medium); font-size: var(--text-sm); color: var(--color-text-primary);">@hint.Title</div>
                <div style="font-size: var(--text-xs); color: var(--color-text-muted); margin-top: 2px;">@hint.Example</div>
            </div>
        }
    </div>

    <!-- Prompt input -->
    <MudTextField @bind-Value="_prompt"
                  Label="Describe your task"
                  Placeholder="e.g. Create an HTML prototype of a fund performance dashboard with Q1 metrics and a chart placeholder."
                  Lines="5"
                  Variant="Variant.Outlined"
                  Class="mb-3"
                  FullWidth="true" />

    <!-- File upload -->
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

    <!-- Submit -->
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

### `Components/Pages/TaskPage.razor`

```razor
@page "/tasks/{TaskId}"
@inject AgentApiClient AgentApi
@inject CoworkSessionService Session
@implements IAsyncDisposable

<PageTitle>Task — FAIT Cowork</PageTitle>

<div style="max-width: 800px; margin: 0 auto; padding: 32px 16px; font-family: var(--font-primary);">

    <!-- Task status header -->
    <div style="display:flex; align-items:center; gap:10px; margin-bottom:24px;">
        <div style="width:10px;height:10px;border-radius:50%;background:@(_done ? "var(--color-success)" : "var(--color-gold)");"></div>
        <span style="font-size:var(--text-sm);color:var(--color-text-secondary);">
            @(_done ? "Completed" : "In progress…")
        </span>
    </div>

    <!-- Live step feed -->
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

    <!-- Output panel (appears when result arrives) -->
    @if (_outputText is not null || _outputHtmlBase64 is not null)
    {
        <div style="background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-lg);padding:20px;">
            <h2 style="font-size:var(--text-lg);font-weight:var(--font-semibold);color:var(--color-text-primary);margin:0 0 16px 0;">
                Output
            </h2>

            <!-- HTML preview (iframe) — shown if any .html output -->
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

            <!-- Text output -->
            @if (_outputText is not null)
            {
                <div style="background:var(--color-bg-page);border:1px solid var(--color-border);border-radius:var(--radius-md);padding:16px;margin-bottom:16px;">
                    <pre style="white-space:pre-wrap;font-family:var(--font-primary);font-size:var(--text-sm);color:var(--color-text-primary);margin:0;line-height:1.6;">@_outputText</pre>
                </div>
            }

            <!-- Download links -->
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
                _outputHtmlBase64 = chunk.Base64; // base64-encoded HTML content
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

## Section 3: CoworkAgent — Node.js TypeScript API

### `package.json`

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
    "@anthropic-ai/claude-agent-sdk": "^1.0.0",
    "express": "^4.18.0",
    "multer": "^1.4.5",
    "jsonwebtoken": "^9.0.0",
    "@aws-sdk/client-cloudwatch-logs": "^3.0.0"
  },
  "devDependencies": {
    "@types/express": "^4.17.0",
    "@types/multer": "^1.4.0",
    "@types/jsonwebtoken": "^9.0.0",
    "@types/node": "^22.0.0",
    "tsx": "^4.0.0",
    "typescript": "^5.0.0"
  }
}
```

**Pin `@anthropic-ai/claude-agent-sdk` to a specific version** — do not use `"latest"` or `"^1.0.0"` without testing. The SDK API surface is v1.x; check the latest stable release on npm and pin to it (e.g., `"1.2.3"`).

### SSE Event Format

The agent API emits SSE events consumed by Blazor's `AgentApiClient.OpenStreamAsync()`. Every event is:

```
data: <JSON>\n\n
```

**Defined event types:**

| `type` | Fields | When emitted |
|--------|--------|--------------|
| `step` | `text` | Every assistant text block or tool call description |
| `tool_call` | `text`, `toolName` | When SDK invokes a tool (Read/Write/Edit/Bash) |
| `result` | `text` | Final task result (plain text output) |
| `html_output` | `base64` | If agent wrote an `.html` file — base64-encoded content |
| `file_output` | `fileName`, `downloadUrl` | For each output file (pre-signed S3 URL or local path) |
| `error` | `text` | Fatal error ending the task |

**Output type detection:** The Node.js agent API scans the working directory after task completion. If any `.html` files exist, emit `html_output` with base64-encoded content. All files emit `file_output` with a download URL. This is how the Blazor `TaskPage.razor` knows what type of output to render.

### `src/server.ts`

```typescript
import express from 'express';
import multer from 'multer';
import { tasksRouter } from './routes/tasks';
import { authMiddleware } from './middleware/auth';

const app = express();
const upload = multer({ dest: '/tmp/uploads/' });

app.use(express.json());
app.use(authMiddleware);  // Validate internal JWT on all routes
app.use('/tasks', tasksRouter);

const port = parseInt(process.env.PORT ?? '3000', 10);
app.listen(port, () => {
  console.log(`CoworkAgent listening on :${port}`);
});
```

### `src/middleware/auth.ts`

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

### `src/routes/tasks.ts`

```typescript
import express from 'express';
import multer from 'multer';
import path from 'path';
import fs from 'fs/promises';
import { runTask } from '../agent/runner';
import type { AuthedRequest } from '../middleware/auth';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/' });

// In-memory task store (Sprint 1; replaced with Redis in Sprint 2)
const taskStreams = new Map<string, AsyncGenerator<SseChunk>>();

interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'html_output' | 'file_output' | 'error';
  text?: string;
  toolName?: string;
  base64?: string;
  fileName?: string;
  downloadUrl?: string;
}

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

  // Copy uploaded files into working dir
  const files = req.files as Express.Multer.File[] | undefined;
  if (files) {
    for (const file of files) {
      const dest = path.join(workingDir, file.originalname);
      await fs.rename(file.path, dest);
    }
  }

  // Create async generator for streaming
  async function* generateChunks(): AsyncGenerator<SseChunk> {
    yield* runTask({
      taskId,
      userId:     authed.userId,
      userEmail:  authed.userEmail,
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

      // End stream on terminal events
      if (chunk.type === 'result' || chunk.type === 'error') {
        taskStreams.delete(id); // Clean up
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

### `src/agent/runner.ts`

```typescript
import path from 'path';
import fs from 'fs/promises';
import { query } from '@anthropic-ai/claude-agent-sdk';
import { auditLog } from './audit';
import { queryForgeContext } from '../services/forgeClient';

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

  // Fetch FORGE context for this user (best-effort — if it fails, continue without it)
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
          COWORK_TASK_ID:    params.taskId,
          COWORK_USER_ID:    params.userId,
          COWORK_USER_EMAIL: params.userEmail,
          // Do NOT pass AWS creds or internal secrets into Bash env
        },
        hooks: {
          preToolCall: async (toolName: string, toolInput: any) => {
            await auditLog({
              event: 'tool_call',
              taskId: params.taskId,
              userId: params.userId,
              data: { tool: toolName, input: safeSerialize(toolInput) },
            });
            // Phase 1: auto-approve all (approval gate UI in Sprint 2)
            return { action: 'allow' as const };
          },
        },
      },
    })) {
      // Normalize SDK messages to SSE chunks
      if ('result' in message) {
        // Task complete — scan working dir for outputs
        const outputs = await collectOutputFiles(params.workingDir);
        for (const chunk of outputs) yield chunk;

        await auditLog({ event: 'task_completed', taskId: params.taskId, userId: params.userId });
        yield { type: 'result', text: typeof message.result === 'string' ? message.result : JSON.stringify(message.result) };
      } else if (message.type === 'assistant') {
        // Extract human-readable steps from assistant message content
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

      // All files get a file_output chunk (Sprint 1: serve from local path)
      // Sprint 2: upload to S3, return pre-signed URL
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

### `src/agent/audit.ts`

```typescript
import { CloudWatchLogsClient, PutLogEventsCommand, CreateLogStreamCommand } from '@aws-sdk/client-cloudwatch-logs';

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

**CloudWatch log group:** `/cowork/tasks` — created manually or via IaC before first deployment. Log streams are per-taskId. Retention: 90 days (set on the log group, not in code).

**CloudWatch log format:**

```json
{
  "timestamp": "2026-03-17T09:00:00Z",
  "event": "tool_call",
  "taskId": "b3f1a2c4-...",
  "userId": "08de7605-...",
  "data": {
    "tool": "Write",
    "input": { "file_path": "prototype.html" }
  }
}
```

Events logged: `task_started`, `tool_call`, `task_completed`, `task_failed`.

### `src/services/forgeClient.ts`

```typescript
const FORGE_API_URL  = process.env.FORGE_API_URL  ?? 'https://fait.dev.fortressam.ai';
const FORGE_API_KEY  = process.env.FORGE_API_KEY   ?? '';  // Service-level FAIT API key for FORGE search

/**
 * Query FORGE for context relevant to the task prompt.
 * Passes x-user-id so FAIT can scope KB results to the user in future sprints.
 *
 * NOTE: x-user-id is NOT yet respected by the FAIT backend in Sprint 1.
 * It is included from day 1 so FAIT can add per-user scoping without a Cowork change.
 */
export async function queryForgeContext(prompt: string, userId: string, userEmail: string): Promise<string> {
  if (!FORGE_API_KEY) return '';

  const resp = await fetch(`${FORGE_API_URL}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key':  FORGE_API_KEY,
      'x-user-id':  userId,       // Per-user identity — FAIT will scope on this in future sprint
      'x-user-email': userEmail,
    },
    body: JSON.stringify({
      query: prompt.slice(0, 500),  // Truncate — FORGE doesn't need the full prompt
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

## Section 4: Dockerfiles + BuildSpec

### `Dockerfile.web` (CoworkWeb Blazor)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Restore (includes FipShared dependency)
COPY fip/shared/FipShared/FipShared.csproj ./shared/FipShared/
COPY fip/cowork/src/CoworkWeb/CoworkWeb.csproj ./cowork/src/CoworkWeb/
RUN dotnet restore ./cowork/src/CoworkWeb/CoworkWeb.csproj

# Copy source and build
COPY fip/shared/FipShared/ ./shared/FipShared/
COPY fip/cowork/src/CoworkWeb/ ./cowork/src/CoworkWeb/
RUN dotnet publish ./cowork/src/CoworkWeb/CoworkWeb.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "CoworkWeb.dll"]
```

### `Dockerfile.agent` (CoworkAgent Node.js)

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY fip/cowork/src/CoworkAgent/package.json fip/cowork/src/CoworkAgent/package-lock.json ./
RUN npm ci
COPY fip/cowork/src/CoworkAgent/ ./
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

### `buildspec.yml`

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
      # Build context is fip/ monorepo root (same pattern as post-WI813)
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

## Files Changed Summary

### New repo: `fip/cowork/`

| File | Notes |
|------|-------|
| `src/CoworkWeb/CoworkWeb.csproj` | .NET 9, MudBlazor 7.*, FipShared ref |
| `src/CoworkWeb/Program.cs` | FIP auth cookie consumer + data protection |
| `src/CoworkWeb/Services/CoworkSessionService.cs` | Scoped user identity for Blazor circuits |
| `src/CoworkWeb/Services/InternalTokenService.cs` | Signs Blazor→Node JWTs |
| `src/CoworkWeb/Services/AgentApiClient.cs` | HTTP client for Node.js API |
| `src/CoworkWeb/Components/App.razor` | HTML shell; `blazor.server.js` |
| `src/CoworkWeb/Components/Layout/MainLayout.razor` | FipNavBar + auth state init |
| `src/CoworkWeb/Components/Pages/NewTask.razor` | Task creation UI |
| `src/CoworkWeb/Components/Pages/TaskPage.razor` | SSE consumer + output panel |
| `src/CoworkWeb/wwwroot/css/cowork.css` | Minimal; imports fip-tokens |
| `src/CoworkAgent/package.json` | Express, multer, Agent SDK, jsonwebtoken |
| `src/CoworkAgent/tsconfig.json` | Standard Node.js TS config |
| `src/CoworkAgent/src/server.ts` | Express app entry |
| `src/CoworkAgent/src/middleware/auth.ts` | JWT validation |
| `src/CoworkAgent/src/routes/tasks.ts` | POST /tasks, GET /tasks/:id/stream |
| `src/CoworkAgent/src/agent/runner.ts` | Agent SDK loop, SSE chunk generator |
| `src/CoworkAgent/src/agent/audit.ts` | CloudWatch audit log |
| `src/CoworkAgent/src/services/forgeClient.ts` | FORGE kb-search with x-user-id |
| `src/CoworkAgent/src/services/tokenValidator.ts` | (merged into middleware/auth.ts) |
| `Dockerfile.web` | .NET Blazor image |
| `Dockerfile.agent` | Node.js image |
| `buildspec.yml` | CodeBuild: two images |

### Modified: `fip/shared/FipShared/Models/FipModule.cs`

One file: add `Cowork` to enum + three switch expressions.

**Total: 21 new files in `fip/cowork/` + 1 modified in `fip/shared/`.**

---

## Environment Variables

### CoworkWeb container

```
ASPNETCORE_ENVIRONMENT=Production
Auth__CookieDomain=.fortressam.ai
FIP__LoginUrl=https://fip.dev.fortressam.ai
FIP__CoworkCallbackUrl=https://cowork.dev.fortressam.ai/auth/cowork-session
ConnectionStrings__KeyRingDb=Host=<postgres>;Database=fred_dev;...
CoworkAgent__BaseUrl=http://cowork-agent:3000    ← internal network URL
CoworkAgent__InternalSecret=<random 32+ byte secret>
```

### CoworkAgent container

```
NODE_ENV=production
PORT=3000
CLAUDE_CODE_USE_BEDROCK=1
AWS_REGION=us-east-1
ANTHROPIC_DEFAULT_SONNET_MODEL=us.anthropic.claude-sonnet-4-6
COWORK_INTERNAL_SECRET=<same secret as CoworkAgent__InternalSecret above>
COWORK_MAX_BUDGET_USD=0.50
COWORK_MAX_TURNS=30
FORGE_API_URL=https://fait.dev.fortressam.ai
FORGE_API_KEY=<FAIT service account API key>
```

**`COWORK_INTERNAL_SECRET` must match between the two containers.** It is the shared secret for the HMAC-signed JWTs. If they differ, Blazor's tokens will fail validation in Node.js.

---

## Acceptance Criteria

1. **FIP auth:** Navigating to `https://cowork.dev.fortressam.ai` unauthenticated redirects to FIP portal → Entra SSO → back to Cowork. After login, the FipNavBar shows the user's name and "FAIT Cowork" as the active app.

2. **Waffle menu:** FipNavBar waffle menu shows FAIT, FIRM, FORMS, Cowork. Clicking Cowork from another FIP app navigates to `cowork.fortressintelligence.com`.

3. **Task creation:** User can type a task description and submit. File upload dropzone accepts up to 5 files. "Start Task →" button is disabled when textarea is empty. Submitting navigates to `/tasks/<uuid>`.

4. **Agent execution:** The task page shows numbered steps streaming in real time as Claude works. The "Claude is working…" indicator is visible while the task runs.

5. **HTML output:** If the agent creates an `.html` file, the task page renders an `<iframe srcdoc="...">` preview of it. The iframe has `sandbox="allow-scripts"`.

6. **Text output:** If the agent produces a text result, it appears in a `<pre>` block.

7. **Download links:** All output files have download links below the output.

8. **CloudWatch:** Every task emits `task_started` → one or more `tool_call` events → `task_completed` (or `task_failed`) to the `/cowork/tasks` log group. Confirm via CloudWatch Logs console.

9. **User identity flows:** The CloudWatch `task_started` event includes `userId` and `userEmail` matching the logged-in FIP user — not a service account. Verify with a test task from Elise's or Lauren's account.

10. **x-user-id in FORGE request:** The FORGE `kb-search` call from Node.js includes `x-user-id` header with the user's FIP userId. Verify in CoworkAgent container logs (add a `console.log` at the fetch call during testing, remove before ship).

---

## Constraints for CC

- `SetApplicationName("FortressAI")` in `AddDataProtection()` is **MANDATORY** and must match exactly. If it differs from FAIT/FIRM/FORMS, the shared session cookie will fail to decrypt and all users will be unauthenticated.
- `DisableAutomaticKeyGeneration()` is **MANDATORY**. Without it, CoworkWeb will generate its own data protection keys and overwrite the shared FIP key ring, breaking auth for ALL FIP apps.
- The `blazor.server.js` script tag — NOT `blazor.web.js`. Cowork is pure Blazor Server. FORMS uses `blazor.web.js` (it has static SSR). Cowork does not.
- `COWORK_INTERNAL_SECRET` must be identical in both containers. If it is not set or mismatches, `authMiddleware` in Node.js will reject all requests from Blazor with 401.
- The `env` object passed to `query()` options in `runner.ts` must NOT include `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `COWORK_INTERNAL_SECRET`, `FORGE_API_KEY`, or any other secret. The Agent SDK may expose the env to the `Bash` tool. Only pass safe non-secret identifiers (`COWORK_TASK_ID`, `COWORK_USER_ID`, `COWORK_USER_EMAIL`).
- `FipModule.Cowork` — verify ALL THREE switch expressions are updated: `FullName`, `ShortName`, `Url`. C# exhaustive switch emits only a warning (not error) for missing cases — a missing Cowork case silently falls to `_` (returns `"Cowork"` from `ToString()` for ShortName, which happens to work, but `FullName` and `Url` would be wrong).
- The iframe `sandbox` attribute must be `sandbox="allow-scripts"` — NOT `sandbox="allow-scripts allow-same-origin"`. Adding `allow-same-origin` to a same-origin iframe removes the sandbox entirely. Sprint 1 HTML output is from files the agent created — not from the internet — but sandboxing is still required.
- Do NOT touch `fip/fait/`, `fip/firm/`, `fip/forms/`, or any existing `FipShared` component other than `FipModule.cs`. The only `FipShared` change is the `FipModule.cs` enum addition.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify SetApplicationName("FortressAI") and DisableAutomaticKeyGeneration()
          are both present in CoworkWeb Program.cs AddDataProtection() chain.
          Missing either breaks shared-cookie auth for ALL FIP apps.
          Check against FAIT Program.cs lines 234-242 exactly.

⚠️  HIGH: Verify COWORK_INTERNAL_SECRET is set in BOTH containers and matches.
          Test by making a task creation request and confirming the Node.js
          middleware validates it (no 401). A mismatch produces a silent 401
          that Blazor surfaces as "Failed to start task: Response status code 401."

⚠️  HIGH: Verify the Bash env in runner.ts does NOT include any secrets.
          Check the env object passed to query() options. Only COWORK_TASK_ID,
          COWORK_USER_ID, COWORK_USER_EMAIL are acceptable. Any credential
          in the env can be read by Claude via a Bash "env" command.

⚠️  HIGH: Verify iframe uses sandbox="allow-scripts" only (NOT allow-same-origin).
          The combination sandbox="allow-scripts allow-same-origin" on a same-origin
          iframe is a known security no-op — the sandbox is bypassed.

⚠️  MEDIUM: Verify FipModule.Cowork is added to all three switch expressions
            in FipModuleExtensions (FullName, ShortName, Url). Check that Url()
            returns "https://cowork.fortressintelligence.com", not "#" or empty.

⚠️  MEDIUM: Verify blazor.server.js is used (not blazor.web.js) in App.razor.
            Check the <script> tag near the bottom of the <body>.

⚠️  MEDIUM: Verify ANTHROPIC_DEFAULT_SONNET_MODEL env var is set in the CoworkAgent
            container. Without this, the Agent SDK picks up the default alias which
            may change when Anthropic releases a new model, breaking the pinning.

⚠️  LOW: Verify the CloudWatch log group /cowork/tasks exists before first deploy.
         The auditLog function silently swallows failures (non-fatal by design),
         so a missing log group won't crash the container but audit events will
         be silently dropped. Create the log group in the console first.

⚠️  LOW: Verify @anthropic-ai/claude-agent-sdk is pinned to a specific version
         in package.json (not "latest" or "^"). Check the npm page for the
         latest stable 1.x version and use that exact semver.
```

---

_Spec by Reed Richards | Cowork S1: 21 new files + 1 modified. .NET Blazor + Node.js Agent SDK + Bedrock. FIP auth from day 1. Per-user identity flows through the full call chain._
