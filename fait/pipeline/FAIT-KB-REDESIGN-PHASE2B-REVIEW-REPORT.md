# Review Report: FAIT KB Redesign — Phase 2b

**Reviewer:** Hawkeye  
**Commit:** `83b6963`  
**Review Cycle:** 1 of 2  
**Date:** 2026-03-09  

---

## Verdict: NEEDS-CHANGES

Core architecture of Phase 2b is sound and the changes to `KnowledgeBaseService.cs` are correct. However, there are **3 Important issues** where callers of `StartIngestionAsync()` were not updated alongside the new `tier` parameter — they default to `KbTier.Personal` when they should be routing to `KbTier.Team` (2 callers) and `KbTier.Project` (3 callers). These are silent misfires: documents upload to the right S3 bucket, but ingestion triggers the wrong Bedrock KB. No data loss, but the new KBs will not be indexed promptly.

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Result |
|---|---|
| `appsettings.json` config keys ↔ `KnowledgeBaseService.cs` `_config[...]` reads | ✅ All 4 KB IDs match |
| `appsettings.json` config keys ↔ `KbDocumentService.cs` property reads | ✅ All 8 KB/DS ID keys match |
| `KbDocumentService.cs` metadata `"ownerId"` key ↔ `KnowledgeBaseService.cs` `RetrievePersonalAsync` filter | ✅ Match |
| `KbDocumentService.cs` metadata `"teamId"` key ↔ `KnowledgeBaseService.cs` `RetrieveTeamAsync` filter | ✅ Match |
| `KbDocumentService.cs` metadata `"projectId"` key ↔ `KnowledgeBaseService.cs` `RetrieveProjectAsync` filter | ✅ Match |
| `KbDocumentService.StartIngestionAsync(KbTier)` new signature ↔ all call sites | ❌ 5 callers still use no-arg version (see Important #1–3) |
| `RetrieveMultiQueryAsync` removed ↔ all callers | ✅ Only caller (`ChatView.razor`) updated correctly |
| `RepairPersonalKbMetadataAsync` removed ↔ all callers | ✅ Only caller (`DatabaseInitializationService.cs`) removed |
| `FortressKbId` / `PersonalTeamKbId` config keys removed ↔ all `_config[...]` reads | ✅ No remaining references |
| `ChatView.razor` Layer 1 guard ↔ Task Brief spec | ❌ Guard is `if (hasCorpKb || hasPersonalKb)` — not `if (hasCorpKb)` only (see Important #4) |
| `KbDocumentService.UploadDocumentAsync` Corp path | ❌ No `KbTier.Corporate` case for S3 key or metadata (see Important #5) |

**Undocumented Dependencies Searched:**

- `grep -rn "StartIngestionAsync" src/` — found 5 no-arg callers not in the 6-file change set
- `grep -rn "RetrieveAsync\|RetrieveMultiQuery" src/` — no stray callers of the removed methods
- `grep -rn "RepairPersonalKb" src/` — no remaining callers
- `grep -rn "FortressKbId\|PersonalTeamKbId" src/` — clean, no remaining references

---

## Critical Issues — 0

None found.

---

## Important Issues — 5

### I1: `KnowledgeBaseManagement.razor` — Team document upload triggers Personal KB ingestion

- **File:** `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` (lines 687, 710)
- **Category:** Missing ingestion path
- **Issue:** After uploading or deleting a **Team KB document**, `StartIngestionAsync()` is called with no tier argument. The default is `KbTier.Personal`, so the ingestion job is fired against the Personal Bedrock KB — not the Team KB that just received the document. Team documents land in S3 at `kb-docs/teams/{teamId}/...` but the Team Bedrock KB (`NRGEACKSBJ`) is never told to re-index.
- **Evidence:**
  ```csharp
  // KnowledgeBaseManagement.razor:686-687 — UploadTeamDocument
  await KbDocumentService.UploadDocumentAsync(stream, file.Name, file.ContentType, KbTier.Team, ...);
  await KbDocumentService.StartIngestionAsync();  // ← defaults to KbTier.Personal — wrong KB
  
  // KnowledgeBaseManagement.razor:710 — DeleteTeamDocument
  try { await KbDocumentService.StartIngestionAsync(); }  // ← same problem
  ```
- **Impact:** Team documents silently never appear in team search results until a manual ingestion trigger or scheduled sync.
- **Fix:**
  ```diff
  - await KbDocumentService.StartIngestionAsync();
  + await KbDocumentService.StartIngestionAsync(KbTier.Team);
  ```
  Apply to both the upload path (line 687) and the delete path (line 710).

---

### I2: `DocumentService.cs` — Project document upload + delete triggers Personal KB ingestion

- **File:** `src/FortressAI.Web/Services/DocumentService.cs` (lines ~85, ~230)
- **Category:** Missing ingestion path
- **Issue:** `DocumentService.UploadDocumentAsync` uploads to the Project Bedrock KB via `UploadProjectDocumentAsync`, but then calls `StartIngestionAsync()` with no tier — triggering Personal KB ingestion instead of Project KB ingestion. Same issue in `DeleteDocument` path (calls `StartIngestionAsync()` after removing a project doc from S3).
- **Evidence:**
  ```csharp
  // DocumentService.cs ~L85
  var s3Key = await _kbDocumentService.UploadProjectDocumentAsync(buffer, ...);
  // ...
  await _kbDocumentService.StartIngestionAsync();  // ← should be StartProjectIngestionAsync()
  
  // DocumentService.cs ~L230
  await _kbDocumentService.DeleteDocumentAsync(doc.S3Key);
  await _kbDocumentService.StartIngestionAsync();  // ← same problem
  ```
- **Impact:** Project KB documents never appear in project-scoped RAG results until a manual re-index. The `StartProjectIngestionAsync()` method was added specifically for this path — it's just not wired in.
- **Fix:**
  ```diff
  - await _kbDocumentService.StartIngestionAsync();
  + await _kbDocumentService.StartProjectIngestionAsync();
  ```
  Apply to both call sites in `DocumentService.cs`.

---

### I3: `DatabaseInitializationService.cs` — Clean slate re-ingestion targets wrong KB

- **File:** `src/FortressAI.Web/Services/DatabaseInitializationService.cs` (line ~558)
- **Category:** Missing ingestion path
- **Issue:** The "project clean slate" migration deletes all project documents from S3, then calls `StartIngestionAsync()` (no tier) to rebuild the vector index. This fires against the Personal KB. The Project KB (`A5U1GKN0TS`) is not told to re-index after the S3 wipe.
- **Evidence:**
  ```csharp
  // DatabaseInitializationService.cs ~L558
  await kbDocumentService.StartIngestionAsync();  // ← should be StartProjectIngestionAsync()
  ```
- **Impact:** After a clean slate migration, project documents are properly deleted from S3 but the Project KB vector store retains stale vectors until the next unrelated sync. Project searches may return ghost results for documents that no longer exist.
- **Fix:**
  ```diff
  - await kbDocumentService.StartIngestionAsync();
  + await kbDocumentService.StartProjectIngestionAsync();
  ```

---

### I4: `ChatView.razor` — Layer 1 guard incorrectly includes `hasPersonalKb`

- **File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (line 418)
- **Category:** Correctness
- **Issue:** The guard for the Corp KB retrieval block is:
  ```csharp
  if (hasCorpKb || hasPersonalKb)
  ```
  The Task Brief spec states Layer 1 should fire **only** when `hasCorpKb`. Personal KB no longer goes through Layer 1 — it routes exclusively through Layer 2 (`ForgeQuery.GetKbContextMultiQueryAsync`). When a user has personal KB enabled but Corp KB disabled (`hasCorpKb=false, hasPersonalKb=true`), this block still runs. `RetrieveCorpMultiQueryAsync` returns empty (since `_corpKbId` would be empty or the Corp KB isn't appropriate to search), so functionally it's a no-op. But it wastes query generation + API call attempts and creates misleading `kbResult.WasSearched = true` state when Corp KB didn't actually contribute.
- **Evidence:**
  ```csharp
  // Line 418
  if (hasCorpKb || hasPersonalKb)  // ← should be: if (hasCorpKb)
  {
      var corpChunks = await KbSvc.RetrieveCorpMultiQueryAsync(searchQueries, minScore: 0.35);
  ```
- **Impact:** Unnecessary API calls when personal KB is enabled but corp KB is not. Logging/diagnostics misleading.
- **Fix:**
  ```diff
  - if (hasCorpKb || hasPersonalKb)
  + if (hasCorpKb)
  ```

---

### I5: `KbDocumentService.UploadDocumentAsync` — No `KbTier.Corporate` case

- **File:** `src/FortressAI.Web/Services/KbDocumentService.cs` (lines 48–68)
- **Category:** Missing ingestion path
- **Issue:** `UploadDocumentAsync` has an S3 key builder and metadata writer that only handle `KbTier.Team` vs everything-else. `KbTier.Corporate` falls into the `else` branch and gets treated as Personal:
  - S3 key: `kb-docs/personal/{userId}/...` instead of `kb-docs/fortress/...`
  - Metadata: `{"ownerId": "..."}` instead of empty metadata (no metadata needed for Corp)
  
  The Task Brief requires Corp KB documents at `kb-docs/fortress/` with no metadata. Currently no one calls `UploadDocumentAsync(KbTier.Corporate)` — corp docs go through `ForgeService` / admin-only text entry. But the method signature accepts `KbTier.Corporate` and silently misbehaves.
- **Evidence:**
  ```csharp
  // KbDocumentService.cs:48-68
  var key = tier == KbTier.Team
      ? $"kb-docs/teams/{teamId}/{safeFilename}"
      : $"kb-docs/personal/{userId}/{safeFilename}";  // ← Corporate falls here (wrong prefix)
  
  var metadataDict = tier == KbTier.Team
      ? new Dictionary<string, object> { ["teamId"] = teamId!.Value.ToString() }
      : new Dictionary<string, object> { ["ownerId"] = userId.ToString() };  // ← Corporate gets ownerId (wrong)
  ```
- **Impact:** If a Corp KB document upload path is wired in the future, it silently stores under personal prefix with a userId metadata attribute. The Corp Bedrock KB's S3 data source likely points to `kb-docs/fortress/` — documents stored elsewhere would never be indexed.
- **Fix:**
  ```diff
  - var key = tier == KbTier.Team
  -     ? $"kb-docs/teams/{teamId}/{safeFilename}"
  -     : $"kb-docs/personal/{userId}/{safeFilename}";
  + var key = tier switch
  + {
  +     KbTier.Team      => $"kb-docs/teams/{teamId}/{safeFilename}",
  +     KbTier.Corporate => $"kb-docs/fortress/{safeFilename}",
  +     _                => $"kb-docs/personal/{userId}/{safeFilename}"
  + };
  
  - var metadataDict = tier == KbTier.Team
  -     ? new Dictionary<string, object> { ["teamId"] = teamId!.Value.ToString() }
  -     : new Dictionary<string, object> { ["ownerId"] = userId.ToString() };
  + // Corp KB docs have no metadata (structural isolation — entire KB is Corp)
  + if (tier != KbTier.Corporate)
  + {
  +     var metadataDict = tier == KbTier.Team
  +         ? new Dictionary<string, object> { ["teamId"] = teamId!.Value.ToString() }
  +         : new Dictionary<string, object> { ["ownerId"] = userId.ToString() };
  +     // ... write metadata file as before
  + }
  ```
  Note: Corp KB docs need no `.metadata.json` companion (no per-doc filter needed). Skip the metadata write entirely for `KbTier.Corporate`.

---

## Nitpicks — 1

### N1: `KbSyncRetryService` retry uses no-tier `StartIngestionAsync` — silent scoping gap
- **File:** `src/FortressAI.Web/Services/KbSyncRetryService.cs` (line 52)
- **Issue:** The retry service calls `StartIngestionAsync(throwOnConflict: true)` with no tier. This means retries only ever target the Personal KB. If Team or Project ingestion was the one that conflicted, the conflict re-try fires the wrong KB. The TODO comment in `PollIngestionJobAsync`'s backward-compat overload already acknowledges this: *"Update KbSyncRetryService to store KB type alongside job ID"*. Not blocking — the TODO is accurate and Phase 2c is the right time to fix it. Flagging so it's tracked.

---

## Positive Observations

- **Structural isolation is cleanly expressed.** The removal of `AndAll` compound filters and the single-key metadata attributes are exactly right. The comments explaining *why* the old hacks are gone are excellent and will help future maintainers.
- **`RetrieveCorpMultiQueryAsync` is a clean rename and scope reduction.** Old method accepted 3 bool/Guid parameters it mostly ignored; new method is dead simple.
- **`StartProjectIngestionAsync` is well-structured.** Correct ConflictException handling, correct EnqueueJobForPolling call, identical pattern to the parent `StartIngestionAsync`. Good.
- **`PollIngestionJobAsync` signature upgrade is done right.** The full-signature overload takes explicit kbId/dsId; the backward-compat overload defaults to Personal with a clear TODO. Clean evolution.
- **Metadata simplification is correct.** Personal: `{ownerId}`, Team: `{teamId}`, Project: `{projectId}`. No extraneous `tier` or `kbType` fields. All three match the corresponding `RetrieveXxxAsync` filters in `KnowledgeBaseService.cs` — consistency verified.
- **`appsettings.json` is clean.** Old keys gone, 8 new keys present, all empty string (ECS env vars supply values). `MinRelevanceScore`, `MaxInjectedChunks`, etc. untouched.
- **Scope creep check passed.** Auth, chat, project, and email logic is untouched. Only KB plumbing changed.
- **Build result (0 errors) consistent with diff.** No phantom changes.

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|---|---|
| 1 | 4 KB ID fields correct (`_corpKbId`, `_personalKbId`, `_teamKbId`, `_projectKbId`) | ✅ Verified — all read from correct config keys |
| 2 | `RetrieveCorpAsync` — no filter, `KbType = "Fortress"` | ✅ Verified |
| 3 | `RetrievePersonalAsync` — single `Equals("ownerId", userId)` filter, no `AndAll`, no `NotEquals(kbType,project)` | ✅ Verified |
| 4 | `RetrieveTeamAsync` — targets `_teamKbId`, `Equals("teamId", ...)` | ✅ Verified |
| 5 | `RetrieveProjectAsync` — targets `_projectKbId`, `Equals("projectId", ...)`, no `kbType` filter | ✅ Verified |
| 6 | `RetrieveCorpMultiQueryAsync` — Corp KB only, dedup by content hash, minScore threshold | ✅ Verified |
| 7 | Old `RetrieveAsync` / `RetrieveMultiQueryAsync` removed, no callers remain | ✅ Verified |
| 8 | `FormatKbContext` and `ComputeContentHash` unchanged | ✅ Verified |
| 9 | `StartIngestionAsync(KbTier)` switch — correct KB/DS pairs for Personal, Team, Corporate | ✅ Verified |
| 10 | `StartProjectIngestionAsync` targets `ProjectKbId`/`ProjectDataSourceId` | ✅ Verified |
| 11 | `PollIngestionJobAsync(jobId, kbId, dsId)` + backward-compat overload | ✅ Verified |
| 12 | Metadata simplified (Personal: ownerId; Team: teamId; Project: projectId) | ✅ Verified |
| 13 | `RepairPersonalKbMetadataAsync` removed, no callers remain | ✅ Verified |
| 14 | Corp KB upload path correct | ❌ **Not met** — `UploadDocumentAsync` falls through to personal path for `KbTier.Corporate` (I5) |
| 15 | Layer 1 corp call guard is `if (hasCorpKb)` only | ❌ **Not met** — guard is `if (hasCorpKb \|\| hasPersonalKb)` (I4) |
| 16 | Layer 2 call unchanged | ✅ Verified |
| 17 | Old config keys removed | ✅ Verified |
| 18 | New config keys present (8 total) | ✅ Verified |
| 19 | EmailController stub is safe | ✅ Verified — empty list returned, no exception |
| 20 | No unrelated changes | ✅ Verified |

---

## Summary

**Critical:** 0  
**Important:** 5  
**Nitpick:** 1  

The Phase 2b architecture is correct. All KB retrieval paths in `KnowledgeBaseService.cs` are right. The metadata simplification is right. The config migration is right. The issues are entirely in the **ingestion trigger call sites** that were not in the 6-file change set — they're all pre-existing code that needs to be updated to pass the new `tier` parameter or call `StartProjectIngestionAsync()`. Without these fixes, documents upload to the correct S3 prefixes but only the Personal Bedrock KB gets told to re-index.

**Fixes needed before PASS:**
1. `KnowledgeBaseManagement.razor` lines 687, 710 → `StartIngestionAsync(KbTier.Team)`
2. `DocumentService.cs` lines ~85, ~230 → `StartProjectIngestionAsync()`
3. `DatabaseInitializationService.cs` line ~558 → `StartProjectIngestionAsync()`
4. `ChatView.razor` line 418 → `if (hasCorpKb)` (remove `|| hasPersonalKb`)
5. `KbDocumentService.UploadDocumentAsync` → handle `KbTier.Corporate` S3 key + skip metadata write

---

_Reviewed by Hawkeye · 2026-03-09_
