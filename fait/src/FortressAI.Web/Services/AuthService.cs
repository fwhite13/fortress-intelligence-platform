using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class AuthService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IDbContextFactory<AppDbContext> contextFactory, ILogger<AuthService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error, AppUser? User)> RegisterAsync(string email, string password, string? displayName)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower().Trim());

        if (existingUser == null)
        {
            // No pre-invite record — reject self-signup
            return (false, "Registration is by invitation only. Contact your administrator.", null);
        }

        if (!string.IsNullOrEmpty(existingUser.PasswordHash))
        {
            // Already registered
            return (false, "An account with this email already exists.", null);
        }
        // Entra/SSO users — registration not needed
        if (existingUser.IsEntraUser)
            return (false, "This account uses Microsoft SSO. Please sign in with Microsoft.", null);

        // Pre-invited user completing registration — set password and display name
        existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        if (!string.IsNullOrWhiteSpace(displayName))
            existingUser.DisplayName = displayName.Trim();
        // NOTE: AppUser does NOT have an UpdatedAt property, so do NOT add that line

        await db.SaveChangesAsync();

        _logger.LogInformation("Invited user completed registration: {Email} (Role: {Role})", existingUser.Email, existingUser.Role);
        return (true, null, existingUser);
    }

    public async Task<(bool Success, string? Error, AppUser? User)> LoginAsync(string email, string password)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower().Trim());

        if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return (false, "Invalid email or password.", null);

        user.LastLogin = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return (true, null, user);
    }

    public async Task<AppUser?> GetUserAsync(Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Users.FindAsync(userId);
    }

    public async Task<List<AppUser>> GetAllUsersAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Users.OrderBy(u => u.Email).ToListAsync();
    }

    public async Task<bool> DeleteUserAsync(Guid userId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user == null) return false;

        // Explicit cleanup — conversations not cascade-configured for user FK
        var conversations = await db.Conversations
            .Where(c => c.UserId == userId)
            .ToListAsync();
        db.Conversations.RemoveRange(conversations);

        // UserMcpTokens, UserModulePermissions have OnDelete(Cascade) — handled by EF
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return true;
    }
}
