using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Data;

/// <summary>
/// DbContext for accessing shared FAIT tables in fait_dev.
/// Used by FIRM to read/refresh user_microsoft_tokens stored by FAIT's OAuth consent flow.
/// </summary>
public class FaitSharedDbContext : DbContext
{
    public FaitSharedDbContext(DbContextOptions<FaitSharedDbContext> options) : base(options) { }

    public DbSet<UserMicrosoftToken> UserMicrosoftTokens => Set<UserMicrosoftToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserMicrosoftToken>(entity =>
        {
            entity.ToTable("user_microsoft_tokens");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("UserId");
            entity.Property(e => e.AccessToken).HasColumnName("AccessToken");
            entity.Property(e => e.RefreshToken).HasColumnName("RefreshToken");
            entity.Property(e => e.ExpiresAt).HasColumnName("ExpiresAt");
            entity.Property(e => e.MicrosoftEmail).HasColumnName("MicrosoftEmail");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt");
        });
    }
}
