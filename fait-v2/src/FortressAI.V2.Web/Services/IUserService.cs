using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public interface IUserService
{
    Task<User?> GetByEntraOidAsync(string entraOid, CancellationToken ct = default);
}

public class UserService : IUserService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;

    public UserService(IDbContextFactory<FaitV2DbContext> dbFactory) => _dbFactory = dbFactory;

    public async Task<User?> GetByEntraOidAsync(string entraOid, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await using var db = await _dbFactory.CreateDbContextAsync(cts.Token);
        return await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid, cts.Token);
    }
}
