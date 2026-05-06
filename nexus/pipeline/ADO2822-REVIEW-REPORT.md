# Review Report — ADO#2822

## Verdict: FAIL

**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Commit:** `eaf36b7` (bundled in `feat(nexus#2806)`)  
**File reviewed:** `src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor`

---

## Spec Compliance Check

**Brief:** `pipeline/ADO2822-brief.md` (workspace memory)

**§ Files modified:**
- `src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` — ✅ Only file changed

**§ Out of scope:**
- ✅ No out-of-scope changes detected

**§ Acceptance Criteria:**

| # | Criterion | Status |
|---|-----------|--------|
| 1 | NexusAdmin sees button, non-Admin does not | ✅ PASS |
| 2 | Confirmation dialog with org (pre-filled) + project dropdown | ⚠️ Partial — dropdown populates but selection is never used |
| 3 | Project dropdown from `GetProjectsAsync` | ✅ PASS |
| 4 | `CreateWorkItemBatchAsync` called with all WorkItemRecords | ✅ PASS (but project not forwarded) |
| 5 | Progress indicator during posting | ✅ PASS |
| 6 | Results panel: per-WI status chip + ADO link | ✅ PASS |
| 7 | DB write-back: `AdoWorkItemId`, `AdoWorkItemUrl`, `Status`, `ErrorDetail` | ✅ PASS |
| 8 | Button disabled during `_editMode` | ✅ PASS |
| 9 | Non-Admin cannot see button | ✅ PASS |
| 10 | Build compiles with zero errors | ✅ PASS |

**Spec compliance verdict:** ❌ NON-COMPLIANT (AC#2 intent violated — blocks PASS)

---

## CC Review Summary

CC reviewed all 5 files (razor, IAdoService, AdoWorkItemDto, WorkItemRecord, UserContextService). 9 of 10 checklist items passed cleanly. CC surfaced one critical defect (project selection not wired through to the service call), one important correctness issue (title-based write-back matching), one important UX hole (results not shown when write-back throws), and three nitpicks.

The critical defect was independently visible from reading the spec: the confirmation dialog collects `_selectedAdoProject` from the user, but `PostToAdoAsync` never assigns it to `_artifactSet!.AdoProjectName` before calling `CreateWorkItemBatchAsync`. The dropdown is cosmetic.

---

## Consistency Audit

**Cross-referenced:**
- `WorkItemRecord` fields ↔ `AdoWorkItemDto` fields ↔ `MapToDto` — ✅ All `WorkItemRecord` fields with DTO counterparts are mapped. `AdoWorkItemDto.StoryPoints` and `Tags` have no source in `WorkItemRecord` — data model gap, not a mapping bug.
- `IAdoService` interface ↔ calls in `PostToAdoAsync` and `OpenAdoConfirmDialogAsync` — ✅ Both calls go through the interface, no concrete class usage anywhere.
- `NexusRoles.Admin` = `"NexusAdmin"` ↔ `IsAdminAsync()` ↔ `_isAdmin` gate on button — ✅ Chain is correct.
- `IDbContextFactory` injection ↔ usage in `WriteBackResultsAsync` — ✅ Uses `DbFactory.CreateDbContextAsync()`, not the page's loaded context.

---

## Critical Issues (1)

### C1: `_selectedAdoProject` never forwarded to `CreateWorkItemBatchAsync`

| | |
|---|---|
| **File** | `NexusArtifacts.razor` |
| **Line** | ~973 (`PostToAdoAsync`) |
| **Category** | Correctness / Spec fidelity |
| **Issue** | The admin selects a project in the confirmation dialog. That selection is stored in `_selectedAdoProject` and displayed in the UI (status string + dialog label) but is never written to `_artifactSet!.AdoProjectName` before calling `CreateWorkItemBatchAsync`. `ArtifactSet` has `AdoProjectName` which is how the service routes the post. The dropdown selection has zero effect — the post always goes to whatever project was recorded at artifact generation time. |

**Evidence:**
```csharp
// PostToAdoAsync (line ~967-974)
_postingStatus = $"Posting {_workItems.Count} work items to {_selectedAdoProject}..."; // string only
StateHasChanged();

try
{
    var dtos = _workItems.Select(MapToDto).ToList();
    var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos); // _artifactSet.AdoProjectName never updated
```

**Fix:**
```diff
+ _artifactSet!.AdoProjectName = _selectedAdoProject!;
  var dtos = _workItems.Select(MapToDto).ToList();
  var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos);
```

---

## Important Issues (2)

### I1: Write-back matches by Title — fragile given editable titles

| | |
|---|---|
| **File** | `NexusArtifacts.razor` |
| **Line** | ~994 (`WriteBackResultsAsync`) |
| **Category** | Correctness |
| **Issue** | The write-back loop finds DB records by `ArtifactSetId + Title`. Titles are user-editable on this same page. If a user edits a title during the session and then posts, the match silently fails (`record is null` → `continue`) — the ADO result is lost and the DB is not updated. The `results` list from `CreateWorkItemBatchAsync` originates from the DB and carries valid `Id` values. Matching by `result.Id` is unambiguous. |

**Evidence:**
```csharp
var record = await db.WorkItemRecords
    .FirstOrDefaultAsync(w => w.ArtifactSetId == _artifactSet!.Id && w.Title == result.Title);
if (record is null) continue;
```

**Fix:**
```diff
- var record = await db.WorkItemRecords
-     .FirstOrDefaultAsync(w => w.ArtifactSetId == _artifactSet!.Id && w.Title == result.Title);
+ var record = await db.WorkItemRecords.FindAsync(result.Id);
  if (record is null) continue;
```

### I2: `_postResults` not shown when `WriteBackResultsAsync` throws

| | |
|---|---|
| **File** | `NexusArtifacts.razor` |
| **Line** | ~974–975 (`PostToAdoAsync`) |
| **Category** | UX / Correctness |
| **Issue** | `_postResults = results` is assigned AFTER `await WriteBackResultsAsync(results)`. If the DB write-back throws (network issue, EF error), the exception bubbles to the catch block — the snackbar fires but `_postResults` remains `null`. The results panel never renders even though ADO work items may have been created successfully. The admin loses the link list. |

**Evidence:**
```csharp
var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos);
await WriteBackResultsAsync(results);
_postResults = results;  // ← never reached if WriteBack throws
```

**Fix:**
```diff
  var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos);
+ _postResults = results;   // show results even if write-back fails
  await WriteBackResultsAsync(results);
- _postResults = results;
  Snackbar.Add(...);
```

---

## Nitpicks (3)

- **N1:** Results panel header always shows green `CheckCircle` icon regardless of error count. When errors > 0, icon/color should reflect degraded outcome. (`NexusArtifacts.razor` ~L451)
- **N2:** Confirmation dialog uses inline `Style="min-width:400px; max-width:500px;"`. Brief only allows `Style="flex:1"` as an inline style exception. Move to a CSS class. (~L488)
- **N3:** Confirm dialog shows `@_workItems.Count` total items, which includes external dependencies. These may not be posted to ADO or may be handled differently by the service. The count may mislead the admin. (~L515)

---

## Positive Observations

- `IAdoService` interface used correctly throughout — no concrete class leakage.
- `IDbContextFactory` write-back pattern is correct for Blazor Server.
- `MapToDto` maps all 10 available source fields with no silent omissions.
- `_isAdmin` gate is correctly isolated from `_isEditor` (Reviewer can't see button).
- Snackbar on both error paths (project load failure + post failure) — good UX.
- Button disabled during both `_editMode` and `_isPosting` — no double-submit window.
- `Target="_blank"` on ADO icon links.

---

## What Tony Needs to Fix

**C1 is the blocking issue.** Two lines change:

1. **In `PostToAdoAsync`**, add before the service call:
   ```csharp
   _artifactSet!.AdoProjectName = _selectedAdoProject!;
   ```

2. **In `WriteBackResultsAsync`**, replace title-based match with ID match:
   ```diff
   - var record = await db.WorkItemRecords
   -     .FirstOrDefaultAsync(w => w.ArtifactSetId == _artifactSet!.Id && w.Title == result.Title);
   + var record = await db.WorkItemRecords.FindAsync(result.Id);
   ```

3. **In `PostToAdoAsync`**, move `_postResults = results` to before `WriteBackResultsAsync`:
   ```diff
     var results = await AdoService.CreateWorkItemBatchAsync(_artifactSet!, dtos);
   + _postResults = results;
     await WriteBackResultsAsync(results);
   - _postResults = results;
   ```

Nitpicks (N1–N3) are optional but N1 especially is a quick win.

Once these three changes are made, submit for Cycle 2.
