using Amazon.Lambda;
using Amazon.Lambda.Model;
using System.Text.Json;
using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

/// <summary>
/// Orchestrates morning briefing generation by invoking the briefing-builder Lambda,
/// then storing the result in the web app database.
/// Used by both the /api/briefing/generate endpoint and Dashboard.razor directly,
/// avoiding the HttpClient auth cookie issue in Blazor Server.
/// </summary>
public class BriefingGenerationService
{
    private readonly BriefingService _briefingSvc;
    private readonly AssistantConfigService _configSvc;
    private readonly ForgeQueryService _forgeQuery;
    private readonly ILogger<BriefingGenerationService> _logger;
    private readonly IConfiguration _config;

    public BriefingGenerationService(
        BriefingService briefingSvc,
        AssistantConfigService configSvc,
        ForgeQueryService forgeQuery,
        ILogger<BriefingGenerationService> logger,
        IConfiguration config)
    {
        _briefingSvc = briefingSvc;
        _configSvc = configSvc;
        _forgeQuery = forgeQuery;
        _logger = logger;
        _config = config;
    }

    /// <summary>
    /// Generates a morning briefing for the given user by invoking the briefing-builder Lambda.
    /// Stores the result in the web app database and returns the briefing record.
    /// </summary>
    public async Task<(bool Success, BriefingHistory? Briefing, string? Error)> GenerateBriefingAsync(Guid userId)
    {
        try
        {
            var assistantConfig = await _configSvc.GetOrCreateConfigAsync(userId);
            var personality = assistantConfig.PersonalityPreset ?? "friendly";

            _logger.LogInformation("Invoking briefing-builder Lambda for user {UserId}", userId);

            var functionName = _config.GetValue<string>("BriefingLambdaFunction", "briefing-builder");
            var regionStr = _config.GetValue<string>("AWS_REGION_OVERRIDE", "us-east-1");
            var region = Amazon.RegionEndpoint.GetBySystemName(regionStr);

            using var lambdaClient = new AmazonLambdaClient(region);

            string? forgeKbContext = null;
            try { forgeKbContext = await _forgeQuery.GetKbContextAsync(userId, "tasks priorities meeting notes"); if (string.IsNullOrEmpty(forgeKbContext)) forgeKbContext = null; }
            catch (Exception ex) { _logger.LogWarning(ex, "FORGE KB pre-fetch failed for briefing user {UserId}", userId); }

            var payload = JsonSerializer.Serialize(new
            {
                userId = userId.ToString(),
                assistantName = assistantConfig.AssistantName,
                personality,
                forgeKbContext
            });

            var lambdaRequest = new InvokeRequest
            {
                FunctionName = functionName,
                InvocationType = InvocationType.RequestResponse,
                Payload = payload
            };

            var lambdaResponse = await lambdaClient.InvokeAsync(lambdaRequest);

            if (lambdaResponse.FunctionError != null)
            {
                _logger.LogError("Lambda returned FunctionError for user {UserId}: {Error}", userId, lambdaResponse.FunctionError);
                return (false, null, $"Lambda invocation error: {lambdaResponse.FunctionError}");
            }

            using var reader = new StreamReader(lambdaResponse.Payload);
            var responseJson = await reader.ReadToEndAsync();
            _logger.LogDebug("Lambda response for user {UserId}: {Response}", userId, responseJson);

            var briefingResult = JsonSerializer.Deserialize<LambdaBriefingResult>(responseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (briefingResult?.Success != true || string.IsNullOrWhiteSpace(briefingResult.BriefingContent))
            {
                _logger.LogWarning("Lambda returned success=false or empty content for user {UserId}: {LambdaError}",
                    userId, briefingResult?.Error);
                return (false, null, briefingResult?.Error ?? "Lambda returned success=false or empty briefing content");
            }

            _logger.LogInformation(
                "Lambda generated briefing for user {UserId}: {EmailCount} emails, {EventCount} events, {TaskCount} tasks",
                userId, briefingResult.EmailCount, briefingResult.EventCount, briefingResult.TaskCount);

            // Store in web app DB (canonical store — Lambda may not have Aurora connection in all envs)
            var briefing = await _briefingSvc.StoreBriefingAsync(userId, DateOnly.FromDateTime(DateTime.UtcNow), briefingResult.BriefingContent);
            return (true, briefing, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BriefingGenerationService failed for user {UserId}", userId);
            return (false, null, ex.Message);
        }
    }
}

/// <summary>DTO for deserializing the briefing-builder Lambda response.</summary>
public record LambdaBriefingResult(
    bool Success,
    string? BriefingContent,
    string? Error,
    int EmailCount,
    int EventCount,
    int TaskCount);
