namespace FortressAI.Web.Services;

public class HelpService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<HelpService> _logger;

    public HelpService(IWebHostEnvironment env, ILogger<HelpService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<string> GetHelpContentAsync(string fileName)
    {
        try
        {
            // Sanitize: strip directory traversal — only allow bare filenames
            var safeFileName = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName != fileName)
            {
                _logger.LogWarning("Rejected unsafe help file path: {File}", fileName);
                return "Invalid help file requested.";
            }

            var path = Path.Combine(_env.WebRootPath, "help", safeFileName);
            if (!File.Exists(path)) return $"Help content not found: {safeFileName}";
            return await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load help content: {File}", fileName);
            return "Help content unavailable.";
        }
    }
}
