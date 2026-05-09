namespace FortressAI.V2.Web.Services;

public record CCInterventionRequest(
    string InterventionId,   // UUID — correlates request with response
    string TaskId,
    string ActionType,       // "send_email" | "ado_post" | "kb_write" | "send_message" etc.
    string ActionSummary,    // Human-readable: "Send email to fred@example.com: Subject: Q2 Report"
    string? ActionDetails    // JSON payload preview (truncated to 500 chars)
);
