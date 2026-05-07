# Hawkeye — ADO#2845 Cycle 3 Review Brief

You are performing an adversarial code review for FAIT v2 ADO#2845, cycle 3.
Commit: ca856e5

## Task
Verify all 5 cycle 2 findings are correctly fixed in the current source. Also check for any new issues or regressions introduced by the fixes.

## Files to review

### IUserProvisioningService.cs
```csharp
namespace FortressAI.V2.Web.Services;

public record ProvisioningResult(bool WasProvisioned, string WorkspaceS3Prefix, string PgSchemaName);

public record WizardData(
    string Role,
    string Responsibilities,
    string CommunicationStyle,
    string ResponseFormat,
    bool ShowCitations,
    List<string> UseCases,
    string PreferredName,
    string AssistantName,
    string? AdditionalContext,
    string? AccentColor  // UI-only — not persisted at provisioning time
);

public interface IUserProvisioningService
{
    Task<ProvisioningResult> ProvisionAsync(
        string userId,
        string entraOid,
        string email,
        string displayName,
        WizardData? wizardData = null,
        CancellationToken ct = default);
}
```

### UserProvisioningService.cs — BuildSoulMdContent method
```csharp
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
        if (wizardData.UseCases?.Count > 0)
            sb.AppendLine($"- **Primary use cases:** {string.Join(", ", wizardData.UseCases)}");
        if (!string.IsNullOrEmpty(wizardData.AssistantName))
            sb.AppendLine($"- **Assistant name:** {wizardData.AssistantName}");
        if (!string.IsNullOrWhiteSpace(wizardData.AdditionalContext))
            sb.AppendLine($"- **Additional context:** {wizardData.AdditionalContext}");

        sb.AppendLine();
        sb.AppendLine("## Communication Style");
        sb.AppendLine($"- Style: {wizardData.CommunicationStyle}");
        sb.AppendLine($"- Format: {wizardData.ResponseFormat}");
        sb.AppendLine($"- Citations: {(wizardData.ShowCitations ? "Show sources" : "Omit sources")}");
    }

    sb.AppendLine();
    sb.AppendLine("## Purpose");
    sb.AppendLine("I help you work smarter — drafting, researching, analyzing, and executing complex tasks.");

    sb.AppendLine();
    sb.AppendLine("## Personality");
    sb.AppendLine("Precise, proactive, and honest. I surface what matters and flag what's uncertain.");

    return sb.ToString();
}
```

### ProvisionAsync entraOid guard (UserProvisioningService.cs)
```csharp
if (!Guid.TryParse(userId, out _))
    throw new ArgumentException($"userId must be a valid GUID, got: {userId}", nameof(userId));
if (string.IsNullOrWhiteSpace(entraOid))
    throw new ArgumentException("entraOid cannot be empty", nameof(entraOid));
```

### Onboarding.razor — BuildWizardData()
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
    AdditionalContext: _additionalContext,
    AccentColor: _accentColor
);
```

### Onboarding.razor — OnInitializedAsync entraOid guard
```csharp
if (string.IsNullOrWhiteSpace(_entraOid))
{
    _errorMessage = "Unable to determine your identity. Please sign out and sign in again.";
    _provisionError = true;
    StateHasChanged();
    return;
}
```

## Cycle 2 findings to verify

**I1 — AdditionalContext field:**
- Is `string? AdditionalContext` present in `WizardData` record? ✓/✗
- Is it populated in `BuildWizardData()` as `AdditionalContext: _additionalContext`? ✓/✗
- Is it emitted in `BuildSoulMdContent()` under User Context when non-whitespace? ✓/✗
- Is the field order correct in the record (before AccentColor)? ✓/✗

**I2 — Dead if/else in Personality section:**
- Is the dead `if (wizardData != null)` / `else` block collapsed to a single unconditional block for the Personality section? ✓/✗
- In the code above, `## Personality` is emitted unconditionally (outside the `if (wizardData != null)` block). Confirm this is correct.

**I3 — UseCases null guard:**
- Is `wizardData.UseCases?.Count > 0` (with null-conditional) used in `BuildSoulMdContent()`? ✓/✗

**N1 — IsNullOrWhiteSpace consistency:**
- Does `OnInitializedAsync` use `IsNullOrWhiteSpace(_entraOid)`? ✓/✗
- Does `ProvisionAsync` use `IsNullOrWhiteSpace(entraOid)`? ✓/✗
- Are they now consistent? ✓/✗

**N2 — AccentColor comment:**
- Does the `AccentColor` field in `WizardData` have `// UI-only — not persisted at provisioning time`? ✓/✗

## Additional checks (regression / new issues)

1. **AdditionalContext null vs empty:** The field is `string? AdditionalContext` (nullable) but `_additionalContext` in the Razor is initialized to `""`. The guard `IsNullOrWhiteSpace(wizardData.AdditionalContext)` handles both null and empty correctly. Is this consistent and correct?

2. **IsNullOrEmpty vs IsNullOrWhiteSpace in BuildSoulMdContent:** Role, Responsibilities, and AssistantName use `IsNullOrEmpty` while AdditionalContext uses `IsNullOrWhiteSpace`. Is this inconsistency a real issue?

3. **Personality section guard removed (I2):** Confirm the Personality section is now outside the `if (wizardData != null)` block and emitted unconditionally. This was the correct fix — both old branches emitted the same content.

4. **AdditionalContext position in WizardData record:** Is `AdditionalContext` placed before `AccentColor` in the record? This matters because the positional constructor must match all call sites.

5. **BuildWizardData() positional order:** Does the `BuildWizardData()` call in Onboarding.razor use named parameters? If so, parameter order doesn't matter. Confirm.

6. **Any new hardcoded values that should be constants?**

7. **Any new security issues introduced?**

## Report format

For each cycle-2 finding: VERIFIED FIXED / NOT FIXED / PARTIALLY FIXED with explanation.
For new issues: NONE or list with severity (Critical/Important/Nitpick).
Overall verdict: PASS or NEEDS-CHANGES.
