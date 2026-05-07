using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Data;

/// <summary>
/// Read-only access to the FIP portal's user_microsoft_tokens table (fip_dev).
/// Used by FipTokenProvider to retrieve delegated Entra tokens written at login.
/// </summary>
public class FipPortalDbContext : DbContext
{
    public FipPortalDbContext(DbContextOptions<FipPortalDbContext> options) : base(options) { }

    public DbSet<FipPortalUserMicrosoftToken> UserMicrosoftTokens => Set<FipPortalUserMicrosoftToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FipPortalUserMicrosoftToken>(entity =>
        {
            entity.ToTable("user_microsoft_tokens");
            entity.HasKey(e => e.EntraOid);
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(128);
            entity.Property(e => e.AccessToken).HasColumnName("access_token").IsRequired();
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.MicrosoftEmail).HasColumnName("microsoft_email").HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });
    }
}

public class FipPortalUserMicrosoftToken
{
    public string EntraOid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public string? MicrosoftEmail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
