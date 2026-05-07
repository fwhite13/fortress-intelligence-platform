namespace FortressAI.V2.Web.Services;

public interface IDesignAgentService
{
    /// <summary>Generate an HTML screen from a text prompt via Stitch. Returns the generated HTML content.</summary>
    Task<DesignAgentResult> GenerateScreenAsync(string userId, string prompt, string? designDnaContext = null, CancellationToken ct = default);

    /// <summary>Extract design DNA (colors, fonts, layout) from an existing screen HTML or image.</summary>
    Task<string> ExtractDesignContextAsync(string userId, string screenHtmlOrImageBase64, CancellationToken ct = default);

    /// <summary>Iteratively refine an existing screen with a follow-up prompt.</summary>
    Task<DesignAgentResult> RefineScreenAsync(string userId, string existingScreenId, string refinementPrompt, CancellationToken ct = default);

    /// <summary>Save a generated artifact to S3 and persist a DB record. Returns the S3 key.</summary>
    Task<string> SaveArtifactAsync(string userId, string sessionId, string html, string artifactName, string? stitchScreenId = null, bool isFallback = false, CancellationToken ct = default);

    /// <summary>Is Stitch available? Returns false if GCP credentials not configured.</summary>
    Task<bool> IsStitchAvailableAsync(string userId, CancellationToken ct = default);
}

public record DesignAgentResult(
    string Html,
    string? ScreenId,
    string? ProjectId,
    bool IsFallback,
    string? SessionId = null
);
