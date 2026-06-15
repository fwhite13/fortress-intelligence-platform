namespace FortressAI.Web.Services;

/// <summary>
/// Scoped service populated by ChatView to expose current chat context
/// for the Dev Info dialog. Values are null when not in a chat session.
/// </summary>
public class DevContextService
{
    public Guid? ConversationId { get; set; }
    public string? HarnessSessionId { get; set; }
}
