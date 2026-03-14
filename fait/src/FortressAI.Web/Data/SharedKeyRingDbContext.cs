using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace FortressAI.Web.Data;

/// <summary>
/// Minimal DbContext for reading the shared FIP data protection key ring.
/// Points to fred_dev via FIP_KEYRING_DB_NAME env var — DataProtectionKeys table only.
/// Registered as a standard scoped DbContext (not a factory) so that
/// PersistKeysToDbContext&lt;SharedKeyRingDbContext&gt;() can resolve it from DI.
/// </summary>
public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
