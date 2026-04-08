using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FortressNexus.Web.Data;

/// <summary>
/// Design-time factory for EF Core migrations (dotnet ef migrations add/list/script).
/// Used only by EF tooling — not at runtime.
/// </summary>
public class NexusDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NexusDbContext>
{
    public NexusDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NexusDbContext>()
            .UseMySql(
                "Server=localhost;Database=nexus;User=root;Password=dev;",
                new MySqlServerVersion(new Version(8, 0, 28)))
            .Options;
        return new NexusDbContext(options);
    }
}
