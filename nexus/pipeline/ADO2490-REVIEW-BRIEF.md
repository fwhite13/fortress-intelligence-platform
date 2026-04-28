# REVIEW Assignment: ADO#2490

## Task
**Implement IWiClassifier interface and WiClassifierService**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2490
**Review cycle:** 1 of 2

## Spec (MANDATORY — read before reviewing)
`/home/fredw/.openclaw/workspace/memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`
Focus on **§6 — Service Layer Changes** — this is the authoritative definition for everything you're reviewing.

## Files Modified by Tony (commit `19d2cc8`)
| File | Action |
|------|--------|
| `src/FortressNexus.Web/Services/IWiClassifier.cs` | Created |
| `src/FortressNexus.Web/Services/WiClassifierService.cs` | Created |
| `src/FortressNexus.Web/Program.cs` | Modified — DI registration added |

## Build Report Summary
- Build: SUCCEEDED (0 errors, 1 pre-existing warning in unrelated file)
- Tony used existing `AdoWorkItemDto` type instead of `WorkItemCandidate` (correct — adjusted to match codebase)
- All 12 AC items self-checked by Tony

## Review Focus

### 1. Interface correctness (`IWiClassifier.cs`)
- Does `IWiClassifier` define exactly: `ClassifyStory`, `ShouldGenerateTestCases`, `IsExternalDependency`, `ExtractExternalOwner`?
- Is `WiTemplateType` enum in the same file with values: `Standard`, `Infrastructure`, `Migration`, `TestCase`?

### 2. Classification signal completeness (`WiClassifierService.cs`)
This is the most important check. Compare the signal lists in the implementation against spec §6 exactly:

**Infrastructure signals (11 required):**
`"create ecr"`, `"ecr repo"`, `"ecr repository"`, `"iam role"`, `"task execution role"`, `"ecs service"`, `"alb target"`, `"alb rule"`, `"target group"`, `"fargate task definition"`, `"secrets manager secret"`

**Migration signals (7 required):**
`"migrate"`, `"replace"`, `"move from"`, `"deprecate"`, `"switch from"`, `"transition from"`, `"cut over"`

**Auth/scoping signals (14 required — for ShouldGenerateTestCases):**
`"auth"`, `"token"`, `"entitlement"`, `"scope"`, `"scoping"`, `"permission"`, `"validate"`, `"enforce"`, `"restrict"`, `"deny"`, `"unauthorized"`, `"403"`, `"jwt"`, `"bearer"`

**External dependency signals (12 required — for IsExternalDependency):**
`"rob"`, `"rob nethery"`, `"cloudflare"`, `"cf config"`, `"cf route"`, `"azure access"`, `"iam request"`, `"iam permissions"`, `"secrets manager access"`, `"ado pat"`, `"pat token"`, `"bedrock-agent-runtime"`

### 3. Classification logic correctness
- `ClassifyStory`: does it check Infrastructure BEFORE Migration BEFORE Standard?
- `ShouldGenerateTestCases`: does it short-circuit `false` for Infra/Migration regardless of AC count?
- `ShouldGenerateTestCases`: does it correctly return `true` for Standard + (auth signal OR ≥4 AC items)?
- AC counting: does it count `- [ ]` lines and numbered list items?

### 4. `ExtractExternalOwner` priority order
Must match spec §6 priority exactly:
1. "rob" / "cloudflare" / "cf config" → "Rob Nethery"
2. "iam" / "bedrock-agent-runtime" → "AWS IAM"
3. "azure access" / "azure subscription" → "Azure Admin"
4. "ado pat" / "pat token" → "ADO Admin"
5. default → "External Owner"

### 5. DI registration (`Program.cs`)
- Is `builder.Services.AddScoped<IWiClassifier, WiClassifierService>();` present?
- Is it registered before route plugin registrations (Fastify rule doesn't apply here — but placement should be logical, grouped with other service registrations)?

### 6. Case-insensitive matching
- All signal checks must be case-insensitive. Verify the implementation uses `StringComparison.OrdinalIgnoreCase` or equivalent throughout.

### 7. No external dependencies
- `WiClassifierService` must have no constructor-injected dependencies beyond standard types. Pure string matching only.

## MANDATORY: Use Claude Code CLI
Write your review brief to a file, then execute:
```
cat review-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Your Review Report MUST include the CC invocation used. Do NOT reason about the code without CC reading it first.

## Deliverables
1. **Review Report** at `/home/fredw/projects/fip/nexus/pipeline/ADO2490-REVIEW-REPORT.md`
   - Verdict: PASS / NEEDS-CHANGES / FAIL
   - Any issues categorized: Critical / Important / Nitpick
   - CC invocation used
2. **ADO comment** on WI #2490:
   ```
   mcporter call devops.add_comment project="FAIT" id=2490 text="**[Hawkeye — REVIEW cycle 1]**
   Code review [PASS/NEEDS-CHANGES]. Cycles: 1. [summary of findings or 'No issues.']"
   ```

## When done
```
openclaw system event --text "ADO2490 REVIEW COMPLETE: [PASS/NEEDS-CHANGES]" --mode now
```
