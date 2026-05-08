# NEXUS :55 Work Tracking
**Created:** Fri 2026-05-08 18:06 EDT
**Session:** main-2026-05-08 (direct CC session, no pipeline)
**Target task def:** :55

---

## Items (in build order)

### 1. ✅ DONE TC scan dupe crash fix
**File:** `Services/ArtifactGenerationService.cs` line ~89
**Fix:** Change `items.ToDictionary(w => w.Title ?? "", w => w)` to deduplicate by title first (last-wins on dupe).
```csharp
var titleMap = items
    .GroupBy(w => w.Title ?? "")
    .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
```
**Status:** TODO

---

### 2. ✅ DONE External deps — expandable description + skip on ADO post
**File A:** `Components/Pages/NexusArtifacts.razor`
- Remove the `?.Length > 120 ? ...[..120] + "…"` truncation
- Wrap description in a `<MudText>` that shows full text, or use `MudExpansionPanel` per ext dep card

**File B:** `Services/AdoCreationService.cs`
- In `CreateWorkItemBatchAsync`, filter out `dto.IsExternalDependency == true` before creating WIs
- Log how many were skipped: `[AdoCreationService] Skipping {N} external dependency WIs — not posted to ADO`

**Status:** TODO

---

### 3. ✅ DONE Add Epic in edit mode
**File:** `Components/Pages/NexusArtifacts.razor`
- Add "Add Epic" button in the edit mode toolbar (alongside existing add controls)
- On click: insert a new `WorkItemRecord` with `WorkItemType = "Epic"`, blank title, `ArtifactSetId` set, save to DB
- UI: immediately render it in the epic list so user can title it and add children

**Status:** TODO

---

### 4. ✅ DONE ADO project selector on review/artifacts page
**File:** `Components/Pages/NexusArtifacts.razor`
- In the page header, add a project dropdown (same `IAdoCredentialService.GetProjectsAsync` call used in decomp selector)
- Pre-populate from `_artifactSet.AdoProjectName`
- On change: `UPDATE artifact_sets SET ado_project_name = @val WHERE id = @id`
- Disable "Post to ADO" while project is loading

**Status:** TODO

---

### 5. ✅ DONE Prompt: replace person names with roles
**File:** `appsettings.Production.json` → `Nexus:Prompts:ArtifactGenSystem`
Changes:
- `"outside the pipeline (Tony, Clint, Fred)"` → `"outside the development team"`
- `externalOwner = 'Rob Nethery'` → `externalOwner = 'Network/Infrastructure Team'`  
- `externalOwner = 'ADO Admin'` → `externalOwner = 'DevOps Admin'`
- Remove/replace any other named-person examples throughout
- Also update `TcScanSystem` prompt if it contains names (check)

**Status:** TODO

---

## Deploy checklist
- [ ] All 5 items coded and verified to compile
- [ ] `docker build --no-cache` from `/home/fredw/projects/fip`
- [ ] Push to ECR as `v55`
- [ ] Register task def `:55`, force-redeploy `nexus-web`
- [ ] Verify ECS healthy
- [ ] Fred re-tests: ext dep expansion, Add Epic, project selector, then Post to ADO

## Notes
- Current artifact set in DB: id=2, spec_document_id=4, 59 WIs — do NOT wipe unless Fred requests re-decomp
- No DB migration needed for this batch
- Prompt change takes effect on next decomp (doesn't affect existing artifact set 2)
