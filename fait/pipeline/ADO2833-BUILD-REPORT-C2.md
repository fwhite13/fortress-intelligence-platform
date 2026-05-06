# Build Report C2 — ADO#2833
## KB Upload pre-processing parity — Corrective revert of BdaProcessingService

**Cycle:** 2 (CORRECTIVE)
**Date:** 2026-05-06
**Engineer:** Tony Stark (BUILD agent)
**Commit:** `d512a64`
**Branch:** main

---

## What Was Built

Corrective revert of cycle 1 (`a0ff695`). Removed all `BdaProcessingService` application code. BDA for images is handled infrastructure-side by setting `ParsingStrategy: BEDROCK_NATIVE` on the KB data source config — no application code needed.

Kept from C1: PPTX→PDF conversion in `UploadProjectDocumentAsync` (Project KB path parity — this was the actual missing feature).

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/BdaProcessingService.cs` | **DELETED** |
| `src/FortressAI.Web/FortressAI.Web.csproj` | Removed `AWSSDK.BedrockDataAutomationRuntime` PackageReference |
| `src/FortressAI.Web/Program.cs` | Removed `AddScoped<BdaProcessingService>()` |
| `src/FortressAI.Web/Services/KbDocumentService.cs` | Removed `_bdaService` field, `BdaSupportedImageExtensions` set, `BdaProcessingService` ctor param + `_bdaService = bdaService` assignment, BDA call sites in both `UploadDocumentAsync` and `UploadProjectDocumentAsync`. PPTX→PDF blocks kept in both upload paths. |
| `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` | Updated help text: removed `(images: 3.75 MB)` and `Images indexed via OCR + visual analysis` — kept PPTX auto-convert mention |

---

## Parallelization

Not applicable — single-pass revert. All changes in one CC session, sequential by dependency (delete → csproj → Program.cs → KbDocumentService → Razor).

---

## Acceptance Criteria Verification

- [x] `BdaProcessingService.cs` DELETED — confirmed `ls` returns no such file
- [x] `AWSSDK.BedrockDataAutomationRuntime` NOT in `.csproj` — confirmed via `grep`
- [x] `Program.cs` — no `BdaProcessingService` registration — confirmed via `grep`
- [x] `KbDocumentService` — no `BdaProcessingService` injection, no BDA call sites, no `BdaSupportedImageExtensions` — confirmed via `grep`
- [x] `KbDocumentService.UploadProjectDocumentAsync` — PPTX→PDF conversion present — confirmed 16 PPTX-related lines intact
- [x] `KnowledgeBaseManagement.razor` help text — PPTX mention kept; BDA/image OCR wording removed — confirmed
- [x] Build: 0 errors, 31 warnings (all pre-existing)

---

## Build Result

```
Build succeeded.
    0 Error(s)
    31 Warning(s) — all pre-existing, none related to this change
```

---

## CC Sessions

- 1 CC session (sonnet), synchronous pipe mode
- No parallelization needed — linear dependency chain

---

## Known Edge Cases / Clint Review Notes

- `KbDocumentService` constructor no longer takes `BdaProcessingService`. DI registration is removed from `Program.cs`. The constructor chain is clean.
- PPTX→PDF runs in both `UploadDocumentAsync` (Personal + Team paths) and `UploadProjectDocumentAsync` (Project path). Both call `ConvertPptxToPdfAsync` — identical behavior.
- Images (JPG, PNG, GIF, WEBP) will still be accepted for upload and stored in S3. Bedrock native parsing (`ParsingStrategy: BEDROCK_NATIVE`) handles indexing at ingestion time — no code-level dispatch needed.

---

## Infra Dependency (Rhodey)

⚠️ **Rhodey must set `ParsingStrategy: BEDROCK_NATIVE`** on all FAIT KB data sources:
- Personal KB: `ZCEZCJGHQC`
- Team KB: (equivalent)
- Project KB: (equivalent)

This is the only remaining step to enable image/PDF native processing at ingestion time. Tony's code revert is complete and does not block this infra change.

---

## How to Test Locally

```bash
cd /home/fredw/projects/fip/fait/src/FortressAI.Web
dotnet build   # confirm 0 errors
grep -r "BdaProcessingService\|BedrockDataAutomationRuntime" ../  # confirm 0 results
```

---

## Build Report Sent to Clint

Ready for review — awaiting Clint.
