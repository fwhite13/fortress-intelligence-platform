namespace FortressAI.V2.Web.Services;

/// <summary>
/// Result of a provisioning operation.
/// WasProvisioned = false if user was already provisioned (idempotent no-op).
/// </summary>
public record ProvisioningResult(bool WasProvisioned, string WorkspaceS3Prefix, string PgSchemaName);

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
        CancellationToken ct = default);
}
