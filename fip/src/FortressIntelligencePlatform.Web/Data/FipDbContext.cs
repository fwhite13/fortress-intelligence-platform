using Microsoft.EntityFrameworkCore;

namespace FortressIntelligencePlatform.Web.Data;

public class FipDbContext : DbContext
{
    public FipDbContext(DbContextOptions<FipDbContext> options) : base(options) { }
    public DbSet<FipUserMicrosoftToken> UserMicrosoftTokens => Set<FipUserMicrosoftToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FipUserMicrosoftToken>(entity =>
        {
            entity.ToTable("user_microsoft_tokens");
            entity.HasKey(e => e.EntraOid);
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(128);
            entity.Property(e => e.AccessToken).HasColumnName("access_token").IsRequired();
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.MicrosoftEmail).HasColumnName("microsoft_email").HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
        });
    }
}

public class FipUserMicrosoftToken
{
    public string EntraOid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string? MicrosoftEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
