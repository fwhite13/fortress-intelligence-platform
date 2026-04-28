# ADO#2500 BUILD Report

## Commit
- **Hash:** `5159377`
- **Message:** `feat(nexus#2500): NexusArtifacts UI — WI tree, template badges, predecessor badges, external deps panel, test case grouping`

## Build Result
```
dotnet build — Build succeeded. 0 Error(s), 1 Warning(s) (pre-existing CS8601 in FileStorageService.cs)
```

## Files Modified
| File | Change |
|------|--------|
| `nexus/src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` | **NEW** — Full WI tree page at `/nexus/{id}/artifacts` |
| `nexus/src/FortressNexus.Web/Controllers/NexusArtifactsController.cs` | **NEW** — `GET /nexus/{id}/artifacts/external-dependencies` endpoint |
| `nexus/src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` | Added "View Work Items" nav button |

## CC Invocation
Interactive session (not pipeline-invoked)

## External Dependencies List Strategy
**Filter from loaded list** — `_workItems.Where(w => w.IsExternalDependency)`. The `ArtifactSet.WorkItemRecords` are already loaded via EF Include, so filtering in-memory is simpler and avoids an extra HTTP call. The `GET /nexus/{id}/artifacts/external-dependencies` endpoint is provided for external/API consumers.

## Self-Review Checklist
- [x] External Dependencies panel rendered when `ExternalDependencyCount > 0`
- [x] External Dependencies panel shows owner, title, copy-brief button
- [x] WI Template badges: Infrastructure (Info/teal), Migration (Secondary/purple), TestCase (Primary/blue)
- [x] Standard WIs show no template badge
- [x] Predecessor badges inline after WI title
- [x] Cross-Epic predecessors shown with "Cross-Epic:" prefix and Warning color
- [x] Unresolved predecessors shown with "[!]" prefix, Error color, and tooltip
- [x] Same-Epic predecessors shown with "Blocked by:" prefix and Warning color
- [x] Test Cases grouped under parent User Story in collapsible subsection
- [x] Test Cases excluded from main Epic > Feature > Story > Task tree
- [x] Copy-to-clipboard via JS interop
- [x] MudBlazor v7 compatible (no `IsInitiallyExpanded`, used `Expanded` instead)
