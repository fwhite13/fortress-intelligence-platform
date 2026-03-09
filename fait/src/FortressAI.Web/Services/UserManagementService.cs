using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public interface IUserManagementService
{
    Task<List<UserWithPermissions>> GetAllUsersAsync();
    Task<UserWithPermissions?> GetUserAsync(Guid userId);
    Task SetSystemRoleAsync(Guid userId, string role, Guid grantedByUserId);
    Task SetModulePermissionAsync(Guid userId, string module, string permission, bool granted, Guid grantedByUserId);
    Task<List<UserModulePermission>> GetUserPermissionsAsync(Guid userId);
    Task InviteUserAsync(string email, string? displayName, Guid invitedByUserId);
    Task DisableUserAsync(Guid userId, Guid requestedByUserId);
    Task EnableUserAsync(Guid userId);
    Task<bool> DeleteUserAsync(Guid userId, Guid requestedByUserId);
}

public class UserWithPermissions
{
    public AppUser User { get; set; } = null!;
    public List<UserModulePermission> Permissions { get; set; } = new();
    public bool CognitoEnabled { get; set; } = true;
}

public class UserManagementService : IUserManagementService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonCognitoIdentityProvider _cognito;
    private readonly IConfiguration _config;
    private readonly ILogger<UserManagementService> _logger;

    private string UserPoolId => _config["Auth:CognitoUserPoolId"] ?? string.Empty;

    public UserManagementService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonCognitoIdentityProvider cognito,
        IConfiguration config,
        ILogger<UserManagementService> logger)
    {
        _dbFactory = dbFactory;
        _cognito = cognito;
        _config = config;
        _logger = logger;
    }

    public async Task<List<UserWithPermissions>> GetAllUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.Users.ToListAsync();
        var permsByUser = await db.UserModulePermissions.ToListAsync();
        var permLookup = permsByUser.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.ToList());

        // Try to get Cognito status — non-fatal
        Dictionary<string, bool> cognitoStatus = new();
        if (!string.IsNullOrEmpty(UserPoolId))
        {
            try
            {
                var req = new ListUsersRequest { UserPoolId = UserPoolId, Limit = 60 };
                var resp = await _cognito.ListUsersAsync(req);
                foreach (var u in resp.Users)
                {
                    var emailAttr = u.Attributes.FirstOrDefault(a => a.Name == "email")?.Value;
                    if (!string.IsNullOrEmpty(emailAttr))
                        cognitoStatus[emailAttr.ToLower()] = u.Enabled;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not retrieve Cognito user status (non-fatal): {Message}", ex.Message);
            }
        }

        return users.Select(u => new UserWithPermissions
        {
            User = u,
            Permissions = permLookup.TryGetValue(u.Id, out var perms) ? perms : new(),
            CognitoEnabled = cognitoStatus.TryGetValue(u.Email.ToLower(), out var enabled) ? enabled : true
        }).ToList();
    }

    public async Task<UserWithPermissions?> GetUserAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return null;
        var perms = await db.UserModulePermissions.Where(p => p.UserId == userId).ToListAsync();
        return new UserWithPermissions { User = user, Permissions = perms };
    }

    public async Task SetSystemRoleAsync(Guid userId, string role, Guid grantedByUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        // Verify the caller is actually an admin
        var caller = await db.Users.FindAsync(grantedByUserId);
        if (caller == null || caller.Role != "admin")
            throw new UnauthorizedAccessException("Only admins can change system roles.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found");
        user.Role = role;
        await db.SaveChangesAsync();

        // Update Cognito group — non-fatal
        if (!string.IsNullOrEmpty(UserPoolId))
        {
            try
            {
                // Remove from both groups first
                foreach (var g in new[] { "FAIT-Admins", "FIP-Users" })
                {
                    try
                    {
                        await _cognito.AdminRemoveUserFromGroupAsync(new AdminRemoveUserFromGroupRequest
                        {
                            UserPoolId = UserPoolId,
                            Username = user.Email,
                            GroupName = g
                        });
                    }
                    catch { /* ignore — user may not be in group */ }
                }

                var targetGroup = role == "admin" ? "FAIT-Admins" : "FIP-Users";
                await _cognito.AdminAddUserToGroupAsync(new AdminAddUserToGroupRequest
                {
                    UserPoolId = UserPoolId,
                    Username = user.Email,
                    GroupName = targetGroup
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not update Cognito group (non-fatal): {Message}", ex.Message);
            }
        }
    }

    public async Task SetModulePermissionAsync(Guid userId, string module, string permission, bool granted, Guid grantedByUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserModulePermissions
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Module == module && p.Permission == permission);

        if (existing != null)
        {
            existing.Granted = granted;
            existing.GrantedAt = DateTime.UtcNow;
            existing.GrantedByUserId = grantedByUserId;
        }
        else
        {
            db.UserModulePermissions.Add(new UserModulePermission
            {
                UserId = userId,
                Module = module,
                Permission = permission,
                Granted = granted,
                GrantedAt = DateTime.UtcNow,
                GrantedByUserId = grantedByUserId
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<UserModulePermission>> GetUserPermissionsAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserModulePermissions.Where(p => p.UserId == userId).ToListAsync();
    }

    public async Task InviteUserAsync(string email, string? displayName, Guid invitedByUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Check existing user by email (no nav props in Where)
        var existingIds = await db.Users.Where(u => u.Email == email.ToLower().Trim()).Select(u => u.Id).ToListAsync();
        bool userExistsInAurora = existingIds.Any();

        if (!userExistsInAurora)
        {
            var newUser = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email.ToLower().Trim(),
                PasswordHash = string.Empty,
                DisplayName = !string.IsNullOrWhiteSpace(displayName) ? displayName : email.Split('@')[0],
                Role = "user",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            db.Users.Add(newUser);
            await db.SaveChangesAsync();
        }

        // Cognito invite — non-fatal if pool not configured
        if (!string.IsNullOrEmpty(UserPoolId))
        {
            if (userExistsInAurora)
            {
                // User exists in Aurora: try RESEND; if UserNotFoundException, they were never in Cognito — create fresh
                try
                {
                    await _cognito.AdminCreateUserAsync(new AdminCreateUserRequest
                    {
                        UserPoolId = UserPoolId,
                        Username = email.ToLower().Trim(),
                        MessageAction = MessageActionType.RESEND
                    });
                    _logger.LogInformation("Cognito invite resent for {Email}", email);
                }
                catch (Amazon.CognitoIdentityProvider.Model.UserNotFoundException)
                {
                    // User exists in Aurora but not Cognito — create them fresh
                    await _cognito.AdminCreateUserAsync(new AdminCreateUserRequest
                    {
                        UserPoolId = UserPoolId,
                        Username = email.ToLower().Trim(),
                        TemporaryPassword = GenerateTemporaryPassword(),
                        UserAttributes = new List<AttributeType>
                        {
                            new() { Name = "email", Value = email.ToLower().Trim() },
                            new() { Name = "email_verified", Value = "true" }
                        }
                    });
                    _logger.LogInformation("Cognito user created fresh for {Email}", email);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Cognito invite failed for {Email}", email); }
            }
            else
            {
                // New user — Cognito sends its own welcome email
                try
                {
                    await _cognito.AdminCreateUserAsync(new AdminCreateUserRequest
                    {
                        UserPoolId = UserPoolId,
                        Username = email.ToLower().Trim(),
                        UserAttributes = new List<AttributeType>
                        {
                            new() { Name = "email", Value = email.ToLower().Trim() },
                            new() { Name = "email_verified", Value = "true" }
                        },
                        DesiredDeliveryMediums = new List<string> { "EMAIL" }
                    });
                    _logger.LogInformation("Cognito invite sent for {Email}", email);
                }
                catch (UsernameExistsException)
                {
                    // Already in Cognito — resend invite
                    try
                    {
                        await _cognito.AdminCreateUserAsync(new AdminCreateUserRequest
                        {
                            UserPoolId = UserPoolId,
                            Username = email.ToLower().Trim(),
                            MessageAction = MessageActionType.RESEND
                        });
                    }
                    catch (Exception ex2) { _logger.LogWarning("Cognito resend failed (non-fatal): {Message}", ex2.Message); }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Cognito invite failed (non-fatal): {Message}", ex.Message);
                }
            }
        }
    }

    public async Task DisableUserAsync(Guid userId, Guid requestedByUserId)
    {
        if (userId == requestedByUserId)
            throw new InvalidOperationException("You cannot disable your own account.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found");

        if (user.Role == "admin")
        {
            var requester = await db.Users.FindAsync(requestedByUserId);
            if (requester?.Role != "admin")
                throw new UnauthorizedAccessException("Only admins can disable admin accounts.");
        }

        user.IsActive = false;
        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(UserPoolId))
        {
            try
            {
                await _cognito.AdminDisableUserAsync(new AdminDisableUserRequest
                {
                    UserPoolId = UserPoolId,
                    Username = user.Email
                });
            }
            catch (Exception ex) { _logger.LogWarning("Cognito disable failed (non-fatal): {Message}", ex.Message); }
        }
    }

    public async Task EnableUserAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new InvalidOperationException("User not found");
        user.IsActive = true;
        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(UserPoolId))
        {
            try
            {
                await _cognito.AdminEnableUserAsync(new AdminEnableUserRequest
                {
                    UserPoolId = UserPoolId,
                    Username = user.Email
                });
            }
            catch (Exception ex) { _logger.LogWarning("Cognito enable failed (non-fatal): {Message}", ex.Message); }
        }
    }

    public async Task<bool> DeleteUserAsync(Guid userId, Guid requestedByUserId)
    {
        if (userId == requestedByUserId)
            throw new InvalidOperationException("You cannot delete your own account.");

        await using var db = await _dbFactory.CreateDbContextAsync();
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

    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*";
        const string all = upper + lower + digits + special;
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);
        var chars = bytes.Select((b, i) => i switch
        {
            0 => upper[b % upper.Length],
            1 => lower[b % lower.Length],
            2 => digits[b % digits.Length],
            3 => special[b % special.Length],
            _ => all[b % all.Length]
        }).ToArray();
        // Shuffle
        rng.GetBytes(bytes);
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = bytes[i] % (i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
