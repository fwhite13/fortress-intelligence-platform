using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CoworkWeb.Data;

/// <summary>
/// Minimal DbContext for DataProtection key ring persistence.
/// Connects to the shared FIP key ring database — same DB used by FAIT, FIRM, FORMS.
/// CoworkWeb reads keys only (DisableAutomaticKeyGeneration — FIP portal owns key creation).
/// </summary>
public sealed class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options)
        : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
