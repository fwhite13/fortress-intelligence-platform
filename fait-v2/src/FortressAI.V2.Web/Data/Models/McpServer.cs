using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("mcp_servers")]
public class McpServer
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("name")]
    [MaxLength(100)]
    [Required]
    public string Name { get; set; } = string.Empty;

    [Column("endpoint_url")]
    [MaxLength(500)]
    [Required]
    public string EndpointUrl { get; set; } = string.Empty;

    [Column("auth_type")]
    [MaxLength(20)]
    [Required]
    public string AuthType { get; set; } = "oauth_entra";

    [Column("default_read")]
    public bool DefaultRead { get; set; } = true;

    [Column("default_write")]
    public bool DefaultWrite { get; set; } = false;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
