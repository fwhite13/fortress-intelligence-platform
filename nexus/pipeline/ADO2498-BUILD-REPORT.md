# ADO#2498 — BUILD Report

## Commit
- **Hash:** `d4c0656`
- **Message:** `feat(nexus#2498): integrate IWiClassifier into ArtifactGenerationService`
- **Branch:** `main`

## Files Modified
1. `nexus/src/FortressNexus.Web/Models/DTOs/AdoWorkItemDto.cs` — Added classification fields (`WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`)
2. `nexus/src/FortressNexus.Web/Services/ArtifactGenerationService.cs` — Injected `IWiClassifier`, added post-parse classification loop, Test Case generation loop, AC parsing
3. `nexus/src/FortressNexus.Web/Services/StubAdoService.cs` — Map classification fields from DTO→WorkItemRecord, set `ExternalDependencyCount` on ArtifactSet

## Build Result
```
dotnet build — Build succeeded. 0 Error(s), 1 Warning(s) (pre-existing CS8601 in FileStorageService.cs)
```

## CC Invocation
Interactive session (not pipeline invocation)

## Self-Review Checklist
- [x] **AC1:** `IWiClassifier` injected into `ArtifactGenerationService` constructor
- [x] **AC2:** Classification called on ALL WI types post-parse (WiTemplate, IsExternalDependency, ExternalOwner)
- [x] **AC3:** Test Case DTOs generated for qualifying User Stories via `ShouldGenerateTestCases`
- [x] **AC4:** AC parsing supports checkbox (`- [ ]`), numbered (`1.`), and newline fallback patterns
- [x] **AC5:** `TestedByTitles` populated on parent story with generated TC titles
- [x] **AC6:** `ExternalDependencyCount` set on ArtifactSet in `StubAdoService.CreateWorkItemBatchAsync`
- [x] **AC7:** Existing behavior unchanged — no fields removed, no existing assignments overwritten

## Candidate↔Record Mapping Approach
The `AdoWorkItemDto` was extended with classification fields (`WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`). Classification is applied in `ArtifactGenerationService` directly on the DTO after parsing. The enriched DTOs flow through to `StubAdoService`, which maps all fields (including new classification fields) to `WorkItemRecord`. This avoids changing the service interface signature while carrying classification data through the existing DTO pipeline.
