using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RisePortal.Web.Data.Entities;

[Table("rise_admin_users")]
public class RiseAdminUser
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("entra_oid")]
    public string EntraOid { get; set; } = "";

    [Column("email")]
    public string? Email { get; set; }

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }
}
