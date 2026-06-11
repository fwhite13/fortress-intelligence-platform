using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

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
        var callerId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (callerId != userId)
            throw new HubException("Cannot join another user's group.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"cc-user-{userId}");
    }

    public async Task LeaveUserGroup(string userId)
    {
        var callerId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (callerId != userId)
            throw new HubException("Cannot leave another user's group.");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"cc-user-{userId}");
    }

    public static Task BroadcastProgressAsync(
        IHubContext<CCProgressHub> hubContext,
        string userId,
        object progressEvent)
        => hubContext.Clients.Group($"cc-user-{userId}").SendAsync("OnProgressEvent", progressEvent);
}
