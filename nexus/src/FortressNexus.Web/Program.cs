using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using MySqlConnector;
using FortressNexus.Web.Components;
using FortressNexus.Web.Data;
using FortressNexus.Web.Services;
using FortressNexus.Web.Services.Exporters;

var builder = WebApplication.CreateBuilder(args);

// Razor Components + Interactive Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Entra authentication (Microsoft Identity Web)
builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd");
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// Authorization — all routes require auth by default
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// MudBlazor
builder.Services.AddMudServices();

// Database — NEXUS_DB_* preferred, fall back to FORTRESS_DB_*
var dbHost = builder.Configuration["NEXUS_DB_HOST"]
    ?? builder.Configuration["FORTRESS_DB_HOST"]
    ?? "localhost";
var dbUser = builder.Configuration["NEXUS_DB_USER"]
    ?? builder.Configuration["FORTRESS_DB_USER"]
    ?? "root";
var dbPassword = builder.Configuration["NEXUS_DB_PASSWORD"]
    ?? builder.Configuration["FORTRESS_DB_PASS"]
    ?? "dev";

var csb = new MySqlConnectionStringBuilder
{
    Server = dbHost,
    Port = 3306,
    Database = "nexus_db",
    UserID = dbUser,
    Password = dbPassword,
    GuidFormat = MySqlGuidFormat.None,   // MANDATORY — prevents CHAR(36) auto-cast
    AllowPublicKeyRetrieval = true,
    SslMode = MySqlSslMode.None,
    ConnectionTimeout = 10
};

var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContext<NexusDbContext>(options =>
    options.UseMySql(csb.ConnectionString, serverVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// Application services
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<ISpecGenerationService, SpecGenerationService>();
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IMockupProcessingService, MockupProcessingService>();
builder.Services.AddScoped<IArtifactGenerationService, ArtifactGenerationService>();
builder.Services.AddScoped<UserContextService>();
builder.Services.AddScoped<IAdoService, StubAdoService>();
builder.Services.AddScoped<ISpecExporter, MarkdownExporter>();

// DB initialization (CREATE TABLE IF NOT EXISTS at startup)
builder.Services.AddHostedService<DatabaseInitializationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health endpoint — public, no auth
app.MapGet("/health", () => "OK").AllowAnonymous();

// Auth redirect (for unauthenticated users)
app.MapGet("/auth/redirect-to-login", (HttpContext ctx) =>
{
    var loginUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:LoginUrl"]?.TrimEnd('/') ?? "/";
    return Results.Redirect(loginUrl);
}).AllowAnonymous();

// MicrosoftIdentity UI controllers (handles /signin-oidc callback)
app.MapControllers();

// Blazor components — ALL routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();
