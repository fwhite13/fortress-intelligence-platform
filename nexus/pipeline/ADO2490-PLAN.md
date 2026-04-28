# BUILD Assignment: ADO#2490

## Task
**Implement IWiClassifier interface and WiClassifierService**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2490

## MANDATORY: Read the spec first
Read the full spec before starting ANY code changes:
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§6 — Service Layer Changes** — it has the complete interface definition, enum values, all signal lists with exact strings, classification rules, and the DI registration snippet.

## Repo
`/home/fredw/projects/fip/nexus/`
Working directory: `src/FortressNexus.Web/`

## What to build

### 1. Create `Services/IWiClassifier.cs`
Interface + WiTemplateType enum, exactly as defined in spec §6:

```csharp
public interface IWiClassifier
{
    WiTemplateType ClassifyStory(WorkItemCandidate story);
    bool ShouldGenerateTestCases(WorkItemCandidate story);
    bool IsExternalDependency(WorkItemCandidate wi);
    string? ExtractExternalOwner(WorkItemCandidate wi);
}

public enum WiTemplateType
{
    Standard,
    Infrastructure,
    Migration,
    TestCase
}
```

Note: Check what the existing WI candidate model is called in the codebase — it may not be `WorkItemCandidate`. Use the correct existing type name.

### 2. Create `Services/WiClassifierService.cs`
Full implementation of IWiClassifier. Signal lists are in spec §6 — copy them exactly.

**ClassifyStory evaluation order (MANDATORY — infrastructure before migration before standard):**
- Infrastructure signals: `"create ecr"`, `"ecr repo"`, `"iam role"`, `"ecs service"`, `"alb target"`, `"alb rule"`, `"secrets manager secret"`, `"target group"`, `"fargate task definition"`, `"ecr repository"`, `"task execution role"`
- Migration signals: `"migrate"`, `"replace"`, `"move from"`, `"deprecate"`, `"switch from"`, `"transition from"`, `"cut over"`
- Default: Standard

**ShouldGenerateTestCases:**
- Returns false if ClassifyStory returns Infrastructure or Migration
- Returns true if Standard AND (has auth/scoping/entitlement signals OR has ≥4 distinct AC items)
- Auth/scoping signals: `"auth"`, `"token"`, `"entitlement"`, `"scope"`, `"scoping"`, `"permission"`, `"validate"`, `"enforce"`, `"restrict"`, `"deny"`, `"unauthorized"`, `"403"`, `"jwt"`, `"bearer"`

**IsExternalDependency signals** (any match = true):
`"rob"`, `"rob nethery"`, `"cloudflare"`, `"cf config"`, `"cf route"`, `"azure access"`, `"iam request"`, `"iam permissions"`, `"secrets manager access"`, `"ado pat"`, `"pat token"`, `"bedrock-agent-runtime"`

**ExtractExternalOwner priority order:**
1. "rob" OR "cloudflare" OR "cf config" → "Rob Nethery"
2. "iam" OR "bedrock-agent-runtime" → "AWS IAM"
3. "azure access" OR "azure subscription" → "Azure Admin"
4. "ado pat" OR "pat token" → "ADO Admin"
5. default → "External Owner"

All string matching must be case-insensitive. Check title AND description fields of the candidate.

### 3. Register in `Program.cs`
Add: `builder.Services.AddScoped<IWiClassifier, WiClassifierService>();`
Place it with other service registrations in the existing DI section.

## Implementation notes
- No external dependencies — pure string matching only
- Look at the existing models to understand what fields the WI candidate object has (title, description, acceptanceCriteria, etc.)
- The AC count for ShouldGenerateTestCases should count lines starting with `- [ ]` or numbered list items
- Build must compile cleanly with `dotnet build`

## ADO Updates (MANDATORY)
After implementing, add a comment to ADO WI #2490:
```
mcporter call devops.add_comment project="FAIT" id=2490 text="**[Tony Stark — BUILD cycle 1]**
Commit {hash}: {summary}. Build: SUCCEEDED."
```

## Build Report required
Create `/home/fredw/projects/fip/nexus/pipeline/ADO2490-BUILD-REPORT.md` with:
- Files created/modified (with full paths)
- Commit hash
- Build result (dotnet build output)
- CC invocation command used
- Self-review checklist: all AC items verified

## Notify Maria when done
When completely finished, run:
openclaw system event --text "ADO2490 BUILD COMPLETE: IWiClassifier + WiClassifierService implemented" --mode now
