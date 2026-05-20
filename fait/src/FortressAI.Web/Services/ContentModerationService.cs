using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FortressAI.Web.Services;

public record ModerationResult(bool Passed, string? Reason);

public class ContentModerationService
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly IAmazonRekognition _rekognitionClient;
    private readonly ILogger<ContentModerationService> _logger;

    private readonly string _haikuModelId;
    private const float ConfidenceThreshold = 70f;

    public ContentModerationService(
        IAmazonBedrockRuntime bedrockClient,
        IAmazonRekognition rekognitionClient,
        IConfiguration configuration,
        ILogger<ContentModerationService> logger)
    {
        _bedrockClient = bedrockClient;
        _rekognitionClient = rekognitionClient;
        _logger = logger;
        _haikuModelId = configuration["Bedrock:ModerationModelId"]
            ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0";
    }

    public async Task<ModerationResult> CheckNameAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new ModerationResult(true, null);

        if (text.Length > 100) text = text[..100];

        var prompt = $"""
            You are a content moderation assistant. Evaluate whether the following text is appropriate as a display name in a professional workplace application. Flag it if it contains profanity, slurs, sexual content, hate speech, threats, or any attempt to bypass moderation (substitutions, symbols, spacing tricks). Respond with only "PASS" or "FAIL: [brief reason]".

            Text: {text}
            """;

        try
        {
            var request = new InvokeModelRequest
            {
                ModelId = _haikuModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new
                    {
                        anthropic_version = "bedrock-2023-05-31",
                        max_tokens = 64,
                        temperature = 0.0,
                        messages = new[] { new { role = "user", content = prompt } }
                    })
                ))
            };
            var response = await _bedrockClient.InvokeModelAsync(request);
            var responseBody = await new StreamReader(response.Body).ReadToEndAsync();

            using var doc = JsonDocument.Parse(responseBody);
            var responseText = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()?.Trim() ?? "";

            if (responseText.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
                return new ModerationResult(true, null);

            if (responseText.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = responseText.IndexOf(':');
                var reason = colonIndex >= 0 ? responseText[(colonIndex + 1)..].Trim() : responseText;
                return new ModerationResult(false, reason);
            }

            _logger.LogWarning("[ContentModerationService] CheckNameAsync unexpected response format — fail-open");
            return new ModerationResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ContentModerationService] CheckNameAsync failed — fail-open");
            return new ModerationResult(true, null);
        }
    }

    public async Task<ModerationResult> CheckImageAsync(Stream stream, string contentType)
    {
        try
        {
            var imageBytes = await ReadStreamAsync(stream);

            var request = new DetectModerationLabelsRequest
            {
                Image = new Amazon.Rekognition.Model.Image
                {
                    Bytes = new MemoryStream(imageBytes)
                },
                MinConfidence = ConfidenceThreshold
            };
            var response = await _rekognitionClient.DetectModerationLabelsAsync(request);

            if (response.ModerationLabels.Any())
            {
                var topLabel = response.ModerationLabels.OrderByDescending(l => l.Confidence).First().Name;
                return new ModerationResult(false, $"Image flagged: {topLabel}");
            }

            return new ModerationResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ContentModerationService] CheckImageAsync failed — fail-open");
            return new ModerationResult(true, null);
        }
    }

    private const int MaxImageBytes = 5 * 1024 * 1024;

    private static async Task<byte[]> ReadStreamAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        if (ms.Length > MaxImageBytes)
            throw new InvalidOperationException($"Image exceeds {MaxImageBytes}-byte Rekognition limit.");
        return ms.ToArray();
    }
}
