using Microsoft.AspNetCore.SignalR;
using FortressAI.V2.Web.Services;

namespace FortressAI.V2.Web.Components.Hubs;

public class CCProgressHub : Hub
{
    public async Task SendProgress(string userId, CCProgressUpdate update)
    {
        await Clients.User(userId).SendAsync("ReceiveProgress", update);
    }

    // Harness → client: request approval for an external write action
    public async Task SendInterventionRequired(string userId, CCInterventionRequest request)
    {
        await Clients.User(userId).SendAsync("ReceiveInterventionRequired", request);
    }

    // Client → harness: user responded to intervention request
    public async Task RespondToIntervention(string interventionId, bool approved)
    {
        await Clients.Group($"harness-{Context.UserIdentifier}")
            .SendAsync("InterventionResponse", new { interventionId, approved });
    }
}
