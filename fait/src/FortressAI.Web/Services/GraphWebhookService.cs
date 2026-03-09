using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;
using FortressAI.Shared.Models;
using System.Security.Cryptography;

namespace FortressAI.Web.Services;

public class GraphWebhookService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly MicrosoftTokenService _tokenService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<GraphWebhookService> _logger;

    public GraphWebhookService(
        IDbContextFactory<AppDbContext> dbFactory,
        MicrosoftTokenService tokenService,
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<GraphWebhookService> logger)
    {
        _dbFactory = dbFactory;
        _tokenService = tokenService;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Creates a Graph webhook subscription for a user's inbox messages.
    /// Uses the REST API directly (no Graph SDK dependency needed).
    /// </summary>
    public async Task<GraphSubscription?> CreateEmailSubscriptionAsync(Guid userId)
    {
        var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
        if (accessToken == null)
        {
            _logger.LogWarning("No valid access token for user {UserId}. Cannot create webhook subscription.", userId);
            return null;
        }

        var webhookUrl = _config["Graph:WebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogError("Graph:WebhookUrl not configured. Cannot create subscription.");
            return null;
        }

        var clientState = GenerateClientState(userId);

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var payload = new
        {
            changeType = "created",
            notificationUrl = webhookUrl,
            resource = "me/mailFolders('Inbox')/messages",
            expirationDateTime = DateTimeOffset.UtcNow.AddDays(3).ToString("o"),
            clientState = clientState,
            latestSupportedTlsVersion = "v1_2"
        };

        var response = await http.PostAsJsonAsync("https://graph.microsoft.com/v1.0/subscriptions", payload);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create Graph subscription for user {UserId}: {StatusCode} {Error}",
                userId, response.StatusCode, error);
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<GraphSubscriptionResponse>();
        if (result == null) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = new GraphSubscription
        {
            UserId = userId,
            SubscriptionId = result.Id,
            ClientState = clientState,
            ExpiresAt = result.ExpirationDateTime.UtcDateTime
        };
        db.GraphSubscriptions.Add(sub);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created Graph email subscription {SubId} for user {UserId}, expires {Expires}",
            result.Id, userId, sub.ExpiresAt);

        return sub;
    }

    /// <summary>
    /// Renews a Graph subscription by extending its expiration by 3 days.
    /// </summary>
    public async Task<bool> RenewSubscriptionAsync(Guid userId, string subscriptionId)
    {
        var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
        if (accessToken == null)
        {
            _logger.LogWarning("No valid token for user {UserId}. Cannot renew subscription {SubId}.", userId, subscriptionId);
            return false;
        }

        var http = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var newExpiry = DateTimeOffset.UtcNow.AddDays(3);
        var payload = new { expirationDateTime = newExpiry.ToString("o") };

        var request = new HttpRequestMessage(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/subscriptions/{subscriptionId}")
        {
            Content = JsonContent.Create(payload)
        };

        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to renew subscription {SubId}: {StatusCode} {Error}",
                subscriptionId, response.StatusCode, error);
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.GraphSubscriptions.FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId);
        if (sub != null)
        {
            sub.ExpiresAt = newExpiry.UtcDateTime;
            await db.SaveChangesAsync();
        }

        _logger.LogInformation("Renewed subscription {SubId}, new expiry {Expires}", subscriptionId, newExpiry);
        return true;
    }

    /// <summary>
    /// Renews all subscriptions expiring within 48 hours.
    /// </summary>
    public async Task RenewExpiringSubscriptionsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(2);

        var expiring = await db.GraphSubscriptions
            .Where(s => s.ExpiresAt < cutoff)
            .ToListAsync();

        _logger.LogInformation("Found {Count} expiring subscriptions to renew", expiring.Count);

        foreach (var sub in expiring)
        {
            try
            {
                await RenewSubscriptionAsync(sub.UserId, sub.SubscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew subscription {SubId} for user {UserId}",
                    sub.SubscriptionId, sub.UserId);
            }
        }
    }

    /// <summary>
    /// Deletes a subscription from Graph and the database.
    /// </summary>
    public async Task DeleteSubscriptionAsync(Guid userId, string subscriptionId)
    {
        var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
        if (accessToken != null)
        {
            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            await http.DeleteAsync($"https://graph.microsoft.com/v1.0/subscriptions/{subscriptionId}");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.GraphSubscriptions.FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId && s.UserId == userId);
        if (sub != null)
        {
            db.GraphSubscriptions.Remove(sub);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Gets the active subscription for a user, if any.
    /// </summary>
    public async Task<GraphSubscription?> GetActiveSubscriptionAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.GraphSubscriptions
            .Where(s => s.UserId == userId && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.ExpiresAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Validates a client state token against the database.
    /// Returns the userId if valid, null otherwise.
    /// </summary>
    public async Task<Guid?> ValidateClientStateAsync(string clientState)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sub = await db.GraphSubscriptions.FirstOrDefaultAsync(s => s.ClientState == clientState);
        return sub?.UserId;
    }

    private static string GenerateClientState(Guid userId)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(16);
        return $"{userId}:{Convert.ToBase64String(randomBytes)}";
    }

    // Response DTO for Graph API
    private class GraphSubscriptionResponse
    {
        public string Id { get; set; } = "";
        public DateTimeOffset ExpirationDateTime { get; set; }
    }
}
