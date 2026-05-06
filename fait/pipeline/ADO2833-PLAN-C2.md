# BUILD Plan C2 — ADO#2833 (CORRECTED)
## KB Upload pre-processing parity — PPTX→PDF for all tiers, BDA via KB config (not code)

**WI:** ADO#2833 | FAIT
**Repo:** `/home/fredw/projects/fip/fait/`
**Risk:** low (revert BdaProcessingService, keep only PPTX parity + help text)

---

## What Changed (Fred's direction, 2026-05-06 17:15)

BDA for images is handled by setting `ParsingStrategy: BEDROCK_NATIVE` on the KB data source config — **this is an infrastructure change, not a code change.** No `BdaProcessingService` should exist in the application code.

**Revert everything Tony built in `a0ff695` except:**
1. PPTX→PDF addition in `UploadProjectDocumentAsync`
2. Help text update in `KnowledgeBaseManagement.razor` (remove the BDA/image-specific parts — images are handled transparently by BEDROCK_NATIVE; no user-visible change needed for that)

---

## Scope of C2 Build

### KEEP from `a0ff695`
- `KbDocumentService.UploadProjectDocumentAsync` — PPTX→PDF conversion (applies to all: Personal, Team, Project paths)
  - Wait — `UploadDocumentAsync` already has PPTX→PDF for Personal/Team. The missing piece was `UploadProjectDocumentAsync` for Project KB. Keep that addition.
- `KnowledgeBaseManagement.razor` help text — update to mention PPTX auto-conversion. Remove any BDA/image OCR wording.

### REVERT from `a0ff695`
- `src/FortressAI.Web/Services/BdaProcessingService.cs` — **DELETE this file entirely**
- `src/FortressAI.Web/FortressAI.Web.csproj` — **REMOVE** `AWSSDK.BedrockDataAutomationRuntime` PackageReference
- `src/FortressAI.Web/Program.cs` — **REMOVE** `builder.Services.AddScoped<BdaProcessingService>();`
- `src/FortressAI.Web/Services/KbDocumentService.cs` — **REMOVE** `BdaProcessingService` injection, `Task.Run` BDA calls, `_bdaSupportedExtensions` set; **KEEP** PPTX→PDF in `UploadProjectDocumentAsync`

---

## PPTX→PDF scope clarification (Fred: "all paths")

Fred confirmed scope = Personal, Team, AND Project. 

- `UploadDocumentAsync` (Personal + Team) — PPTX→PDF **already existed** before this WI ✅
- `UploadProjectDocumentAsync` (Project) — PPTX→PDF **missing** — **this is the only code change to keep**

---

## Infra change (separate, Rhodey handles)

Rhodey needs to set `ParsingStrategy: BEDROCK_NATIVE` on the FAIT KB data sources (Personal, Team, Project) via the AWS Bedrock console or SDK. This enables native image/PDF processing during ingestion. Tony does NOT touch this.

Tony should note in Build Report: "Infra: Rhodey to set ParsingStrategy=BEDROCK_NATIVE on all FAIT KB data sources (FAIT KB IDs: ZCEZCJGHQC personal, and team/project equivalents). This enables BDA-style image processing at ingestion time with no application code changes."

---

## Acceptance Criteria (C2)

- [ ] `BdaProcessingService.cs` DELETED — file does not exist
- [ ] `AWSSDK.BedrockDataAutomationRuntime` NOT in `.csproj`
- [ ] `Program.cs` — no `BdaProcessingService` registration
- [ ] `KbDocumentService` — no `BdaProcessingService` injection, no BDA call sites, no `_bdaSupportedExtensions`
- [ ] `KbDocumentService.UploadProjectDocumentAsync` — PPTX→PDF conversion present (from C1 — keep)
- [ ] `KnowledgeBaseManagement.razor` help text — PPTX auto-convert mention kept; remove BDA/image OCR wording
- [ ] Build compiles with 0 errors

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```
