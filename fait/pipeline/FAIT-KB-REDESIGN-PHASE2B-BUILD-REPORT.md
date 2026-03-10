# FAIT KB Redesign — Phase 2b Build Report
**Date:** 2026-03-09
**Branch:** `main`
**Commit:** `83b6963`
**Build Result:** ✅ 0 Errors, 27 Warnings (all pre-existing)

---

## Summary

Refactored from a 2-KB-ID design (`FortressKbId` + `PersonalTeamKbId`) to 4 separate Bedrock KB IDs with structural isolation. Each retrieval method now targets its own dedicated KB — no compound metadata filters, no cross-type exclusion hacks.

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/KnowledgeBaseService.cs` | Full rewrite — 4 KB ID fields, 4 retrieval methods, new `RetrieveCorpMultiQueryAsync` |
| `src/FortressAI.Web/Services/KbDocumentService.cs` | Added KB ID/DataSource props, updated `StartIngestionAsync`, added `StartProjectIngestionAsync`, updated `PollIngestionJobAsync`, simplified metadata, removed `RepairPersonalKbMetadataAsync` |
| `src/FortressAI.Web/Components/Chat/ChatView.razor` | Updated Layer 1 call site + KB decision matrix comment |
| `src/FortressAI.Web/appsettings.json` | Replaced KnowledgeBase section with 4-KB structure |
| `src/FortressAI.Web/Controllers/EmailController.cs` | Fixed call to removed `RetrieveAsync` — KB lookup deferred (no userId in email flow) |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | Removed `RepairPersonalKbMetadataAsync` call — replaced with comment |

---

## Old Config Keys Removed

| Removed Key | Reason |
|---|---|
| `KnowledgeBase:FortressKbId` | Replaced by `KnowledgeBase:CorpKbId` |
| `KnowledgeBase:PersonalTeamKbId` | Split into `PersonalKbId` + `TeamKbId` |

---

## New Config Keys Added

| New Key | Purpose |
|---|---|
| `KnowledgeBase:CorpKbId` | Corp KB ID (was `FortressKbId`) |
| `KnowledgeBase:PersonalKbId` | Personal KB ID (split from `PersonalTeamKbId`) |
| `KnowledgeBase:TeamKbId` | Team KB ID (split from `PersonalTeamKbId`) |
| `KnowledgeBase:ProjectKbId` | Project KB ID (new) |
| `KnowledgeBase:CorpDataSourceId` | Corp data source for ingestion |
| `KnowledgeBase:PersonalDataSourceId` | Personal data source (existed, now explicit) |
| `KnowledgeBase:TeamDataSourceId` | Team data source for ingestion |
| `KnowledgeBase:ProjectDataSourceId` | Project data source for ingestion |

---

## Methods Removed

| Method | File | Reason |
|---|---|---|
| `RetrieveAsync(query, useFortressKb, usePersonalKb, userId)` | `KnowledgeBaseService.cs` | Replaced by dedicated per-KB methods |
| `RetrieveMultiQueryAsync(queries, useFortressKb, usePersonalKb, userId, minScore)` | `KnowledgeBaseService.cs` | Replaced by `RetrieveCorpMultiQueryAsync` (scoped to Corp only) |
| `RepairPersonalKbMetadataAsync()` | `KbDocumentService.cs` | No longer needed — structural isolation means no metadata conflicts between KB types |

---

## Methods Added / Renamed

| Method | File | Notes |
|---|---|---|
| `RetrieveCorpAsync(string query)` | `KnowledgeBaseService.cs` | Was `RetrieveAsync(useFortressKb=true)`. No filter — entire KB is Corp. NumberOfResults=3, Score>0.3 |
| `RetrievePersonalAsync(string query, Guid userId)` | `KnowledgeBaseService.cs` | Now targets `_personalKbId`. Single `Equals("ownerId")` filter — no compound AndAll. NumberOfResults=5 |
| `RetrieveTeamAsync(string query, int teamId)` | `KnowledgeBaseService.cs` | Now targets `_teamKbId`. Single `Equals("teamId")` filter. NumberOfResults=5 |
| `RetrieveProjectAsync(string query, Guid projectId)` | `KnowledgeBaseService.cs` | Now targets `_projectKbId`. Single `Equals("projectId")` filter — no kbType compound filter. NumberOfResults=8 |
| `RetrieveCorpMultiQueryAsync(queries, minScore)` | `KnowledgeBaseService.cs` | NEW. Fans out queries to Corp KB in parallel, deduplicates, filters by minScore, returns top 6 |
| `StartIngestionAsync(KbTier tier, bool throwOnConflict)` | `KbDocumentService.cs` | Now accepts `KbTier` param (default: Personal). Routes to correct KB+DataSource via switch expression |
| `StartProjectIngestionAsync(bool throwOnConflict)` | `KbDocumentService.cs` | NEW. Separate method for project KB ingestion (Project not in KbTier enum) |
| `PollIngestionJobAsync(string jobId, string kbId, string dsId)` | `KbDocumentService.cs` | Now accepts explicit kbId/dsId params instead of hardcoding PersonalTeamKbId |
| `PollIngestionJobAsync(string jobId)` | `KbDocumentService.cs` | NEW convenience overload defaulting to Personal KB for backward compat with `KbSyncRetryService` |

---

## Metadata Simplification

### `UploadDocumentAsync` (Personal/Team docs)

| Field | Old | New | Reason |
|---|---|---|---|
| `tier` | ✅ written | ❌ removed | Not needed — structural isolation |
| `ownerId` | ✅ written (always) | ✅ written (Personal only) | Still needed for per-user filtering within Personal KB |
| `teamId` | ✅ written (Team only) | ✅ written (Team only) | Still needed for per-team filtering within Team KB |

### `UploadProjectDocumentAsync` (Project docs)

| Field | Old | New | Reason |
|---|---|---|---|
| `kbType` | ✅ written (`"project"`) | ❌ removed | Not needed — Project KB structurally contains ONLY project docs |
| `projectId` | ✅ written | ✅ written | Still needed for per-project filtering within Project KB |
| `ownerId` | ✅ written | ❌ removed | Not needed — structural isolation |

---

## ChatView.razor Changes

- Updated KB Decision Matrix comment to reflect 4-KB architecture (removed Layer 1/Layer 2 two-layer framing)
- Replaced `KbSvc.RetrieveMultiQueryAsync(searchQueries, useFortressKb: _fortressKbEnabled, usePersonalKb: false, ...)` with `KbSvc.RetrieveCorpMultiQueryAsync(searchQueries, minScore: 0.35)`

---

## Build Result

```
Build succeeded.
    0 Error(s)
    27 Warning(s)  ← all pre-existing (CS8602 nullable refs, MUD0002 MudBlazor analyzer, BedrockRuntime1002)
```

---

## Stale Reference Grep Output

```bash
grep -rn "FortressKbId|PersonalTeamKbId|_fortressKbId|_personalTeamKbId|RetrieveMultiQueryAsync|RepairPersonalKbMetadataAsync" \
  src/FortressAI.Web/Services/ src/FortressAI.Web/Components/ --include="*.cs" --include="*.razor"
```

**Result:** One comment-only hit in `DatabaseInitializationService.cs:650` — a code comment explaining the removal. No live references.

---

## Additional Call Sites Fixed

Two callers of removed methods were found outside the primary spec scope and fixed:

| File | Issue | Fix |
|---|---|---|
| `EmailController.cs:141` | Called `kbService.RetrieveAsync(...)` — removed method | Replaced with empty list + comment noting email flow needs userId wiring before KB lookup can work |
| `DatabaseInitializationService.cs:658` | Called `kbDocSvc.RepairPersonalKbMetadataAsync()` — removed method | Replaced with comment explaining removal |

---

## ECS Environment Variables Required

These env vars need to be set in the ECS task definition before deploying:

| Env Var | Value |
|---|---|
| `KnowledgeBase__CorpKbId` | `WYSKBKWHPL` |
| `KnowledgeBase__PersonalKbId` | `ZCEZCJGHQC` |
| `KnowledgeBase__TeamKbId` | `NRGEACKSBJ` |
| `KnowledgeBase__ProjectKbId` | `A5U1GKN0TS` |
| `KnowledgeBase__CorpDataSourceId` | TBD (Fred to provide) |
| `KnowledgeBase__PersonalDataSourceId` | TBD (Fred to provide) |
| `KnowledgeBase__TeamDataSourceId` | TBD (Fred to provide) |
| `KnowledgeBase__ProjectDataSourceId` | TBD (Fred to provide) |

**Remove:** `KnowledgeBase__FortressKbId`, `KnowledgeBase__PersonalTeamKbId`

---

## Notes

- `KbSyncRetryService` calls `PollIngestionJobAsync(jobId)` (the overload defaulting to Personal KB) — this is correct for now since the retry service currently only queues personal/team uploads. A TODO comment is in the convenience overload for future multi-KB polling support.
- `KbQueryService.cs` had no stale references (confirmed via grep before changes).
- `appsettings.json` keeps `PersonalDataSourceId` — it's a new key in the 4-DataSource structure, not a stale old key.

---

## Review Cycle 2 Fixes

**Date:** 2026-03-09
**Commit:** `b5f9b50`
**Build Result:** ✅ 0 Errors, 27 Warnings (all pre-existing, unrelated)

All 5 issues from the review report resolved:

| ID | File | Fix | Status |
|----|------|-----|--------|
| I1 | `KnowledgeBaseManagement.razor` | Team ingestion calls now pass `KbTier.Team` — both upload (line 687) and delete (line 710) paths fixed | ✅ |
| I2 | `DocumentService.cs` | Project upload (line 85) and delete (line 230) paths now call `StartProjectIngestionAsync()` instead of `StartIngestionAsync()` | ✅ |
| I3 | `DatabaseInitializationService.cs` | "Project clean slate" migration block (line 558) now calls `StartProjectIngestionAsync()` | ✅ |
| I4 | `ChatView.razor` | Layer 1 guard (line 418) corrected to `if (hasCorpKb)` — Personal KB routes exclusively through Layer 2 (ForgeQuery) | ✅ |
| I5 | `KbDocumentService.cs` | S3 key builder updated to switch expression with explicit `KbTier.Corporate => kb-docs/fortress/` case; metadata write wrapped in `if (tier != KbTier.Corporate)` guard (structural isolation — no per-doc filter needed for Corp KB) | ✅ |

**Verification:**
```
grep results confirmed correct routing in all 3 ingestion files.
ChatView.razor line 418: if (hasCorpKb)  ✓
Build: 0 errors ✓
```
