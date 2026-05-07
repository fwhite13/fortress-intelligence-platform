# CC Brief — ADO#2845 BUILD cycle 2
## FAIT v2: 4-step onboarding wizard — 2 critical fixes

You are making **exactly 2 targeted fixes** to the FAIT v2 onboarding feature. No scope creep. No refactoring beyond what's described.

---

## Files to modify

1. `src/FortressAI.V2.Web/Services/IUserProvisioningService.cs`
2. `src/FortressAI.V2.Web/Services/UserProvisioningService.cs`
3. `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor`

---

## Fix C1 — Wire wizard data into SOUL.md via WizardData parameter

### C1.1 — `IUserProvisioningService.cs`

Add a `WizardData` record and update the `ProvisionAsync` signature.

**Current file content:**
```csharp
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
```

**Replace the entire file with:**
```csharp
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
```

### C1.2 — `UserProvisioningService.cs`

**Changes needed:**

1. Update the `ProvisionAsync` method signature to accept `WizardData? wizardData = null` before `CancellationToken ct = default`.

2. Add a private `BuildSoulMdContent` method that uses wizardData to produce a richer SOUL.md.

3. In the S3 file writing section (Step 3), replace the inline SOUL.md template substitution with a call to `BuildSoulMdContent(displayName, wizardData)`.

**In `ProvisionAsync`, find this signature:**
```csharp
    public async Task<ProvisioningResult> ProvisionAsync(
        string userId,
        string entraOid,
        string email,
        string displayName,
        CancellationToken ct = default)
```
**Replace with:**
```csharp
    public async Task<ProvisioningResult> ProvisionAsync(
        string userId,
        string entraOid,
        string email,
        string displayName,
        WizardData? wizardData = null,
        CancellationToken ct = default)
```

**In `ProvisionAsync`, find this S3 files dictionary (Step 3):**
```csharp
            var files = new Dictionary<string, string>
            {
                [$"{s3Prefix}assistants/SOUL.md"]   = SoulMdTemplate.Replace("{DisplayName}", displayName),
                [$"{s3Prefix}assistants/USER.md"]   = UserMdTemplate
                    .Replace("{DisplayName}", displayName)
                    .Replace("{Email}", email),
                [$"{s3Prefix}assistants/AGENTS.md"] = AgentsMdTemplate,
                [$"{s3Prefix}memory/MEMORY.md"]     = MemoryMdTemplate,
            };
```
**Replace with:**
```csharp
            var files = new Dictionary<string, string>
            {
                [$"{s3Prefix}assistants/SOUL.md"]   = BuildSoulMdContent(displayName, wizardData),
                [$"{s3Prefix}assistants/USER.md"]   = UserMdTemplate
                    .Replace("{DisplayName}", displayName)
                    .Replace("{Email}", email),
                [$"{s3Prefix}assistants/AGENTS.md"] = AgentsMdTemplate,
                [$"{s3Prefix}memory/MEMORY.md"]     = MemoryMdTemplate,
            };
```

**Add this private method to `UserProvisioningService` (after the `DropPgSchemaAsync` method, before the closing brace of the class):**
```csharp
    // ── SOUL.md builder ───────────────────────────────────────────────────

    private static string BuildSoulMdContent(string displayName, WizardData? wizardData)
    {
        var sb = new System.Text.StringBuilder();
        var name = !string.IsNullOrWhiteSpace(wizardData?.PreferredName)
            ? wizardData.PreferredName
            : displayName;

        sb.AppendLine($"# SOUL.md — {name}'s Assistant");
        sb.AppendLine();
        sb.AppendLine("## Identity");
        sb.AppendLine("I am your personal AI assistant on the Fortress Intelligence Platform.");

        if (wizardData != null)
        {
            sb.AppendLine();
            sb.AppendLine("## User Context");
            if (!string.IsNullOrEmpty(wizardData.Role))
                sb.AppendLine($"- **Role:** {wizardData.Role}");
            if (!string.IsNullOrEmpty(wizardData.Responsibilities))
                sb.AppendLine($"- **Responsibilities:** {wizardData.Responsibilities}");
            if (wizardData.UseCases.Count > 0)
                sb.AppendLine($"- **Primary use cases:** {string.Join(", ", wizardData.UseCases)}");
            if (!string.IsNullOrEmpty(wizardData.AssistantName))
                sb.AppendLine($"- **Assistant name:** {wizardData.AssistantName}");

            sb.AppendLine();
            sb.AppendLine("## Communication Style");
            sb.AppendLine($"- Style: {wizardData.CommunicationStyle}");
            sb.AppendLine($"- Format: {wizardData.ResponseFormat}");
            sb.AppendLine($"- Citations: {(wizardData.ShowCitations ? "Show sources" : "Omit sources")}");
        }

        sb.AppendLine();
        sb.AppendLine("## Purpose");
        sb.AppendLine("I help you work smarter — drafting, researching, analyzing, and executing complex tasks.");

        if (wizardData != null)
        {
            sb.AppendLine();
            sb.AppendLine("## Personality");
            sb.AppendLine("Precise, proactive, and honest. I surface what matters and flag what's uncertain.");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("## Personality");
            sb.AppendLine("Precise, proactive, and honest. I surface what matters and flag what's uncertain.");
        }

        return sb.ToString();
    }
```

### C1.3 — `Onboarding.razor`

In the `@code` section, update `FinishWizard()` to:
1. Build a `WizardData` object from the collected wizard fields.
2. Pass it to `ProvisionAsync`.

**Find `BuildEnrichedDisplayName` method:**
```csharp
    private string BuildEnrichedDisplayName()
    {
        var name = string.IsNullOrWhiteSpace(_preferredName) ? _displayName : _preferredName;
        return name;
    }
```
**Replace with:**
```csharp
    private WizardData BuildWizardData() => new WizardData(
        Role: _role,
        Responsibilities: _responsibilities,
        CommunicationStyle: _communicationStyle,
        ResponseFormat: _responseFormat,
        ShowCitations: _showCitations,
        UseCases: _selectedUseCases.ToList(),
        PreferredName: string.IsNullOrWhiteSpace(_preferredName) ? _displayName.Split(' ').FirstOrDefault() ?? _displayName : _preferredName,
        AssistantName: _assistantName,
        AccentColor: _accentColor
    );
```

**Find the `FinishWizard` method body — specifically this section:**
```csharp
        try
        {
            var enrichedDisplayName = BuildEnrichedDisplayName();
            var userId = await GetOrCreateUserId();

            await ProvisioningService.ProvisionAsync(
                userId: userId,
                entraOid: _entraOid,
                email: _email,
                displayName: enrichedDisplayName
            );
```
**Replace with:**
```csharp
        try
        {
            var wizardData = BuildWizardData();
            var userId = await GetOrCreateUserId();

            await ProvisioningService.ProvisionAsync(
                userId: userId,
                entraOid: _entraOid,
                email: _email,
                displayName: _displayName,
                wizardData: wizardData
            );
```

---

## Fix C2 — Guard against empty EntraOid

### C2.1 — `UserProvisioningService.cs`

In `ProvisionAsync`, find the existing GUID guard:
```csharp
        if (!Guid.TryParse(userId, out _))
            throw new ArgumentException($"userId must be a valid GUID, got: {userId}", nameof(userId));
```
**Replace with:**
```csharp
        if (!Guid.TryParse(userId, out _))
            throw new ArgumentException($"userId must be a valid GUID, got: {userId}", nameof(userId));
        if (string.IsNullOrWhiteSpace(entraOid))
            throw new ArgumentException("entraOid cannot be empty", nameof(entraOid));
```

### C2.2 — `Onboarding.razor`

In `OnInitializedAsync`, after the line that sets `_preferredName`, add an empty entraOid guard.

**Find:**
```csharp
        _preferredName = _displayName.Split(' ').FirstOrDefault() ?? _displayName;
    }
```
**Replace with:**
```csharp
        _preferredName = _displayName.Split(' ').FirstOrDefault() ?? _displayName;

        if (string.IsNullOrEmpty(_entraOid))
        {
            _errorMessage = "Unable to determine your identity. Please sign out and sign in again.";
            _provisionError = true;
            StateHasChanged();
            return;
        }
    }
```

---

## Constraints

- Do NOT modify any other files.
- Do NOT change CSS classes, routing, or any other functionality.
- Do NOT rename or restructure existing methods beyond what's described.
- The `WizardData` record goes in `IUserProvisioningService.cs` (same file as the interface).
- `BuildSoulMdContent` is a private static method on `UserProvisioningService`.
- Keep the `SoulMdTemplate` constant — it's still used as a fallback pattern reference even if `BuildSoulMdContent` replaces its direct use.

## Acceptance criteria

1. `IUserProvisioningService` interface `ProvisionAsync` signature includes `WizardData? wizardData = null` before `CancellationToken`.
2. `UserProvisioningService.ProvisionAsync` signature matches.
3. `BuildSoulMdContent` uses `wizardData` fields to enrich SOUL.md content.
4. S3 file write for SOUL.md calls `BuildSoulMdContent(displayName, wizardData)`.
5. `Onboarding.razor` calls `ProvisionAsync` with `wizardData:` parameter.
6. `ProvisionAsync` throws if `entraOid` is null/whitespace.
7. `OnInitializedAsync` sets `_provisionError = true` and `_errorMessage` if `_entraOid` is empty.
8. `dotnet build` in `src/FortressAI.V2.Web` reports 0 errors.
