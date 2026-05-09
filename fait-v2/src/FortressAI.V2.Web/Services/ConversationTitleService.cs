using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using FortressAI.V2.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FortressAI.V2.Web.Services;

public interface IConversationTitleService
{
    Task<string?> GenerateTitleAsync(string userMessage, string assistantResponse, CancellationToken ct = default);
    Task CreateTaskAndUpdateTitleAsync(string userId, string taskId, string userMessage, string assistantResponse);
}

public class ConversationTitleService : IConversationTitleService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ConversationTitleService> _logger;

    private string TitleModelId => _config.GetValue<string>("Bedrock:TitleModelId", "us.anthropic.claude-haiku-4-5-20251001-v1:0")!;

    public ConversationTitleService(IDbContextFactory<FaitV2DbContext> dbFactory, IConfiguration config, ILogger<ConversationTitleService> logger)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<string?> GenerateTitleAsync(string userMessage, string assistantResponse, CancellationToken ct = default)
    {
        try
        {
            var prompt = $"""
                Generate a short, descriptive title (3-6 words) for this conversation based on the first exchange.
                Do not use quotes. Do not use punctuation at the end. Just the title, nothing else.
                User: {userMessage[..Math.Min(300, userMessage.Length)]}
                Assistant: {assistantResponse[..Math.Min(300, assistantResponse.Length)]}
                Title:
                """;

            var bedrockClient = new AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.USEast1);
            var body = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 20,
                temperature = 0.3,
                messages = new[] { new { role = "user", content = prompt } }
            });

            var response = await bedrockClient.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = TitleModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body))
            }, ct);

            using var reader = new StreamReader(response.Body);
            var json = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var title = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim().TrimEnd('.', '!', '?');

            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Title generation failed — keeping placeholder");
            return null;
        }
    }

    public async Task CreateTaskAndUpdateTitleAsync(string userId, string taskId, string userMessage, string assistantResponse)
    {
        var title = await GenerateTitleAsync(userMessage, assistantResponse);
        if (string.IsNullOrWhiteSpace(title)) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);
            var task = await db.ConversationTasks.FirstOrDefaultAsync(t => t.Id == taskId, cts.Token);
            if (task != null)
            {
                task.Title = title;
                task.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update task title for taskId={TaskId}", taskId);
        }
    }
}
