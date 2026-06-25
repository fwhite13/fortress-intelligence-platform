using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RisePortal.Web.Data.Entities;

[Table("rise_app_cards")]
public class RiseAppCard
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("url")]
    public string? Url { get; set; }

    [Column("icon")]
    public string? Icon { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("restricted")]
    public bool Restricted { get; set; }

    [Column("active")]
    public bool Active { get; set; } = true;

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    public ICollection<RiseAppCardAccess> AccessList { get; set; } = new List<RiseAppCardAccess>();
}
