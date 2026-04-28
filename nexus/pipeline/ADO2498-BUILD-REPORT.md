# ADO#2498 — BUILD Report

## Cycle 1 Commit
- **Hash:** `d4c0656`
- **Message:** `feat(nexus#2498): integrate IWiClassifier into ArtifactGenerationService`
- **Branch:** `main`

## Cycle 1 Files Modified
1. `nexus/src/FortressNexus.Web/Models/DTOs/AdoWorkItemDto.cs` — Added classification fields (`WiTemplate`, `IsExternalDependency`, `ExternalOwner`, `TestedByTitles`)
2. `nexus/src/FortressNexus.Web/Services/ArtifactGenerationService.cs` — Injected `IWiClassifier`, added post-parse classification loop, Test Case generation loop, AC parsing
3. `nexus/src/FortressNexus.Web/Services/StubAdoService.cs` — Map classification fields from DTO→WorkItemRecord, set `ExternalDependencyCount` on ArtifactSet

---

## Cycle 2 Commit
- **Hash:** `a965b58`
- **Message:** `fix(nexus#2498): add ParentTitle property + PredecessorTitles mapping`
- **Branch:** `main`

## Cycle 2 Files Modified
1. `nexus/src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs` — Added `ParentTitle` property (string?, nullable)
2. `nexus/src/FortressNexus.Web/Data/NexusDbContext.cs` — Added EF column mapping: `parent_title VARCHAR(500) NULL`
3. `nexus/src/FortressNexus.Web/Migrations/20260428131416_AddWorkItemRecordParentTitle.cs` — New EF migration adding `parent_title` column to `work_item_records`
4. `nexus/src/FortressNexus.Web/Migrations/20260428131416_AddWorkItemRecordParentTitle.Designer.cs` — Migration designer file
5. `nexus/src/FortressNexus.Web/Migrations/NexusDbContextModelSnapshot.cs` — Updated model snapshot
6. `nexus/src/FortressNexus.Web/Models/DTOs/AdoWorkItemDto.cs` — Added `PredecessorTitles` property (`List<string>?`)
7. `nexus/src/FortressNexus.Web/Services/StubAdoService.cs` — Added `ParentTitle` and `PredecessorTitles` mapping in both `CreateWorkItemAsync` and `CreateWorkItemBatchAsync`

## Cycle 2 Build Result
```
dotnet build — Build succeeded. 0 Error(s), 1 Warning(s) (pre-existing CS8601 in FileStorageService.cs)
```

## CC Invocation
Interactive session (not pipeline invocation)

## Cycle 2 Fix Confirmation
- [x] **C1 — ParentTitle:** `WorkItemRecord.ParentTitle` property added, EF column mapping (`parent_title VARCHAR(500) NULL`) configured, migration `AddWorkItemRecordParentTitle` created, mapped in both StubAdoService methods
- [x] **C2 — PredecessorTitles:** `AdoWorkItemDto.PredecessorTitles` property added, mapped in both StubAdoService methods (`CreateWorkItemAsync` and `CreateWorkItemBatchAsync`). AI response JSON auto-deserializes via `PropertyNameCaseInsensitive = true` in `ArtifactGenerationService.ParseWorkItems`.
