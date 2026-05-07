namespace FortressAI.V2.Web.Services;

/// <summary>
/// Result of a provisioning operation.
/// WasProvisioned = false if user was already provisioned (idempotent no-op).
/// </summary>
public record ProvisioningResult(bool WasProvisioned, string WorkspaceS3Prefix, string PgSchemaName);

/// <summary>
/// Wizard preferences collected during onboarding.
/// Passed to ProvisionAsync so they can be incorporated into the SOUL.md template.
/// </summary>
public record WizardData(
    string Role,
    string Responsibilities,
    string CommunicationStyle,
    string ResponseFormat,
    bool ShowCitations,
    List<string> UseCases,
    string PreferredName,
    string AssistantName,
    string? AccentColor
);

public interface IUserProvisioningService
{
    /// <summary>
    /// Provisions all resources for a new user. Idempotent — safe to call multiple times.
    /// Returns WasProvisioned=false if already provisioned (onboarding_completed_at is set).
    /// Throws ProvisioningException on failure after attempting rollback.
    /// </summary>
    Task<ProvisioningResult> ProvisionAsync(
        string userId,
        string entraOid,
        string email,
        string displayName,
        WizardData? wizardData = null,
        CancellationToken ct = default);
}
