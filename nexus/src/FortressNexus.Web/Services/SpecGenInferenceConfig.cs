namespace FortressNexus.Web.Services;

/// <summary>
/// Bedrock inference configuration for spec generation (text + vision).
/// Bound to Bedrock:SpecGen section in configuration.
/// </summary>
public class SpecGenInferenceConfig
{
    public string ModelId { get; set; } = "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
    public string VisionModelId { get; set; } = "us.anthropic.claude-sonnet-4-5-20250929-v1:0";
    public int MaxTokens { get; set; } = 8192;
    public int VisionMaxTokens { get; set; } = 8192;
    public int TimeoutSeconds { get; set; } = 120;
}
