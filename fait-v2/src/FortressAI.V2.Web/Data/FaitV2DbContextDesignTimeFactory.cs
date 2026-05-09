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
        var host = Environment.GetEnvironmentVariable("FORTRESS_DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("FORTRESS_DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("FORTRESS_DB_USER") ?? "root";
        var pass = Environment.GetEnvironmentVariable("FORTRESS_DB_PASS") ?? "dev";
        var db   = Environment.GetEnvironmentVariable("FORTRESS_DB_NAME") ?? "fait_dev";

        // Use MySqlConnectionStringBuilder to safely handle special characters in password
        var csb = new MySqlConnector.MySqlConnectionStringBuilder
        {
            Server = host,
            Port = uint.Parse(port),
            Database = db,
            UserID = user,
            Password = pass,
            GuidFormat = MySqlConnector.MySqlGuidFormat.None
        };
        var connStr = csb.ConnectionString;

        var options = new DbContextOptionsBuilder<FaitV2DbContext>()
            .UseMySql(connStr, new MySqlServerVersion(new Version(8, 0, 28)))
            .Options;
        return new FaitV2DbContext(options);
    }
}
