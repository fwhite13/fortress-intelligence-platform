namespace FamOs.Web.Data.Dtos;

public class AlertDto
{
    public string Type { get; set; } = "info";   // "info", "warning", "danger"
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Package { get; set; }          // "A", "B", or null for both
    public string? LineSlug { get; set; }
}
