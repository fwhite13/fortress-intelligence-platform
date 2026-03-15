namespace FortressAI.Web.Models;

public class TestSessionRequest
{
    public string Secret { get; set; } = string.Empty;
    public string UserId { get; set; } = "test-user@fortressam.ai";
    public string DisplayName { get; set; } = "Natasha Romanoff (Test)";
}
