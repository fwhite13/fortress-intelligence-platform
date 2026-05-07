using Microsoft.AspNetCore.SignalR;
using FortressAI.V2.Web.Services;

namespace FortressAI.V2.Web.Components.Hubs;

public class CCProgressHub : Hub
{
    public async Task SendProgress(string userId, CCProgressUpdate update)
    {
        await Clients.User(userId).SendAsync("ReceiveProgress", update);
    }
}
