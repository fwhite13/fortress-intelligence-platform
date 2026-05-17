using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Web.Data.Models;

[Table("user_sessions")]
public class UserSession
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? TaskArn { get; set; }
    public string? PrivateIp { get; set; }
    public string? FargateStatus { get; set; }
    public string? FargateSessionId { get; set; }
    public string? TaskDefinitionRevision { get; set; }
    public string? HarnessVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
