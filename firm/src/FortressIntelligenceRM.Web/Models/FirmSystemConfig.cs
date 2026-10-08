using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

// WI #8034 — org-wide key/value settings that must be mutable at runtime (no redeploy).
public class FirmSystemConfig
{
    [MaxLength(100)]
    public string ConfigKey { get; set; } = "";
    public string? ConfigValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
