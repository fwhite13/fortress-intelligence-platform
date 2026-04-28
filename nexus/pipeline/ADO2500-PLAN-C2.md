# BUILD Assignment: ADO#2500 — Cycle 2 (Fix Only)

## Task
**NexusArtifacts UI — Review Cycle 2**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2500

## Review result: NEEDS-CHANGES
Clint found 2 criticals + 7 important issues. Fix ALL of them — no scope creep beyond these.

---

## CRITICAL FIXES

### C1 — Cross-Epic chip must show Epic name

**Problem:** `IsCrossEpicPredecessor` returns `bool` and discards the Epic title it finds. Chip shows `"⛓ Cross-Epic: [WI title]"` — spec requires `"⛓ Cross-Epic: [Epic name] > [short title]"`.

**Fix:** Change the helper to return `string?` (the Epic name) instead of `bool`:
```csharp
private string? GetCrossEpicName(WorkItemRecord wi, string predecessorTitle)
{
    // Find the predecessor WI
    var pred = _workItems.FirstOrDefault(w =>
        string.Equals(w.Title, predecessorTitle, StringComparison.OrdinalIgnoreCase));
    if (pred == null) return null; // unresolved — not cross-Epic

    // Walk up the parent chain to find pred's Epic
    var predEpic = GetEpicTitle(pred);
    var wiEpic = GetEpicTitle(wi);

    return predEpic != null && predEpic != wiEpic ? predEpic : null;
}
```

Then in the Razor markup:
```razor
@{
    var crossEpicName = GetCrossEpicName(wi, predTitle);
    var isUnresolved = !_workItems.Any(w =>
        string.Equals(w.Title, predTitle, StringComparison.OrdinalIgnoreCase));
}
@if (isUnresolved)
{
    <MudTooltip Text="Could not be auto-linked">
        <MudChip Color="Color.Error" Size="Size.Small">⛓ [!] @Truncate(predTitle, 30)</MudChip>
    </MudTooltip>
}
else if (crossEpicName != null)
{
    <MudTooltip Text="@predTitle">
        <MudChip Color="Color.Warning" Size="Size.Small" Style="background-color: orange;">
            ⛓ Cross-Epic: @crossEpicName > @Truncate(predTitle, 25)
        </MudChip>
    </MudTooltip>
}
else
{
    <MudTooltip Text="@predTitle">
        <MudChip Color="Color.Warning" Size="Size.Small">⛓ Blocked by: @Truncate(predTitle, 30)</MudChip>
    </MudTooltip>
}
```

Cross-Epic = orange (use `Style="background-color: orange;"` on MudChip or `Color.Warning` with a secondary class).
Same-Epic = amber (`Color.Warning` default).
Visual distinction is required by spec.

### C2 — WorkItemRecord.Description missing; Copy brief broken

**Problem:** `WorkItemRecord` has no `Description` property. Copy brief calls `CopyToClipboard(extWi.Title)` — copies the title, not the brief. Description preview in the External Dependencies panel is also absent.

**Fix requires multiple files:**

#### 2a. Add `Description` to `Models/Entities/WorkItemRecord.cs`
```csharp
public string? Description { get; set; }
```

#### 2b. Add EF column mapping in `Data/NexusDbContext.cs`
Column: `description TEXT NULL` — descriptions can be long, use TEXT not VARCHAR.

#### 2c. Create EF Core migration `AddWorkItemRecordDescription`
nexus-web auto-applies migrations on startup — no manual step needed.

#### 2d. Map in StubAdoService — BOTH methods
```csharp
record.Description = dto.Description;
```
In BOTH `CreateWorkItemAsync` AND `CreateWorkItemBatchAsync`.

#### 2e. Ensure `AdoWorkItemDto.Description` exists
Check `Models/DTOs/AdoWorkItemDto.cs` — add `public string? Description { get; set; }` if not present.

#### 2f. Populate in `ArtifactGenerationService`
After parsing each WI candidate from the AI response, make sure `dto.Description` is being set from the parsed field. Check what the AI response model calls the description field (`description`, `developerBrief`, or similar) and map it.

#### 2g. Fix the Copy brief button in NexusArtifacts.razor
```razor
<MudButton OnClick="@(() => CopyToClipboard(extWi.Description))" ...>Copy brief</MudButton>
```

#### 2h. Add the 120-char preview
```razor
<MudText Typo="Typo.body2">
    @(extWi.Description?.Length > 120 ? extWi.Description[..120] + "…" : extWi.Description)
</MudText>
```

---

## IMPORTANT FIXES

### I1 — Tag chips: use correct field
`WorkItemRecord` has no `Tags` property. The External Dependencies panel should NOT attempt to show tags (or show them from a Tags field if one exists). Check if there's a `Tags` property — if not, remove the tag chips loop entirely for now. Don't invent a field that doesn't exist.

### I2 — Template badge emojis
The badges show text ("Infra", "Migration", "TC") instead of the emoji characters. Fix to use actual emoji:
- Infrastructure: `🏗️` (U+1F3D7 + variation selector)
- Migration: `🔄` (U+1F504)
- Test Case: `🧪` (U+1F9EA)

### I3 — ⛓ emoji in predecessor chips
The ⛓ character (U+26D3) is currently absent from the chip labels. Add it: `"⛓ Blocked by: ..."`, `"⛓ Cross-Epic: ..."`, `"⛓ [!] ..."`.

### I4 — Cross-Epic vs same-Epic visual distinction
Already covered in C1 fix — orange for cross-Epic, amber for same-Epic.

### I5 — Replace `@inject NexusDbContext Db` with `IDbContextFactory`
Blazor Server creates one component per circuit. Injecting `DbContext` directly causes lifetime issues (scoped DbContext outlives the request in a Blazor circuit).

```razor
@inject IDbContextFactory<NexusDbContext> DbFactory
```

Then in `OnInitializedAsync`:
```csharp
await using var db = await DbFactory.CreateDbContextAsync();
// use db for queries
```

Check how other Blazor pages in this project handle DB access — follow that pattern exactly.

### I6 — No ownership check
The WI tree page currently allows any authenticated user to view any submission. At minimum, check that the current user matches the submission owner, or match the auth pattern used by the existing artifact/submission pages. Look at how other pages in `Components/Pages/` handle this.

### I7 — Description in ArtifactGenerationService / DTO pipeline
Already covered in C2 fix (2f above) — just ensure the AI response description field is mapped through.

---

## What NOT to change
- Cross-Epic detection logic (`GetEpicTitle` parent-chain traversal) — Clint confirmed it's correct
- Test Case grouping structure — correct
- Controller endpoint structure — correct (just needs Description field available)
- MudBlazor v7 API usage — correct

---

## ADO Updates (MANDATORY)
```
mcporter call devops.add_comment project="FAIT" id=2500 text="**[Tony Stark — BUILD cycle 2]**
Commit {hash}: fixed C1 (cross-Epic Epic name in chip label), C2 (WorkItemRecord.Description + migration + copy brief fix), I1-I7. Build: SUCCEEDED."
```

## Build Report
Append a cycle 2 section to `/home/fredw/projects/fip/nexus/pipeline/ADO2500-BUILD-REPORT.md`:
- Files modified
- New commit hash
- Build result
- CC invocation
- Confirmation all 9 items (C1, C2, I1-I7) addressed

## Notify Maria when done
```
openclaw system event --text "ADO2500 BUILD C2 COMPLETE: cross-Epic label, Description field, badge emojis, all important fixes" --mode now
```
