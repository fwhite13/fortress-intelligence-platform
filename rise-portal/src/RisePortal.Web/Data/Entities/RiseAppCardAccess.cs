using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RisePortal.Web.Data.Entities;

[Table("rise_app_card_access")]
public class RiseAppCardAccess
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("app_card_id")]
    public int AppCardId { get; set; }

    [Column("entra_oid")]
    public string EntraOid { get; set; } = "";

    [Column("email")]
    public string? Email { get; set; }

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("granted_at")]
    public DateTime? GrantedAt { get; set; }

    [Column("granted_by_oid")]
    public string? GrantedByOid { get; set; }

    public RiseAppCard AppCard { get; set; } = null!;
}
