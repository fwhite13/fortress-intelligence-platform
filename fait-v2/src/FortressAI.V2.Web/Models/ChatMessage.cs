namespace FortressAI.V2.Web.Models;

public record ChatMessage(string Role, string Content, DateTimeOffset? Timestamp = null);
