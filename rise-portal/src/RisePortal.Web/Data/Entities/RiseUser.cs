using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RisePortal.Web.Data.Entities;

[Table("rise_users")]
public class RiseUser
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

    [Column("first_login")]
    public DateTime? FirstLogin { get; set; }

    [Column("last_login")]
    public DateTime? LastLogin { get; set; }
}
