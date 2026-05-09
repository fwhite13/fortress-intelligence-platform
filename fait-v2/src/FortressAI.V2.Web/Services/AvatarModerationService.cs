using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace FortressAI.V2.Web.Services;

public interface IAvatarModerationService
{
    Task<AvatarModerationResult> CheckImageAsync(Stream imageStream, string contentType, CancellationToken ct = default);
}

public record AvatarModerationResult(bool IsAllowed, string? Reason = null);

public class AvatarModerationService : IAvatarModerationService
{
    private const string ModerationModel = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly ILogger<AvatarModerationService> _logger;

    public AvatarModerationService(IAmazonBedrockRuntime bedrock, ILogger<AvatarModerationService> logger)
    {
        _bedrock = bedrock;
        _logger = logger;
    }

    public async Task<AvatarModerationResult> CheckImageAsync(Stream imageStream, string contentType, CancellationToken ct = default)
    {
        try
        {
            // Read image bytes
            using var ms = new MemoryStream();
            await imageStream.CopyToAsync(ms, ct);
            var imageBytes = ms.ToArray();
            var base64Image = Convert.ToBase64String(imageBytes);

            // Map content type to Bedrock image format
            var mediaType = contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => "image/jpeg",
                "image/jpg"  => "image/jpeg",
                "image/png"  => "image/png",
                "image/gif"  => "image/gif",
                "image/webp" => "image/webp",
                _            => "image/jpeg"
            };

            var body = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 100,
                system = "You are a content moderation system. Respond with only 'SAFE' or 'UNSAFE: {reason}' for the following image.",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = mediaType,
                                    data = base64Image
                                }
                            },
                            new
                            {
                                type = "text",
                                text = "Is this image safe for use as a profile avatar in a professional business application?"
                            }
                        }
                    }
                }
            });

            var response = await _bedrock.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = ModerationModel,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body))
            }, ct);

            using var reader = new StreamReader(response.Body);
            var json = await reader.ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim() ?? string.Empty;

            if (text.StartsWith("UNSAFE", StringComparison.OrdinalIgnoreCase))
            {
                var reason = text.Length > 7 ? text[7..].TrimStart(':', ' ') : "Content not appropriate for a profile avatar";
                return new AvatarModerationResult(false, reason);
            }

            return new AvatarModerationResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Avatar moderation check failed — failing open to allow upload");
            return new AvatarModerationResult(true); // fail open
        }
    }
}
