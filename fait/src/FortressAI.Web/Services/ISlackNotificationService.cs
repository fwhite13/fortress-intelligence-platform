namespace FortressAI.Web.Services;

public interface ISlackNotificationService
{
    /// <summary>
    /// Sends a DM to the Slack user whose Slack account email matches <paramref name="userEmail"/>.
    /// Best-effort — implementations must not throw.
    /// </summary>
    Task SendDmAsync(string userEmail, string message);
}
