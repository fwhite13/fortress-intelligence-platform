using Microsoft.EntityFrameworkCore;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;
using System.Reflection;
using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Services;

/// <summary>
/// Runs EF Core migrations on startup so the database schema is always current.
/// Uses NexusDbContext (reads FIP_DB_NAME env var — correct production database).
/// </summary>
public class DatabaseInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[NEXUS] Running EF Core migrations on startup...");
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("[NEXUS] EF Core migrations complete.");

            // Seed NexusAdmin role for Fred White
            const string fredUpn = "fwhite@fortressaffinitygroup.com";
            var hasAdminRole = await db.NexusUserRoles
                .AnyAsync(r => r.UserUpn == fredUpn && r.Role == NexusRoles.Admin, cancellationToken);
            if (!hasAdminRole)
            {
                db.NexusUserRoles.Add(new NexusUserRole
                {
                    UserUpn = fredUpn,
                    Role = NexusRoles.Admin,
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "system-seed"
                });
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("[NEXUS] Seeded NexusAdmin role for {Upn}", fredUpn);
            }

            // Seed FORGE KB MCP Server spec submission for E2E decomp test
            const string forgeKbTitle = "FORGE KB MCP Server";
            var hasForgeSubmission = await db.Submissions
                .AnyAsync(s => s.Title == forgeKbTitle && s.SubmittedBy == fredUpn, cancellationToken);

            if (!hasForgeSubmission)
            {
                // Read spec content from embedded resource
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(
                    "FortressNexus.Web.Resources.forge-kb-spec-seed.md");
                if (stream is null)
                {
                    _logger.LogError("[NEXUS] forge-kb-spec-seed.md embedded resource not found — FORGE KB seed skipped.");
                }
                else
                {
                    using var reader = new StreamReader(stream);
                    var specContent = await reader.ReadToEndAsync();

                    var now = DateTime.UtcNow;

                    // 1. Create submission
                    var submission = new Submission
                    {
                        Title = forgeKbTitle,
                        FeatureArea = "FORGE KB",
                        NarrativeText = "FORGE KB MCP Server implementation spec — seeded for E2E decomp validation.",
                        SubmittedBy = fredUpn,
                        SubmittedAt = now,
                        Status = SubmissionStatus.AwaitingReview,
                        MockupFileId = null
                    };
                    db.Submissions.Add(submission);
                    await db.SaveChangesAsync(cancellationToken); // get submission.Id

                    // 2. Create SpecDocument with spec content
                    var specDoc = new SpecDocument
                    {
                        SubmissionId = submission.Id,
                        Version = 1,
                        Content = specContent,
                        GeneratedAt = now,
                        GeneratedBy = "system-seed",
                        IsApproved = false,
                        PromptTokensUsed = 0,
                        CompletionTokensUsed = 0
                    };
                    db.SpecDocuments.Add(specDoc);
                    await db.SaveChangesAsync(cancellationToken); // get specDoc.Id

                    // 3. Wire ActiveSpecDocumentId back
                    submission.ActiveSpecDocumentId = specDoc.Id;
                    await db.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation("[NEXUS] Seeded FORGE KB spec submission id={Id} specDocId={SpecId}",
                        submission.Id, specDoc.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NEXUS] EF Core migration failed on startup — DB may be unavailable or schema mismatch.");
            // Non-fatal: app continues but DB features will fail
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
