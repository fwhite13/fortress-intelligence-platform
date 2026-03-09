using System.Security.Claims;
using FortressAI.Web.Data;
using FortressAI.Web.Services;
using FortressAI.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Middleware;

/// <summary>
/// DEV ONLY: Ensures the stub-authenticated user exists in the database and
/// initializes the UserSessionService. Semaphore-protected against race conditions
/// on concurrent requests during startup.
/// </summary>
public class StubAuthUserInitializationMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public StubAuthUserInitializationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IDbContextFactory<AppDbContext> dbFactory, UserSessionService session)
    {
        if (context.User.Identity?.IsAuthenticated == true && !session.IsAuthenticated)
        {
            await _semaphore.WaitAsync();
            try
            {
                // Re-check after acquiring semaphore (double-checked locking)
                if (!session.IsAuthenticated)
                {
                    var email = context.User.FindFirst(ClaimTypes.Email)?.Value ?? "fred@fortressam.ai";
                    var name = context.User.FindFirst(ClaimTypes.Name)?.Value ?? "Fred White";

                    await using var db = await dbFactory.CreateDbContextAsync();
                    var appUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

                    if (appUser == null)
                    {
                        appUser = new AppUser
                        {
                            Email = email,
                            DisplayName = name,
                            Role = "admin",
                            PasswordHash = "",
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Users.Add(appUser);
                        await db.SaveChangesAsync();
                    }

                    appUser.LastLogin = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    session.SetUser(appUser);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        await _next(context);
    }
}
