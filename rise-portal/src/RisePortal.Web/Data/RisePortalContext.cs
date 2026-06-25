using Microsoft.EntityFrameworkCore;
using RisePortal.Web.Data.Entities;

namespace RisePortal.Web.Data;

public class RisePortalContext : DbContext
{
    public RisePortalContext(DbContextOptions<RisePortalContext> options) : base(options) { }

    public DbSet<RiseUser> RiseUsers { get; set; } = null!;
    public DbSet<RiseAppCard> RiseAppCards { get; set; } = null!;
    public DbSet<RiseAppCardAccess> RiseAppCardAccesses { get; set; } = null!;
    public DbSet<RiseAdminUser> RiseAdminUsers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RiseAppCardAccess>()
            .HasOne(a => a.AppCard)
            .WithMany(c => c.AccessList)
            .HasForeignKey(a => a.AppCardId);
    }
}
