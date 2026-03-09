namespace FortressAI.Web.Services;

public class EmailClassifierService
{
    private readonly BedrockService _bedrockService;
    private readonly ILogger<EmailClassifierService> _logger;

    public EmailClassifierService(BedrockService bedrockService, ILogger<EmailClassifierService> logger)
    {
        _bedrockService = bedrockService;
        _logger = logger;
    }

    /// <summary>
    /// Classifies an email's importance as HIGH, MEDIUM, or LOW.
    /// </summary>
    public async Task<string> ClassifyEmailAsync(string senderEmail, string subject, string bodyPreview, string userName)
    {
        var prompt = $@"Classify the importance of this email for {userName}:

From: {senderEmail}
Subject: {subject}
Preview: {bodyPreview}

Is this email:
- HIGH importance (urgent, requires immediate attention, from key contacts)
- MEDIUM importance (relevant but not time-sensitive)
- LOW importance (informational, newsletters, automated notifications)

Respond with exactly one word: HIGH, MEDIUM, or LOW";

        try
        {
            var response = await _bedrockService.InvokeClaudeAsync(prompt, maxTokens: 10);
            var classification = response.Trim().ToUpper();

            if (classification != "HIGH" && classification != "MEDIUM" && classification != "LOW")
            {
                if (classification.Contains("HIGH")) return "HIGH";
                if (classification.Contains("MEDIUM")) return "MEDIUM";
                return "LOW";
            }

            return classification;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email classification failed for '{Subject}' from {Sender}", subject, senderEmail);
            return "MEDIUM";
        }
    }

    /// <summary>
    /// Summarizes an email thread.
    /// </summary>
    public async Task<string> SummarizeEmailAsync(string senderEmail, string subject, string body,
        string userName, string assistantName, string personalityPreset, List<string>? kbContext = null)
    {
        var kbSection = kbContext?.Any() == true
            ? $"\nContext from knowledge base:\n{string.Join("\n", kbContext)}"
            : "";

        var prompt = $@"Summarize this email for {userName}:

From: {senderEmail}
Subject: {subject}
Body: {body}
{kbSection}

Provide a 2-3 sentence summary. Focus on:
- What the sender wants
- Any action items for {userName}
- Relevant background from KB (if available)";

        try
        {
            return await _bedrockService.InvokeClaudeAsync(prompt, maxTokens: 500,
                systemPrompt: $"You are {assistantName}, a {personalityPreset} assistant.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email summarization failed for '{Subject}'", subject);
            return $"Email from {senderEmail}: {subject}";
        }
    }

    /// <summary>
    /// Drafts a response to an email.
    /// </summary>
    public async Task<string?> DraftResponseAsync(string senderEmail, string subject, string body,
        string summary, string userName, string assistantName, string personalityPreset, List<string>? kbContext = null)
    {
        var kbSection = kbContext?.Any() == true
            ? $"\nContext from KB:\n{string.Join("\n", kbContext)}"
            : "";

        var prompt = $@"Draft a reply to this email for {userName}:

Original email:
From: {senderEmail}
Subject: {subject}
Body: {body}

Summary: {summary}
{kbSection}

Draft a professional, concise reply that:
- Addresses the sender's request
- Uses knowledge base context where relevant
- Matches {userName}'s typical communication style

Do not include greeting/closing (user will add those). Just the body.
If no response is needed (e.g., it's a notification or FYI), respond with exactly: NO_RESPONSE_NEEDED";

        try
        {
            var draft = await _bedrockService.InvokeClaudeAsync(prompt, maxTokens: 1000,
                systemPrompt: $"You are {assistantName}, a {personalityPreset} assistant.");

            if (draft.Trim() == "NO_RESPONSE_NEEDED")
                return null;

            return draft;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Response drafting failed for '{Subject}'", subject);
            return null;
        }
    }
}
