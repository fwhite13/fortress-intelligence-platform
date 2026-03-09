namespace FortressAI.Shared.Models;

public class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BedrockModelId { get; set; } = string.Empty;

    public static readonly List<ModelInfo> AvailableModels = new()
    {
        new ModelInfo
        {
            Id = "claude-sonnet-4-6",
            DisplayName = "Claude Sonnet 4.6",
            Description = "Best balance of speed and capability",
            BedrockModelId = "us.anthropic.claude-sonnet-4-6"
        },
        new ModelInfo
        {
            Id = "claude-opus-4-6",
            DisplayName = "Claude Opus 4.6",
            Description = "Most capable, best for complex tasks",
            BedrockModelId = "us.anthropic.claude-opus-4-6-v1"
        },
        new ModelInfo
        {
            Id = "claude-haiku-4-5",
            DisplayName = "Claude Haiku 4.5",
            Description = "Fastest and most affordable",
            BedrockModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0"
        }
    };

    public static ModelInfo GetModel(string id) =>
        AvailableModels.FirstOrDefault(m => m.Id == id) ?? AvailableModels[0];
}
