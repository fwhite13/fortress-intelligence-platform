using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace FortressAI.V2.Web.Data;

/// <summary>
/// Minimal DbContext for reading the shared FIP data protection key ring.
/// Points to fred_dev (FIP portal's database) — DataProtectionKeys table only.
/// fait-v2 must read from the same key ring as fip.fortressam.ai to decrypt the shared auth cookie.
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
