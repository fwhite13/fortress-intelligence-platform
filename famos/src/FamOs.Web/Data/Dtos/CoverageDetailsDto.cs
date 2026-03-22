namespace FamOs.Web.Data.Dtos;

public class CoverageDetailsDto
{
    public string Id { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string QuoteNum { get; set; } = "";
    public string AmRating { get; set; } = "";
    public string AmLabel { get; set; } = "";
    public string Billing { get; set; } = "";
    public string EsStatus { get; set; } = "";
    public decimal Premium { get; set; }
    public List<string> BundleLines { get; set; } = new();
    public List<string> Includes { get; set; } = new();
    public List<string> Excludes { get; set; } = new();
    public Dictionary<string, string> Vals { get; set; } = new();
    public string AiNotes { get; set; } = "";
}
