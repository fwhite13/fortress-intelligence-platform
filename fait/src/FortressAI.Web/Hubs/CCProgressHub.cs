using Microsoft.AspNetCore.SignalR;

namespace FortressAI.Web.Hubs;

/// <summary>
/// SignalR hub for real-time Claude Code task progress updates (Feature 2.4).
/// Clients join a user-specific group on connect; harness-side progress events
/// are pushed via IHubContext&lt;CCProgressHub&gt; from the internal API endpoint.
/// </summary>
public class CCProgressHub : Hub
{
    public async Task JoinUserGroup(string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cc-user-{userId}");
    }

    public async Task LeaveUserGroup(string userId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cc-user-{userId}");
    }
}
