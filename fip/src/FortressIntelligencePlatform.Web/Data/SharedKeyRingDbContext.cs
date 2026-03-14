using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligencePlatform.Web.Data;

/// <summary>
/// Key ring store for the entire FIP suite.
/// FIP portal GENERATES and rotates keys here (in fred_dev.DataProtectionKeys).
/// FAIT, FIRM, and FORMS consume keys read-only with DisableAutomaticKeyGeneration().
/// </summary>
public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options)
        : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
