using System.Text.Json;

namespace FortressAI.Shared.Models;

public class McpToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public JsonElement InputSchema { get; set; }
}
