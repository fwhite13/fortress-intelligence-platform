namespace FortressNexus.Web.Services.Discovery;

/// <summary>
/// Bedrock inference configuration for Discovery question generation.
/// Loaded from appsettings.json Bedrock:Discovery* keys.
/// </summary>
public class DiscoveryInferenceConfig
{
    public string ModelId { get; set; } = "us.anthropic.claude-3-5-sonnet-20241022-v2:0";
    public int MaxTokens { get; set; } = 4096;
    public float Temperature { get; set; } = 0.3f;
}
