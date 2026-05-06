# BUILD Assignment: ADO#2821
## NEXUS Decomp Tree Editor: inline hierarchy editor for generated WI set

**WI:** ADO#2821 | Project: Fortress | Feature: #2816 | Epic: #2793
**Risk:** medium | **Pipeline path:** full (Tony → Clint → Rhodey → Natasha)
**Spec file:** `/home/fredw/.openclaw/workspace/memory/projects/nexus-tree-editor-spec-2026-05-06.md`
**ADO attribution prefix for all comments:** `**[Tony Stark — BUILD cycle 1]**`

---

## Pre-work: Read the spec

**Read the full spec at** `memory/projects/nexus-tree-editor-spec-2026-05-06.md` before writing a single line of code.

---

## Architecture Notes (read before coding)

### Hierarchy is tracked by ParentTitle string — NOT by a ParentId FK

`WorkItemRecord` uses `ParentTitle` (string) to record hierarchy. There is **no `ParentId` integer FK**. All `GetChildren()` lookups in `NexusArtifacts.razor` use `w.ParentTitle == parentTitle`. This is the existing pattern — do not add a `ParentId` column.

Implications:
- **Reparent** = update `WorkItemRecord.ParentTitle` to the new parent's `Title`
- **Cascade delete** = service must walk the tree recursively by `ParentTitle`, collecting all descendants, then delete in leaf-first order (or let service handle it). The DB has `OnDelete(DeleteBehavior.Restrict)` on ArtifactSet FK — no DB-level cascade on parent chain.
- **New WI** = `ParentTitle` set to the clicked parent's `Title`

### AcceptanceCriteria is a nullable string field

`WorkItemRecord.AcceptanceCriteria` is `string?` stored as `text` in the DB. It is **newline-delimited** (existing convention — match it). The AC list UI splits on `\n` and joins on `\n` for save.

### Existing tree structure

`NexusArtifacts.razor` (367 lines) uses `GetChildren(wiType, parentTitle)` for lookup. Edit Mode is a toggle on this same page — do **not** rewrite it; extend it.

### Auth pattern

`UserContextService` already has `IsAdminAsync()` and `IsReviewerAsync()`. Add a convenience method:
```csharp
public async Task<bool> IsNexusEditorAsync()
{
    var authState = await _authStateProvider.GetAuthenticationStateAsync();
    return authState.User.IsInRole(NexusRoles.Admin) || authState.User.IsInRole(NexusRoles.Reviewer);
}
```

### API controller

Add new endpoints to `NexusArtifactsController.cs` (existing file at `Controllers/NexusArtifactsController.cs`). Auth guard via `[Authorize]` + role check in handler body using `UserContextService`. Return `403` if not Admin/Reviewer.

---

## What to Build

### 1. UserContextService — add IsNexusEditorAsync()

```csharp
public async Task<bool> IsNexusEditorAsync()
{
    var authState = await _authStateProvider.GetAuthenticationStateAsync();
    return authState.User.IsInRole(NexusRoles.Admin) || authState.User.IsInRole(NexusRoles.Reviewer);
}
```

### 2. New API endpoints on NexusArtifactsController

Route prefix: `/api/nexus/{submissionId}/artifacts/`

**All 6 endpoints:**

```csharp
// PATCH wi/{wiId}/title
// Body: { "title": "..." }
// Updates WorkItemRecord.Title in DB
// Also updates all WorkItemRecord.ParentTitle references in the same ArtifactSet

// PATCH wi/{wiId}/description
// Body: { "description": "..." }
// Updates WorkItemRecord.Description

// PATCH wi/{wiId}/ac
// Body: { "acceptanceCriteria": "..." }  (newline-delimited string)
// Updates WorkItemRecord.AcceptanceCriteria

// PATCH wi/{wiId}/parent
// Body: { "parentTitle": "..." }
// Updates WorkItemRecord.ParentTitle; validate target type compatibility

// POST wi
// Body: { WorkItemRecord fields }
// Inserts new WorkItemRecord with ArtifactSetId; Title = "New [Type]"; ParentTitle = parent's title
// Returns created record (with DB-assigned Id)

// DELETE wi/{wiId}
// Recursively deletes WI + all descendants (walk by ParentTitle)
// Returns count of deleted records
```

**PATCH wi/{wiId}/title cascade rule:** When a WI's title is updated, all other `WorkItemRecord` rows in the same ArtifactSet that have `ParentTitle == oldTitle` must also be updated to the new title. Do this in a single DB transaction.

**Auth guard on all endpoints:**
```csharp
var isEditor = await UserContextService.IsNexusEditorAsync();
if (!isEditor) return Forbid();
```

**Validate submission ownership** (same as existing GET endpoint: check `submissionId` maps to a real submission, verify user is owner or admin, then check ArtifactSet belongs to that submission).

### 3. NexusArtifacts.razor — Edit Mode

Extend the existing Razor component. Do **not** rewrite it — add Edit Mode on top.

**New state fields:**
```csharp
private bool _editMode = false;
private bool _isEditor = false;  // set in OnInitializedAsync from IsNexusEditorAsync()
```

**In OnInitializedAsync:** add:
```csharp
_isEditor = await UserContextService.IsNexusEditorAsync();
```

#### 3a. Page header buttons

Replace the existing `<MudText Typo="Typo.h5">` header block with:
```
[Edit] button (visible only if _isEditor && !_editMode) → sets _editMode = true
[Done Editing] button (visible only if _editMode) → sets _editMode = false
[Post to ADO] button — always visible; disabled+tooltip if _editMode (covered by #2822, add disabled stub now)
```

Edit Mode indicator: when `_editMode`, show a `MudChip` or `MudAlert` with "✏️ EDIT MODE" near the header.

#### 3b. WI row changes in Edit Mode

For each WI (Epic, Feature, Story, Task, TC) in Edit Mode, add to the row's div:

**Title:** Replace `<MudText>@wi.Title</MudText>` with conditional:
```razor
@if (_editMode)
{
    <MudTextField @bind-Value="wi.Title"
                  Variant="Variant.Outlined"
                  Margin="Margin.Dense"
                  Style="min-width:300px"
                  @onblur="@(() => SaveTitle(wi))"
                  @onkeydown="@((e) => { if (e.Key == "Enter") SaveTitle(wi); })" />
}
else
{
    <MudText>@wi.Title</MudText>
}
```

**Description (collapsible, Edit Mode only):**
Under each WI row in Edit Mode, add a collapsed `MudExpansionPanel` labeled "Description ▾":
```razor
<MudTextField @bind-Value="wi.Description"
              Variant="Variant.Outlined"
              Lines="3"
              @onblur="@(() => SaveDescription(wi))" />
```

**AC list (collapsible, Edit Mode only — Stories only):**
For User Story WIs in Edit Mode, add a collapsed "Acceptance Criteria ▾" section:
- Split `wi.AcceptanceCriteria` on `\n` → list of strings for display
- Each AC item: `MudTextField` single-line + `[×]` remove button → on remove: update local list + call SaveAc(wi)
- `[+ Add AC]` button at bottom → appends empty string, focuses new field
- On blur of any AC field: rejoin list with `\n` → call SaveAc(wi)

**Move dropdown (Stories, Tasks, TCs, Features in Edit Mode):**
```razor
<MudMenu Label="Move ▾" Icon="@Icons.Material.Filled.DragIndicator" Size="Size.Small">
    @foreach (var target in GetValidParents(wi))
    {
        <MudMenuItem OnClick="@(() => MoveWi(wi, target.Title))">@target.Title</MudMenuItem>
    }
</MudMenu>
```
`GetValidParents(wi)` returns:
- Feature → all Epics in artifact set
- Story → all Features in artifact set
- Task → all User Stories in artifact set
- TC → all User Stories in artifact set
- Epic → empty (no Move option)

**Delete button (Edit Mode only):**
```razor
<MudIconButton Icon="@Icons.Material.Filled.Delete"
               Color="Color.Error"
               Size="Size.Small"
               OnClick="@(() => ConfirmDelete(wi))" />
```

**Add buttons (Edit Mode only):**
At the bottom of each group in Edit Mode:
- After Features list (within Epic): `[+ Add Feature]`
- After Stories list (within Feature): `[+ Add Story]`
- After Tasks list (within Story): `[+ Add Task]`
- At bottom of TC section (within Story): `[+ Add Test Case]`

#### 3c. Save methods (C# code section)

```csharp
private async Task SaveTitle(WorkItemRecord wi)
{
    // POST PATCH /api/nexus/{Id}/artifacts/wi/{wi.Id}/title
    // On failure: show Snackbar error, revert wi.Title to original
}

private async Task SaveDescription(WorkItemRecord wi)
{
    // PATCH .../wi/{wi.Id}/description
}

private async Task SaveAc(WorkItemRecord wi)
{
    // PATCH .../wi/{wi.Id}/ac  body: { acceptanceCriteria: joined string }
}

private async Task MoveWi(WorkItemRecord wi, string newParentTitle)
{
    // PATCH .../wi/{wi.Id}/parent  body: { parentTitle: newParentTitle }
    // On success: update wi.ParentTitle locally, call StateHasChanged()
}

private async Task ConfirmDelete(WorkItemRecord wi)
{
    var children = GetAllDescendants(wi);
    // Show MudDialog:
    //   no children: "Delete '[Title]'? Cannot be undone." [Cancel][Delete]
    //   has children: "Delete '[Title]' and its [N] children? Cannot be undone." [Cancel][Delete with children]
    // On confirm: DELETE /api/nexus/{Id}/artifacts/wi/{wi.Id}
    // On success: remove wi + descendants from _workItems, StateHasChanged()
}

private async Task AddWi(string wiType, string parentTitle)
{
    // POST /api/nexus/{Id}/artifacts/wi
    // Body: { WorkItemType: wiType, ParentTitle: parentTitle, Title: "New {wiType}", ArtifactSetId: _artifactSet.Id }
    // On success: add returned record to _workItems, StateHasChanged()
}

private List<WorkItemRecord> GetAllDescendants(WorkItemRecord wi)
{
    // Walk _workItems recursively by ParentTitle
}
```

**HTTP calls:** Use `HttpClient` injected into the Razor component (or `IHttpClientFactory`) to call the local API endpoints. Use `@inject HttpClient Http` and configure the base URL appropriately. Add `Authorization` header if needed (cookies should handle this in Blazor Server + cookie auth, but verify).

**Snackbar on save failure:** `Snackbar.Add("Save failed — check connection.", Severity.Error)`

#### 3d. Delete dialog

Use `IDialogService` for the MudBlazor dialog. The existing codebase should already inject `IDialogService` or can be added.

---

## 4. EF Migration

**Check first:** Does `work_item_records` table already have an `acceptance_criteria` column? Run:
```bash
mysql -h ... -e "SHOW COLUMNS FROM work_item_records LIKE 'acceptance_criteria';"
```
Or check the latest migration files. If the column doesn't exist, add an EF migration:
```csharp
// AddAcceptanceCriteriaToWorkItemRecord
migrationBuilder.AddColumn<string>(
    name: "acceptance_criteria",
    table: "work_item_records",
    type: "text",
    nullable: true);
```

If it already exists, **no migration needed**.

Also add `AcceptanceCriteria` to `NexusDbContext.cs` mapping if not already mapped:
```csharp
entity.Property(e => e.AcceptanceCriteria).HasColumnName("acceptance_criteria").HasColumnType("text");
```

---

## 5. Open Questions Resolved

- **OQ-1 (CASCADE):** No DB-level cascade on ParentTitle chain — service layer walks tree manually.
- **OQ-2 (Reviewer delete):** Allow delete for NexusReviewer in v1.
- **OQ-3 (AC storage):** Newline-delimited string — match `WorkItemRecord.AcceptanceCriteria` field.
- **OQ-4 (IArtifactEditorService):** Tony's call — inline service logic in controller is fine; no new interface required unless Tony prefers it.

---

## Build Report Format

```markdown
# Build Report — ADO#2821

## CC Invocation
`cat brief.md | claude --model sonnet --print --dangerously-skip-permissions`

## Changes
- Files modified (list each)
- New files (if any)
- Migration (if needed)

## AcceptanceCriteria Check
1. Edit Mode toggle visible to NexusAdmin/NexusReviewer — [PASS/FAIL]
2. Title editable, persists on blur — [PASS/FAIL]
3. Description/AC collapsible, persists on blur — [PASS/FAIL]
4. Add WI at any level, appears immediately — [PASS/FAIL]
5. Delete with cascade confirmation, all descendants removed — [PASS/FAIL]
6. Move dropdown, type-compatible targets only — [PASS/FAIL]
7. All badges preserved in Edit Mode — [PASS/FAIL]
8. Post to ADO disabled while Edit Mode active — [PASS/FAIL]
9. Non-editor cannot see Edit button, API returns 403 — [PASS/FAIL]
10. Save failure shows snackbar, field reverts — [PASS/FAIL]
11. Tree re-renders after add/delete/move without full page reload — [PASS/FAIL]

## Build self-review checklist
- [ ] ParentTitle cascade on title rename
- [ ] AcceptanceCriteria column verified/migrated
- [ ] IsNexusEditorAsync() added to UserContextService
- [ ] All 6 PATCH/POST/DELETE endpoints added
- [ ] Auth guard (IsNexusEditorAsync → 403) on all write endpoints
- [ ] Submission ownership validation on all endpoints
- [ ] Save-failure snackbar + field revert wired
- [ ] No `ParentId` FK added (use ParentTitle pattern)
- [ ] GetAllDescendants walks by ParentTitle, not FK
- [ ] Post to ADO button stub present (disabled in Edit Mode)
```

---

## ADO Comment

After build completes, post:
```
mcporter call devops.add_comment project=Fortress id=2821 text="**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: Edit Mode toggle, 6 API endpoints (PATCH title/desc/ac/parent, POST wi, DELETE wi), cascade delete, reparent via Move dropdown, Add buttons at each level. Build: SUCCEEDED."
```

---

## MANDATORY: Use Claude Code CLI

```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30

cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/nexus/`

The brief file is `/home/fredw/projects/fip/nexus/pipeline/ADO2821-PLAN.md`. Write your own CC brief file summarizing the work, then pipe to CC. CC Sonnet default. Do not use `edit`/`write` tools directly.
