using Microsoft.AspNetCore.Authorization;

namespace FortressAI.Web.Auth;

/// <summary>
/// Authorization requirement: request must have been authenticated via AppKeyAuth scheme.
/// Prevents cookie-authenticated browser sessions from accessing API-key-only endpoints.
/// </summary>
public class AppKeyRequirement : IAuthorizationRequirement { }

public class AppKeyAuthorizationHandler : AuthorizationHandler<AppKeyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppKeyRequirement requirement)
    {
        // Only succeed if the identity was authenticated by AppKeyAuth scheme
        var isApiKeyAuth = context.User.Identities
            .Any(i => i.AuthenticationType == "AppKeyAuth" && i.IsAuthenticated);

        if (isApiKeyAuth)
            context.Succeed(requirement);
        // else: do not call Succeed or Fail — let other handlers run (there are none, so it fails)

        return Task.CompletedTask;
    }
}
