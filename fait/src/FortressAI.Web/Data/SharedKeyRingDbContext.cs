using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace FortressAI.Web.Data;

/// <summary>
/// Minimal DbContext for reading the shared FIP data protection key ring.
/// Points to fred_dev (FAIT's database) — DataProtectionKeys table only.
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
