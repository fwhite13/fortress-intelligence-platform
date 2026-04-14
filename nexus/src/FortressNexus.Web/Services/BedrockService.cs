using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace FortressNexus.Web.Services;

/// <summary>
/// Wraps AWS Bedrock Runtime for non-streaming AI text generation.
/// Replicates FAIT BedrockService.InvokeClaudeAsync pattern.
/// </summary>
public class BedrockService : IDisposable
{
    private readonly AmazonBedrockRuntimeClient _client;
    private readonly ILogger<BedrockService> _logger;

    public BedrockService(ILogger<BedrockService> logger)
    {
        _logger = logger;
        _client = new AmazonBedrockRuntimeClient(new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1
        });
    }

    /// <summary>
    /// Invokes Claude and returns the full text response.
    /// Returns (text, promptTokens, completionTokens).
    /// </summary>
    public async Task<(string Text, int PromptTokens, int CompletionTokens)> InvokeAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        string modelId)
    {
        var model = modelId;

        var requestObj = new JsonObject
        {
            ["anthropic_version"] = "bedrock-2023-05-31",
            ["max_tokens"] = maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userPrompt
                }
            }
        };

        var json = requestObj.ToJsonString();
        var request = new InvokeModelRequest
        {
            ModelId = model,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        _logger.LogInformation("[BEDROCK] Invoking model {Model}, maxTokens={MaxTokens}", model, maxTokens);

        var response = await _client.InvokeModelAsync(request);
        var responseJson = await new StreamReader(response.Body).ReadToEndAsync();

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

        int promptTokens = 0;
        int completionTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it)) promptTokens = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) completionTokens = ot.GetInt32();
        }

        _logger.LogInformation("[BEDROCK] Response: {PromptTokens} prompt + {CompletionTokens} completion tokens",
            promptTokens, completionTokens);

        return (text, promptTokens, completionTokens);
    }

    /// <summary>
    /// Invokes Claude with an image (vision) — passes image bytes as base64 in the user message.
    /// Falls back to text-only if image bytes are null/empty (logs warning, does not crash).
    /// </summary>
    public async Task<(string Text, int PromptTokens, int CompletionTokens)> InvokeWithImageAsync(
        string systemPrompt,
        string userPrompt,
        byte[]? imageBytes,
        string mimeType,
        int maxTokens,
        string modelId)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            _logger.LogWarning("[BEDROCK] InvokeWithImageAsync called but imageBytes is empty — falling back to text-only");
            return await InvokeAsync(systemPrompt, userPrompt, maxTokens, modelId);
        }

        var model = modelId;
        var base64Image = Convert.ToBase64String(imageBytes);

        var contentArray = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject
                {
                    ["type"] = "base64",
                    ["media_type"] = mimeType,
                    ["data"] = base64Image
                }
            },
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = userPrompt
            }
        };

        var requestObj = new JsonObject
        {
            ["anthropic_version"] = "bedrock-2023-05-31",
            ["max_tokens"] = maxTokens,
            ["system"] = systemPrompt,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = contentArray
                }
            }
        };

        var json = requestObj.ToJsonString();
        var request = new InvokeModelRequest
        {
            ModelId = model,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        _logger.LogInformation("[BEDROCK] Invoking model {Model} with image ({MimeType}, {Bytes} bytes), maxTokens={MaxTokens}",
            model, mimeType, imageBytes.Length, maxTokens);

        var response = await _client.InvokeModelAsync(request);
        var responseJson = await new StreamReader(response.Body).ReadToEndAsync();

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var text = root.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

        int promptTokens = 0;
        int completionTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var it)) promptTokens = it.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var ot)) completionTokens = ot.GetInt32();
        }

        return (text, promptTokens, completionTokens);
    }

    public void Dispose() => _client.Dispose();
}
