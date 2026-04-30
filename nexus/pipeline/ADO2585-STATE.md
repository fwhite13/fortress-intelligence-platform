# Pipeline State: ADO#2585

## Current Stage: BUILDING
## Risk Level: medium
## Pipeline Path: full (code change — needs review + deploy)
## Review Cycles: 0

### WI
- **Title:** NEXUS ArtifactGen: Implement two-call Bedrock architecture for TC generation
- **ADO ID:** 2585
- **ADO URL:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2585
- **Spec:** `/home/fredw/.openclaw/workspace/memory/projects/nexus-two-call-tc-spec-2026-04-29.md`
- **Repo:** nexus-web (`/home/fredw/projects/fip/nexus/`)

### Pre-build findings
- ArtifactGenerationService.cs: TC gen currently in C# via _wiClassifier.ShouldGenerateTestCases()
- BedrockService.InvokeAsync(systemPrompt, userContent, maxTokens, modelId)
- ArtifactGenSystem NOT in appsettings.Production.json — must be added
- max_tokens currently 8192; spec requires 32768 for both calls

### Blocked WIs
- ADO#2586 (v7 validation) — blocked on this WI deploying

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN  | ✅ DONE | Jarvis/Maria | 00:43 | 00:45 | Spec verified, pre-build findings noted |
| BUILD | 🔄 ACTIVE | Tony | 00:45 | — | Two-call arch + config |
