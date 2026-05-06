using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FortressAI.V2.Web.Data;

/// <summary>
/// Design-time factory for EF Core migrations (dotnet ef migrations add/list/script).
/// Used only by EF tooling — not at runtime.
/// </summary>
public class FaitV2DbContextDesignTimeFactory : IDesignTimeDbContextFactory<FaitV2DbContext>
{
    public FaitV2DbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FaitV2DbContext>()
            .UseMySql(
                "Server=localhost;Database=fait_v2_dev;User=root;Password=dev;GuidFormat=None;",
                new MySqlServerVersion(new Version(8, 0, 28)))
            .Options;
        return new FaitV2DbContext(options);
    }
}
