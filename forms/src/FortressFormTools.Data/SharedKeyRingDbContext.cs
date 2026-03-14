using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace FortressFormTools.Data;

/// <summary>
/// Minimal DbContext for reading the shared FIP data protection key ring.
/// Points to fred_dev (FAIT's database) — DataProtectionKeys table only.
/// EF convention maps DbSet name "DataProtectionKeys" → table "DataProtectionKeys" automatically.
/// </summary>
public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
