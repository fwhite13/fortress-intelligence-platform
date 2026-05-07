# CC Brief — ADO#2845 Build Cycle 3

## Context
FAIT v2 onboarding wizard. Apply 5 targeted fixes (I1 blocks PASS, I2/I3/N1/N2 tracked). No scope creep.

## Files to modify
1. `src/FortressAI.V2.Web/Services/IUserProvisioningService.cs`
2. `src/FortressAI.V2.Web/Services/UserProvisioningService.cs`
3. `src/FortressAI.V2.Web/Components/Pages/Onboarding.razor`

---

## Fix I1 — AdditionalContext missing from WizardData (BLOCKS PASS)

The UI has a textarea field `_additionalContext` in Step 3 ("Anything else you'd like your assistant to know?") that is bound but never included in WizardData or emitted in SOUL.md.

### In `IUserProvisioningService.cs`

The `WizardData` record currently ends with:
```csharp
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
```

Change to add `AdditionalContext` before `AccentColor`, and add `// UI-only — not persisted at provisioning time` comment on AccentColor (this also satisfies N2):
```csharp
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
```

### In `Onboarding.razor`

In the `BuildWizardData()` method, the current call is:
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

Add `AdditionalContext: _additionalContext,` before `AccentColor`:
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

### In `UserProvisioningService.cs` — `BuildSoulMdContent()`

After the line:
```csharp
            if (!string.IsNullOrEmpty(wizardData.AssistantName))
                sb.AppendLine($"- **Assistant name:** {wizardData.AssistantName}");
```

Add (emit AdditionalContext under User Context):
```csharp
            if (!string.IsNullOrWhiteSpace(wizardData.AdditionalContext))
                sb.AppendLine($"- **Additional context:** {wizardData.AdditionalContext}");
```

---

## Fix I2 — Dead if/else in BuildSoulMdContent Personality section

In `UserProvisioningService.cs`, the Personality section currently has identical if/else branches:
```csharp
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
```

Simplify to a single unconditional block:
```csharp
        sb.AppendLine();
        sb.AppendLine("## Personality");
        sb.AppendLine("Precise, proactive, and honest. I surface what matters and flag what's uncertain.");
```

---

## Fix I3 — Null guard on UseCases

In `UserProvisioningService.cs` `BuildSoulMdContent()`, the line:
```csharp
            if (wizardData.UseCases.Count > 0)
```

Change to:
```csharp
            if (wizardData.UseCases?.Count > 0)
```

---

## Fix N1 — IsNullOrEmpty vs IsNullOrWhiteSpace inconsistency on entraOid

In `Onboarding.razor`, in `OnInitializedAsync`, the guard currently uses `IsNullOrEmpty`:
```csharp
        if (string.IsNullOrEmpty(_entraOid))
```

Change to `IsNullOrWhiteSpace` to be consistent with the service layer:
```csharp
        if (string.IsNullOrWhiteSpace(_entraOid))
```

---

## Fix N2 — AccentColor comment
Already handled in Fix I1 by adding `// UI-only — not persisted at provisioning time` inline on the AccentColor parameter in the WizardData record.

---

## Constraints
- Touch ONLY the 3 files listed above
- Do NOT modify any other files
- Do NOT reformat or reorganize code beyond the specific changes listed
- Do NOT add tests, new classes, or additional features

## After all edits, run:
```bash
cd /home/fredw/projects/fip/fait-v2 && dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
```

Report: pass/fail + any compiler errors.
